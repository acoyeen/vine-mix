//! Embeds the Windows version resource, manifest and application icon.

include!("../../tools/winres.rs");

fn main() {
    winres::embed("VineMix", "VineMix", "VineMix.exe", Some("assets/branding/app.ico"), true);
    println!("cargo::rerun-if-env-changed=VINEMIX_RUNTIME_ZIP");
    println!("cargo::rerun-if-env-changed=VINEMIX_RUNTIME_MANIFEST");
    source_revision();
}

/// The checkout's commit, as the .NET SDK appends `+<revision>` to the
/// informational version. An explicit `VINEMIX_SOURCE_REVISION` wins.
fn source_revision() {
    println!("cargo::rerun-if-env-changed=VINEMIX_SOURCE_REVISION");
    if std::env::var_os("VINEMIX_SOURCE_REVISION").is_some() {
        return;
    }
    let root = std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join("../..");
    let git = root.join(".git");
    println!("cargo::rerun-if-changed={}", git.join("HEAD").display());
    if let Ok(head) = std::fs::read_to_string(git.join("HEAD"))
        && let Some(reference) = head.trim().strip_prefix("ref: ")
    {
        println!("cargo::rerun-if-changed={}", git.join(reference).display());
    }
    let output = std::process::Command::new("git").arg("-C").arg(&root).args(["rev-parse", "HEAD"]).output();
    if let Ok(output) = output {
        let revision = String::from_utf8_lossy(&output.stdout).trim().to_owned();
        if output.status.success() && revision.len() == 40 && revision.bytes().all(|b| b.is_ascii_hexdigit()) {
            println!("cargo::rustc-env=VINEMIX_SOURCE_REVISION={revision}");
        }
    }
}
