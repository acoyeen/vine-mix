//! The build scripts' shared helpers, exercised against disposable fixtures.
//!
//! Source identity (RUST-REV-023): clean and dirty trees, a linked worktree
//! (where `.git` is a file) and packed refs (where the branch has no file of
//! its own). Resource compiler selection (RUST-REV-024): only the pinned
//! LLVM-MinGW directory, whatever else sits beside it.

include!("../../../tools/source_identity.rs");
include!("../../../tools/winres.rs");

use std::path::{Path, PathBuf};
use std::process::Command;

struct Scratch(PathBuf);

impl Scratch {
    fn new(name: &str) -> Self {
        static NEXT: std::sync::atomic::AtomicU32 = std::sync::atomic::AtomicU32::new(0);
        let unique = NEXT.fetch_add(1, std::sync::atomic::Ordering::Relaxed);
        let path = std::env::temp_dir().join(format!("vinemix-source-identity-{}-{unique}-{name}", std::process::id()));
        let _ = std::fs::remove_dir_all(&path);
        std::fs::create_dir_all(&path).unwrap();
        Self(path)
    }
}

impl Drop for Scratch {
    fn drop(&mut self) {
        // Git marks its object files read-only; clear that so removal works.
        fn unlock(path: &Path) {
            let Ok(entries) = std::fs::read_dir(path) else { return };
            for entry in entries.flatten() {
                let path = entry.path();
                if path.is_dir() {
                    unlock(&path);
                } else if let Ok(metadata) = entry.metadata() {
                    let mut permissions = metadata.permissions();
                    #[allow(clippy::permissions_set_readonly_false)]
                    permissions.set_readonly(false);
                    let _ = std::fs::set_permissions(&path, permissions);
                }
            }
        }
        unlock(&self.0);
        let _ = std::fs::remove_dir_all(&self.0);
    }
}

fn git(directory: &Path, arguments: &[&str]) -> String {
    let output = Command::new("git")
        .arg("-C")
        .arg(directory)
        .args(["-c", "user.name=Example", "-c", "user.email=example@example.invalid", "-c", "commit.gpgsign=false", "-c", "core.autocrlf=false"])
        .args(arguments)
        .output()
        .expect("git is required for these tests");
    assert!(output.status.success(), "git {arguments:?} failed: {}", String::from_utf8_lossy(&output.stderr));
    String::from_utf8_lossy(&output.stdout).trim().to_owned()
}

/// A repository with one commit: a tracked file and an ignore rule.
fn repository(scratch: &Scratch) -> PathBuf {
    let root = scratch.0.join("checkout");
    std::fs::create_dir_all(&root).unwrap();
    git(&root, &["init", "--quiet", "--initial-branch=main"]);
    std::fs::write(root.join("tracked.txt"), "one\n").unwrap();
    std::fs::write(root.join(".gitignore"), "ignored/\n").unwrap();
    git(&root, &["add", "."]);
    git(&root, &["commit", "--quiet", "-m", "first"]);
    root
}

fn contents(paths: &[PathBuf]) -> Vec<(PathBuf, Vec<u8>)> {
    paths.iter().map(|p| (p.clone(), std::fs::read(p).unwrap_or_default())).collect()
}

#[test]
fn clean_and_dirty_trees_are_distinguished() {
    let scratch = Scratch::new("dirty");
    let root = repository(&scratch);
    let head = git(&root, &["rev-parse", "HEAD"]);

    let clean = source_identity::read(&root).expect("a checkout has an identity");
    assert_eq!(clean.revision, head);
    assert_eq!(clean.dirty, Some(false));
    assert_eq!(source_identity::local_label(&clean), format!("{head}-local"));
    assert!(!clean.watch.is_empty() && clean.watch.iter().all(|p| p.is_file()), "only existing files are watched: {:?}", clean.watch);

    // An ignored file is not a source change.
    std::fs::create_dir_all(root.join("ignored")).unwrap();
    std::fs::write(root.join("ignored").join("output.bin"), "x").unwrap();
    assert_eq!(source_identity::read(&root).unwrap().dirty, Some(false));

    // An untracked source file is compiled without being part of the commit.
    std::fs::write(root.join("new.txt"), "new\n").unwrap();
    let untracked = source_identity::read(&root).unwrap();
    assert_eq!(untracked.dirty, Some(true));
    assert_eq!(source_identity::local_label(&untracked), format!("{head}-local-dirty"));
    std::fs::remove_file(root.join("new.txt")).unwrap();
    assert_eq!(source_identity::read(&root).unwrap().dirty, Some(false));

    // So is an edit to a tracked file, staged or not.
    std::fs::write(root.join("tracked.txt"), "two\n").unwrap();
    assert_eq!(source_identity::read(&root).unwrap().dirty, Some(true));
    git(&root, &["add", "tracked.txt"]);
    assert_eq!(source_identity::read(&root).unwrap().dirty, Some(true));
    git(&root, &["commit", "--quiet", "-m", "second"]);
    let committed = source_identity::read(&root).unwrap();
    assert_eq!(committed.dirty, Some(false));
    assert_ne!(committed.revision, head);
}

#[test]
fn reading_the_identity_never_rewrites_what_it_watches() {
    let scratch = Scratch::new("stable");
    let root = repository(&scratch);
    // A touched-but-identical file is the case where `git status` would
    // refresh the index if it were allowed to.
    std::fs::write(root.join("tracked.txt"), "one\n").unwrap();
    let first = source_identity::read(&root).unwrap();
    let before = contents(&first.watch);
    for _ in 0..3 {
        assert_eq!(source_identity::read(&root).unwrap(), first);
    }
    assert_eq!(contents(&first.watch), before, "a watched file changed merely by being read: the build script would rerun forever");
}

#[test]
fn linked_worktree_resolves_its_own_head() {
    let scratch = Scratch::new("worktree");
    let root = repository(&scratch);
    let linked = scratch.0.join("linked");
    git(&root, &["worktree", "add", "--quiet", "-b", "side", linked.to_str().unwrap()]);
    assert!(linked.join(".git").is_file(), "a linked worktree's .git is a file, not a directory");

    let main = source_identity::read(&root).unwrap();
    let before = source_identity::read(&linked).expect("a linked worktree has an identity");
    assert_eq!(before.revision, main.revision);
    assert_eq!(before.dirty, Some(false));
    assert!(!before.watch.is_empty() && before.watch.iter().all(|p| p.is_file()));
    assert!(before.watch.iter().all(|p| !p.starts_with(&linked)), "the watched files live in the main repository's metadata: {:?}", before.watch);
    let watched = contents(&before.watch);

    std::fs::write(linked.join("tracked.txt"), "side\n").unwrap();
    assert_eq!(source_identity::read(&linked).unwrap().dirty, Some(true));
    assert_eq!(source_identity::read(&root).unwrap().dirty, Some(false), "the main checkout is unaffected");
    git(&linked, &["commit", "--quiet", "-am", "side change"]);

    let after = source_identity::read(&linked).unwrap();
    assert_ne!(after.revision, before.revision);
    assert_eq!(after.revision, git(&linked, &["rev-parse", "HEAD"]));
    assert_ne!(contents(&before.watch), watched, "a commit in the worktree changes a watched file, so the build script reruns");
    assert_eq!(source_identity::read(&root).unwrap().revision, main.revision);
}

#[test]
fn packed_refs_still_identify_the_commit_and_trigger_rebuilds() {
    let scratch = Scratch::new("packed");
    let root = repository(&scratch);
    git(&root, &["pack-refs", "--all"]);
    let loose = PathBuf::from(git(&root, &["rev-parse", "--path-format=absolute", "--git-path", "refs/heads/main"]));
    assert!(!loose.exists(), "the branch now exists only in packed-refs");

    let packed = source_identity::read(&root).expect("packed refs do not hide the commit");
    assert_eq!(packed.revision, git(&root, &["rev-parse", "HEAD"]));
    assert!(packed.watch.iter().all(|p| p.is_file()), "a missing ref file is never watched: {:?}", packed.watch);
    assert!(packed.watch.iter().any(|p| p.file_name().is_some_and(|n| n == "packed-refs")));
    let watched = contents(&packed.watch);

    std::fs::write(root.join("tracked.txt"), "three\n").unwrap();
    git(&root, &["commit", "--quiet", "-am", "after packing"]);
    let after = source_identity::read(&root).unwrap();
    assert_ne!(after.revision, packed.revision);
    assert_ne!(contents(&packed.watch), watched, "the new commit changes a watched file even though the old ref was packed");
}

#[test]
fn directory_inside_another_repository_has_no_identity() {
    let scratch = Scratch::new("nested");
    let root = repository(&scratch);
    // An exported source tree unpacked somewhere inside an unrelated
    // repository must not report that repository's commit as its own.
    let exported = root.join("ignored").join("exported-source");
    std::fs::create_dir_all(&exported).unwrap();
    assert_eq!(source_identity::read(&exported), None);
    // Nor does a directory that is in no repository at all.
    let outside = scratch.0.join("plain");
    std::fs::create_dir_all(&outside).unwrap();
    let identity = source_identity::read(&outside);
    assert!(identity.is_none(), "{identity:?}");
}

fn fake_tool(tools: &Path, directory: &str) -> PathBuf {
    let path = tools.join(directory).join("bin").join("llvm-windres.exe");
    std::fs::create_dir_all(path.parent().unwrap()).unwrap();
    std::fs::write(&path, "fixture").unwrap();
    path
}

#[test]
fn resource_compiler_comes_only_from_the_pinned_directory() {
    let scratch = Scratch::new("windres");
    let tools = scratch.0.join("tools");
    let pins = r#"{ "target": "example-target", "llvmMingw": "llvm-mingw-20990101-example" }"#;
    // The old helper took the first `llvm-mingw*` directory the file system
    // listed, so this one, which sorts first, would have been chosen.
    fake_tool(&tools, "llvm-mingw-00000000-older");
    assert_eq!(winres::pinned_windres(&tools, pins), None, "another LLVM-MinGW is not a substitute for the pinned one");
    let pinned = fake_tool(&tools, "llvm-mingw-20990101-example");
    assert_eq!(winres::pinned_windres(&tools, pins), Some(pinned.clone()));
    fake_tool(&tools, "llvm-mingw-99999999-newer");
    assert_eq!(winres::pinned_windres(&tools, pins), Some(pinned), "a newer directory beside it changes nothing");
    // A byte-order mark in the pins file does not hide the name.
    assert!(winres::pinned_windres(&tools, &format!("{}{pins}", '\u{feff}')).is_some());
}

#[test]
#[should_panic(expected = "must name one llvm-mingw-* directory")]
fn resource_compiler_pin_cannot_point_outside_the_tools_directory() {
    let scratch = Scratch::new("windres-escape");
    let _ = winres::pinned_windres(&scratch.0, r#"{ "llvmMingw": "llvm-mingw-x/../../elsewhere" }"#);
}

#[test]
fn the_checked_in_pins_agree_with_the_toolchain_file() {
    let workspace = Path::new(env!("CARGO_MANIFEST_DIR")).join("../..");
    let pins = std::fs::read_to_string(workspace.join("tools/rust-pins.json")).unwrap();
    let toolchain = std::fs::read_to_string(workspace.join("rust-toolchain.toml")).unwrap();
    let value = |key: &str| {
        let rest = &pins[pins.find(&format!("\"{key}\"")).unwrap_or_else(|| panic!("the pins lack {key}"))..];
        rest.split('"').nth(3).unwrap().to_owned()
    };
    let target = value("target");
    assert!(toolchain.contains(&format!("\"{target}\"")), "rust-toolchain.toml installs the pinned build target {target}");
    assert!(value("llvmMingw").starts_with("llvm-mingw-"));
    // This test binary was itself built for a Windows target with that toolchain family.
    assert!(target.ends_with("-windows-gnullvm"), "{target}");
}
