//! Release discovery, signed-manifest trust and verified downloads
//! (`VineMix.UpdateCore`).
//!
//! A release is offered only when its `VineMix-<version>-Updates.json`
//! envelope verifies with a pinned RSA-PSS-SHA256 key, names exactly the
//! published assets (origin, size, SHA-256) and matches this build's
//! settings schema, protocol and Windows requirements.

pub mod crypto;
pub mod http;
pub mod json;
pub mod version;
#[cfg(feature = "testing")]
pub mod testing;

use std::path::{Path, PathBuf};
use std::time::Duration;

use serde_json::Value;

pub use version::SemanticVersion;

pub const MAXIMUM_ENVELOPE_BYTES: u64 = 256 * 1024;
pub const MAXIMUM_PAYLOAD_BYTES: usize = 128 * 1024;
pub const MAXIMUM_ASSET_BYTES: u64 = 1024 * 1024 * 1024;
pub const REPOSITORY: &str = "acoyeen/vine-mix";
pub const ALGORITHM: &str = "RSA-PSS-SHA256";

/// Why an update step failed.
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum Failure {
    Unavailable(String),
    RateLimited(String),
    Invalid(String),
    Incompatible(String),
    Cancelled,
}

impl Failure {
    pub fn message(&self) -> String {
        match self {
            Self::Unavailable(m) | Self::RateLimited(m) | Self::Invalid(m) | Self::Incompatible(m) => m.clone(),
            Self::Cancelled => "Update check cancelled.".into(),
        }
    }
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Asset {
    pub file_name: String,
    pub url: String,
    pub size: u64,
    pub sha256: String,
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Manifest {
    pub version: String,
    pub numeric_version: String,
    pub minimum_windows_build: u32,
    pub settings_schema: i64,
    pub protocol_version: i64,
    pub notes: String,
    pub installer: Asset,
    pub standalone: Asset,
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Offer {
    pub manifest: Manifest,
    pub key_id: String,
    pub envelope: Vec<u8>,
    pub envelope_sha256: String,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct Compatibility {
    pub windows_build: u32,
    pub x64: bool,
    pub settings_schema: i64,
    pub protocol_version: i64,
}

impl Compatibility {
    pub fn current() -> Self {
        Self { windows_build: windows_build(), x64: cfg!(target_arch = "x86_64"), settings_schema: 2, protocol_version: 1 }
    }
}

/// The running Windows build number.
pub fn windows_build() -> u32 {
    use windows::Win32::System::SystemInformation::OSVERSIONINFOW;
    #[link(name = "ntdll")]
    unsafe extern "system" {
        fn RtlGetVersion(info: *mut OSVERSIONINFOW) -> i32;
    }
    let mut info = OSVERSIONINFOW { dwOSVersionInfoSize: std::mem::size_of::<OSVERSIONINFOW>() as u32, ..Default::default() };
    // SAFETY: valid, sized structure.
    if unsafe { RtlGetVersion(&mut info) } == 0 { info.dwBuildNumber } else { 0 }
}

pub fn asset_url(version: &str, file_name: &str) -> String {
    format!("https://github.com/{REPOSITORY}/releases/download/v{version}/{file_name}")
}

pub fn manifest_file_name(version: &str) -> String {
    format!("VineMix-{version}-Updates.json")
}

fn valid_key_id(id: &str) -> bool {
    !id.is_empty() && id.len() <= 80 && id.bytes().all(|b| b.is_ascii_alphanumeric() || b == b'-' || b == b'_')
}

/// Pinned public keys (`release-trust.json`).
#[derive(Clone, Debug)]
pub struct Trust {
    keys: Vec<(String, Vec<u8>, Vec<u8>)>,
}

impl Trust {
    pub fn production() -> Result<Self, String> {
        Self::from_json(include_bytes!("../../../updates/release-trust.json"))
    }

    /// A trust store with one raw RSA key (tests and fixtures).
    pub fn from_key(key_id: &str, modulus: Vec<u8>, exponent: Vec<u8>) -> Self {
        Self { keys: vec![(key_id.to_owned(), modulus, exponent)] }
    }

    pub fn from_json(bytes: &[u8]) -> Result<Self, String> {
        let root: Value = serde_json::from_slice(bytes).map_err(|_| "The update trust resource is invalid.")?;
        if root["formatVersion"].as_i64() != Some(1) {
            return Err("Unknown update trust format.".into());
        }
        let mut keys = Vec::new();
        for key in root["keys"].as_array().ok_or("The update trust resource is invalid.")? {
            if key["algorithm"].as_str() != Some(ALGORITHM) {
                return Err("Unknown update trust algorithm.".into());
            }
            let id = key["keyId"].as_str().ok_or("The update trust resource is invalid.")?;
            let pem = key["publicKeyPem"].as_str().ok_or("The update trust resource is invalid.")?;
            if !valid_key_id(id) {
                return Err("Update trust needs named public keys.".into());
            }
            let (modulus, exponent) = crypto::rsa_public_key(pem)?;
            let bits = crypto::modulus_bits(&modulus);
            if !(3072..=8192).contains(&bits) {
                return Err("Update trust requires RSA keys between 3072 and 8192 bits.".into());
            }
            keys.push((id.to_owned(), modulus, exponent));
        }
        if keys.is_empty() {
            return Err("Update trust needs named public keys.".into());
        }
        Ok(Self { keys })
    }

    /// Verifies a signed envelope for `expected_version` (`UpdateTrust.Verify`).
    pub fn verify(&self, envelope: &[u8], expected_version: &str, compatibility: Compatibility) -> Result<Offer, Failure> {
        let invalid = |m: &str| Failure::Invalid(m.to_owned());
        if envelope.is_empty() || envelope.len() as u64 > MAXIMUM_ENVELOPE_BYTES {
            return Err(invalid("The signed update information is too large or empty."));
        }
        let malformed = || invalid("The signed update information is malformed or could not be verified.");
        let root = json::parse(envelope, 12).map_err(|_| malformed())?;
        let object = json::exact(&root, &["formatVersion", "keyId", "algorithm", "payload", "signature"], &[]).map_err(|_| malformed())?;
        let key_id = object["keyId"].as_str().ok_or_else(malformed)?;
        let key = self.keys.iter().find(|(id, _, _)| id == key_id);
        if object["formatVersion"].as_i64() != Some(1) || object["algorithm"].as_str() != Some(ALGORITHM) || !valid_key_id(key_id) || key.is_none() {
            return Err(invalid("The update signature uses an unknown key or format."));
        }
        let (_, modulus, exponent) = key.ok_or_else(malformed)?;
        let payload_text = object["payload"].as_str().ok_or_else(malformed)?;
        let signature_text = object["signature"].as_str().ok_or_else(malformed)?;
        if payload_text.is_empty() || payload_text.len() > (MAXIMUM_PAYLOAD_BYTES + 2) / 3 * 4 || signature_text.is_empty() || signature_text.len() > 1400 {
            return Err(invalid("The signed update payload is invalid."));
        }
        let payload = crypto::base64_decode(payload_text).ok_or_else(malformed)?;
        let signature = crypto::base64_decode(signature_text).ok_or_else(malformed)?;
        if payload.is_empty() || payload.len() > MAXIMUM_PAYLOAD_BYTES {
            return Err(invalid("The signed update payload is too large or empty."));
        }
        if !crypto::verify_rsa_pss(modulus, exponent, &payload, &signature).map_err(|_| malformed())? {
            return Err(invalid("The update signature could not be verified."));
        }
        let manifest = json::parse(&payload, 12).map_err(|_| malformed())?;
        let manifest = parse_manifest(&manifest).map_err(|_| malformed())?;
        validate_manifest(&manifest, expected_version, compatibility)?;
        Ok(Offer {
            manifest,
            key_id: key_id.to_owned(),
            envelope: envelope.to_vec(),
            envelope_sha256: crypto::hex(&crypto::sha256(envelope).map_err(|_| malformed())?),
        })
    }
}

fn parse_asset(value: &Value) -> Result<Asset, String> {
    let object = json::exact(value, &["fileName", "url", "size", "sha256"], &[])?;
    Ok(Asset {
        file_name: object["fileName"].as_str().ok_or("fileName")?.to_owned(),
        url: object["url"].as_str().ok_or("url")?.to_owned(),
        size: object["size"].as_u64().ok_or("size")?,
        sha256: object["sha256"].as_str().ok_or("sha256")?.to_owned(),
    })
}

fn parse_manifest(value: &Value) -> Result<Manifest, String> {
    let fields = [
        "formatVersion", "product", "version", "numericVersion", "architecture", "minimumWindowsBuild", "settingsSchema", "protocolVersion", "notes",
        "installer", "standalone",
    ];
    let object = json::exact(value, &fields, &[])?;
    if object["formatVersion"].as_i64() != Some(1) || object["product"].as_str() != Some("VineMix") || object["architecture"].as_str() != Some("x64") {
        return Err("identity".into());
    }
    Ok(Manifest {
        version: object["version"].as_str().ok_or("version")?.to_owned(),
        numeric_version: object["numericVersion"].as_str().ok_or("numericVersion")?.to_owned(),
        minimum_windows_build: u32::try_from(object["minimumWindowsBuild"].as_i64().ok_or("build")?).map_err(|_| "build")?,
        settings_schema: object["settingsSchema"].as_i64().ok_or("schema")?,
        protocol_version: object["protocolVersion"].as_i64().ok_or("protocol")?,
        notes: object["notes"].as_str().ok_or("notes")?.to_owned(),
        installer: parse_asset(&object["installer"])?,
        standalone: parse_asset(&object["standalone"])?,
    })
}

fn validate_asset(asset: &Asset, version: &str, kind: &str) -> Result<(), Failure> {
    let expected = format!("VineMix-{version}-{kind}-x64.exe");
    if asset.file_name != expected
        || asset.url != asset_url(version, &expected)
        || asset.size == 0
        || asset.size > MAXIMUM_ASSET_BYTES
        || asset.sha256.len() != 64
        || !asset.sha256.bytes().all(|b| b.is_ascii_hexdigit())
    {
        return Err(Failure::Invalid("The update asset name, origin, size or digest is invalid.".into()));
    }
    Ok(())
}

fn validate_manifest(manifest: &Manifest, expected: &str, compatibility: Compatibility) -> Result<(), Failure> {
    let Some(version) = SemanticVersion::parse(&manifest.version) else {
        return Err(Failure::Invalid("The update manifest identity, version or format is invalid.".into()));
    };
    if manifest.version != expected || manifest.notes.len() > 32768 || manifest.minimum_windows_build < 22000 {
        return Err(Failure::Invalid("The update manifest identity, version or format is invalid.".into()));
    }
    let numbers: Option<Vec<u32>> = manifest
        .numeric_version
        .split('.')
        .map(|p| if p.is_empty() || (p.len() > 1 && p.starts_with('0')) { None } else { p.parse().ok() })
        .collect();
    let valid = match numbers {
        Some(n) if n.len() == 4 => n.iter().all(|&x| x <= 65535) && n[0] == version.major && n[1] == version.minor && n[2] == version.patch,
        _ => false,
    };
    if !valid {
        return Err(Failure::Invalid("The update's numeric version does not match its release.".into()));
    }
    validate_asset(&manifest.installer, &manifest.version, "Setup")?;
    validate_asset(&manifest.standalone, &manifest.version, "Standalone")?;
    if manifest.settings_schema != compatibility.settings_schema
        || manifest.protocol_version != compatibility.protocol_version
        || manifest.minimum_windows_build > compatibility.windows_build
        || !compatibility.x64
    {
        return Err(Failure::Incompatible("This release needs a different Windows, settings or engine protocol version.".into()));
    }
    Ok(())
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub enum CheckResult {
    Available(Box<Offer>),
    UpToDate(String),
}

fn http_failure(response: &http::Response) -> Failure {
    if response.status == 404 {
        return Failure::Unavailable("Public update releases are not available yet, or the repository is private.".into());
    }
    if response.status == 429 || (response.status == 403 && (response.retry_after || response.rate_limit_exhausted)) {
        return Failure::RateLimited("GitHub's update request limit was reached. Try again later.".into());
    }
    Failure::Unavailable(format!("The update service returned HTTP {}. Try again later.", response.status))
}

fn published_asset_matches(assets: &[Value], expected: &Asset) -> bool {
    let matching: Vec<&Value> = assets.iter().filter(|a| a["name"].as_str() == Some(expected.file_name.as_str())).collect();
    matching.len() == 1
        && matching[0]["browser_download_url"].as_str() == Some(expected.url.as_str())
        && matching[0]["size"].as_u64() == Some(expected.size)
        && matching[0]["state"].as_str() == Some("uploaded")
}

/// Checks GitHub for a newer signed release (`GitHubUpdateSource.CheckAsync`).
pub fn check(trust: &Trust, current: &str, include_prereleases: bool, compatibility: Compatibility, cancelled: &dyn Fn() -> bool) -> Result<CheckResult, Failure> {
    let current = SemanticVersion::parse(current).ok_or_else(|| Failure::Unavailable("The installed version could not be compared with published releases.".into()))?;
    let timeout = Duration::from_secs(45);
    let malformed = || Failure::Invalid("The release service returned malformed update information.".into());
    let mut releases: Vec<(SemanticVersion, Vec<Value>)> = Vec::new();
    for page in 1..=4 {
        if cancelled() {
            return Err(Failure::Cancelled);
        }
        let response = http::get(&format!("https://api.github.com/repos/{REPOSITORY}/releases?per_page=100&page={page}"), false, timeout, cancelled)?;
        if !response.success() {
            return Err(http_failure(&response));
        }
        let body = response.read_bounded(2 * 1024 * 1024, cancelled)?;
        let document = json::parse(&body, 32).map_err(|_| malformed())?;
        let items = document.as_array().filter(|a| a.len() <= 100).ok_or_else(|| Failure::Invalid("The release service returned invalid release information.".into()))?;
        for item in items {
            if item["draft"].as_bool().ok_or_else(malformed)? || item["published_at"].is_null() {
                continue;
            }
            let Some(tag) = item["tag_name"].as_str() else { continue };
            let Some(version) = tag.strip_prefix('v').and_then(SemanticVersion::parse) else { continue };
            let prerelease = item["prerelease"].as_bool().ok_or_else(malformed)?;
            if prerelease != version.is_prerelease() || (!include_prereleases && prerelease) {
                continue;
            }
            let assets = item["assets"].as_array().filter(|a| a.len() <= 200).ok_or_else(malformed)?.clone();
            releases.push((version, assets));
        }
        if items.len() < 100 {
            break;
        }
        if page == 4 {
            return Err(Failure::Unavailable("The release history is larger than this updater can safely inspect. Check the releases page.".into()));
        }
    }
    if releases.is_empty() {
        return Err(Failure::Unavailable("No published releases are available on this update channel yet.".into()));
    }
    let mut newer: Vec<&(SemanticVersion, Vec<Value>)> = releases.iter().filter(|(v, _)| *v > current).collect();
    newer.sort_by(|a, b| b.0.cmp(&a.0));
    if newer.is_empty() {
        return Ok(CheckResult::UpToDate("This version is up to date on the selected release channel.".into()));
    }
    let mut rejected: Option<Failure> = None;
    for (version, assets) in newer {
        if cancelled() {
            return Err(Failure::Cancelled);
        }
        let version_text = version.to_string();
        let manifest_name = manifest_file_name(&version_text);
        let result = (|| -> Result<Offer, Failure> {
            let manifests: Vec<&Value> = assets.iter().filter(|a| a["name"].as_str() == Some(manifest_name.as_str())).collect();
            if manifests.len() != 1 {
                return Err(Failure::Invalid("A newer release does not contain exactly one signed update manifest.".into()));
            }
            let expected_url = asset_url(&version_text, &manifest_name);
            let size = manifests[0]["size"].as_u64().unwrap_or(0);
            if size == 0
                || size > MAXIMUM_ENVELOPE_BYTES
                || manifests[0]["browser_download_url"].as_str() != Some(expected_url.as_str())
                || manifests[0]["state"].as_str() != Some("uploaded")
            {
                return Err(Failure::Invalid("The published update manifest has an invalid origin, state or size.".into()));
            }
            let response = http::get(&expected_url, true, timeout, cancelled)?;
            if !response.success() {
                return Err(http_failure(&response));
            }
            let bytes = response.read_bounded(MAXIMUM_ENVELOPE_BYTES, cancelled)?;
            if bytes.len() as u64 != size {
                return Err(Failure::Invalid("The signed update manifest does not match the published asset length.".into()));
            }
            let offer = trust.verify(&bytes, &version_text, compatibility)?;
            if !published_asset_matches(assets, &offer.manifest.installer) || !published_asset_matches(assets, &offer.manifest.standalone) {
                return Err(Failure::Invalid("The signed update does not match the assets in its published release.".into()));
            }
            Ok(offer)
        })();
        match result {
            Ok(offer) => return Ok(CheckResult::Available(Box::new(offer))),
            Err(failure @ (Failure::Invalid(_) | Failure::Incompatible(_))) => {
                if rejected.is_none() {
                    rejected = Some(failure);
                }
            }
            Err(failure) => return Err(failure),
        }
    }
    Err(rejected.unwrap_or_else(|| Failure::Unavailable("No compatible signed update is available.".into())))
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum AssetKind {
    Installer,
    Standalone,
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Downloaded {
    pub file: PathBuf,
    pub staging: PathBuf,
    pub asset: Asset,
}

/// Downloads and verifies an asset into a fresh staging directory
/// (`UpdateDownloader.DownloadAsync`).
pub fn download(offer: &Offer, kind: AssetKind, staging_parent: &Path, progress: &dyn Fn(u64, u64), cancelled: &dyn Fn() -> bool) -> Result<Downloaded, Failure> {
    use std::io::Write;
    let asset = match kind {
        AssetKind::Installer => offer.manifest.installer.clone(),
        AssetKind::Standalone => offer.manifest.standalone.clone(),
    };
    let io = |e: std::io::Error| Failure::Unavailable(format!("The update could not be saved. {e}"));
    std::fs::create_dir_all(staging_parent).map_err(io)?;
    if std::fs::symlink_metadata(staging_parent).map_err(io)?.file_type().is_symlink() {
        return Err(Failure::Invalid("The update staging directory cannot be a link.".into()));
    }
    let nonce = crypto::sha256(format!("{:?}{}", std::time::SystemTime::now(), std::process::id()).as_bytes()).map_err(Failure::Unavailable)?;
    let staging = staging_parent.join(format!("vinemix-update-{}", crypto::hex(&nonce[..16])));
    std::fs::create_dir(&staging).map_err(io)?;
    let partial = staging.join(format!("{}.partial", asset.file_name));
    let complete = staging.join(&asset.file_name);
    let result = (|| -> Result<(), Failure> {
        let response = http::get(&asset.url, true, Duration::from_secs(600), cancelled)?;
        if !response.success() {
            return Err(Failure::Unavailable(format!("The update download returned HTTP {}.", response.status)));
        }
        if response.content_length.is_some_and(|l| l != asset.size) {
            return Err(Failure::Invalid("The download length does not match its signed manifest.".into()));
        }
        let mut file = std::fs::OpenOptions::new().write(true).create_new(true).open(&partial).map_err(io)?;
        let mut hash = crypto::Sha256::new().map_err(Failure::Unavailable)?;
        let mut written = 0u64;
        response.read_with(asset.size.min(MAXIMUM_ASSET_BYTES), cancelled, |chunk| {
            if cancelled() {
                return Err(Failure::Cancelled);
            }
            hash.update(chunk).map_err(Failure::Unavailable)?;
            file.write_all(chunk).map_err(io)?;
            written += chunk.len() as u64;
            progress(written, asset.size);
            Ok(())
        })?;
        let digest = hash.finish().map_err(Failure::Unavailable)?;
        let expected = crypto::from_hex(&asset.sha256).ok_or_else(|| Failure::Invalid("The update digest is invalid.".into()))?;
        if written != asset.size || digest.as_slice() != expected.as_slice() {
            return Err(Failure::Invalid("The download was interrupted or its SHA-256 digest did not match.".into()));
        }
        file.flush().map_err(io)?;
        file.sync_all().map_err(io)?;
        drop(file);
        std::fs::rename(&partial, &complete).map_err(io)?;
        Ok(())
    })();
    if let Err(error) = result {
        let _ = std::fs::remove_dir_all(&staging);
        return Err(error);
    }
    Ok(Downloaded { file: complete, staging, asset })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn production_trust_loads_and_rejects_forgeries() {
        let trust = Trust::production().unwrap();
        let envelope = br#"{"formatVersion":1,"keyId":"vinemix-updates-2026-01","algorithm":"RSA-PSS-SHA256","payload":"e30=","signature":"AAAA"}"#;
        let error = trust.verify(envelope, "0.6.0-beta", Compatibility::current()).unwrap_err();
        assert_eq!(error, Failure::Invalid("The update signature could not be verified.".into()));
        let duplicate = br#"{"formatVersion":1,"formatVersion":1,"keyId":"x","algorithm":"RSA-PSS-SHA256","payload":"e30=","signature":"AAAA"}"#;
        assert!(trust.verify(duplicate, "0.6.0-beta", Compatibility::current()).is_err());
        assert_eq!(manifest_file_name("0.5.0-beta"), "VineMix-0.5.0-beta-Updates.json");
    }
}
