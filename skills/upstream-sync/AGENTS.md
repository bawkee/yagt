# Skill: upstream-sync

How to take changes from upstream [SourceGit](https://github.com/sourcegit-scm/sourcegit) without losing the fork's work, and how to keep future merges cheap.

## Layout

| | |
|---|---|
| `origin` | `bawkee/yagt`. The fork's working branch is `master`. |
| `upstream` | `sourcegit-scm/sourcegit`, which uses git-flow. All work lands on `develop`. Roughly every 1–3 weeks a `release/vYYYY.NN` branch is merged into `master` and tagged `vYYYY.NN`, and `VERSION` is bumped. |
| Upstream automation | CI, `dotnet format` and the localization bot run only on `develop`. The bot commits `doc: Update translation status and sort locale files`, which regenerates `TRANSLATION.md` and re-sorts every locale file. `release.yml` runs on **tag pushes**. |

Two things follow for the fork:

- None of upstream's checks run on the fork's `master`. Build and run `dotnet format --verify-no-changes` locally.
- **Never push upstream's `v*` tags to `origin`** (`git push --tags`), because that triggers a release build.

## See what's missing

```sh
python skills/upstream-sync/scripts/upstream_status.py            # fetch, compare with upstream/develop
python skills/upstream-sync/scripts/upstream_status.py --branch master --fork --no-fetch
```

The script adds the `upstream` remote if it is missing. It reports:

- whether the latest release tag is merged
- every upstream commit not yet in HEAD (cherry-picked ones are excluded), oldest first, grouped by intake priority: security-related, then fixes, then compatibility/maintenance, then features, then other
- with `--fork`, the fork-only commits and the upstream files the fork modifies, which is the merge-conflict surface

## Intake, by priority

1. **Security and bug fixes.**
   - Released fixes come in with the next release merge.
   - For an urgent fix that is only on `develop`, run `git cherry-pick -x <sha>` (oldest first). If it conflicts, it probably depends on an earlier upstream commit. Pick that one too rather than re-implementing the fix.
2. **Compatibility and maintenance** (Avalonia/.NET upgrades, OS fixes, `project:` commits). These arrive mainly through release merges.
3. **Features.** Wait for the release that contains them. Cherry-pick a feature early only when the fork actually needs it.

**Merging a release:**

1. On a fresh branch from `master`, run `git merge --no-ff vYYYY.NN`.
2. Resolve conflicts.
3. Build, run `dotnet format --verify-no-changes`, run `check_resources.py`, and smoke-test the app.
4. Merge the branch into `master`.

Always *merge*. Never rebase the fork onto upstream, and never squash upstream commits. Keeping upstream's SHAs is what makes `--cherry-pick` detection and every future merge work. Don't merge `upstream/develop` wholesale, because it contains unreleased work.

## Resolving conflicts

| File(s) | Resolution |
|---|---|
| `src/Resources/Locales/*.axaml`, `TRANSLATION.md` | Take upstream's version (the bot re-sorts these), then re-add the fork's keys to `en_US.axaml` and run `check_resources.py`. |
| `Icons.axaml`, `Themes.axaml`, `Styles.axaml` | Keep both sides. Keep `Icons.axaml` sorted and make sure each key appears once. |
| `VERSION`, `build/`, `.github/workflows/` | Take upstream's version unless the fork has deliberately diverged. |
| `README.md` | The fork's version (fork docs and branding). |
| New upstream PNG icons | Accept the merge, then convert them in a follow-up `ux:` commit ([icons-and-graphics](../icons-and-graphics/AGENTS.md)). |
| Large upstream files (`ViewModels/Repository.cs`, `Views/*.axaml.cs`) | Take upstream's version and re-apply the fork's small insertion by hand. Don't hand-merge big hunks line by line. |

## Keeping future merges cheap

- Follow the root `AGENTS.md` fork policy. Add new files rather than editing upstream ones. Make minimal insertions in shared files. Don't rename, reformat or move upstream code.
- Run `--fork` occasionally. Every entry in "upstream files modified by the fork" costs something on each merge, so look for ones that could become self-contained files.
- Use one logical change per commit with upstream's prefixes, so fork fixes can go back upstream as PRs (upstream PRs target `develop`) and can be reverted cleanly when upstream ships its own fix.
