#!/usr/bin/env bash
#
# Repair on-disk file/folder casing so that it matches exactly what git tracks.
#
# WHY THIS EXISTS
#   macOS and Windows use case-insensitive filesystems. When a case-only rename is
#   recorded in git, `git pull` does NOT apply it locally: git writes the file into
#   the directory that already exists under the old casing, then reports a clean
#   tree. The divergence is completely invisible to `git status`.
#
#   That breaks multiplayer. Unity reports whatever casing the DISK has, and Netcode
#   for GameObjects identifies scenes across the network as XXHash32(full scene path),
#   which is case-sensitive. Two machines whose casing differs compute different
#   hashes for the same scene, so joining fails with:
#       Scene Hash <n> does not exist in the HashToBuildIndex table!
#
# USAGE
#   Tools/fix-asset-casing.sh           repair casing (refuses while Unity is open)
#   Tools/fix-asset-casing.sh --check   report only; exit 1 if anything diverges
#   Tools/fix-asset-casing.sh --quiet   silent when nothing is wrong
#   Tools/fix-asset-casing.sh --force   repair even if Unity appears to be running

set -eu

CHECK_ONLY=0
QUIET=0
FORCE=0
for arg in "$@"; do
  case "$arg" in
    --check) CHECK_ONLY=1 ;;
    --quiet) QUIET=1 ;;
    --force) FORCE=1 ;;
    *) echo "unknown option: $arg" >&2; exit 2 ;;
  esac
done

cd "$(git rev-parse --show-toplevel)"

WORK="${TMPDIR:-/tmp}"
DISK="$WORK/casefix-disk.$$"
IDX="$WORK/casefix-idx.$$"
PAIRS="$WORK/casefix-pairs.$$"
DIRS="$WORK/casefix-dirs.$$"
trap 'rm -f "$DISK" "$IDX" "$PAIRS" "$DIRS"' EXIT

TAB="$(printf '\t')"

# Only descend into top-level entries that actually contain tracked files, so we
# never walk Library/ or Temp/ (which are huge and untracked).
tracked_tops() { git ls-files -z | tr '\0' '\n' | awk -F/ 'NF{print $1}' | sort -u; }

# Write "<real-on-disk-path><TAB><path-as-git-records-it>" for every mismatch.
scan() {
  tops=()
  while IFS= read -r top; do
    [ -n "$top" ] && tops+=("$top")
  done < <(tracked_tops)
  if [ "${#tops[@]}" -eq 0 ]; then
    : > "$PAIRS"
    return 0
  fi
  find "${tops[@]}" -type f 2>/dev/null | sed 's|^\./||' > "$DISK"
  git ls-files -z | tr '\0' '\n' > "$IDX"
  awk 'NR==FNR { disk[tolower($0)] = $0; next }
       { d = disk[tolower($0)]; if (d != "" && d != $0) print d "\t" $0 }' "$DISK" "$IDX" > "$PAIRS"
}

# Reduce the file-level mismatches to the SHALLOWEST path segment that differs, so we
# rename one directory instead of thousands of individual files. Sorted by depth so
# parents are corrected before their children.
shallowest_renames() {
  awk -F'\t' '{
      n = split($1, a, "/"); split($2, b, "/")
      pr = ""; pw = ""
      for (i = 1; i <= n; i++) {
        pr = (i == 1 ? a[i] : pr "/" a[i])
        pw = (i == 1 ? b[i] : pw "/" b[i])
        if (a[i] != b[i]) { print i "\t" pr "\t" pw; break }
      }
    }' "$PAIRS" | sort -u | sort -n -s -k1,1
}

# 0 = Unity is running, 1 = it is not, 2 = we cannot tell on this system.
# Git Bash on Windows ships neither pgrep nor a `ps` that can see native processes,
# so fall back to tasklist there, and refuse to guess if neither exists.
unity_state() {
  if command -v pgrep >/dev/null 2>&1; then
    pgrep -f 'Unity\.app/Contents/MacOS/Unity' >/dev/null 2>&1 && return 0
    pgrep -x 'Unity' >/dev/null 2>&1 && return 0
    return 1
  fi
  if command -v tasklist >/dev/null 2>&1; then
    tasklist 2>/dev/null | grep -qi '^Unity\.exe' && return 0
    return 1
  fi
  return 2
}

total=0
pass=0
while [ "$pass" -lt 20 ]; do
  pass=$((pass + 1))
  scan
  [ -s "$PAIRS" ] || break
  shallowest_renames > "$DIRS"
  [ -s "$DIRS" ] || break

  if [ "$total" -eq 0 ]; then
    echo "Asset casing does not match git:"
    # Renaming folders under a live Editor makes Unity reimport mid-flight, so make
    # the human close it rather than doing it behind their back. If we cannot even
    # determine whether Unity is up, refuse rather than risk it.
    if [ "$CHECK_ONLY" -eq 0 ] && [ "$FORCE" -eq 0 ]; then
      ustate=0
      unity_state || ustate=$?
      if [ "$ustate" -eq 0 ]; then
        CHECK_ONLY=1
        UNITY_BLOCKED=1
      elif [ "$ustate" -eq 2 ]; then
        CHECK_ONLY=1
        UNITY_UNKNOWN=1
      fi
    fi
  fi

  while IFS="$TAB" read -r _depth real want; do
    [ -n "${real:-}" ] || continue
    [ "$real" != "$want" ] || continue
    [ -e "$real" ] || continue
    printf '  %s  ->  %s\n' "$real" "$want"
    total=$((total + 1))
    if [ "$CHECK_ONLY" -eq 0 ]; then
      # A direct `mv foo Foo` is a no-op on a case-insensitive filesystem, so bounce
      # through a temporary name.
      tmp="$(dirname "$want")/.__casefix_tmp_$$__"
      mv "$real" "$tmp"
      mv "$tmp" "$want"
    fi
  done < "$DIRS"

  [ "$CHECK_ONLY" -eq 1 ] && break
done

if [ "$total" -eq 0 ]; then
  [ "$QUIET" -eq 1 ] || echo "Asset casing matches git."
  exit 0
fi

echo
if [ -n "${UNITY_BLOCKED:-}" ]; then
  echo "Unity is running, so nothing was renamed."
  echo "Close Unity and run:  Tools/fix-asset-casing.sh"
  exit 1
fi
if [ -n "${UNITY_UNKNOWN:-}" ]; then
  echo "Could not determine whether Unity is running, so nothing was renamed."
  echo "Close Unity, then run:  Tools/fix-asset-casing.sh --force"
  exit 1
fi
if [ "$CHECK_ONLY" -eq 1 ]; then
  echo "$total path(s) diverge from git. Close Unity and run:  Tools/fix-asset-casing.sh"
  exit 1
fi
echo "Repaired $total path(s) to match git."
exit 0
