//! `VineMix.SetupHelper.exe` (`installer/VineMix.SetupHelper`): read-only
//! detection for Setup. It never installs or removes drivers, changes
//! devices, terminates processes or changes settings; it writes only the
//! requested INI report.
//!
//! ```text
//! VineMix.SetupHelper.exe detect --output <file> [--install-dir <directory>]
//! VineMix.SetupHelper.exe --self-test
//! ```
//!
//! Exit codes: 0 complete detection, 2 conservative partial detection (the
//! INI is still written), 1 invalid arguments or a failed report.

#![cfg(windows)]

mod detect;
mod inf;
mod policy;

use std::path::PathBuf;
use std::process::ExitCode;

use policy::{DriverFamily, DriverResult};

fn usage() -> ExitCode {
    eprintln!("Usage: VineMix.SetupHelper detect --output <file> [--install-dir <directory>] | --self-test");
    ExitCode::from(1)
}

/// Printable ASCII only: values can never inject INI lines.
pub fn sanitize(value: &str) -> String {
    value.chars().map(|c| if (' '..='~').contains(&c) { c } else { ' ' }).collect()
}

fn write(text: &mut String, key: &str, value: &str) {
    text.push_str(key);
    text.push('=');
    text.push_str(&sanitize(value));
    text.push_str("\r\n");
}

fn flag(value: bool) -> &'static str {
    if value { "1" } else { "0" }
}

fn write_driver(text: &mut String, section: &str, result: &DriverResult) {
    text.push_str(&format!("[{section}]\r\n"));
    write(text, "DetectSucceeded", flag(result.succeeded));
    write(text, "Installed", flag(result.installed));
    write(text, "State", &result.state);
    write(text, "Detail", &result.detail);
    write(text, "Version", &result.version);
}

fn run(arguments: &[String]) -> Result<ExitCode, String> {
    if arguments == ["--self-test"] {
        return policy::self_tests().map(|checks| {
            println!("PASS: {checks} setup detection checks. No devices or running processes changed.");
            ExitCode::SUCCESS
        });
    }
    if arguments.first().map(String::as_str) != Some("detect") {
        return Ok(usage());
    }
    let (mut output, mut install_directory): (Option<PathBuf>, Option<PathBuf>) = (None, None);
    let mut index = 1;
    while index < arguments.len() {
        if index + 1 >= arguments.len() {
            return Ok(usage());
        }
        match arguments[index].as_str() {
            "--output" if output.is_none() => output = Some(PathBuf::from(&arguments[index + 1])),
            "--install-dir" if install_directory.is_none() => {
                install_directory = Some(std::path::absolute(&arguments[index + 1]).map_err(|e| e.to_string())?);
            }
            _ => return Ok(usage()),
        }
        index += 2;
    }
    let Some(output) = output.filter(|o| !o.as_os_str().is_empty()) else { return Ok(usage()) };
    let devices = detect::read_devices();
    let packages = detect::read_packages();
    let vb = policy::evaluate(DriverFamily::VBCable, &devices, &packages);
    let vac = policy::evaluate(DriverFamily::Vac, &devices, &packages);
    let application = detect::read_application(install_directory.as_deref())?;
    let mut text = String::from("; VineMix read-only setup detection. No devices or processes were changed.\r\n");
    write_driver(&mut text, "VBCable", &vb);
    write_driver(&mut text, "VAC", &vac);
    text.push_str("[Application]\r\n");
    write(&mut text, "DetectSucceeded", flag(application.succeeded));
    write(&mut text, "Running", flag(application.running));
    write(&mut text, "RunningInstalled", flag(application.running_installed));
    write(&mut text, "InstallPathSafe", flag(application.install_path_safe));
    write(&mut text, "State", if application.running { "Running" } else if application.succeeded { "Stopped" } else { "DetectionFailed" });
    write(&mut text, "Detail", &application.detail);
    let destination = std::path::absolute(&output).map_err(|e| e.to_string())?;
    if let Some(parent) = destination.parent() {
        std::fs::create_dir_all(parent).map_err(|e| e.to_string())?;
    }
    let temporary = PathBuf::from(format!("{}.{}.tmp", destination.display(), std::process::id()));
    let written = std::fs::write(&temporary, text.as_bytes()).and_then(|()| std::fs::rename(&temporary, &destination));
    if temporary.exists() {
        let _ = std::fs::remove_file(&temporary);
    }
    written.map_err(|e| e.to_string())?;
    Ok(if vb.succeeded && vac.succeeded && application.succeeded { ExitCode::SUCCESS } else { ExitCode::from(2) })
}

fn main() -> ExitCode {
    let arguments: Vec<String> = std::env::args().skip(1).collect();
    match run(&arguments) {
        Ok(code) => code,
        Err(error) => {
            // A detection failure must never become permission to install a second driver.
            eprintln!("Setup detection could not finish ({error}).");
            ExitCode::from(1)
        }
    }
}
