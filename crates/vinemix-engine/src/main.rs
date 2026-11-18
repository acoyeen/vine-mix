//! `VineMix.Engine.exe`: the background audio engine.
//!
//! A drop-in replacement for the 0.5 C++ engine: same command line, same
//! single-instance locks, same per-user control pipe and protocol.

use std::path::PathBuf;
use std::process::ExitCode;
use std::sync::atomic::{AtomicPtr, Ordering};

use serde_json::json;
use vinemix_engine::store::validate_settings_file;
use vinemix_engine::Engine;
use vinemix_platform::release;

static CURRENT_ENGINE: AtomicPtr<Engine> = AtomicPtr::new(std::ptr::null_mut());
static CONSOLE_LOCK: std::sync::RwLock<()> = std::sync::RwLock::new(());

unsafe extern "system" fn console_control(kind: u32) -> windows::core::BOOL {
    use windows::Win32::System::Console::{CTRL_BREAK_EVENT, CTRL_CLOSE_EVENT, CTRL_C_EVENT, CTRL_SHUTDOWN_EVENT};
    if matches!(kind, CTRL_C_EVENT | CTRL_BREAK_EVENT | CTRL_CLOSE_EVENT | CTRL_SHUTDOWN_EVENT) {
        let _guard = CONSOLE_LOCK.read();
        let engine = CURRENT_ENGINE.load(Ordering::Acquire);
        // SAFETY: the pointer is cleared under the write lock before the
        // engine is dropped, so a non-null value is live here.
        if let Some(engine) = unsafe { engine.as_ref() } {
            engine.request_stop();
        }
        return true.into();
    }
    false.into()
}

struct ConsoleRegistration;

impl ConsoleRegistration {
    fn new(engine: &Engine) -> Self {
        CURRENT_ENGINE.store(std::ptr::from_ref(engine).cast_mut(), Ordering::Release);
        // SAFETY: registering a valid handler.
        unsafe {
            let _ = windows::Win32::System::Console::SetConsoleCtrlHandler(Some(console_control), true);
        }
        Self
    }
}

impl Drop for ConsoleRegistration {
    fn drop(&mut self) {
        {
            let _guard = CONSOLE_LOCK.write();
            CURRENT_ENGINE.store(std::ptr::null_mut(), Ordering::Release);
        }
        // SAFETY: removing the handler registered above.
        unsafe {
            let _ = windows::Win32::System::Console::SetConsoleCtrlHandler(Some(console_control), false);
        }
    }
}

const HELP: &str = "VineMix Engine
  --list-devices   Enumerate endpoints without opening audio streams
  --settings FILE Use a separate configuration file
  --validate-settings FILE Validate settings without starting audio or writing files
  --version       Print the release version without loading settings
";

fn run() -> Result<ExitCode, String> {
    vinemix_platform::checked_development_instance()?;
    let mut settings: Option<PathBuf> = None;
    let mut validation: Option<PathBuf> = None;
    let mut list_devices = false;
    let mut version = false;
    let mut arguments = std::env::args_os().skip(1);
    while let Some(option) = arguments.next() {
        match option.to_str() {
            Some("--list-devices") => list_devices = true,
            Some("--settings") => match arguments.next() {
                Some(path) => settings = Some(PathBuf::from(path)),
                None => return Err("Unknown argument. Use --help.".into()),
            },
            Some("--validate-settings") => match arguments.next() {
                Some(path) => validation = Some(PathBuf::from(path)),
                None => return Err("Unknown argument. Use --help.".into()),
            },
            Some("--version") => version = true,
            Some("--help") => {
                print!("{HELP}");
                return Ok(ExitCode::SUCCESS);
            }
            _ => return Err("Unknown argument. Use --help.".into()),
        }
    }
    if version {
        println!("VineMix {}", release::VERSION);
        return Ok(ExitCode::SUCCESS);
    }
    if let Some(path) = validation {
        let (store, configuration) = validate_settings_file(&path);
        let loaded = store.load_state() == vinemix_engine::store::LoadState::Loaded;
        let report = json!({
            "valid": loaded,
            "settingsState": store.load_state().as_str(),
            "warning": store.warning(),
            "version": release::VERSION,
            "revision": configuration.revision,
        });
        println!("{}", vinemix_model::json::dump(&report));
        return Ok(if loaded { ExitCode::SUCCESS } else { ExitCode::from(2) });
    }
    if list_devices {
        // Enumeration runs on the catalog's COM thread; no stream is opened.
        let catalog = vinemix_wasapi::DeviceCatalog::new().map_err(|e| e.message())?;
        std::thread::sleep(std::time::Duration::from_millis(750));
        let endpoints = serde_json::to_value(catalog.enumerate()).map_err(|e| e.to_string())?;
        println!("{}", vinemix_model::json::dump_pretty(&endpoints));
        return Ok(ExitCode::SUCCESS);
    }
    let _instance = vinemix_platform::EngineInstance::acquire(&vinemix_platform::user_scope()?)?;
    let engine = Engine::new(settings).map_err(|e| e.to_string())?;
    let _registration = ConsoleRegistration::new(&engine);
    engine.start().map_err(|e| e.to_string())?;
    let served = vinemix_engine::control::run(&engine);
    engine.stop();
    served?;
    Ok(ExitCode::SUCCESS)
}

fn main() -> ExitCode {
    // SAFETY: setting the console output code page to UTF-8.
    unsafe {
        let _ = windows::Win32::System::Console::SetConsoleOutputCP(65001);
    }
    match run() {
        Ok(code) => code,
        Err(error) => {
            eprintln!("VineMix: {error}");
            ExitCode::from(1)
        }
    }
}
