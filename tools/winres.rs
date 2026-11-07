// Shared build-script helper (`include!`d by build.rs files): compiles a
// Windows resource with the version information the update worker and
// release checks read, plus the application manifest and icon.
//
// FileVersion is the four-part numeric version (released update workers
// parse that string); ProductVersion is the semantic release version.

#[allow(dead_code)]
mod winres {
    use std::path::{Path, PathBuf};

    fn field(text: &str, key: &str) -> String {
        let pattern = format!("\"{key}\"");
        let start = text.find(&pattern).unwrap_or_else(|| panic!("release.json lacks {key}"));
        let rest = &text[start + pattern.len()..];
        let open = rest.find('"').expect("string value") + 1;
        let close = rest[open..].find('"').expect("closing quote") + open;
        rest[open..close].to_owned()
    }

    fn windres(workspace: &Path) -> PathBuf {
        if let Some(explicit) = std::env::var_os("VINEMIX_WINDRES") {
            return PathBuf::from(explicit);
        }
        if let Ok(entries) = std::fs::read_dir(workspace.join(".tools")) {
            for entry in entries.flatten() {
                let candidate = entry.path().join("bin").join("llvm-windres.exe");
                if entry.file_name().to_string_lossy().starts_with("llvm-mingw") && candidate.is_file() {
                    return candidate;
                }
            }
        }
        PathBuf::from("llvm-windres")
    }

    fn escape(text: &str) -> String {
        text.replace('\\', "\\\\").replace('"', "\"\"")
    }

    fn resource_path(path: &Path) -> String {
        path.to_string_lossy().replace('\\', "/")
    }

    /// Embeds version information, and optionally an icon and the manifest.
    pub fn embed(description: &str, internal_name: &str, original_filename: &str, icon: Option<&str>, manifest: bool) {
        if std::env::var("CARGO_CFG_TARGET_OS").as_deref() != Ok("windows") {
            return;
        }
        let crate_dir = PathBuf::from(std::env::var("CARGO_MANIFEST_DIR").unwrap());
        let workspace = crate_dir.join("../..");
        let release = workspace.join("release.json");
        println!("cargo::rerun-if-changed={}", release.display());
        let text = std::fs::read_to_string(&release).expect("release.json");
        let text = text.trim_start_matches('\u{feff}');
        let version = field(text, "version");
        let numeric = field(text, "numericVersion");
        let parts: Vec<u16> = numeric.split('.').map(|p| p.parse().expect("numeric version")).collect();
        assert_eq!(parts.len(), 4, "numericVersion needs four parts");
        let comma = format!("{},{},{},{}", parts[0], parts[1], parts[2], parts[3]);
        let flags = if version.contains('-') { "0x2" } else { "0x0" };
        let mut rc = String::new();
        if let Some(icon) = icon {
            let path = crate_dir.join(icon);
            println!("cargo::rerun-if-changed={}", path.display());
            rc += &format!("1 ICON \"{}\"\n", resource_path(&path));
        }
        if manifest {
            let path = workspace.join("tools").join("VineMix.manifest");
            println!("cargo::rerun-if-changed={}", path.display());
            rc += &format!("1 24 \"{}\"\n", resource_path(&path));
        }
        rc += &format!(
            "1 VERSIONINFO\nFILEVERSION {comma}\nPRODUCTVERSION {comma}\nFILEFLAGSMASK 0x3f\nFILEFLAGS {flags}\nFILEOS 0x40004\nFILETYPE 0x1\nFILESUBTYPE 0x0\nBEGIN\n BLOCK \"StringFileInfo\"\n BEGIN\n  BLOCK \"040904b0\"\n  BEGIN\n   VALUE \"CompanyName\", \"VineMix\\0\"\n   VALUE \"FileDescription\", \"{}\\0\"\n   VALUE \"FileVersion\", \"{numeric}\\0\"\n   VALUE \"InternalName\", \"{}\\0\"\n   VALUE \"LegalCopyright\", \"Copyright (c) 2026 acoyeen\\0\"\n   VALUE \"OriginalFilename\", \"{}\\0\"\n   VALUE \"ProductName\", \"VineMix\\0\"\n   VALUE \"ProductVersion\", \"{}\\0\"\n  END\n END\n BLOCK \"VarFileInfo\"\n BEGIN\n  VALUE \"Translation\", 0x409, 1200\n END\nEND\n",
            escape(description),
            escape(internal_name),
            escape(original_filename),
            escape(&version)
        );
        let out = PathBuf::from(std::env::var("OUT_DIR").unwrap());
        let source = out.join("vinemix.rc");
        let object = out.join("vinemix-resources.o");
        std::fs::write(&source, rc).expect("resource script");
        let tool = windres(&workspace);
        let status = std::process::Command::new(&tool)
            .arg("-i")
            .arg(&source)
            .arg("-o")
            .arg(&object)
            .arg("-O")
            .arg("coff")
            .status()
            .unwrap_or_else(|e| panic!("could not run {}: {e}", tool.display()));
        assert!(status.success(), "windres failed");
        println!("cargo::rustc-link-arg-bins={}", object.display());
    }
}
