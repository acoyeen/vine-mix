//! Scripted input for verifying the desktop without touching the real
//! mouse or keyboard. Active only in a development instance
//! (`VINEMIX_DEV_INSTANCE`) with `VINEMIX_UI_AUTOMATION=<script file>`.
//!
//! One step per line (coordinates in points, `#` starts a comment):
//! `wait <ms>`, `move <x> <y>`, `click <x> <y> [mods]`, `rclick <x> <y>`,
//! `dbl <x> <y>`, `drag <x1> <y1> <x2> <y2> [mods]`, `wheel <x> <y> <dy> [mods]`,
//! `key <Name> [mods]` (egui key names; mods like `ctrl+shift`),
//! `text <characters>`, `shot <file.png>`, `log <text>`, `dump`, `zoom <factor>`,
//! `size <width> <height>` (points), `quit`.
//! Results (`log`, `dump`, errors) go to `<script>.log`.

use std::collections::VecDeque;
use std::io::Write as _;
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;
use std::time::{Duration, Instant};

use eframe::egui::{self, Event, Modifiers, PointerButton, Pos2, Vec2};

enum Step {
    Events(Vec<Event>),
    Wait(Duration),
    Shot(PathBuf),
    Log(String),
    Dump,
    Quit,
    Zoom(f32),
    Size(f32, f32),
}

pub struct Automation {
    steps: VecDeque<Step>,
    log: PathBuf,
    waiting_until: Option<Instant>,
    shot_pending: Option<Arc<AtomicBool>>,
    pointer: Pos2,
    /// Set when the script asks for a state dump; the app fills it.
    pub dump_requested: bool,
    pub quit_requested: bool,
}

impl std::fmt::Debug for Automation {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("Automation").field("remaining", &self.steps.len()).finish_non_exhaustive()
    }
}

fn modifiers(text: Option<&str>) -> Modifiers {
    let mut result = Modifiers::NONE;
    for part in text.unwrap_or("").split('+') {
        match part.to_ascii_lowercase().as_str() {
            "ctrl" => {
                result.ctrl = true;
                result.command = true;
            }
            "shift" => result.shift = true,
            "alt" => result.alt = true,
            _ => {}
        }
    }
    result
}

fn button(pos: Pos2, button: PointerButton, pressed: bool, modifiers: Modifiers) -> Event {
    Event::PointerButton { pos, button, pressed, modifiers }
}

impl Automation {
    pub fn from_environment() -> Option<Self> {
        vinemix_platform::development_instance()?;
        let script = PathBuf::from(std::env::var_os("VINEMIX_UI_AUTOMATION")?);
        let text = std::fs::read_to_string(&script).ok()?;
        let log = script.with_extension("log");
        let _ = std::fs::write(&log, "");
        let mut automation = Self {
            steps: VecDeque::new(),
            log,
            waiting_until: None,
            shot_pending: None,
            pointer: Pos2::ZERO,
            dump_requested: false,
            quit_requested: false,
        };
        let base = script.parent().map(Path::to_path_buf).unwrap_or_default();
        for (number, line) in text.lines().enumerate() {
            let line = line.split('#').next().unwrap_or("").trim();
            if line.is_empty() {
                continue;
            }
            if let Err(error) = automation.parse(line, &base) {
                automation.write(&format!("line {}: {error}", number + 1));
            }
        }
        Some(automation)
    }

    fn parse(&mut self, line: &str, base: &Path) -> Result<(), String> {
        let (command, rest) = line.split_once(' ').unwrap_or((line, ""));
        let words: Vec<&str> = rest.split_whitespace().collect();
        let number = |index: usize| -> Result<f32, String> {
            words.get(index).ok_or("missing number")?.parse::<f32>().map_err(|_| "invalid number".to_owned())
        };
        let point = |index: usize| -> Result<Pos2, String> { Ok(Pos2::new(number(index)?, number(index + 1)?)) };
        let frame = Duration::from_millis(50);
        match command {
            "wait" => self.steps.push_back(Step::Wait(Duration::from_millis(number(0)? as u64))),
            "move" => {
                self.steps.push_back(Step::Events(vec![Event::PointerMoved(point(0)?)]));
                self.steps.push_back(Step::Wait(frame));
            }
            "click" | "rclick" | "dbl" => {
                let pos = point(0)?;
                let mods = modifiers(words.get(2).copied());
                let which = if command == "rclick" { PointerButton::Secondary } else { PointerButton::Primary };
                self.steps.push_back(Step::Events(vec![Event::PointerMoved(pos)]));
                self.steps.push_back(Step::Wait(frame));
                let repeats = if command == "dbl" { 2 } else { 1 };
                for _ in 0..repeats {
                    self.steps.push_back(Step::Events(vec![button(pos, which, true, mods)]));
                    self.steps.push_back(Step::Events(vec![button(pos, which, false, mods)]));
                }
                self.steps.push_back(Step::Wait(frame));
            }
            "drag" => {
                let from = point(0)?;
                let to = point(2)?;
                let mods = modifiers(words.get(4).copied());
                self.steps.push_back(Step::Events(vec![Event::PointerMoved(from)]));
                self.steps.push_back(Step::Events(vec![button(from, PointerButton::Primary, true, mods)]));
                for index in 1..=12 {
                    let t = index as f32 / 12.0;
                    self.steps.push_back(Step::Events(vec![Event::PointerMoved(from + (to - from) * t)]));
                    self.steps.push_back(Step::Wait(Duration::from_millis(16)));
                }
                self.steps.push_back(Step::Events(vec![button(to, PointerButton::Primary, false, mods)]));
                self.steps.push_back(Step::Wait(frame));
            }
            "wheel" => {
                let pos = point(0)?;
                let delta = number(2)?;
                let mods = modifiers(words.get(3).copied());
                self.steps.push_back(Step::Events(vec![Event::PointerMoved(pos)]));
                self.steps.push_back(Step::Events(vec![Event::MouseWheel {
                    unit: egui::MouseWheelUnit::Line,
                    delta: Vec2::new(0.0, delta),
                    phase: egui::TouchPhase::Move,
                    source: egui::MouseWheelSource::Unknown,
                    modifiers: mods,
                }]));
                self.steps.push_back(Step::Wait(frame));
            }
            "key" => {
                let name = words.first().ok_or("missing key")?;
                let key = egui::Key::from_name(name).ok_or_else(|| format!("unknown key {name}"))?;
                let mods = modifiers(words.get(1).copied());
                let event = |pressed| Event::Key { key, physical_key: Some(key), pressed, repeat: false, modifiers: mods };
                self.steps.push_back(Step::Events(vec![event(true)]));
                self.steps.push_back(Step::Events(vec![event(false)]));
                self.steps.push_back(Step::Wait(frame));
            }
            "text" => {
                self.steps.push_back(Step::Events(vec![Event::Text(rest.to_owned())]));
                self.steps.push_back(Step::Wait(frame));
            }
            "shot" => {
                let path = PathBuf::from(words.first().ok_or("missing file")?);
                self.steps.push_back(Step::Shot(if path.is_absolute() { path } else { base.join(path) }));
            }
            "log" => self.steps.push_back(Step::Log(rest.to_owned())),
            "dump" => self.steps.push_back(Step::Dump),
            "quit" => self.steps.push_back(Step::Quit),
            "zoom" => {
                self.steps.push_back(Step::Zoom(number(0)?));
                self.steps.push_back(Step::Wait(Duration::from_millis(300)));
            }
            "size" => {
                self.steps.push_back(Step::Size(number(0)?, number(1)?));
                self.steps.push_back(Step::Wait(Duration::from_millis(500)));
            }
            other => return Err(format!("unknown step {other}")),
        }
        Ok(())
    }

    pub fn write(&self, text: &str) {
        if let Ok(mut file) = std::fs::OpenOptions::new().append(true).create(true).open(&self.log) {
            let _ = writeln!(file, "{text}");
        }
    }

    /// Feeds the next scripted events into this frame's input.
    pub fn inject(&mut self, ctx: &egui::Context, input: &mut egui::RawInput) {
        // Keep the real pointer from interfering with the script.
        input.events.retain(|event| {
            !matches!(event, Event::PointerMoved(_) | Event::PointerButton { .. } | Event::PointerGone | Event::MouseWheel { .. })
        });
        if self.steps.is_empty() && self.shot_pending.is_none() {
            return;
        }
        ctx.request_repaint();
        if let Some(pending) = &self.shot_pending {
            if !pending.load(Ordering::Acquire) {
                return;
            }
            self.shot_pending = None;
        }
        if let Some(until) = self.waiting_until {
            if Instant::now() < until {
                return;
            }
            self.waiting_until = None;
        }
        while let Some(step) = self.steps.pop_front() {
            match step {
                Step::Events(events) => {
                    for event in &events {
                        if let Event::PointerMoved(pos) | Event::PointerButton { pos, .. } = event {
                            self.pointer = *pos;
                        }
                    }
                    // egui tracks held modifiers from ModifiersChanged events,
                    // so press them before and release them after the step.
                    for event in events {
                        let (modifiers, release) = match &event {
                            Event::Key { modifiers, pressed, .. } | Event::PointerButton { modifiers, pressed, .. } => (*modifiers, !*pressed),
                            Event::MouseWheel { modifiers, .. } => (*modifiers, true),
                            _ => (Modifiers::NONE, false),
                        };
                        let held = !modifiers.is_none();
                        if held {
                            input.events.push(Event::ModifiersChanged(modifiers));
                        }
                        input.events.push(event);
                        if held && release {
                            input.events.push(Event::ModifiersChanged(Modifiers::NONE));
                        }
                    }
                    return;
                }
                Step::Wait(duration) => {
                    self.waiting_until = Some(Instant::now() + duration);
                    return;
                }
                Step::Shot(path) => {
                    let done = Arc::new(AtomicBool::new(false));
                    let flag = Arc::clone(&done);
                    let log = self.log.clone();
                    let repaint = ctx.clone();
                    ctx.send_viewport_cmd(egui::ViewportCommand::Screenshot(egui::ScreenshotCallback::new(move |image| {
                        let result = write_png(&path, &image);
                        if let Ok(mut file) = std::fs::OpenOptions::new().append(true).create(true).open(&log) {
                            let _ = match result {
                                Ok(()) => writeln!(file, "shot {}", path.display()),
                                Err(error) => writeln!(file, "shot failed {}: {error}", path.display()),
                            };
                        }
                        flag.store(true, Ordering::Release);
                        repaint.request_repaint();
                    })));
                    self.shot_pending = Some(done);
                    return;
                }
                Step::Log(text) => self.write(&text),
                Step::Dump => {
                    self.dump_requested = true;
                    return;
                }
                Step::Zoom(factor) => {
                    ctx.set_zoom_factor(factor);
                    return;
                }
                Step::Size(width, height) => {
                    ctx.send_viewport_cmd(egui::ViewportCommand::InnerSize(egui::vec2(width, height)));
                    return;
                }
                Step::Quit => {
                    self.quit_requested = true;
                    return;
                }
            }
        }
    }
}

fn crc32(data: &[u8], mut crc: u32) -> u32 {
    crc = !crc;
    for &byte in data {
        crc ^= u32::from(byte);
        for _ in 0..8 {
            crc = if crc & 1 != 0 { (crc >> 1) ^ 0xEDB8_8320 } else { crc >> 1 };
        }
    }
    !crc
}

fn chunk(out: &mut Vec<u8>, kind: &[u8; 4], data: &[u8]) {
    out.extend_from_slice(&(data.len() as u32).to_be_bytes());
    let start = out.len();
    out.extend_from_slice(kind);
    out.extend_from_slice(data);
    let crc = crc32(&out[start..], 0);
    out.extend_from_slice(&crc.to_be_bytes());
}

/// A minimal RGBA PNG writer (zlib stored blocks).
fn write_png(path: &Path, image: &egui::ColorImage) -> std::io::Result<()> {
    let [width, height] = image.size;
    let mut raw = Vec::with_capacity((width * 4 + 1) * height);
    for row in 0..height {
        raw.push(0);
        for pixel in &image.pixels[row * width..(row + 1) * width] {
            let [r, g, b, a] = pixel.to_srgba_unmultiplied();
            raw.extend_from_slice(&[r, g, b, a]);
        }
    }
    let mut zlib = vec![0x78, 0x01];
    let blocks = raw.chunks(65_535).collect::<Vec<_>>();
    for (index, block) in blocks.iter().enumerate() {
        zlib.push(u8::from(index + 1 == blocks.len()));
        let length = block.len() as u16;
        zlib.extend_from_slice(&length.to_le_bytes());
        zlib.extend_from_slice(&(!length).to_le_bytes());
        zlib.extend_from_slice(block);
    }
    let (mut a, mut b) = (1u32, 0u32);
    for &byte in &raw {
        a = (a + u32::from(byte)) % 65_521;
        b = (b + a) % 65_521;
    }
    zlib.extend_from_slice(&((b << 16) | a).to_be_bytes());
    let mut out = vec![0x89, b'P', b'N', b'G', 0x0D, 0x0A, 0x1A, 0x0A];
    let mut header = Vec::new();
    header.extend_from_slice(&(width as u32).to_be_bytes());
    header.extend_from_slice(&(height as u32).to_be_bytes());
    header.extend_from_slice(&[8, 6, 0, 0, 0]);
    chunk(&mut out, b"IHDR", &header);
    chunk(&mut out, b"IDAT", &zlib);
    chunk(&mut out, b"IEND", &[]);
    if let Some(parent) = path.parent() {
        std::fs::create_dir_all(parent)?;
    }
    std::fs::write(path, out)
}
