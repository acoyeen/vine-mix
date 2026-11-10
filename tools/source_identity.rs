// Shared build-script helper (`include!`d by crates/vinemix-ui/build.rs and by
// crates/vinemix-ui/tests/source_identity.rs): the checkout's commit and tree
// state, resolved by Git itself.
//
// Nothing here assumes how the repository is laid out on disk. `.git` is a
// file in a linked worktree, and a branch may exist only in `packed-refs`, so
// every path comes from `git rev-parse --git-path`.

#[allow(dead_code)]
mod source_identity {
    use std::path::{Path, PathBuf};

    #[derive(Debug, Clone, PartialEq, Eq)]
    pub struct Identity {
        /// The full commit hash of HEAD.
        pub revision: String,
        /// Whether tracked files, or untracked files that are not ignored,
        /// differ from that commit. `None` when Git could not tell.
        pub dirty: Option<bool>,
        /// Existing files that change when HEAD or the index moves.
        pub watch: Vec<PathBuf>,
    }

    fn git(root: &Path, arguments: &[&str]) -> Option<String> {
        // `--no-optional-locks`: reading the state never rewrites the index,
        // which would otherwise look like a change to the watched files.
        let output = std::process::Command::new("git").arg("--no-optional-locks").arg("-C").arg(root).args(arguments).output().ok()?;
        output.status.success().then(|| String::from_utf8_lossy(&output.stdout).trim_end_matches(['\r', '\n']).to_owned())
    }

    /// The identity of the checkout whose top level is `root`, or `None` when
    /// `root` is not the top of a Git work tree (an exported source archive,
    /// even one unpacked inside some other repository).
    pub fn read(root: &Path) -> Option<Identity> {
        let top = git(root, &["rev-parse", "--show-toplevel"])?;
        if std::fs::canonicalize(&top).ok()? != std::fs::canonicalize(root).ok()? {
            return None;
        }
        let revision = git(root, &["rev-parse", "--verify", "HEAD"])?;
        if revision.len() != 40 || !revision.bytes().all(|b| b.is_ascii_hexdigit()) {
            return None;
        }
        let mut names = vec!["HEAD".to_owned(), "logs/HEAD".to_owned(), "index".to_owned(), "packed-refs".to_owned()];
        if let Some(reference) = git(root, &["symbolic-ref", "--quiet", "HEAD"]) {
            names.push(reference);
        }
        let mut watch = Vec::new();
        for name in &names {
            if let Some(path) = git(root, &["rev-parse", "--path-format=absolute", "--git-path", name]) {
                let path = PathBuf::from(path);
                // A missing file would make Cargo rerun the script every time.
                if path.is_file() {
                    watch.push(path);
                }
            }
        }
        let dirty = git(root, &["status", "--porcelain=v1", "--untracked-files=normal"]).map(|changes| !changes.trim().is_empty());
        Some(Identity { revision, dirty, watch })
    }

    /// What a build made outside the release script reports as its revision.
    ///
    /// The release script verifies the tree before and after compiling and
    /// passes the plain commit hash (or `<hash>-dirty` when it was told to
    /// allow that). A plain `cargo build` only looked when its build script
    /// last ran, so it always says `local`: `<hash>-local`,
    /// `<hash>-local-dirty`, or `<hash>-local-unverified` when Git could not
    /// report the tree state.
    pub fn local_label(identity: &Identity) -> String {
        let state = match identity.dirty {
            Some(false) => "local",
            Some(true) => "local-dirty",
            None => "local-unverified",
        };
        format!("{}-{state}", identity.revision)
    }
}
