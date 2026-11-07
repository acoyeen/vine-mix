//! Detection policy (`DetectionPolicy`) and its self-tests (`SelfTests`).

use crate::inf::{read_inf_text, InfIdentity};

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum DriverFamily {
    VBCable,
    Vac,
}

pub const FAMILIES: [DriverFamily; 2] = [DriverFamily::VBCable, DriverFamily::Vac];

#[derive(Clone, Debug)]
pub struct DeviceEvidence {
    pub hardware_ids: Vec<String>,
    pub version: String,
    pub state: String,
}

#[derive(Clone, Debug)]
pub struct PackageEvidence {
    pub family: DriverFamily,
    pub version: String,
    pub in_driver_store: bool,
}

#[derive(Clone, Debug)]
pub struct Evidence<T> {
    pub items: Vec<T>,
    pub succeeded: bool,
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct DriverResult {
    pub succeeded: bool,
    pub installed: bool,
    pub state: String,
    pub detail: String,
    pub version: String,
}

#[derive(Clone, Debug)]
pub struct ProcessEvidence {
    pub same_user: Option<bool>,
    pub path: Option<String>,
    pub still_running: bool,
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct ApplicationResult {
    pub succeeded: bool,
    pub running: bool,
    pub running_installed: bool,
    pub detail: String,
    pub install_path_safe: bool,
}

pub fn hardware_id(family: DriverFamily) -> &'static str {
    match family {
        DriverFamily::VBCable => "VBAudioVACWDM",
        DriverFamily::Vac => "VirtualAudioCable_83ed7f0e",
    }
}

pub fn matches_hardware_id(value: &str, family: DriverFamily) -> bool {
    let value = value.trim().trim_matches('"');
    let id = hardware_id(family);
    value.eq_ignore_ascii_case(id) || value.eq_ignore_ascii_case(&format!("ROOT\\{id}"))
}

pub fn evaluate(family: DriverFamily, devices: &Evidence<DeviceEvidence>, packages: &Evidence<PackageEvidence>) -> DriverResult {
    let matches: Vec<&DeviceEvidence> = devices.items.iter().filter(|d| d.hardware_ids.iter().any(|id| matches_hardware_id(id, family))).collect();
    if !matches.is_empty() {
        let rank = |d: &DeviceEvidence| match d.state.as_str() {
            "DevicePresent" => 0,
            "DeviceDisabled" => 1,
            _ => 2,
        };
        let best = matches.iter().copied().min_by_key(|d| rank(d)).unwrap_or(matches[0]);
        let detail = match family {
            DriverFamily::Vac => "VAC driver family detected; edition not verified. Existing driver will be kept.",
            DriverFamily::VBCable => "VB-CABLE driver detected. Existing driver will be kept.",
        };
        return DriverResult { succeeded: true, installed: true, state: best.state.clone(), detail: detail.into(), version: best.version.clone() };
    }
    let staged: Vec<&PackageEvidence> = packages.items.iter().filter(|p| p.family == family).collect();
    if let Some(&first) = staged.first() {
        let best = staged.iter().find(|p| p.in_driver_store).copied().unwrap_or(first);
        return DriverResult {
            succeeded: best.in_driver_store,
            installed: true,
            state: if best.in_driver_store { "DriverStaged" } else { "PackageNeedsAttention" }.into(),
            detail: if best.in_driver_store {
                "Driver package already staged in Windows; repair or restart may be needed."
            } else {
                "Matching installed INF found but its driver store state could not be verified."
            }
            .into(),
            version: best.version.clone(),
        };
    }
    if !devices.succeeded || !packages.succeeded {
        return DriverResult {
            succeeded: false,
            installed: false,
            state: "DetectionFailed".into(),
            detail: "Windows driver state could not be completely checked. Automatic driver installation is disabled.".into(),
            version: String::new(),
        };
    }
    DriverResult { succeeded: true, installed: false, state: "Missing".into(), detail: "No matching device or staged driver package found.".into(), version: String::new() }
}

fn trim_separator(path: &str) -> String {
    let full = std::path::absolute(path).map(|p| p.to_string_lossy().into_owned()).unwrap_or_else(|_| path.to_owned());
    if full.len() > 3 { full.trim_end_matches('\\').to_owned() } else { full }
}

fn is_within(path: &str, directory: &str) -> bool {
    let prefix = trim_separator(directory) + "\\";
    trim_separator(path).to_lowercase().starts_with(&prefix.to_lowercase())
}

/// Same directory or a descendant, after final-path resolution.
pub fn is_same_or_within(directory: &str, parent: &str) -> bool {
    let normalized = trim_separator(directory).to_lowercase();
    let root = trim_separator(parent).to_lowercase();
    normalized == root || normalized.starts_with(&(root + "\\"))
}

pub fn evaluate_processes(processes: &[ProcessEvidence], mut succeeded: bool, install_directory: Option<&str>) -> ApplicationResult {
    let (mut running, mut installed) = (false, false);
    for process in processes.iter().filter(|p| p.still_running && p.same_user != Some(false)) {
        running = true;
        let (Some(_), Some(path)) = (process.same_user, process.path.as_deref()) else {
            succeeded = false;
            installed = true;
            continue;
        };
        if install_directory.is_some_and(|directory| is_within(path, directory)) {
            installed = true;
        }
    }
    if !succeeded {
        return ApplicationResult {
            succeeded: false,
            running: true,
            running_installed: true,
            detail: "A possible VineMix process could not be checked safely. Close VineMix before continuing.".into(),
            install_path_safe: true,
        };
    }
    ApplicationResult {
        succeeded: true,
        running,
        running_installed: installed,
        detail: if running {
            "VineMix or its audio engine is already running. The current session will be left untouched."
        } else {
            "No VineMix session is running for this Windows user."
        }
        .into(),
        install_path_safe: true,
    }
}

/// `\\?\C:\x` → `C:\x`; `\\?\UNC\s\x` → `\\s\x`.
pub fn normalize_final_path(value: &str) -> String {
    if value.len() >= 8 && value[..8].eq_ignore_ascii_case(r"\\?\UNC\") {
        format!(r"\\{}", &value[8..])
    } else if let Some(rest) = value.strip_prefix(r"\\?\") {
        rest.to_owned()
    } else {
        value.to_owned()
    }
}

/// The self-tests Setup runs before trusting the helper (`--self-test`).
pub fn self_tests() -> Result<usize, String> {
    let mut checks = 0usize;
    let mut require = |condition: bool, label: &str| -> Result<(), String> {
        checks += 1;
        if condition { Ok(()) } else { Err(format!("Self-test failed: {label}")) }
    };
    let inf_text = "[Version]\nClass = MEDIA\nProvider = %Vendor%\nDriverVer=10/07/2024,3.3.1.7\n[Manufacturer]\n%Vendor%=Cable,NTamd64.10.0...16299,NTarm64.10.0...16299\n[Cable.NTamd64.10.0...16299]\n%Device%=Install, %HardwareId%\n[Strings]\nVendor=\"VB-Audio Software\"\nHardwareId=\"VBAudioVACWDM\"\nDevice=\"Name may be changed; semicolon is quoted\"";
    let inf = InfIdentity::parse(inf_text);
    require(inf.identifies(DriverFamily::VBCable), "decorated Manufacturer and string substitution")?;
    require(!inf.identifies(DriverFamily::Vac), "families isolated")?;
    require(inf.version() == "3.3.1.7", "driver version extraction")?;
    require(!InfIdentity::parse(&inf_text.replace("%HardwareId%", "VBAudioVACWDM_EXTRA")).identifies(DriverFamily::VBCable), "similar hardware ID rejected")?;
    require(!InfIdentity::parse(&inf_text.replace("VB-Audio Software", "Unrelated Vendor")).identifies(DriverFamily::VBCable), "vendor impersonation in friendly name rejected")?;
    require(
        !InfIdentity::parse("[Version]\nClass=MEDIA\nProvider=VB-Audio Software\n; VBAudioVACWDM\n[Strings]\nDevice=VBAudioVACWDM").identifies(DriverFamily::VBCable),
        "comment or display name alone rejected",
    )?;
    let vac = inf_text.replace("VB-Audio Software", "Eugene Muzychenko").replace("VBAudioVACWDM", "VirtualAudioCable_83ed7f0e");
    require(InfIdentity::parse(&vac).identifies(DriverFamily::Vac), "VAC source identity")?;
    require(InfIdentity::parse(&inf_text.replace("%HardwareId%", "ROOT\\VBAudioVACWDM")).identifies(DriverFamily::VBCable), "root-enumerated exact hardware identity")?;
    let mut utf16 = vec![0xff, 0xfe];
    utf16.extend(inf_text.encode_utf16().flat_map(u16::to_le_bytes));
    require(read_inf_text(&utf16).contains("VB-Audio Software"), "UTF-16 original INF")?;
    let no_devices = Evidence { items: Vec::new(), succeeded: true };
    let no_packages = Evidence { items: Vec::new(), succeeded: true };
    let missing = evaluate(DriverFamily::VBCable, &no_devices, &no_packages);
    require(missing.succeeded && !missing.installed && missing.state == "Missing", "only complete negative evidence permits install")?;
    for state in ["DevicePresent", "DeviceDisabled", "DeviceUnavailable", "DeviceNeedsAttention"] {
        let devices = Evidence { items: vec![DeviceEvidence { hardware_ids: vec!["VBAudioVACWDM".into()], version: "1".into(), state: state.into() }], succeeded: true };
        let result = evaluate(DriverFamily::VBCable, &devices, &no_packages);
        require(result.installed && result.succeeded, &format!("{state} blocks duplicate install"))?;
    }
    let staged = Evidence { items: vec![PackageEvidence { family: DriverFamily::Vac, version: "4.71".into(), in_driver_store: true }], succeeded: true };
    let result = evaluate(DriverFamily::Vac, &no_devices, &staged);
    require(result.installed && result.state == "DriverStaged", "staged-only package blocks duplicate install")?;
    let questionable = Evidence { items: vec![PackageEvidence { family: DriverFamily::Vac, version: String::new(), in_driver_store: false }], succeeded: true };
    let result = evaluate(DriverFamily::Vac, &no_devices, &questionable);
    require(result.installed && !result.succeeded, "broken package requires attention rather than reinstall")?;
    require(!evaluate(DriverFamily::Vac, &Evidence { items: Vec::new(), succeeded: false }, &no_packages).succeeded, "partial Windows enumeration never means absent")?;
    let target = r"C:\Users\Example\AppData\Local\Programs\VineMix";
    let process = |same: Option<bool>, path: Option<&str>, running: bool| ProcessEvidence { same_user: same, path: path.map(str::to_owned), still_running: running };
    let r = evaluate_processes(&[process(Some(true), Some(&format!("{target}\\VineMix.exe")), true)], true, Some(target));
    require(r.running && r.running_installed, "current desktop target blocks overwrite")?;
    let r = evaluate_processes(&[process(Some(true), Some(r"E:\Other\VineMix.Engine.exe"), true)], true, Some(target));
    require(r.running && !r.running_installed, "current engine in another folder blocks a second launch")?;
    let r = evaluate_processes(&[process(Some(true), Some(&format!("{target}\\AudioMixer.exe")), true)], true, Some(target));
    require(r.running && r.running_installed, "running target blocks overwrite")?;
    let r = evaluate_processes(&[process(Some(true), Some(r"E:\Other\AudioMixer.Engine.exe"), true)], true, Some(target));
    require(r.running && !r.running_installed, "other folder session blocks second launch only")?;
    require(!evaluate_processes(&[process(Some(false), Some(&format!("{target}\\AudioMixer.exe")), true)], true, Some(target)).running, "other user is not current user session")?;
    let r = evaluate_processes(&[process(None, None, true)], true, Some(target));
    require(!r.succeeded && r.running && r.running_installed, "unreadable process conservatively blocks overwrite and launch")?;
    require(
        !evaluate_processes(&[process(Some(true), Some(&format!("{target}2\\AudioMixer.exe")), true)], true, Some(target)).running_installed,
        "directory boundary avoids sibling prefix collision",
    )?;
    require(!evaluate_processes(&[process(Some(true), None, false)], true, Some(target)).running, "exited processes ignored")?;
    require(!crate::sanitize("value\r\nInjected=1\u{e9}").contains('\n'), "INI injection prevented")?;
    require(normalize_final_path(r"\\?\C:\Programs\VineMix") == r"C:\Programs\VineMix", "DOS final path normalization")?;
    require(normalize_final_path(r"\\?\UNC\server\share\VineMix") == r"\\server\share\VineMix", "UNC final path normalization")?;
    require(is_same_or_within(r"C:\Data\AudioMixer\Nested", r"C:\Data\AudioMixer"), "protected data descendant blocked after final-path resolution")?;
    require(is_same_or_within(r"C:\Data\AudioMixer", r"C:\Data\AudioMixer"), "protected data directory blocked after final-path resolution")?;
    require(!is_same_or_within(r"C:\Data\AudioMixer2", r"C:\Data\AudioMixer"), "protected data sibling prefix remains distinct")?;
    Ok(checks)
}

#[cfg(test)]
mod tests {
    #[test]
    fn self_tests_pass() {
        assert_eq!(super::self_tests().unwrap(), 31);
    }
}
