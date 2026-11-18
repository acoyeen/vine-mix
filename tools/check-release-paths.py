"""Reject local build paths in uncompressed installer inputs and final packages."""

import argparse
from pathlib import Path
import re


def private_strings(prefixes):
    # Forward and backslash forms occur in Rust resources. Windows paths can
    # also appear as UTF-16LE in PE data, or escaped in an embedded JSON file.
    variants = set()
    for prefix in prefixes:
        for sep in ("/", "\\", "\\\\"):
            variant = prefix.replace("\\", "/").replace("/", sep)
            for encoding in ("utf-8", "utf-16le"):
                variants.add(variant.encode(encoding))
    return re.compile(b"|".join(re.escape(value) for value in sorted(variants)), re.IGNORECASE)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--prefix", action="append", required=True)
    parser.add_argument("files", nargs="+", type=Path)
    args = parser.parse_args()
    pattern = private_strings(args.prefix)
    for file in args.files:
        if pattern.search(file.read_bytes()):
            parser.exit(1, f"Private build path found in {file.name}.\n")


if __name__ == "__main__":
    main()
