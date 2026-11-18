"""Fixture tests for tools/rust-notices.py (RUST-REV-025).

    py -3 tools/test_rust_notices.py

Every case builds throwaway packages in a temporary directory and feeds the
generator synthetic `cargo metadata`; no real dependency is read and nothing
in the repository is written.
"""

import importlib.util
import json
import os
import shutil
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("rust_notices", os.path.join(HERE, "rust-notices.py"))
notices = importlib.util.module_from_spec(spec)
spec.loader.exec_module(notices)

MIT_TEXT = "MIT License\n\nCopyright (c) 2020 Example Author\n\nPermission is hereby granted..."
APACHE_TEXT = "Apache License\nVersion 2.0, January 2004\n..."


class Fixture:
    """A workspace with one shipped root crate and the given dependencies."""

    def __init__(self):
        self.root = tempfile.mkdtemp(prefix="vinemix-notices-")
        self.supplements = os.path.join(self.root, "supplements")
        self.packages = [self._package("app", "1.0.0", None, workspace=True)]
        self.dependencies = []

    def close(self):
        shutil.rmtree(self.root, ignore_errors=True)

    def _package(self, name, version, licence, licence_file=None, workspace=False):
        directory = os.path.join(self.root, "packages", f"{name}-{version}")
        os.makedirs(directory, exist_ok=True)
        return {
            "id": f"{name} {version}",
            "name": name,
            "version": version,
            "license": licence,
            "license_file": licence_file,
            "manifest_path": os.path.join(directory, "Cargo.toml"),
            "repository": None if workspace else f"https://example.invalid/{name}",
            "authors": ["Example Author"],
        }

    def add(self, name, licence, files=None, licence_file=None, kind=None, version="1.0.0"):
        package = self._package(name, version, licence, licence_file)
        directory = os.path.dirname(package["manifest_path"])
        for relative, text in (files or {}).items():
            path = os.path.join(directory, relative)
            os.makedirs(os.path.dirname(path), exist_ok=True)
            with open(path, "w", encoding="utf-8") as handle:
                handle.write(text)
        self.packages.append(package)
        self.dependencies.append({"pkg": package["id"], "dep_kinds": [{"kind": kind}]})
        return directory

    def supplement(self, name, text):
        os.makedirs(self.supplements, exist_ok=True)
        with open(os.path.join(self.supplements, name), "w", encoding="utf-8") as handle:
            handle.write(text)

    def metadata(self):
        nodes = [{"id": "app 1.0.0", "deps": self.dependencies}]
        nodes += [{"id": p["id"], "deps": []} for p in self.packages[1:]]
        return {"packages": self.packages, "workspace_members": ["app 1.0.0"], "resolve": {"nodes": nodes}}

    def generate(self):
        return notices.generate(self.metadata(), self.supplements, roots=["app"])


def header(crate, manifest, used, extra="", review="Example Reviewer, against the source repository"):
    return f"Crate: {crate}\nManifest-License: {manifest}\nUsed-Under: {used}\n{extra}Basis: Fixture basis.\nReview: {review}\n"


class NoticeTests(unittest.TestCase):
    def setUp(self):
        self.fixture = Fixture()
        self.addCleanup(self.fixture.close)

    def test_root_licence_files_are_reproduced(self):
        self.fixture.add("plain", "MIT", {"LICENSE": MIT_TEXT, "README.md": "not a licence"})
        text, errors, pending = self.fixture.generate()
        self.assertEqual(errors, [])
        self.assertEqual(pending, [])
        self.assertIn("plain 1.0.0  (MIT)", text)
        self.assertIn("--- LICENSE ---\n" + MIT_TEXT, text)
        self.assertNotIn("not a licence", text)

    def test_nested_licence_file_named_by_the_manifest_is_reproduced(self):
        self.fixture.add("nested", None, {"legal/terms/CUSTOM.txt": "Custom terms of the nested crate."}, licence_file="legal/terms/CUSTOM.txt")
        text, errors, _ = self.fixture.generate()
        self.assertEqual(errors, [])
        self.assertIn("--- legal/terms/CUSTOM.txt ---\nCustom terms of the nested crate.", text)
        self.assertIn("Licence: legal/terms/CUSTOM.txt", text)

    def test_manifest_licence_file_outside_the_package_is_rejected(self):
        directory = self.fixture.add("escape", None, licence_file="../outside.txt")
        with open(os.path.join(os.path.dirname(directory), "outside.txt"), "w", encoding="utf-8") as handle:
            handle.write("Someone else's text.")
        text, errors, _ = self.fixture.generate()
        self.assertTrue(any("outside the package" in e for e in errors), errors)
        self.assertNotIn("Someone else's text.", text)

    def test_missing_manifest_licence_file_is_rejected(self):
        self.fixture.add("absent", None, licence_file="LICENSE.txt")
        _, errors, _ = self.fixture.generate()
        self.assertTrue(any("absent 1.0.0" in e and "missing" in e for e in errors), errors)

    def test_reuse_licenses_directory_is_reproduced(self):
        self.fixture.add("reuse", "MIT OR Apache-2.0", {"LICENSES/MIT.txt": MIT_TEXT, "LICENSES/Apache-2.0.txt": APACHE_TEXT})
        text, errors, _ = self.fixture.generate()
        self.assertEqual(errors, [])
        self.assertIn("--- LICENSES/MIT.txt ---\n" + MIT_TEXT, text)
        self.assertIn("--- LICENSES/Apache-2.0.txt ---\n" + APACHE_TEXT, text)

    def test_non_mit_crate_without_a_licence_file_stops_the_run(self):
        # The old generator wrote an MIT text "named in its manifest" here.
        self.fixture.add("apache-only", "Apache-2.0")
        text, errors, _ = self.fixture.generate()
        self.assertEqual(len(errors), 1, errors)
        self.assertIn("apache-only 1.0.0: no licence text could be resolved (manifest licence: Apache-2.0)", errors[0])
        self.assertNotIn("MIT License", text)
        self.assertNotIn("Permission is hereby granted", text)

    def test_mit_crate_without_a_licence_file_also_stops_the_run(self):
        # No text is invented even when the manifest does offer MIT.
        self.fixture.add("bare-mit", "MIT")
        text, errors, _ = self.fixture.generate()
        self.assertEqual(len(errors), 1, errors)
        self.assertNotIn("Permission is hereby granted", text)
        self.assertNotIn("Copyright (c) Example Author", text)

    def test_crate_without_any_declared_licence_is_rejected(self):
        self.fixture.add("undeclared", None)
        _, errors, _ = self.fixture.generate()
        self.assertTrue(any("declares no licence" in e for e in errors), errors)

    def test_reviewed_supplement_supplies_the_text(self):
        self.fixture.add("bare", "MIT OR Apache-2.0")
        self.fixture.supplement("Apache-2.0.txt", APACHE_TEXT)
        self.fixture.supplement("bare.txt", header("bare", "MIT OR Apache-2.0", "Apache-2.0", "Text: Apache-2.0.txt\n"))
        text, errors, pending = self.fixture.generate()
        self.assertEqual(errors, [])
        self.assertEqual(pending, [])
        self.assertIn("used under\nApache-2.0, which its manifest offers", text)
        self.assertIn("Fixture basis.", text)
        self.assertIn("--- Apache-2.0.txt ---\n" + APACHE_TEXT, text)

    def test_supplement_text_may_follow_its_header(self):
        self.fixture.add("inline", "MIT")
        self.fixture.supplement("inline.txt", header("inline", "MIT", "MIT") + "\n" + MIT_TEXT + "\n")
        text, errors, _ = self.fixture.generate()
        self.assertEqual(errors, [])
        self.assertIn("--- supplement ---\n" + MIT_TEXT, text)

    def test_pending_review_is_reported(self):
        self.fixture.add("waiting", "MIT")
        self.fixture.supplement("waiting.txt", header("waiting", "MIT", "MIT", review="Pending: compare with the repository") + "\n" + MIT_TEXT)
        _, errors, pending = self.fixture.generate()
        self.assertEqual(errors, [])
        self.assertEqual(pending, ["waiting"])
        previous_roots = notices.ROOTS[:]
        notices.ROOTS[:] = ["app"]
        self.addCleanup(lambda: notices.ROOTS.__setitem__(slice(None), previous_roots))
        metadata = os.path.join(self.fixture.root, "metadata.json")
        with open(metadata, "w", encoding="utf-8") as handle:
            json.dump(self.fixture.metadata(), handle)
        output = os.path.join(self.fixture.root, "out", "notices.txt")
        args = ["--metadata", metadata, "--output", output, "--supplements", self.fixture.supplements]
        self.assertEqual(notices.main(args), 0)
        self.assertEqual(notices.main(args + ["--check", "--require-reviewed"]), 1)

    def test_supplement_for_a_changed_manifest_licence_needs_a_new_review(self):
        self.fixture.add("moved", "Apache-2.0")
        self.fixture.supplement("moved.txt", header("moved", "MIT OR Apache-2.0", "Apache-2.0") + "\n" + APACHE_TEXT)
        _, errors, _ = self.fixture.generate()
        self.assertTrue(any("review it again" in e for e in errors), errors)

    def test_supplement_cannot_choose_a_licence_the_manifest_does_not_offer(self):
        self.fixture.add("strict", "Apache-2.0")
        self.fixture.supplement("strict.txt", header("strict", "Apache-2.0", "MIT") + "\n" + MIT_TEXT)
        text, errors, _ = self.fixture.generate()
        self.assertTrue(any("does not offer" in e for e in errors), errors)
        self.assertNotIn(MIT_TEXT, text)

    def test_supplement_written_for_another_crate_is_rejected(self):
        self.fixture.add("mine", "MIT")
        self.fixture.supplement("mine.txt", header("other", "MIT", "MIT") + "\n" + MIT_TEXT)
        _, errors, _ = self.fixture.generate()
        self.assertTrue(any("written for other" in e for e in errors), errors)

    def test_supplement_text_cannot_point_outside_the_directory(self):
        self.fixture.add("sneaky", "MIT")
        self.fixture.supplement("sneaky.txt", header("sneaky", "MIT", "MIT", "Text: ../elsewhere.txt\n"))
        _, errors, _ = self.fixture.generate()
        self.assertTrue(any("one file name" in e for e in errors), errors)

    def test_stale_supplements_are_reported(self):
        self.fixture.add("fine", "MIT", {"LICENSE": MIT_TEXT})
        self.fixture.supplement("gone.txt", header("gone", "MIT", "MIT") + "\n" + MIT_TEXT)
        self.fixture.supplement("orphan-text.txt", APACHE_TEXT)
        _, errors, _ = self.fixture.generate()
        self.assertTrue(any("gone.txt" in e and "no shipped crate needs" in e for e in errors), errors)
        self.assertTrue(any("orphan-text.txt" in e and "no supplement refers" in e for e in errors), errors)

    def test_supplement_is_dropped_once_the_crate_ships_its_own_text(self):
        self.fixture.add("grown", "MIT", {"LICENSE": MIT_TEXT})
        self.fixture.supplement("grown.txt", header("grown", "MIT", "MIT") + "\n" + MIT_TEXT)
        _, errors, _ = self.fixture.generate()
        self.assertTrue(any("now carries its own licence text" in e for e in errors), errors)

    def test_development_dependencies_are_not_listed(self):
        self.fixture.add("shipped", "MIT", {"LICENSE": MIT_TEXT})
        self.fixture.add("dev-only", "Apache-2.0", kind="dev")
        text, errors, _ = self.fixture.generate()
        self.assertEqual(errors, [], "a development dependency without a licence file is not this file's concern")
        self.assertIn("shipped 1.0.0", text)
        self.assertNotIn("dev-only", text)

    def test_check_mode_detects_a_stale_file_and_writes_nothing(self):
        self.fixture.add("plain", "MIT", {"LICENSE": MIT_TEXT})
        notices.ROOTS[:] = ["app"]
        self.addCleanup(lambda: notices.ROOTS.__setitem__(slice(None), ["vinemix-ui", "vinemix-engine", "vinemix-setup-helper"]))
        metadata = os.path.join(self.fixture.root, "metadata.json")
        with open(metadata, "w", encoding="utf-8") as handle:
            json.dump(self.fixture.metadata(), handle)
        output = os.path.join(self.fixture.root, "out", "Rust-crates.txt")
        arguments = ["--metadata", metadata, "--output", output, "--supplements", self.fixture.supplements]
        self.assertEqual(notices.main(arguments + ["--check"]), 1, "a missing file is not current")
        self.assertFalse(os.path.exists(output), "--check never writes")
        self.assertEqual(notices.main(arguments), 0)
        self.assertEqual(notices.main(arguments + ["--check"]), 0)
        with open(output, "a", encoding="utf-8") as handle:
            handle.write("edited by hand\n")
        self.assertEqual(notices.main(arguments + ["--check"]), 1)
        with open(output, encoding="utf-8") as handle:
            self.assertTrue(handle.read().endswith("edited by hand\n"), "--check left the file alone")

    def test_unresolved_licences_write_nothing(self):
        self.fixture.add("apache-only", "Apache-2.0")
        notices.ROOTS[:] = ["app"]
        self.addCleanup(lambda: notices.ROOTS.__setitem__(slice(None), ["vinemix-ui", "vinemix-engine", "vinemix-setup-helper"]))
        metadata = os.path.join(self.fixture.root, "metadata.json")
        with open(metadata, "w", encoding="utf-8") as handle:
            json.dump(self.fixture.metadata(), handle)
        output = os.path.join(self.fixture.root, "out", "Rust-crates.txt")
        self.assertEqual(notices.main(["--metadata", metadata, "--output", output, "--supplements", self.fixture.supplements]), 1)
        self.assertFalse(os.path.exists(output))

    def test_licence_expression_identifiers(self):
        self.assertEqual(notices.identifiers("(MIT OR Apache-2.0) AND OFL-1.1 AND Ubuntu-font-1.0"), {"MIT", "Apache-2.0", "OFL-1.1", "Ubuntu-font-1.0"})
        self.assertEqual(notices.identifiers("MIT/Apache-2.0"), {"MIT", "Apache-2.0"})
        self.assertEqual(notices.identifiers("Apache-2.0 WITH LLVM-exception"), {"Apache-2.0", "LLVM-exception"})
        self.assertEqual(notices.identifiers(None), set())


if __name__ == "__main__":
    unittest.main(argv=[sys.argv[0], "-v"] if "-v" in sys.argv else [sys.argv[0]])
