#!/usr/bin/env python3
"""Build a verified release or test-tools MSIL package without deploying it."""

import argparse
import json
import os
import shutil
import subprocess
import tempfile
from pathlib import Path

from verify_mod_package import verify_dll, verify_package


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", nargs="?", default="release", choices=("release", "test-tools"))
    parser.add_argument("--no-restore", action="store_true", help="Use already restored NuGet dependencies")
    parser.add_argument("--godot", help="Path to the Godot .NET editor")
    args = parser.parse_args()
    project = Path(__file__).resolve().parent.parent
    godot = args.godot or os.environ.get("GODOT") or shutil.which("godot")
    if not godot:
        bundled = Path("/Applications/Godot_mono.app/Contents/MacOS/Godot")
        if bundled.is_file():
            godot = str(bundled)
    if not godot:
        parser.error("Godot .NET editor not found; specify --godot or GODOT")

    enabled = "true" if args.mode == "test-tools" else "false"
    props = [f"-p:EnableNndxTestTools={enabled}", "-p:DeployMod=false", "-p:PlatformTarget=AnyCPU"]
    build = ["dotnet", "build", "sts_nndx.csproj", "-c", "ExportRelease", *props]
    if args.no_restore:
        build.append("--no-restore")
    subprocess.run(build, cwd=project, check=True)
    target = subprocess.check_output(
        ["dotnet", "msbuild", "sts_nndx.csproj", "-nologo", "-getProperty:TargetPath",
         "-p:Configuration=ExportRelease", *props], cwd=project, text=True,
    ).strip()
    dll = Path(target)
    verify_dll(dll, args.mode)

    artifacts = project / "artifacts"
    artifacts.mkdir(exist_ok=True)
    (artifacts / ".gdignore").touch()
    # Exporting a resource pack must not invoke the project's automatic deployment.
    env = dict(os.environ, DeployMod="false", EnableNndxTestTools=enabled)
    env.setdefault("DOTNET_ROLL_FORWARD", "LatestMajor")
    with tempfile.TemporaryDirectory(prefix=f".{args.mode}-", dir=artifacts) as temporary:
        staging = Path(temporary)
        shutil.copy2(dll, staging / "sts_nndx.dll")
        shutil.copy2(project / "sts_nndx.json", staging / "sts_nndx.json")
        subprocess.run(
            [godot, "--headless", "--path", str(project), "--export-pack", "Windows Desktop",
             str(staging / "sts_nndx.pck")], cwd=project, env=env, check=True,
        )
        result = verify_package(staging, args.mode)
        output = artifacts / args.mode
        output.mkdir(exist_ok=True)
        for name in ("sts_nndx.dll", "sts_nndx.json", "sts_nndx.pck"):
            os.replace(staging / name, output / name)
        result = verify_package(output, args.mode)
        (output / "build-info.json").write_text(
            json.dumps({"mode": args.mode, **result}, ensure_ascii=False, indent=2) + "\n",
        )
    print(f"Verified {args.mode} package: {output}")
    print(json.dumps(result, ensure_ascii=False))


if __name__ == "__main__":
    main()
