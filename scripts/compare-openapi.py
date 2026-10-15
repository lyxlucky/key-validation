"""Strict API migration gate. Usage: compare-openapi.py baseline.json candidate.json."""
import argparse
import json
from pathlib import Path


def differences(before, after, path=""):
    if type(before) is not type(after):
        yield path or "/"
    elif isinstance(before, dict):
        for key in sorted(before.keys() | after.keys()):
            pointer = path + "/" + key.replace("~", "~0").replace("/", "~1")
            if key not in before or key not in after:
                yield pointer
            else:
                yield from differences(before[key], after[key], pointer)
    elif isinstance(before, list):
        if before != after:
            yield path
    elif before != after:
        yield path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("baseline", type=Path)
    parser.add_argument("candidate", type=Path)
    args = parser.parse_args()
    before = json.loads(args.baseline.read_text(encoding="utf-8-sig"))
    after = json.loads(args.candidate.read_text(encoding="utf-8-sig"))
    # Ignore deployment-specific server addresses and document branding only.
    for document in (before, after):
        if not isinstance(document.get("paths"), dict) or not isinstance(document.get("components"), dict):
            parser.error("Both documents must contain paths and components objects")
    changes = list(differences(
        {key: before[key] for key in ("paths", "components")},
        {key: after[key] for key in ("paths", "components")}))
    for pointer in changes[:100]:
        print("Contract differs at", pointer)
    if changes:
        print(f"FAIL: {len(changes)} differences. Runtime protocol tests are also required.")
        return 1
    print("PASS: paths and components match. Runtime protocol tests are still required.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
