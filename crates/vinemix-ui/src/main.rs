//! `VineMix.exe`: the desktop window, tray icon and engine supervisor
//! (`App.OnStartup`), plus the inert release check and the update worker.

#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use std::path::PathBuf;
use std::process::ExitCode;
use std::sync::atomic::{AtomicBool, AtomicI32, Ordering};
use std::sync::{Arc, OnceLock};

use eframe::egui;
use vinemix_ui::connection::Expectations;
use vinemix_ui::link::EngineLink;
use vinemix_ui::mixer::MixerState;
use vinemix_ui::platform::{self, MessageKind, NamedEvent, SessionOwnership};
use vinemix_ui::ui::app::{App, Command, Startup};
use vinemix_ui::{paths, supervisor, tray, update};

#[derive(Default)]
struct Options {
    arguments: Vec<String>,
    background: bool,
    preview: bool,
    preview_waiting: bool,
    preview_signal: bool,
    preview_fixture: Option<PathBuf>,
    standalone_context: bool,
    settings: Option<PathBuf>,
    release_smoke: Option<PathBuf>,
    apply_update: Option<PathBuf>,
    update_health: Option<PathBuf>,
    update_reverted: Option<PathBuf>,
}

fn parse() -> Result<Options, String> {
    let mut options = Options { arguments: std::env::args().skip(1).collect(), ..Options::default() };
    let mut arguments = options.arguments.clone().into_iter();
    let path = |value: Option<String>, name: &str| -> Result<PathBuf, String> { value.map(PathBuf::from).ok_or_else(|| format!("{name} requires a path.")) };
    while let Some(argument) = arguments.next() {
        match argument.to_ascii_lowercase().as_str() {
            "--background" => options.background = true,
            "--preview" => options.preview = true,
            // Preview fixture variants (`UiPreview.CreateSnapshot`).
            "--waiting" => options.preview_waiting = true,
            "--signal" => options.preview_signal = true,
            "--preview-fixture" => options.preview_fixture = Some(path(arguments.next(), "--preview-fixture")?),
            "--standalone-context" => options.standalone_context = true,
            "--settings" => {
                let value = arguments.next().ok_or("--settings requires a workspace filename.")?;
                options.settings = Some(paths::absolute(&PathBuf::from(value)));
            }
            "--release-smoke" => options.release_smoke = Some(path(arguments.next(), "--release-smoke")?),
            "--apply-update" => options.apply_update = Some(path(arguments.next(), "--apply-update")?),
            "--update-health" => options.update_health = Some(paths::absolute(&path(arguments.next(), "--update-health")?)),
            "--update-reverted" => options.update_reverted = Some(path(arguments.next(), "--update-reverted")?),
            other => return Err(format!("Unknown argument {other}.")),
        }
    }
    Ok(options)
}

fn fail(title: &str, text: &str) -> ExitCode {
    platform::message_box(title, text, MessageKind::Information);
    ExitCode::from(1)
}

fn code(value: i32) -> ExitCode {
    ExitCode::from(u8::try_from(value).unwrap_or(1))
}

#[allow(clippy::too_many_lines)]
fn main() -> ExitCode {
    let options = match parse() {
        Ok(options) => options,
        Err(error) => return fail("VineMix", &error),
    };
    // Inert package check: before any user data, window or engine.
    if let Some(directory) = &options.release_smoke {
        return code(vinemix_ui::smoke::run(directory));
    }
    // Normal launches reopen once from the desktop shell so this process and
    // the engine see the ordinary per-user file system (`StandaloneLaunch`).
    if !options.preview && !options.standalone_context {
        let executable = std::env::current_exe().unwrap_or_else(|_| PathBuf::from("VineMix.exe"));
        let mut arguments = options.arguments.clone();
        arguments.push("--standalone-context".into());
        let directory = std::env::current_dir().unwrap_or_default();
        return match platform::relaunch_from_shell(&executable, &arguments, &directory) {
            Ok(()) => ExitCode::SUCCESS,
            Err(error) => fail(
                "VineMix · Could not start",
                &format!("VineMix could not open its normal saved workspace. Please try opening it again.\n\n{error}"),
            ),
        };
    }
    if let Some(transaction) = &options.apply_update {
        let platform = update::windows::WindowsPlatform;
        return code(update::worker::run(transaction, &platform, None, None));
    }
    // Without a resolvable per-user data folder nothing is opened, created
    // or saved: never fall back to the working directory.
    if !options.preview
        && let Err(error) = paths::try_data_directory()
    {
        return fail(
            "VineMix \u{b7} Could not start",
            &format!(
                "VineMix could not locate its application data folder, so no saved workspace was opened or changed. Sign out and back in, then try again.\n\n{error}"
            ),
        );
    }
    if let Some(transaction) = &options.update_health {
        if update::session::begin_health(transaction, options.settings.as_deref()).is_err() {
            return ExitCode::from(1);
        }
    } else if !options.preview {
        match update::session::handle_pending_updates() {
            Ok(true) => return ExitCode::SUCCESS,
            Ok(false) => {}
            Err(error) => return fail("VineMix update recovery", &error),
        }
    }
    let scope = match vinemix_platform::user_scope() {
        Ok(scope) => scope,
        Err(error) => return fail("VineMix · Could not start", &error),
    };
    // One desktop per user session; a second launch shows the first.
    let ownership = if options.preview { None } else { SessionOwnership::acquire("Desktop", &scope) };
    if !options.preview && ownership.is_none() {
        for product in ["AudioMixer", "VineMix"] {
            NamedEvent::signal_existing(&format!(r"Local\{product}.Show.{scope}"));
        }
        return ExitCode::SUCCESS;
    }
    let show_events: Vec<NamedEvent> = if options.preview {
        Vec::new()
    } else {
        ["VineMix", "AudioMixer"].iter().filter_map(|product| NamedEvent::create(&format!(r"Local\{product}.Show.{scope}"))).collect()
    };
    paths::set_custom_settings(options.settings.clone());
    let settings_existed = paths::settings().is_file();
    let ctx_slot: Arc<OnceLock<egui::Context>> = Arc::new(OnceLock::new());
    let repaint_slot = Arc::clone(&ctx_slot);
    let repaint: Arc<dyn Fn() + Send + Sync> = Arc::new(move || {
        if let Some(ctx) = repaint_slot.get() {
            ctx.request_repaint();
        }
    });
    let link = if options.preview {
        let mut state = MixerState::new(true);
        let mut snapshot = vinemix_ui::preview::create_snapshot(options.preview_waiting, options.preview_signal);
        if let Some(fixture) = &options.preview_fixture {
            let applied = std::fs::read_to_string(fixture).map_err(|e| e.to_string()).and_then(|text| vinemix_ui::preview::apply_fixture(&mut snapshot, &text));
            if let Err(error) = applied {
                return fail("VineMix · UI Preview", &error);
            }
        }
        let _ = state.apply_preview_snapshot(snapshot, Vec::new());
        state.needs_initial_frame = true;
        let r = Arc::clone(&repaint);
        EngineLink::preview(state, Box::new(move || r()))
    } else {
        let engine = match supervisor::resolve_engine_path() {
            Ok(engine) => engine,
            Err(error) => return fail("VineMix · Could not open workspace", &error),
        };
        if let Err(error) = supervisor::validate_engine(&engine) {
            return fail("VineMix · Could not open workspace", &error);
        }
        if options.settings.is_none() && vinemix_platform::development_instance().is_none() {
            if let Err(error) = vinemix_ui::migration::run_if_needed(&scope, &engine) {
                return fail("VineMix · Could not open workspace", &error);
            }
        }
        paths::lifetime_log::start();
        // Panics abort; leave the reason in desktop.log before the default report.
        let report = std::panic::take_hook();
        std::panic::set_hook(Box::new(move |info| {
            paths::lifetime_log::write(&format!("Fatal error: {info}"));
            report(info);
        }));
        let mut state = MixerState::new(false);
        if options.update_health.is_some() {
            state.set_update_health_mode(true);
        }
        let expectations = Expectations {
            engine_path: engine,
            settings_path: paths::settings(),
            release_version: vinemix_platform::release::VERSION.into(),
        };
        let r = Arc::clone(&repaint);
        EngineLink::new(state, expectations, Box::new(move || r()))
    };
    if options.background {
        link.state().visible = false;
    }
    let (sender, receiver) = std::sync::mpsc::channel::<Command>();
    let _tray = (!options.preview).then(|| tray::Tray::start(sender.clone(), Arc::clone(&repaint)));
    for event in show_events {
        let sender = sender.clone();
        let repaint = Arc::clone(&repaint);
        std::thread::spawn(move || loop {
            if event.wait(std::time::Duration::from_secs(3600)) {
                let _ = sender.send(Command::Show);
                repaint();
            }
        });
    }
    // The local activation scripts recognise a ready workspace by this exact
    // title together with the window class winit registers ("Window Class");
    // see `WorkspaceFramework` in scripts/assert-desktop-data-context.ps1.
    // Change either only together with that guard and its fixtures.
    #[cfg(not(feature = "rust-edition"))]
    let (icon, title) = (include_bytes!("../assets/branding/app.png").as_slice(), "VineMix · Patch workspace");
    #[cfg(feature = "rust-edition")]
    let (icon, title) = (include_bytes!("../assets/branding/app-rust.png").as_slice(), "VineMix (Rust) · Patch workspace");
    let icon = eframe::icon_data::from_png_bytes(icon).ok();
    let mut viewport = egui::ViewportBuilder::default()
        .with_title(if options.preview { "VineMix · UI Preview" } else { title })
        .with_inner_size([1280.0, 800.0])
        .with_min_inner_size([900.0, 580.0])
        .with_decorations(false)
        .with_resizable(true)
        .with_visible(!options.background);
    if let Some(icon) = icon {
        viewport = viewport.with_icon(icon);
    }
    let native = eframe::NativeOptions { viewport, centered: true, persist_window: false, ..Default::default() };
    let exit_code = Arc::new(AtomicI32::new(0));
    let startup = Startup {
        background: options.background,
        preview: options.preview,
        settings_existed,
        exit_flag: Arc::new(AtomicBool::new(false)),
        exit_code: Arc::clone(&exit_code),
        update_health: options.update_health.clone(),
        rollback_notice: options.update_reverted.as_deref().map(update::session::rollback_notice),
        repaint: Arc::clone(&repaint),
    };
    let start_link = Arc::clone(&link);
    let result = eframe::run_native(
        "VineMix",
        native,
        Box::new(move |cc| {
            let _ = ctx_slot.set(cc.egui_ctx.clone());
            start_link.start();
            Ok(Box::new(App::new(cc, start_link, receiver, sender, startup)))
        }),
    );
    drop(ownership);
    match result {
        Ok(()) => code(exit_code.load(Ordering::Acquire)),
        Err(error) => fail("VineMix", &format!("The window could not be created. {error}")),
    }
}
