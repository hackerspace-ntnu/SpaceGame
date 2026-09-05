#!/usr/bin/env python3
"""Type-check Assembly-CSharp (and optionally Assembly-CSharp-Editor) without opening
the Unity Editor.

Reuses the response file Unity's own build (Bee) last generated, so the define
symbols, the ~400 assembly references and the language version are exactly the
ones the Editor compiles with. Only the source list is refreshed, because the
rsp is a snapshot: a file added since the last Editor compile is not in it, and
a compile that silently omits the file you just wrote proves nothing.

Usage:
    python3 tools/typecheck.py            # runtime assembly only
    python3 tools/typecheck.py --editor   # runtime, then editor + tests

--editor compiles Assembly-CSharp-Editor against the Assembly-CSharp this run just
built, NOT against the stale one in Library/ScriptAssemblies. That distinction is the
whole point: after a runtime API change the on-disk dll is the OLD api, so an editor
check against it reports no errors for exactly the calls that are now broken -- and
the editor assembly is where every test and every prefab builder lives.

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


def newest_rsp(name: str = "Assembly-CSharp.rsp") -> pathlib.Path:
    """The most recently written Assembly-CSharp.rsp from this project's own Bee cache.

    The MPPM clone directories under Library/VP are excluded deliberately: a clone
    can be running a stale domain, and type-checking against its snapshot reports
    errors that do not exist (and misses ones that do).
    """
    candidates = [
        p for p in (ROOT / "Library/Bee/artifacts").glob("*/" + name)
    ]
    if not candidates:
        sys.exit(
            f"No {name} under Library/Bee/artifacts. Open the project in "
            "the Unity Editor once so Bee generates one, then re-run."
        )
    return max(candidates, key=lambda p: p.stat().st_mtime)


def source_files(editor: bool = False) -> list[str]:
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
        if claimed(cs):
            continue
        if editor_only(cs) != editor:
            continue
        out.append(str(cs.relative_to(ROOT)))
    return out


def compile_one(dotnet, csc, rsp, sources, label, out_dll, swap_ref=None):
    """Run Roslyn over one assembly. Returns (errors, path to the dll it wrote)."""
    kept = []
    swapped = False
    for line in rsp.read_text().splitlines():
        # Drop the outputs (we want a check, not a build artifact that could be
        # mistaken for Unity's own) and drop the stale source list.
        if line.startswith(("-out:", "-refout:")):
            continue
        if line.strip().strip('"').endswith(".cs"):
            continue
        # Point the editor assembly at the Assembly-CSharp this run just built. Without
        # this it links Bee's cached Assembly-CSharp.ref.dll, which predates whatever
        # change is being checked -- so every call the change just broke still resolves
        # and the check reports success over a project that cannot compile.
        if swap_ref and line.startswith("-r:"):
            ref = pathlib.PurePath(line[3:].strip('"')).name
            if ref in ("Assembly-CSharp.dll", "Assembly-CSharp.ref.dll"):
                line = f'-r:"{swap_ref}"'
                swapped = True
        kept.append(line)

    if swap_ref and not swapped:
        sys.exit(
            f"{label}: no Assembly-CSharp reference found in {rsp} to redirect. Checking "
            "against Bee's cached copy would report success over code that cannot "
            "compile, so this is a hard stop rather than a warning."
        )

    kept.insert(0, f'-out:"{out_dll}"')
    kept.extend(f'"{s}"' for s in sources)

    response = out_dll.parent / (out_dll.stem + ".rsp")
    response.write_text("\n".join(kept))

    print(f"{label}: {len(sources)} sources | rsp {rsp.parent.name}")
    result = subprocess.run(
        [str(dotnet), str(csc), f"@{response}", "-nologo"],
        cwd=ROOT,
        capture_output=True,
        text=True,
    )

    output = (result.stdout + result.stderr).strip()
    return [l for l in output.splitlines() if ": error " in l], out_dll


def main() -> int:
    want_editor = "--editor" in sys.argv

    version = editor_version()
    scripting = UNITY / version / "Unity.app/Contents/Resources/Scripting"
    dotnet = scripting / "NetCoreRuntime/dotnet"
    csc = scripting / "DotNetSdkRoslyn/csc.dll"

    for required in (dotnet, csc):
        if not required.exists():
            sys.exit(f"Missing {required}. Is Unity {version} installed via the Hub?")

    print(f"Unity {version}")

    with tempfile.TemporaryDirectory() as tmp:
        tmpdir = pathlib.Path(tmp)

        errors, runtime_dll = compile_one(
            dotnet, csc, newest_rsp(), source_files(),
            "Assembly-CSharp", tmpdir / "Assembly-CSharp.dll",
        )

        if errors:
            for line in errors:
                print(line)
            print(f"\n{len(errors)} error(s).")
            return 1

        print("Assembly-CSharp: no errors.")

        if not want_editor:
            return 0

        errors, _ = compile_one(
            dotnet, csc, newest_rsp("Assembly-CSharp-Editor.rsp"), source_files(editor=True),
            "Assembly-CSharp-Editor", tmpdir / "Assembly-CSharp-Editor.dll",
            swap_ref=runtime_dll,
        )

    if errors:
        for line in errors:
            print(line)
        print(f"\n{len(errors)} error(s).")
        return 1

    print("Assembly-CSharp-Editor: no errors.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
