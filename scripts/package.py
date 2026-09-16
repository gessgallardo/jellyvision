#!/usr/bin/env python3
"""Package JellyVision as a Jellyfin plugin repository artifact.

Builds the Release dll, zips it in the layout Jellyfin expects, computes the
MD5 checksum the manifest must carry, and regenerates manifest.json.

Usage:
    python3 scripts/package.py --version 0.1.0.0 --base-url https://host/path
"""
import argparse
import hashlib
import json
import os
import shutil
import subprocess
import sys
import zipfile
from datetime import datetime, timezone

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PROJ = os.path.join(ROOT, "Jellyfin.Plugin.JellyVision")
CSPROJ = os.path.join(PROJ, "Jellyfin.Plugin.JellyVision.csproj")
DIST = os.path.join(ROOT, "dist")

GUID = "b9c1f0a2-3d47-4d2e-9d7a-0f8a5c6e1b34"
NAME = "JellyVision"
TARGET_ABI = "10.11.0.0"


def build(version: str) -> str:
    """Build Release and return the path to the dll."""
    subprocess.run(
        [
            "dotnet", "build", CSPROJ, "-c", "Release", "--nologo",
            f"-p:Version={version}",
            f"-p:AssemblyVersion={version}",
            f"-p:FileVersion={version}",
        ],
        check=True,
        cwd=ROOT,
    )
    dll = os.path.join(PROJ, "bin", "Release", "net9.0", "Jellyfin.Plugin.JellyVision.dll")
    if not os.path.exists(dll):
        sys.exit(f"build produced no dll at {dll}")
    return dll


def package(dll: str, version: str) -> tuple[str, str]:
    """Zip the dll and return (zip path, md5 checksum)."""
    os.makedirs(DIST, exist_ok=True)
    zip_path = os.path.join(DIST, f"jellyvision_{version}.zip")
    # Jellyfin unpacks the archive straight into plugins/<name>_<version>/,
    # so the dll must sit at the archive root, not inside a folder.
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as z:
        z.write(dll, "Jellyfin.Plugin.JellyVision.dll")

    with open(zip_path, "rb") as fh:
        checksum = hashlib.md5(fh.read()).hexdigest()  # noqa: S324 - Jellyfin requires md5
    return zip_path, checksum


def manifest(version: str, checksum: str, base_url: str, changelog: str) -> list:
    entry = {
        "version": version,
        "changelog": changelog,
        "targetAbi": TARGET_ABI,
        "sourceUrl": f"{base_url.rstrip('/')}/jellyvision_{version}.zip",
        "checksum": checksum,
        "timestamp": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
    }

    path = os.path.join(ROOT, "manifest.json")
    if os.path.exists(path):
        with open(path) as fh:
            data = json.load(fh)
        versions = [v for v in data[0].get("versions", []) if v["version"] != version]
    else:
        data = [{}]
        versions = []

    data[0].update({
        "guid": GUID,
        "name": NAME,
        "description": (
            "Turns opted-in series and movies into always-on, cable-style "
            "channels on a deterministic wall-clock schedule."
        ),
        "overview": "Turn your library into old-school cable TV channels.",
        "owner": "gessgallardo",
        "category": "Live TV",
        "versions": [entry] + versions,
    })
    return data


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--version", default="0.1.0.0")
    ap.add_argument("--base-url", required=True, help="public URL of the directory holding the zip")
    ap.add_argument("--changelog", default="Deterministic schedule engine and REST API.")
    args = ap.parse_args()

    dll = build(args.version)
    zip_path, checksum = package(dll, args.version)
    data = manifest(args.version, checksum, args.base_url, args.changelog)

    out = os.path.join(ROOT, "manifest.json")
    with open(out, "w") as fh:
        json.dump(data, fh, indent=2)
        fh.write("\n")

    shutil.copy(out, os.path.join(DIST, "manifest.json"))

    print(f"zip      : {zip_path}")
    print(f"md5      : {checksum}")
    print(f"sourceUrl: {data[0]['versions'][0]['sourceUrl']}")
    print(f"manifest : {out}")


if __name__ == "__main__":
    main()
