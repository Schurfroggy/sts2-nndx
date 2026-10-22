#!/usr/bin/env python3
"""Verify MSIL and test-tool isolation before admitting a build into a package."""

import argparse
import json
import struct
from pathlib import Path


def verify_dll(path, mode):
    data = Path(path).read_bytes()
    if data[:2] != b"MZ":
        raise ValueError("DLL is not a managed PE file")
    pe = struct.unpack_from("<I", data, 0x3C)[0]
    if data[pe:pe + 4] != b"PE\0\0":
        raise ValueError("Invalid PE signature")
    machine, sections = struct.unpack_from("<HH", data, pe + 4)
    optional_size = struct.unpack_from("<H", data, pe + 20)[0]
    optional = pe + 24
    magic = struct.unpack_from("<H", data, optional)[0]
    if machine != 0x14C or magic != 0x10B:
        raise ValueError("Only MSIL/AnyCPU DLLs are allowed; native/ARM64 DLL rejected")
    cli_rva = struct.unpack_from("<I", data, optional + 96 + 14 * 8)[0]
    cli = None
    for index in range(sections):
        section = optional + optional_size + index * 40
        virtual_size, address, raw_size, raw_offset = struct.unpack_from("<IIII", data, section + 8)
        if address <= cli_rva < address + max(virtual_size, raw_size):
            cli = raw_offset + cli_rva - address
            break
    if cli is None:
        raise ValueError("Managed CLI header not found")
    flags = struct.unpack_from("<I", data, cli + 16)[0]
    if not flags & 1 or flags & (0x2 | 0x20000):
        raise ValueError("DLL must be IL-only AnyCPU without a 32-bit restriction")

    has_class = b"NndxScaleConsoleCmd" in data
    has_command = "nndx_scale".encode("utf-16-le") in data
    expected = mode == "test-tools"
    if has_class != expected or has_command != expected:
        raise ValueError(f"{mode} DLL contains the wrong test-tool variant")
    return {"architecture": "MSIL/AnyCPU", "test_tools": expected}


def pck_paths(path):
    """Read the unencrypted Godot 4.5 PCK v3 directory, not resource contents."""
    data = Path(path).read_bytes()
    magic, version = struct.unpack_from("<II", data)
    if magic != 0x43504447 or version != 3:
        raise ValueError("Expected a Godot 4.5 PCK v3 package")
    flags = struct.unpack_from("<I", data, 20)[0]
    if flags & 1:
        raise ValueError("Cannot verify an encrypted PCK directory")
    offset = struct.unpack_from("<Q", data, 32)[0]
    count = struct.unpack_from("<I", data, offset)[0]
    offset += 4
    result = []
    for _ in range(count):
        length = struct.unpack_from("<I", data, offset)[0]
        offset += 4
        if length == 0 or offset + length + 36 > len(data):
            raise ValueError("Invalid PCK directory entry")
        name = data[offset:offset + length].rstrip(b"\0").decode("utf-8")
        result.append(name.removeprefix("res://"))
        offset += length + 36  # file offset, size, MD5, flags
    return result


def verify_package(directory, mode):
    directory = Path(directory)
    result = verify_dll(directory / "sts_nndx.dll", mode)
    manifest = json.loads((directory / "sts_nndx.json").read_text())
    if manifest.get("id") != "sts_nndx":
        raise ValueError("Unexpected mod manifest")
    paths = pck_paths(directory / "sts_nndx.pck")
    forbidden = ("Scripts/Testing/", "tests/", "artifacts/", "$(Sts2Dir)/")
    for name in paths:
        if name.startswith(forbidden) or "NndxScaleConsoleCmd" in name:
            raise ValueError(f"Test/build-only resource found in PCK: {name}")
    for resource in ("nndx_sheet.png-", "nndx.mp3-", "dead.mp3-", "face1.png-", "face2.png-", "face3.png-", "face4.png-"):
        if not any(name.startswith(".godot/imported/" + resource) for name in paths):
            raise ValueError(f"Missing imported resource: {resource}")
    return {**result, "pck_entries": len(paths), "test_resources": False}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=("release", "test-tools"))
    parser.add_argument("directory", type=Path)
    args = parser.parse_args()
    try:
        print(json.dumps(verify_package(args.directory, args.mode), ensure_ascii=False))
    except (ValueError, OSError, struct.error) as error:
        parser.exit(1, f"Package verification failed: {error}\n")
