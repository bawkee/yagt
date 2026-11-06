#!/usr/bin/env python3
"""Report what this fork is missing from upstream SourceGit, grouped by intake priority.

Example:
  python skills/upstream-sync/scripts/upstream_status.py              # fetch, compare with upstream/develop
  python skills/upstream-sync/scripts/upstream_status.py --branch master --no-fetch
  python skills/upstream-sync/scripts/upstream_status.py --fork       # also audit fork divergence

* Adds the `upstream` remote if it is missing, then fetches it with tags (skip with --no-fetch).
* Shows the latest upstream release tag and whether HEAD already contains it.
* Lists upstream commits not in HEAD (patch-equivalent cherry-picks excluded), oldest first,
  grouped as: security-related > fixes > compatibility/maintenance > features > other.
  "Security-related" is a keyword heuristic (CVE, injection, ...; credential/secret/... only on fixes).
* --fork lists fork-only commits and the upstream files the fork modifies (merge conflict surface).
"""

import argparse
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
UPSTREAM_URL = "https://github.com/sourcegit-scm/sourcegit.git"
RE_SECURITY = re.compile(r"secur|cve|vulnerab|exploit|inject|xss|travers", re.IGNORECASE)
RE_SECURITY_FIX = re.compile(r"credential|password|secret|keychain|\bauth|leak|unsafe|escap|saniti[sz]|permission",
                             re.IGNORECASE)
RE_PREFIX = re.compile(r"^([A-Za-z_\-]+)(\([^)]*\))?:")
GROUPS = (
    ("Security-related (keyword match, review first)", None),
    ("Fixes", {"fix", "hotfix"}),
    ("Compatibility / maintenance", {"project", "version", "ci", "build", "deps", "refactor", "code_style",
                                     "code_review", "macos", "linux", "windows"}),
    ("Features", {"feature", "enhance", "ux"}),
    ("Other (docs, localization, style)", set()),
)


def git(*args, check=True):
    proc = subprocess.run(["git", "-C", str(ROOT), *args], capture_output=True)
    out = proc.stdout.decode("utf-8", "replace").rstrip()
    if check and proc.returncode != 0:
        sys.exit(f"error: git {' '.join(args)}\n{proc.stderr.decode('utf-8', 'replace').strip()}")
    return out if proc.returncode == 0 else None


def ensure_upstream(fetch):
    if git("remote", "get-url", "upstream", check=False) is None:
        git("remote", "add", "upstream", UPSTREAM_URL)
        print(f"added remote upstream -> {UPSTREAM_URL}")
        fetch = True
    if fetch:
        print("fetching upstream ...", flush=True)
        git("fetch", "upstream", "--tags", "--prune", "--quiet")


def group_of(subject):
    m = RE_PREFIX.match(subject)
    prefix = m.group(1).lower() if m else ""
    if RE_SECURITY.search(subject) or (prefix in GROUPS[1][1] and RE_SECURITY_FIX.search(subject)):
        return 0
    for i, (_, prefixes) in enumerate(GROUPS[1:-1], start=1):
        if prefix in prefixes:
            return i
    return len(GROUPS) - 1


def report_release():
    tags = git("tag", "--merged", "upstream/master", "--sort=-v:refname", "--list", "v*", check=False) or ""
    latest = tags.splitlines()[0] if tags else None
    if not latest:
        print("latest upstream release: (no v* tags found; fetch with tags)")
        return
    merged = subprocess.run(["git", "-C", str(ROOT), "merge-base", "--is-ancestor", latest, "HEAD"]).returncode == 0
    behind = git("rev-list", "--count", "--no-merges", "HEAD..upstream/master")
    state = "merged" if merged else f"NOT merged -> git merge --no-ff {latest}"
    print(f"latest upstream release: {latest} ({state}); upstream/master commits not in HEAD: {behind}")


def report_missing(branch):
    ref = f"upstream/{branch}"
    if git("rev-parse", "--verify", "--quiet", ref, check=False) is None:
        sys.exit(f"error: {ref} not found (fetch upstream or pick another --branch)")
    log = git("log", "--no-merges", "--cherry-pick", "--right-only", "--reverse",
              "--format=%h%x09%ad%x09%s", "--date=short", f"HEAD...{ref}")
    rows = [line.split("\t", 2) for line in log.splitlines() if line]
    print(f"\n{len(rows)} commit(s) in {ref} missing from HEAD (oldest first):")
    buckets = [[] for _ in GROUPS]
    for sha, date, subject in rows:
        buckets[group_of(subject)].append(f"  {sha} {date} {subject}")
    for (title, _), items in zip(GROUPS, buckets):
        if items:
            print(f"\n{title} [{len(items)}]")
            print("\n".join(items))
    if rows:
        print("\nurgent single fix: git cherry-pick -x <sha>   (pick prerequisite commits too if it conflicts)")


def report_fork():
    base = git("merge-base", "HEAD", "upstream/master")
    log = git("log", "--no-merges", "--format=  %h %ad %s", "--date=short", f"{base}..HEAD")
    print(f"\nfork-only commits since merge-base {base[:8]}:")
    print(log or "  (none)")
    changes = [line.split("\t") for line in git("diff", "--name-status", base, "HEAD").splitlines() if line]
    added = [c[-1] for c in changes if c[0] == "A"]
    touched = [f"  {c[0][0]} {c[-1]}" for c in changes if c[0] != "A"]
    print(f"\nupstream files modified by the fork (conflict surface) [{len(touched)}]:")
    print("\n".join(touched) or "  (none)")
    print(f"fork-added files: {len(added)}")


def main():
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--branch", default="develop", help="upstream branch to compare with (default: develop)")
    ap.add_argument("--no-fetch", action="store_true", help="use already fetched refs")
    ap.add_argument("--fork", action="store_true", help="also list fork-only commits and modified upstream files")
    args = ap.parse_args()

    ensure_upstream(not args.no_fetch)
    report_release()
    report_missing(args.branch)
    if args.fork:
        report_fork()


if __name__ == "__main__":
    main()
