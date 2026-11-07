"""Packs the standalone engine runtime: a deterministic stored (uncompressed)
ZIP and the manifest that VineMix.exe verifies before extracting it.

Usage: py -3 tools/pack-runtime.py <staged runtime folder> <output folder>
The folder must contain VineMix.Engine.exe, LICENSE, ATTRIBUTION.md and
THIRD_PARTY_NOTICES.md (plus any licenses/ files).
"""

import hashlib
import json
import os
import sys
import zipfile

REQUIRED = ["VineMix.Engine.exe", "LICENSE", "ATTRIBUTION.md", "THIRD_PARTY_NOTICES.md"]


def main() -> int:
    source, output = sys.argv[1], sys.argv[2]
    names = []
    for root, _, files in os.walk(source):
        for name in files:
            names.append(os.path.relpath(os.path.join(root, name), source).replace(os.sep, "/"))
    names.sort()
    missing = [n for n in REQUIRED if n not in names]
    if missing:
        print("missing runtime files: " + ", ".join(missing), file=sys.stderr)
        return 1
    os.makedirs(output, exist_ok=True)
    archive = os.path.join(output, "VineMix.Runtime.zip")
    entries = []
    with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_STORED) as package:
        for name in names:
            with open(os.path.join(source, name.replace("/", os.sep)), "rb") as handle:
                data = handle.read()
            info = zipfile.ZipInfo(name, date_time=(2026, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_STORED
            info.external_attr = 0o100644 << 16
            package.writestr(info, data)
            entries.append({"path": name, "size": len(data), "sha256": hashlib.sha256(data).hexdigest()})
    with open(archive, "rb") as handle:
        data = handle.read()
    manifest = {
        "schemaVersion": 1,
        "engine": "VineMix.Engine.exe",
        "archiveSize": len(data),
        "archiveSha256": hashlib.sha256(data).hexdigest(),
        "files": entries,
    }
    with open(os.path.join(output, "VineMix.Runtime.manifest.json"), "w", encoding="utf-8", newline="\n") as handle:
        json.dump(manifest, handle, indent=2)
    print(f"{len(entries)} runtime files, archive {len(data)} bytes, sha256 {manifest['archiveSha256']}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
