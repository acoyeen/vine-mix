"""Writes licenses/Rust-crates.txt: every crate linked into the shipped
executables (VineMix.exe, VineMix.Engine.exe, VineMix.SetupHelper.exe) with
its licence expression, source and the licence texts that apply to it.

Usage (from the repository root, with cargo on PATH):

    py -3 tools/rust-notices.py            write licenses/Rust-crates.txt
    py -3 tools/rust-notices.py --check    verify it is current; change nothing

Development-only dependencies are excluded.

Licence texts come only from real inputs, in this order:

1. licence, copying and notice files in the package root;
2. the file the crate's manifest names as `license-file`, wherever it sits
   inside the package;
3. a `LICENSES` directory in the package root (the REUSE layout);
4. a reviewed supplement, licenses/rust-supplements/<crate>.txt, for a crate
   whose published package contains no licence text at all.

Nothing is invented. A crate with none of these stops the run with an error,
as does a supplement that no longer matches its crate. Earlier versions of
this tool wrote a standard MIT text "named in its manifest" for every such
crate, including one whose manifest names only Apache-2.0.

A supplement is a small text file:

    Crate: example
    Manifest-License: MIT OR Apache-2.0
    Used-Under: Apache-2.0
    Text: Apache-2.0.txt
    Basis: why this text is the right one
    Review: who confirmed it against the crate's source repository, or
      "Pending ..." while that is still to be done

`Manifest-License` must equal the crate's current licence expression, so a
change upstream forces a new review. `Used-Under` must be offered by that
expression. `Text` names a file in the supplement directory; without it the
text follows the header after a blank line.
"""

import argparse
import json
import os
import re
import subprocess
import sys

TARGET = "x86_64-pc-windows-gnullvm"
ROOTS = ["vinemix-ui", "vinemix-engine", "vinemix-setup-helper"]
LICENSE_PREFIXES = ("license", "licence", "copying", "notice", "copyright", "unlicense")
OUTPUT = os.path.join("licenses", "Rust-crates.txt")
SUPPLEMENTS = os.path.join("licenses", "rust-supplements")
SUPPLEMENT_KEYS = ("Crate", "Manifest-License", "Used-Under", "Text", "Basis", "Review")


def read_text(path):
    with open(path, encoding="utf-8", errors="replace") as handle:
        return handle.read().replace("\r\n", "\n").strip()


def identifiers(expression):
    """The licence identifiers an SPDX-style expression mentions."""
    words = re.findall(r"[A-Za-z0-9.+-]+", (expression or "").replace("/", " OR "))
    return {word for word in words if word.upper() not in ("OR", "AND", "WITH")}


def inside(directory, path):
    directory = os.path.realpath(directory)
    path = os.path.realpath(path)
    try:
        return os.path.commonpath([directory, path]) == directory and path != directory
    except ValueError:  # Different drives.
        return False


def shipped_packages(metadata, roots=ROOTS):
    packages = {p["id"]: p for p in metadata["packages"]}
    nodes = {n["id"]: n for n in metadata["resolve"]["nodes"]}
    workspace = set(metadata["workspace_members"])
    start = [pid for pid in workspace if packages[pid]["name"] in roots]
    if len(start) != len(roots):
        raise SystemExit("missing a shipped crate")
    shipped = set()
    pending = list(start)
    while pending:
        current = pending.pop()
        if current in shipped:
            continue
        shipped.add(current)
        for dependency in nodes[current]["deps"]:
            kinds = [k.get("kind") for k in dependency["dep_kinds"]]
            # Normal and build-time code generation (proc macros) count;
            # dev-dependencies and build scripts' own tools do not ship.
            if any(kind is None for kind in kinds):
                pending.append(dependency["pkg"])
    return sorted((packages[p] for p in shipped if p not in workspace), key=lambda p: (p["name"].lower(), p["version"]))


def package_texts(package, errors):
    """The licence texts a published package contains: [(label, text)]."""
    directory = os.path.dirname(package["manifest_path"])
    name = f"{package['name']} {package['version']}"
    found = []
    seen = set()

    def add(label, path):
        key = os.path.normcase(os.path.realpath(path))
        if key not in seen:
            seen.add(key)
            found.append((label, read_text(path)))

    entries = sorted(os.listdir(directory))
    for entry in entries:
        if entry.lower().startswith(LICENSE_PREFIXES) and os.path.isfile(os.path.join(directory, entry)):
            add(entry, os.path.join(directory, entry))
    declared = package.get("license_file")
    if declared:
        path = os.path.join(directory, declared)
        if not inside(directory, path):
            errors.append(f"{name}: its manifest names a licence file outside the package ({declared})")
        elif not os.path.isfile(path):
            errors.append(f"{name}: the licence file its manifest names is missing ({declared})")
        else:
            add(declared.replace("\\", "/"), path)
    for entry in entries:
        nested = os.path.join(directory, entry)
        if entry.lower() in ("licenses", "licences") and os.path.isdir(nested):
            for item in sorted(os.listdir(nested)):
                if os.path.isfile(os.path.join(nested, item)):
                    add(f"{entry}/{item}", os.path.join(nested, item))
    return found


def supplement_header(path):
    """The header fields of a supplement file, or None if it is not one."""
    with open(path, encoding="utf-8", errors="replace") as handle:
        raw = handle.read().replace("\r\n", "\n")
    header, _, body = raw.partition("\n\n")
    header = header.strip("\n")
    if not header.startswith("Crate:"):
        return None
    fields = {}
    key = None
    for line in header.split("\n"):
        if line[:1] in (" ", "\t") and key:
            fields[key] += " " + line.strip()
            continue
        key, separator, value = line.partition(":")
        key = key.strip()
        if not separator or key not in SUPPLEMENT_KEYS or key in fields:
            return {"error": f"header line is not understood: {line!r}"}
        fields[key] = value.strip()
    fields["body"] = body.strip()
    return fields


def read_supplement(directory, crate, errors):
    """A reviewed supplement for `crate`, or None when there is none."""
    path = os.path.join(directory, crate + ".txt")
    if not os.path.isfile(path):
        return None
    fields = supplement_header(path)
    if fields is None:
        errors.append(f"{crate}: {path} is not a supplement (it must begin with 'Crate:')")
        return None
    if "error" in fields:
        errors.append(f"{crate}: supplement {fields['error']}")
        return None
    for required in ("Crate", "Manifest-License", "Used-Under", "Basis", "Review"):
        if not fields.get(required):
            errors.append(f"{crate}: supplement lacks {required}")
            return None
    text = fields["body"]
    shared = fields.get("Text")
    if shared:
        if os.path.basename(shared) != shared or shared.startswith(".") or text:
            errors.append(f"{crate}: supplement Text must be one file name in the supplement directory, with no text below the header")
            return None
        if not os.path.isfile(os.path.join(directory, shared)):
            errors.append(f"{crate}: supplement text {shared} is missing")
            return None
        text = read_text(os.path.join(directory, shared))
    if not text:
        errors.append(f"{crate}: supplement has no licence text")
        return None
    fields["text"] = text
    fields["label"] = shared or "supplement"
    return fields


def unused_supplement_files(directory, needed):
    """Errors for supplement files that no shipped crate needs any more."""
    errors = []
    if not os.path.isdir(directory):
        return errors
    files = sorted(entry for entry in os.listdir(directory) if os.path.isfile(os.path.join(directory, entry)))
    shared = set()
    for entry in files:
        fields = supplement_header(os.path.join(directory, entry)) if entry.endswith(".txt") else None
        if fields is None:
            continue
        if entry[:-4] not in needed:
            errors.append(f"{directory}/{entry}: no shipped crate needs this supplement; remove it")
        elif fields.get("Text"):
            shared.add(fields["Text"])
    for entry in files:
        fields = supplement_header(os.path.join(directory, entry)) if entry.endswith(".txt") else None
        if fields is None and entry not in shared and entry.lower() != "readme.md":
            errors.append(f"{directory}/{entry}: no supplement refers to this file; remove it")
    return errors


def generate(metadata, supplements=SUPPLEMENTS, roots=ROOTS):
    """Returns (text, errors, names of crates whose supplement awaits review)."""
    third_party = shipped_packages(metadata, roots)
    errors = []
    pending = []
    needed = set()
    out = [
        "Rust crates linked into VineMix",
        "",
        "Generated by tools/rust-notices.py from Cargo.lock for target " + TARGET + ".",
        "Each crate keeps its own licence; the texts it distributes follow its entry.",
        "Crates offered under a choice of licences (for example MIT OR Apache-2.0)",
        "are used under the MIT terms where offered, unless the entry names another",
        "choice.",
        "",
        "Index",
        "-----",
    ]
    for package in third_party:
        out.append(f"{package['name']} {package['version']}  ({package.get('license') or 'see files'})")
    out.append("")
    for package in third_party:
        directory = os.path.dirname(package["manifest_path"])
        name = f"{package['name']} {package['version']}"
        expression = package.get("license")
        if not expression and not package.get("license_file"):
            errors.append(f"{name}: its manifest declares no licence")
        texts = package_texts(package, errors)
        out.append("=" * 78)
        out.append(name)
        out.append(f"Licence: {expression or package.get('license_file') or 'unspecified'}")
        if package.get("repository"):
            out.append(f"Source: {package['repository']}")
        fonts = os.path.join(directory, "fonts")
        font_notices = sorted(f for f in os.listdir(fonts) if f.lower().endswith(".txt")) if os.path.isdir(fonts) else []
        supplement = read_supplement(supplements, package["name"], errors)
        if supplement and texts:
            errors.append(f"{name}: the package now carries its own licence text; remove {supplements}/{package['name']}.txt")
        elif supplement:
            needed.add(package["name"])
            offered = identifiers(expression)
            chosen = identifiers(supplement["Used-Under"])
            if supplement["Crate"] != package["name"]:
                errors.append(f"{name}: its supplement is written for {supplement['Crate']}")
            elif supplement["Manifest-License"] != (expression or ""):
                errors.append(f"{name}: its supplement was reviewed for '{supplement['Manifest-License']}' but the manifest now says '{expression}'; review it again")
            elif not chosen or not chosen <= offered:
                errors.append(f"{name}: its supplement uses it under '{supplement['Used-Under']}', which '{expression}' does not offer")
            else:
                if supplement["Review"].lower().startswith("pending"):
                    pending.append(package["name"])
                out.append("")
                out.append("The published crate carries no licence text of its own. It is used under")
                out.append(f"{supplement['Used-Under']}, which its manifest offers; that text follows.")
                out.append(supplement["Basis"])
                out.append("")
                out.append(f"--- {supplement['label']} ---")
                out.append(supplement["text"])
        elif not texts:
            needed.add(package["name"])
            errors.append(
                f"{name}: no licence text could be resolved (manifest licence: {expression or 'none'}); "
                f"add a reviewed supplement at {supplements}/{package['name']}.txt"
            )
        for item in font_notices:
            out.append("")
            out.append(f"--- fonts/{item} ---")
            out.append(read_text(os.path.join(fonts, item)))
        for label, text in texts:
            out.append("")
            out.append(f"--- {label} ---")
            out.append(text)
        out.append("")
    errors.extend(unused_supplement_files(supplements, needed))
    return "\n".join(out) + "\n", errors, pending


def main(arguments=None) -> int:
    parser = argparse.ArgumentParser(description="Writes or checks licenses/Rust-crates.txt.")
    parser.add_argument("--check", action="store_true", help="verify the output file is current instead of writing it")
    parser.add_argument("--require-reviewed", action="store_true", help="reject pending licence supplements in release builds")
    parser.add_argument("--output", default=OUTPUT)
    parser.add_argument("--supplements", default=SUPPLEMENTS)
    parser.add_argument("--metadata", help="read `cargo metadata` JSON from this file instead of running cargo")
    options = parser.parse_args(arguments)
    if options.metadata:
        with open(options.metadata, encoding="utf-8") as handle:
            metadata = json.load(handle)
    else:
        metadata = json.loads(subprocess.check_output(
            ["cargo", "metadata", "--format-version", "1", "--filter-platform", TARGET, "--locked"], text=True, encoding="utf-8"))
    text, errors, pending = generate(metadata, options.supplements)
    if errors:
        print("Third-party notices are not resolved; nothing was written:", file=sys.stderr)
        for error in errors:
            print("  " + error, file=sys.stderr)
        return 1
    crates = len(shipped_packages(metadata))
    if pending:
        print(f"{len(pending)} supplement(s) await confirmation against the crate's source repository: {', '.join(pending)}", file=sys.stderr)
        if options.require_reviewed:
            return 1
    if options.check:
        try:
            with open(options.output, encoding="utf-8", newline="") as handle:
                current = handle.read()
        except OSError:
            current = None
        if current != text:
            print(f"{options.output} is not current; run tools/rust-notices.py and review the difference.", file=sys.stderr)
            return 1
        print(f"{crates} crates; {options.output} is current")
        return 0
    os.makedirs(os.path.dirname(options.output) or ".", exist_ok=True)
    with open(options.output, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(text)
    print(f"{crates} crates written to {options.output}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
