#!/usr/bin/env python3
"""Type-check Assembly-CSharp without opening the Unity Editor.

Reuses the response file Unity's own build (Bee) last generated, so the define
symbols, the ~400 assembly references and the language version are exactly the
ones the Editor compiles with. Only the source list is refreshed, because the
rsp is a snapshot: a file added since the last Editor compile is not in it, and
a compile that silently omits the file you just wrote proves nothing.

Usage:
    python3 tools/typecheck.py

Exit code 0 means the project compiles. Anything else prints the errors.
"""

import pathlib
import re
import subprocess
import sys
import tempfile

ROOT = pathlib.Path(__file__).resolve().parent.parent
UNITY = pathlib.Path("/Applications/Unity/Hub/Editor")


def editor_version() -> str:
    text = (ROOT / "ProjectSettings/ProjectVersion.txt").read_text()
    return re.search(r"m_EditorVersion: (\S+)", text).group(1)


def newest_rsp() -> pathlib.Path:
    """The most recently written Assembly-CSharp.rsp from this project's own Bee cache.

    The MPPM clone directories under Library/VP are excluded deliberately: a clone
    can be running a stale domain, and type-checking against its snapshot reports
    errors that do not exist (and misses ones that do).
    """
    candidates = [
        p for p in (ROOT / "Library/Bee/artifacts").glob("*/Assembly-CSharp.rsp")
    ]
    if not candidates:
        sys.exit(
            "No Assembly-CSharp.rsp under Library/Bee/artifacts. Open the project in "
            "the Unity Editor once so Bee generates one, then re-run."
        )
    return max(candidates, key=lambda p: p.stat().st_mtime)


def source_files() -> list[str]:
    """Every .cs file that belongs to Assembly-CSharp.

    Assembly-CSharp is the default assembly: it holds every script under Assets/
    that is NOT claimed by an .asmdef. So a directory containing an .asmdef, and
    everything beneath it, is excluded -- those compile into their own assemblies
    and including them here produces duplicate-definition errors.

    Editor-only folders are excluded too: they compile into Assembly-CSharp-Editor
    against a different reference set (UnityEditor), so folding them in here fails
    on references that are genuinely available where they really live.
    """
    assets = ROOT / "Assets"
    asmdef_dirs = {p.parent for p in assets.rglob("*.asmdef")}

    def claimed(path: pathlib.Path) -> bool:
        return any(d in path.parents for d in asmdef_dirs)

    def editor_only(path: pathlib.Path) -> bool:
        return "Editor" in path.relative_to(assets).parts

    out = []
    for cs in sorted(assets.rglob("*.cs")):
        if claimed(cs) or editor_only(cs):
            continue
        out.append(str(cs.relative_to(ROOT)))
    return out


def main() -> int:
    version = editor_version()
    scripting = UNITY / version / "Unity.app/Contents/Resources/Scripting"
    dotnet = scripting / "NetCoreRuntime/dotnet"
    csc = scripting / "DotNetSdkRoslyn/csc.dll"

    for required in (dotnet, csc):
        if not required.exists():
            sys.exit(f"Missing {required}. Is Unity {version} installed via the Hub?")

    rsp = newest_rsp()
    kept = []
    for line in rsp.read_text().splitlines():
        # Drop the outputs (we want a check, not a build artifact that could be
        # mistaken for Unity's own) and drop the stale source list.
        if line.startswith(("-out:", "-refout:")):
            continue
        if line.strip().strip('"').endswith(".cs"):
            continue
        kept.append(line)

    sources = source_files()
    with tempfile.TemporaryDirectory() as tmp:
        tmpdir = pathlib.Path(tmp)
        kept.insert(0, f'-out:"{tmpdir / "typecheck.dll"}"')
        kept.extend(f'"{s}"' for s in sources)

        response = tmpdir / "typecheck.rsp"
        response.write_text("\n".join(kept))

        print(f"Unity {version} | {len(sources)} sources | rsp {rsp.parent.name}")
        result = subprocess.run(
            [str(dotnet), str(csc), f"@{response}", "-nologo"],
            cwd=ROOT,
            capture_output=True,
            text=True,
        )

    output = (result.stdout + result.stderr).strip()
    errors = [l for l in output.splitlines() if ": error " in l]

    if errors:
        for line in errors:
            print(line)
        print(f"\n{len(errors)} error(s).")
        return 1

    print("No errors.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
