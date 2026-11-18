//! Embeds the Windows version resource, manifest and application icon, and
//! the build's source identity shown under About.

include!("../../tools/winres.rs");
include!("../../tools/source_identity.rs");

fn main() {
    winres::embed("VineMix", "VineMix", "VineMix.exe", Some("assets/branding/app.ico"), true);
    println!("cargo::rerun-if-env-changed=VINEMIX_RUNTIME_ZIP");
    println!("cargo::rerun-if-env-changed=VINEMIX_RUNTIME_MANIFEST");
    source_revision();
    compiler_version();
}

/// The checkout's commit, as the .NET SDK appends `+<revision>` to the
/// informational version.
///
/// `scripts/build-rust-release.ps1` verifies the source tree and passes
/// `VINEMIX_SOURCE_REVISION` (with the snapshot digest and build time); those
/// values win and are used unchanged. Any other build reports what Git said
/// when this script last ran and labels itself `local`, because nothing
/// checked the tree against the compiled inputs afterwards.
fn source_revision() {
    // Rebuild when the release script supplies or changes any of these.
    for name in ["VINEMIX_SOURCE_REVISION", "VINEMIX_SOURCE_SNAPSHOT_SHA256", "VINEMIX_BUILD_TIMESTAMP"] {
        println!("cargo::rerun-if-env-changed={name}");
    }
    if std::env::var_os("VINEMIX_SOURCE_REVISION").is_some() {
        return;
    }
    let root = std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join("../..");
    let Some(identity) = source_identity::read(&root) else {
        return; // Not a Git checkout: About says "unavailable".
    };
    for path in &identity.watch {
        println!("cargo::rerun-if-changed={}", path.display());
    }
    println!("cargo::rustc-env=VINEMIX_SOURCE_REVISION={}", source_identity::local_label(&identity));
}

/// The compiler that built this binary, for the About details.
fn compiler_version() {
    let rustc = std::env::var_os("RUSTC").unwrap_or_else(|| "rustc".into());
    if let Ok(output) = std::process::Command::new(rustc).arg("--version").output()
        && output.status.success()
    {
        let version = String::from_utf8_lossy(&output.stdout);
        let version = version.trim().strip_prefix("rustc ").unwrap_or(version.trim());
        if !version.is_empty() && !version.contains('\n') {
            println!("cargo::rustc-env=VINEMIX_RUSTC_VERSION={version}");
        }
    }
}
