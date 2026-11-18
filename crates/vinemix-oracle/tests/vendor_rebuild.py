"""Check the actual oracle build script invalidates Cargo's incremental cache.

Run with Python 3.11+ in the pinned Rust/LLVM-MinGW build environment. All
edits are to a temporary copy of the native sources.
"""

import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import tomllib


def check() -> None:
    root = Path(__file__).resolve().parents[3]
    packages = tomllib.loads((root / "Cargo.lock").read_text(encoding="utf-8"))["package"]
    cc = next(package["version"] for package in packages if package["name"] == "cc")
    target = "x86_64-pc-windows-gnullvm"
    with tempfile.TemporaryDirectory(prefix="vinemix-oracle-rebuild-") as temp:
        fixture = Path(temp)
        crate = fixture / "crates" / "vinemix-oracle"
        (crate / "src").mkdir(parents=True)
        original = root / "crates" / "vinemix-oracle"
        shutil.copy2(original / "build.rs", crate)
        shutil.copytree(original / "cpp", crate / "cpp")
        shutil.copytree(root / "native", fixture / "native")
        shutil.copytree(root / "third_party" / "src", fixture / "third_party" / "src")
        shutil.copy2(root / "rust-toolchain.toml", fixture)
        (crate / "Cargo.toml").write_text(
            f'[package]\nname="oracle-rebuild-check"\nversion="0.1.0"\nedition="2024"\n'
            f'[build-dependencies]\ncc="={cc}"\n', encoding="utf-8"
        )
        (crate / "src" / "main.rs").write_text(
            'unsafe extern "C" { fn am_fixture_revision() -> i32; }\n'
            'fn main() { println!("{}", unsafe { am_fixture_revision() }); }\n',
            encoding="utf-8",
        )
        vendor = fixture / "native" / "vendor" / "rnnoise_memory.c"
        original_bytes = vendor.read_bytes()
        # Cargo's package fingerprints drive this test. Per-crate rustc
        # incremental caches only add disposable files and long Windows paths.
        env = dict(os.environ, VINEMIX_REQUIRE_ORACLE="1", CARGO_INCREMENTAL="0", CARGO_TARGET_DIR=str(fixture / "target"))
        for revision in [1, 2]:
            vendor.write_bytes(original_bytes + f"\nint am_fixture_revision(void) {{ return {revision}; }}\n".encode())
            output = subprocess.check_output(
                ["cargo", "run", "--quiet", "--offline", "--target", target], cwd=crate, env=env, text=True
            ).strip()
            if output != str(revision):
                raise AssertionError(f"vendor returned {output!r} after changing the reference to {revision}")


if __name__ == "__main__":
    check()
    print("The incrementally rebuilt oracle contains the updated vendor code.")
