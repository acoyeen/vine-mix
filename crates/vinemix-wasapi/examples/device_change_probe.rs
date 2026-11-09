//! Diagnostic: measures how the engine's device workers ride through a device
//! being added or removed. Opens shared-mode streams on every active physical
//! endpoint (render endpoints play silence; capture is discarded), runs a 5 ms
//! mixer like the engine's, and reports every reopen, render underrun and
//! capture gap the mixer would have heard. VB-CABLE capture (where apps such
//! as a music player reach VineMix) is watched too; other virtual endpoints
//! are skipped. Nothing is played or recorded.
//!
//! Usage: device_change_probe <label> <seconds>

use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::Arc;
use std::time::{Duration, Instant};

use vinemix_model::config::DeviceBinding;
use vinemix_rt::{frame_queue, CaptureBufferReader, Consumer, Frame, Producer, QUANTUM_FRAMES, SAMPLE_RATE};
use vinemix_wasapi::{AudioWorker, DeviceCatalog, QueueEnd};

enum Side {
    Capture { consumer: Consumer, reader: CaptureBufferReader, gap_frames: Arc<AtomicU64> },
    Render { producer: Producer },
}

struct Probe {
    name: String,
    worker: Arc<AudioWorker>,
    gap_frames: Arc<AtomicU64>,
    last_state: String,
    last_reconnects: u64,
    last_underruns: u64,
    last_gap: u64,
    fallbacks: u32,
}

fn main() {
    let mut args = std::env::args().skip(1);
    let label = args.next().unwrap_or_else(|| "probe".into());
    let seconds: u64 = args.next().and_then(|s| s.parse().ok()).unwrap_or(60);
    let catalog = Arc::new(DeviceCatalog::new().expect("catalog"));
    let endpoints: Vec<_> = catalog
        .enumerate()
        .into_iter()
        .filter(|e| e.state == "Active" && (e.transport == "physical" || (e.transport == "vbCable" && e.direction == "capture")))
        .collect();
    let mut probes = Vec::new();
    let mut sides = Vec::new();
    for endpoint in &endpoints {
        let (producer, consumer, monitor) = frame_queue(9600);
        let capture = endpoint.direction == "capture";
        let gap_frames = Arc::new(AtomicU64::new(0));
        let (queue, side) = if capture {
            (QueueEnd::Capture(producer), Side::Capture { consumer, reader: CaptureBufferReader::default(), gap_frames: gap_frames.clone() })
        } else {
            (QueueEnd::Render(consumer), Side::Render { producer })
        };
        let vb_cable = endpoint.transport == "vbCable";
        let transport = if vb_cable { "vbCable" } else if capture { "" } else { "listening" };
        let Ok(worker) = AudioWorker::new(capture && !vb_cable, queue, monitor, catalog.clone(), transport) else { continue };
        worker.configure(DeviceBinding { id: endpoint.id.clone(), stable_id: String::new(), name: endpoint.name.clone(), channel: -1 });
        let worker = Arc::new(worker);
        sides.push((worker.clone(), side));
        let name = format!("{} {}", if capture { "in " } else { "out" }, endpoint.name);
        probes.push(Probe {
            name,
            worker,
            gap_frames,
            last_state: String::new(),
            last_reconnects: 0,
            last_underruns: 0,
            last_gap: 0,
            fallbacks: 0,
        });
    }
    let stop = Arc::new(AtomicBool::new(false));
    let mixer_stop = stop.clone();
    let mixer = std::thread::spawn(move || {
        let _mmcss = vinemix_wasapi::worker::mmcss_task("Pro Audio");
        let quantum = Duration::from_nanos(QUANTUM_FRAMES as u64 * 1_000_000_000 / u64::from(SAMPLE_RATE));
        let silence = [Frame::SILENT; QUANTUM_FRAMES];
        let mut block = [Frame::SILENT; QUANTUM_FRAMES];
        let mut next = Instant::now();
        while !mixer_stop.load(Ordering::Relaxed) {
            for (worker, side) in &mut sides {
                match side {
                    Side::Capture { consumer, reader, gap_frames } => {
                        // Any frame a running stream fails to supply, including
                        // re-priming after the queue was emptied, is silence
                        // the mixer hears. Reopens are reported separately.
                        let ready = worker.ready();
                        let read = reader.read(consumer, &mut block, ready, worker.period_frames() as usize, worker.target_queue_frames() as usize);
                        if ready && read < block.len() {
                            gap_frames.fetch_add((block.len() - read) as u64, Ordering::Relaxed);
                        }
                    }
                    Side::Render { producer } => {
                        if worker.ready() {
                            if producer.size() > SAMPLE_RATE as usize / 10 {
                                producer.request_clear();
                            }
                            producer.push(&silence);
                        } else if !producer.clear_pending() {
                            producer.request_clear();
                        }
                    }
                }
            }
            next += quantum;
            let now = Instant::now();
            if next > now {
                std::thread::sleep(next - now);
            } else {
                next = now;
            }
        }
    });
    let start = Instant::now();
    let stamp = |at: Instant| format!("{:>7.2}s", (at - start).as_secs_f64());
    println!("[{label}] watching {} streams for {seconds} s; add or remove a device now", probes.len());
    let mut generation = catalog.generation();
    // Let every stream open and prime before counting.
    std::thread::sleep(Duration::from_secs(3));
    for probe in &mut probes {
        let status = probe.worker.status();
        probe.last_state = format!("{}: {}", status.state, status.message);
        probe.last_reconnects = status.reconnects;
        probe.last_underruns = status.underruns;
        probe.last_gap = probe.gap_frames.load(Ordering::Relaxed);
        println!("[{label}] {} {:<40} {}", stamp(Instant::now()), probe.name, probe.last_state);
    }
    while start.elapsed() < Duration::from_secs(seconds) {
        std::thread::sleep(Duration::from_millis(50));
        let now = Instant::now();
        let current = catalog.generation();
        if current != generation {
            println!("[{label}] {} --- device change notification (catalog generation {current})", stamp(now));
            generation = current;
        }
        for probe in &mut probes {
            let status = probe.worker.status();
            let state = format!("{}: {}", status.state, status.message);
            let gap = probe.gap_frames.load(Ordering::Relaxed);
            let mut events = Vec::new();
            if state != probe.last_state {
                if state.contains("Compatible audio timing") {
                    probe.fallbacks += 1;
                }
                events.push(format!("state -> {state}"));
            }
            if status.reconnects != probe.last_reconnects {
                events.push(format!("REOPENED (+{})", status.reconnects - probe.last_reconnects));
            }
            if status.underruns != probe.last_underruns {
                events.push(format!("underrun +{}", status.underruns - probe.last_underruns));
            }
            if gap != probe.last_gap {
                events.push(format!("capture gap +{:.1} ms", (gap - probe.last_gap) as f64 * 1000.0 / f64::from(SAMPLE_RATE)));
            }
            if !events.is_empty() {
                println!("[{label}] {} {:<40} {}", stamp(now), probe.name, events.join(", "));
            }
            probe.last_state = state;
            probe.last_reconnects = status.reconnects;
            probe.last_underruns = status.underruns;
            probe.last_gap = gap;
        }
    }
    stop.store(true, Ordering::Relaxed);
    let _ = mixer.join();
    println!("[{label}] summary (after the 3 s warm-up):");
    for probe in &probes {
        let status = probe.worker.status();
        println!(
            "[{label}]   {:<40} reopens {:>2}  underruns {:>3}  timing fallbacks {}  queue {:>5.1} ms  target {:>5.1} ms",
            probe.name,
            status.reconnects.saturating_sub(1),
            status.underruns,
            probe.fallbacks,
            status.queue_ms,
            status.target_queue_ms
        );
    }
    for probe in &probes {
        probe.worker.stop();
    }
}
