using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text.Json;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SourceGit;
using Commands = SourceGit.Commands;
using Models = SourceGit.Models;
using VM = SourceGit.ViewModels;
using Views = SourceGit.Views;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var fixtureRoot = OperatingSystem.IsWindows() ? @"C:\Development\.codex\tmp" : Path.GetTempPath();
        var fixture = Path.Combine(fixtureRoot, "yagt-history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        SourceGit.Native.OS.BasicDirectories.ConfigDir = fixture;
        SourceGit.Native.OS.BasicDirectories.CacheDir = fixture;
        VM.Repository repo = null;
        Window window = null;
        try
        {
            File.WriteAllText(Path.Combine(fixture, "preference.json"), "{\"EnableAutoFetch\":false,\"ShowLocalChangesByDefault\":false}");
            App.BuildAvaloniaApp().SetupWithoutStarting();
            App.SetLocale("en_US");
            SourceGit.Native.OS.GitExecutable = SourceGit.Native.OS.FindGitExecutable();
            if (args.Contains("--list-screens", StringComparer.Ordinal))
            {
                window = new Window { ShowActivated = false };
                DisplayPlacement.PositionOnDisplayOne(window);
                return;
            }

            var graph = CreateFixture(fixture);
            CheckQueries(fixture, graph);
            repo = new VM.Repository(false, fixture, Path.Combine(fixture, ".git"));
            repo.Open();
            PumpUntil(() => !repo.Histories.IsLoading && repo.Histories.Commits.Count > 0 && repo.LocalBranchesCount == 33);
            var pref = VM.Preferences.Instance;
            pref.SetCanModify();
            CheckQueuedRefreshCancellation(repo);
            CheckViewModels(repo, graph);
            CheckSettingsMigration(repo);
            CheckRapidSwitching(repo);
            if (args.Contains("--render", StringComparer.Ordinal))
            {
                window = RenderViews(repo);
                Check(window.Screens.ScreenFromWindow(window) is { } actual && actual.Bounds.Equals(s_renderScreen),
                    "render window remains on physical display 1");
            }
            CheckPersistence(repo);
            Console.WriteLine($"PASS: {s_checks} branch history checks");
        }
        finally
        {
            window?.Close();
            repo?.Close();
            if (args.Contains("--keep-fixture", StringComparer.Ordinal))
            {
                Console.WriteLine("Fixture retained at " + fixture);
            }
            else
            {
                var tempRoot = Path.GetFullPath(fixtureRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!Path.GetFullPath(fixture).StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Fixture must stay inside the temporary directory");
                foreach (var file in Directory.EnumerateFiles(fixture, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(fixture, true);
            }
        }
    }

    private static void CheckQueuedRefreshCancellation(VM.Repository repo)
    {
        using var lockWatcher = repo.LockWatcher();
        var uiCancellations = 0;
        EventHandler<FirstChanceExceptionEventArgs> onException = (_, e) =>
        {
            if (e.Exception is OperationCanceledException && Dispatcher.UIThread.CheckAccess())
                uiCancellations++;
        };

        AppDomain.CurrentDomain.FirstChanceException += onException;
        try
        {
            foreach (var focused in new[] { false, true })
            {
                Refresh(repo, () => repo.Histories.IsHistoryFocused = focused);
                var previous = repo.Histories.Commits;
                for (var i = 0; i < 8; i++)
                {
                    repo.RefreshCommits();
                    // Leave the dispatcher paused so each worker can queue its UI snapshot before being superseded.
                    Thread.Sleep(50);
                }

                repo.RefreshCommits();
                PumpUntil(() => !repo.Histories.IsLoading && !ReferenceEquals(previous, repo.Histories.Commits));
                Check(uiCancellations == 0, $"superseded queued {(focused ? "focused" : "overview")} history snapshots exit without UI-thread cancellation exceptions (observed {uiCancellations})");
                Check(repo.Histories.Commits.Count > 0, "latest queued history refresh still supplies commits");
            }

            var stateFile = Path.Combine(repo.GitDir, "sourcegit.uistates");
            var previousState = File.Exists(stateFile) ? File.ReadAllText(stateFile) : null;
            var states = Models.RepositoryUIStates.Load(repo.GitDir);
            states.IsHistoryFocused = true;
            states.SelectedHistoryBranch = RELEASE_REF;
            states.Save();
            var startup = new VM.Repository(false, repo.FullPath, repo.GitDir);
            try
            {
                startup.Open();
                using var startupWatcher = startup.LockWatcher();
                for (var i = 0; i < 8; i++)
                {
                    Thread.Sleep(50);
                    startup.RefreshCommits();
                }

                PumpUntil(() => !startup.Histories.IsLoading && startup.Histories.Commits.Count > 0 && startup.LocalBranchesCount == 33);
                Check(uiCancellations == 0, "opening saved focused history with superseded refreshes throws no UI-thread cancellation exceptions");
                Check(startup.Histories.IsHistoryFocused &&
                    string.Equals(startup.UIStates.SelectedHistoryBranch, RELEASE_REF, StringComparison.Ordinal) &&
                    startup.Histories.Commits.Any(c => c.Decorators.Any(d => string.Equals(d.Name, "release/1.55.6", StringComparison.Ordinal))),
                    "saved focused startup completes with its selected release history");
            }
            finally
            {
                startup.Close();
                if (previousState == null)
                    File.Delete(stateFile);
                else
                    File.WriteAllText(stateFile, previousState);
            }
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= onException;
            Refresh(repo, () => repo.Histories.IsHistoryFocused = false);
        }
    }

    private static Fixture CreateFixture(string path)
    {
        Git(path, "init", "-b", "master");
        Git(path, "config", "user.name", "History Check");
        Git(path, "config", "user.email", "history@example.com");
        Git(path, "config", "commit.gpgsign", "false");
        Git(path, "config", "core.hooksPath", path);
        var tree = Git(path, "mktree", input: string.Empty);
        var root = Commit(path, tree, "repository root", 1_600_000_000);
        var common = Commit(path, tree, "shared history", 1_600_000_100, root);
        var releaseOne = Commit(path, tree, "release first commit", 1_600_000_200, common);
        var mergedSide = Commit(path, tree, "merged topic commit", 1_600_000_300, releaseOne);
        var releaseTwo = Commit(path, tree, "release second commit", 1_600_000_400, releaseOne);
        var merge = Commit(path, tree, "merge topic into release", 1_600_000_500, releaseTwo, mergedSide);
        var releaseTip = Commit(path, tree, "same subject on different branches", 1_600_000_600, merge);
        var masterOne = Commit(path, tree, "master first commit", 1_600_000_700, common);
        var repeatedSubject = Commit(path, tree, "same subject on different branches", 1_600_000_800, masterOne);
        var masterTip = Commit(path, tree, "master tip", 1_600_000_900, repeatedSubject);
        var remoteOnly = Commit(path, tree, "remote-only branch tip", 1_600_001_000, common);
        var upstream = Commit(path, tree, "upstream release ahead", 1_700_000_000, releaseTip);
        var unrelatedTag = Commit(path, tree, "unrelated tagged commit", 1_800_000_000, common);
        Git(path, "update-ref", "refs/heads/master", masterTip);
        Git(path, "update-ref", RELEASE_REF, releaseTip);
        Git(path, "update-ref", "refs/heads/merged-topic", mergedSide);
        Git(path, "update-ref", REMOTE_REF, remoteOnly);
        Git(path, "update-ref", "refs/remotes/origin/release/1.55.6", upstream);
        Git(path, "update-ref", "refs/tags/unrelated-tag", unrelatedTag);
        Git(path, "remote", "add", "origin", ".");
        Git(path, "config", "branch.release/1.55.6.remote", "origin");
        Git(path, "config", "branch.release/1.55.6.merge", "refs/heads/release/1.55.6");
        string featureTip = null;
        for (var i = 0; i < 30; i++)
        {
            var parent = masterOne;
            for (var j = 0; j < 4; j++)
                parent = Commit(path, tree, $"feature {i:D2} commit {j}", 1_700_000_100 + i * 10 + j, parent);
            Git(path, "update-ref", $"refs/heads/feature/{i:D2}", parent);
            featureTip = parent;
        }

        return new Fixture(tree, root, common, releaseOne, releaseTwo, mergedSide, merge, releaseTip,
            masterOne, repeatedSubject, masterTip, remoteOnly, upstream, unrelatedTag, featureTip);
    }

    private static void CheckQueries(string path, Fixture graph)
    {
        var gitDir = Path.Combine(path, ".git");
        var states = new Models.RepositoryUIStates();
        Check(!states.IsHistoryFocused, "overview remains the default");
        var overview = Query(path, states.BuildHistoryParams(gitDir), 8);
        Check(overview.Count == 8 && overview.All(c => !string.Equals(c.SHA, graph.ReleaseTip, StringComparison.Ordinal)),
            "busy repository cap omits the older release branch");
        var focused = Query(path, states.BuildHistoryParams(gitDir, [RELEASE_REF]), 8);
        CheckSet(focused.Select(c => c.SHA), graph.ReleaseHistory, "branch scope is applied before the history cap");
        Check(focused.All(c => !string.Equals(c.SHA, graph.Upstream, StringComparison.Ordinal)), "focused branch does not silently include its upstream");
        Check(focused.All(c => !string.Equals(c.SHA, graph.RepeatedSubject, StringComparison.Ordinal)), "identical subjects do not imply branch membership");
        states.HistoryShowFlags = Models.HistoryShowFlags.Reflog;
        var withReflog = Query(path, states.BuildHistoryParams(gitDir, [RELEASE_REF]), 100);
        CheckSet(withReflog.Select(c => c.SHA), graph.ReleaseHistory, "reflog cannot widen focused history");
        states.HistoryShowFlags = Models.HistoryShowFlags.FirstParentOnly;
        var firstParent = Query(path, states.BuildHistoryParams(gitDir, [RELEASE_REF]), 100);
        CheckSet(firstParent.Select(c => c.SHA), graph.ReleaseFirstParentHistory, "first parent hides merged side commits while retaining the merge");
        states.HistoryShowFlags = Models.HistoryShowFlags.None;
        var combined = Query(path, states.BuildHistoryParams(gitDir, [RELEASE_REF, "refs/heads/master"]), 100);
        CheckSet(combined.Select(c => c.SHA), graph.ReleaseHistory.Concat(graph.MasterHistory), "explicit additional branch forms a union of histories");
        var remote = Query(path, states.BuildHistoryParams(gitDir, [REMOTE_REF]), 100);
        CheckSet(remote.Select(c => c.SHA), [graph.RemoteOnly, graph.Common, graph.Root], "remote-only branch can be focused");
        var empty = Query(path, states.BuildHistoryParams(gitDir, []), 100);
        Check(empty.Count == 0, "empty explicit scope cannot fall back to all branches");

        states.UpdateHistoryFilters("refs/heads/master", Models.FilterType.LocalBranch, Models.FilterMode.Included);
        CheckSet(Query(path, states.BuildHistoryParams(gitDir, [RELEASE_REF]), 100).Select(c => c.SHA), graph.ReleaseHistory,
            "focused scope overrides manual filters without modifying them");
        CheckSet(Query(path, states.BuildHistoryParams(gitDir), 100).Select(c => c.SHA), graph.MasterHistory,
            "manual filters are intact when returning to overview");

        var all = Query(path, new Models.RepositoryUIStates().BuildHistoryParams(gitDir), 1000);
        var hashes = Await(new Commands.QueryHistoryCommitHashes(path, [RELEASE_REF], all, false).GetResultAsync());
        CheckSet(hashes, graph.ReleaseHistory, "highlight membership matches complete release ancestry");
        hashes = Await(new Commands.QueryHistoryCommitHashes(path, [RELEASE_REF], all, true).GetResultAsync());
        CheckSet(hashes, graph.ReleaseFirstParentHistory, "highlight membership obeys first-parent traversal");
        var withoutTip = all.Where(c => !string.Equals(c.SHA, graph.ReleaseTip, StringComparison.Ordinal)).ToList();
        hashes = Await(new Commands.QueryHistoryCommitHashes(path, [RELEASE_REF], withoutTip, false).GetResultAsync());
        CheckSet(hashes, graph.ReleaseHistory.Where(sha => !string.Equals(sha, graph.ReleaseTip, StringComparison.Ordinal)),
            "branch ancestry is identified even when its tip is absent from the visible graph");
        var skewed = new List<Models.Commit>
        {
            new() { SHA = graph.Root, CommitterTime = ulong.MaxValue },
            new() { SHA = graph.RepeatedSubject, CommitterTime = 0 },
        };
        hashes = Await(new Commands.QueryHistoryCommitHashes(path, [RELEASE_REF], skewed, false).GetResultAsync());
        CheckSet(hashes, [graph.Root], "reachability is exact despite skewed commit timestamps");
        hashes = Await(new Commands.QueryHistoryCommitHashes(path, [RELEASE_REF, REMOTE_REF], all, false).GetResultAsync());
        CheckSet(hashes, graph.ReleaseHistory.Append(graph.RemoteOnly), "highlight membership supports multiple local and remote refs");
        hashes = Await(new Commands.QueryHistoryCommitHashes(path, [graph.ReleaseTip], all, false, true).GetResultAsync());
        CheckSet(hashes, graph.ReleaseHistory, "loaded graph fast path matches exact ancestry");
        hashes = Await(new Commands.QueryHistoryCommitHashes(path, [graph.ReleaseTip], all, true, true).GetResultAsync());
        CheckSet(hashes, graph.ReleaseFirstParentHistory, "loaded graph fast path respects first-parent merges");
        hashes = Await(new Commands.QueryHistoryCommitHashes(path, [graph.ReleaseTip, graph.RemoteOnly], all, false, true).GetResultAsync());
        CheckSet(hashes, graph.ReleaseHistory.Append(graph.RemoteOnly), "loaded graph fast path supports multiple history tips");
        hashes = Await(new Commands.QueryHistoryCommitHashes(path, [graph.ReleaseTip], withoutTip, false, true).GetResultAsync());
        CheckSet(hashes, graph.ReleaseHistory.Where(sha => !string.Equals(sha, graph.ReleaseTip, StringComparison.Ordinal)),
            "loaded graph fast path falls back when the target tip is absent");
        hashes = Await(new Commands.QueryHistoryCommitHashes(path, [RELEASE_REF], all, false, true).GetResultAsync());
        CheckSet(hashes, graph.ReleaseHistory, "loaded graph fast path falls back for a named ref");
        var capped = Query(path, new Models.RepositoryUIStates().BuildHistoryParams(gitDir), 8);
        var cappedStreamed = Await(new Commands.QueryHistoryCommitHashes(path, [capped[0].SHA], capped, false).GetResultAsync());
        hashes = Await(new Commands.QueryHistoryCommitHashes(path, [capped[0].SHA], capped, false, true).GetResultAsync());
        CheckSet(hashes, cappedStreamed, "loaded graph fast path matches Git on a capped full-repository prefix");
        hashes = Await(new Commands.QueryHistoryCommitHashes(path, [RELEASE_REF], [all.Find(c => string.Equals(c.SHA, graph.ReleaseTip, StringComparison.Ordinal))], false).GetResultAsync());
        CheckSet(hashes, [graph.ReleaseTip], "reachability can stop as soon as every displayed candidate is found");
        hashes = Await(new Commands.QueryHistoryCommitHashes(path, [], all, false).GetResultAsync());
        Check(hashes.Count == 0, "reachability with no target is empty");
        hashes = Await(new Commands.QueryHistoryCommitHashes(path, [RELEASE_REF], [], false).GetResultAsync());
        Check(hashes.Count == 0, "reachability with no displayed commits is empty");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        try
        {
            Await(new Commands.QueryHistoryCommitHashes(path, [RELEASE_REF], all, false)
            {
                CancellationToken = canceled.Token,
            }.GetResultAsync());
            Check(false, "canceled reachability must not run to completion");
        }
        catch (OperationCanceledException)
        {
            Check(true, "canceled reachability exits before starting Git");
        }
    }

    private static void CheckViewModels(VM.Repository repo, Fixture graph)
    {
        using var lockWatcher = repo.LockWatcher();
        var histories = repo.Histories;
        var branch = repo.Branches.Find(b => string.Equals(b.FullName, RELEASE_REF, StringComparison.Ordinal));
        var master = repo.Branches.Find(b => string.Equals(b.FullName, "refs/heads/master", StringComparison.Ordinal));
        var remote = repo.Branches.Find(b => string.Equals(b.FullName, REMOTE_REF, StringComparison.Ordinal));
        Check(branch != null && master != null && remote != null, "history target choices include local and remote branches");
        repo.SelectHistoryBranch(branch);
        PumpUntil(() => histories.SelectedCommits.Count == 1 && string.Equals(histories.SelectedCommits[0].SHA, graph.ReleaseTip, StringComparison.Ordinal));
        Check(string.Equals(repo.UIStates.SelectedHistoryBranch, RELEASE_REF, StringComparison.Ordinal), "branch click records the branch independently of the inspected commit");
        Check(!histories.IsHistoryFocused && !histories.IsBranchHistoryMode, "branch selection preserves overview mode");

        Refresh(repo, () => histories.GraphHighlighting = Models.CommitGraphHighlighting.SelectedBranchOnly);
        CheckSet(histories.Commits.Where(c => c.IsHighlightedInGraph).Select(c => c.SHA), graph.ReleaseHistory,
            "selected branch is highlighted in repository context");
        CheckMergedMetadata(histories.Commits, graph, "highlight mode preserves checked-out branch membership for commit actions");
        Check(histories.Commits.Any(c => !c.IsHighlightedInGraph), "unrelated histories remain visible and dimmed");
        Check(histories.Commits.Where(c => graph.ReleaseHistory.Contains(c.SHA, StringComparer.Ordinal)).All(c => c.HistoryOpacity == 1),
            "selected branch commit text has full opacity even though master is checked out");
        Check(histories.Commits.Where(c => !graph.ReleaseHistory.Contains(c.SHA, StringComparer.Ordinal)).All(c => c.HistoryOpacity < 1),
            "unrelated commit text follows branch emphasis");
        var before = histories.Commits.Where(c => c.IsHighlightedInGraph).Select(c => c.SHA).ToHashSet(StringComparer.Ordinal);
        histories.SelectedCommits = [histories.Commits.Find(c => string.Equals(c.SHA, graph.MasterTip, StringComparison.Ordinal))];
        Dispatcher.UIThread.RunJobs();
        CheckSet(histories.Commits.Where(c => c.IsHighlightedInGraph).Select(c => c.SHA), before,
            "inspecting a commit on another branch does not move branch focus");
        Check(string.Equals(repo.UIStates.SelectedHistoryBranch, RELEASE_REF, StringComparison.Ordinal), "branch target survives commit inspection");

        Check(histories.CanHighlightSelectedBranch, "selected branch choice is available when a different branch is checked out");
        Git(repo.FullPath, "branch", "release/same-tip", "master");
        repo.RefreshBranches();
        PumpUntil(() => repo.Branches.Any(b => string.Equals(b.FullName, "refs/heads/release/same-tip", StringComparison.Ordinal)));
        var sameTip = repo.Branches.Find(b => string.Equals(b.FullName, "refs/heads/release/same-tip", StringComparison.Ordinal));
        Refresh(repo, () => repo.SelectHistoryBranch(sameTip));
        Check(histories.CanHighlightSelectedBranch && string.Equals(sameTip.Head, master.Head, StringComparison.Ordinal),
            "distinct branches sharing one commit still expose selected-branch highlighting");
        Refresh(repo, () => repo.SelectHistoryBranch(master));
        Check(!histories.CanHighlightSelectedBranch && histories.IsSelectedBranchHighlighting,
            "selecting the checked-out branch hides the duplicate choice without losing follow-selection preference");
        Refresh(repo, () => repo.SelectHistoryBranch(branch));
        Check(histories.CanHighlightSelectedBranch && histories.IsSelectedBranchHighlighting,
            "selecting another branch resumes selected-branch emphasis");
        Git(repo.FullPath, "branch", "-D", "release/same-tip");
        repo.RefreshBranches();
        PumpUntil(() => repo.Branches.All(b => !string.Equals(b.FullName, "refs/heads/release/same-tip", StringComparison.Ordinal)));
        Refresh(repo, () =>
        {
            repo.UIStates.SelectedHistoryBranch = "HEAD";
            histories.RefreshHistoryBranches();
            histories.IncludeCurrentBranchInHistory = true;
        });
        Check(!histories.CanHighlightSelectedBranch && !histories.ShowIncludedCurrentHistoryBranch,
            "restored HEAD focus coalesces with the checked-out branch without a duplicate chip");
        Refresh(repo, () =>
        {
            repo.SelectHistoryBranch(branch);
            histories.IncludeCurrentBranchInHistory = false;
        });

        Refresh(repo, () => histories.IncludeCurrentBranchInHistory = true);
        CheckSet(histories.Commits.Where(c => c.IsHighlightedInGraph).Select(c => c.SHA), graph.ReleaseHistory.Concat(graph.MasterHistory),
            "checked-out and selected branch can be highlighted together");
        Refresh(repo, () => histories.IncludeCurrentBranchInHistory = false);
        Refresh(repo, () => histories.IsHistoryFocused = true);
        CheckSet(histories.Commits.Select(c => c.SHA), graph.ReleaseHistory, "focused view hides unrelated rows and lanes");
        Check(histories.Commits.All(c => c.IsHighlightedInGraph && c.HistoryOpacity == 1), "focused rows and graph are fully emphasized");
        CheckMergedMetadata(histories.Commits, graph, "focused history preserves checked-out ancestry independently of visual emphasis");
        Check(histories.Commits.Count == histories.Graph.Dots.Count, "focused graph layout matches the reduced history");

        Refresh(repo, () => histories.IsHistoryFocused = false);
        Check(histories.GraphHighlighting == Models.CommitGraphHighlighting.SelectedBranchOnly && histories.IsBranchHistoryMode &&
            histories.Commits.Any(c => string.Equals(c.SHA, graph.FeatureTip, StringComparison.Ordinal)),
            "turning focus off restores repository context while preserving selected-branch highlighting");
        foreach (var highlighting in new[]
        {
            Models.CommitGraphHighlighting.All,
            Models.CommitGraphHighlighting.CurrentBranchOnly,
            Models.CommitGraphHighlighting.SelectedBranchOnly,
            Models.CommitGraphHighlighting.SelectedCommitsOnly,
            Models.CommitGraphHighlighting.CurrentBranchAndSelectedCommits,
            Models.CommitGraphHighlighting.SelectedCommitsOnlyFirstParent,
        })
        {
            Refresh(repo, () => histories.GraphHighlighting = highlighting);
            histories.SelectedCommits = [histories.Commits.Find(c => string.Equals(c.SHA, graph.Merge, StringComparison.Ordinal))];
            Dispatcher.UIThread.RunJobs();
            var overviewEmphasis = histories.Commits.Where(c => c.IsHighlightedInGraph).Select(c => c.SHA).ToArray();
            Refresh(repo, () => histories.IsHistoryFocused = true);
            CheckSet(histories.Commits.Select(c => c.SHA), graph.ReleaseHistory, $"focused scope is independent of saved {highlighting} preference");
            Check(histories.GraphHighlighting == highlighting && repo.UIStates.GraphHighlighting == highlighting,
                $"focused history preserves saved {highlighting} preference without replacing it");
            CheckFocusedEmphasis(histories, $"focused commits, paths, links, and text are fully emphasized with saved {highlighting}");
            var focusedGraph = histories.Graph;
            histories.SelectedCommits = [histories.Commits.Find(c => string.Equals(c.SHA, graph.ReleaseOne, StringComparison.Ordinal))];
            Dispatcher.UIThread.RunJobs();
            Check(ReferenceEquals(focusedGraph, histories.Graph), $"inspecting another commit does not regenerate focused graph with saved {highlighting}");
            CheckFocusedEmphasis(histories, $"commit inspection cannot dim focused history with saved {highlighting}");
            histories.SelectedCommits = [histories.Commits.Find(c => string.Equals(c.SHA, graph.Merge, StringComparison.Ordinal))];
            Dispatcher.UIThread.RunJobs();
            Refresh(repo, () => histories.IsHistoryFocused = false);
            Check(histories.GraphHighlighting == highlighting, $"leaving focus restores saved {highlighting} preference");
            CheckSet(histories.Commits.Where(c => c.IsHighlightedInGraph).Select(c => c.SHA), overviewEmphasis,
                $"leaving focus restores actual {highlighting} graph emphasis");
        }

        Check(string.Equals(repo.UIStates.SelectedHistoryBranch, RELEASE_REF, StringComparison.Ordinal),
            "focused emphasis preserves the named branch while inspecting commits");
        Refresh(repo, () => histories.GraphHighlighting = Models.CommitGraphHighlighting.SelectedBranchOnly);
        Refresh(repo, () => histories.IsHistoryFocused = true);
        Refresh(repo, () => histories.SelectedCommits = []);
        Check(histories.SelectedCommits.Count == 1 && string.Equals(histories.SelectedCommits[0].SHA, graph.ReleaseTip, StringComparison.Ordinal),
            "focused refresh selects the target tip when no inspected commit survives");

        Refresh(repo, () => histories.AddHistoryBranch(remote));
        CheckSet(histories.Commits.Select(c => c.SHA), graph.ReleaseHistory.Append(graph.RemoteOnly), "pinned remote branch adds only its own ancestry");
        Check(histories.AdditionalHistoryBranches.Count == 1, "additional branch is visible in the scope controls");
        Refresh(repo, () => histories.RemoveHistoryBranch(remote));
        CheckSet(histories.Commits.Select(c => c.SHA), graph.ReleaseHistory, "removing an additional branch restores the focused scope");

        Refresh(repo, () => histories.ToggleShowFlag(Models.HistoryShowFlags.FirstParentOnly));
        CheckSet(histories.Commits.Select(c => c.SHA), graph.ReleaseFirstParentHistory, "focused first-parent view follows only the main line");
        Refresh(repo, () => histories.ToggleShowFlag(Models.HistoryShowFlags.FirstParentOnly));
        var previousLimit = VM.Preferences.Instance.MaxHistoryCommits;
        VM.Preferences.Instance.MaxHistoryCommits = 3;
        Refresh(repo, () => { });
        Check(histories.Commits.Count == 3 && histories.Commits.Any(c => string.Equals(c.SHA, graph.ReleaseTip, StringComparison.Ordinal)),
            "focused history cap is applied after selecting the branch");
        var busy = repo.Branches.Find(b => string.Equals(b.FullName, "refs/heads/feature/29", StringComparison.Ordinal));
        Refresh(repo, () => histories.AddHistoryBranch(busy));
        Check(histories.Commits.Count == 3 && histories.Commits.All(c => !string.Equals(c.SHA, graph.ReleaseTip, StringComparison.Ordinal)),
            "combined focused histories retain the configured global cap");
        Check(histories.HistoryScopeDescription.Contains("3", StringComparison.Ordinal) &&
            histories.HistoryScopeDescription.Contains("tip", StringComparison.OrdinalIgnoreCase),
            "scope describes the limit when a busy additional branch pushes the selected tip outside it");
        Refresh(repo, () => histories.RemoveHistoryBranch(busy));
        VM.Preferences.Instance.MaxHistoryCommits = previousLimit;
        Refresh(repo, () => { });

        var moved = Commit(repo.FullPath, graph.Tree, "release moved while being inspected", 1_600_001_100, graph.ReleaseTip);
        Git(repo.FullPath, "update-ref", RELEASE_REF, moved);
        repo.RefreshBranches();
        PumpUntil(() => repo.Branches.Any(b => string.Equals(b.FullName, RELEASE_REF, StringComparison.Ordinal) && string.Equals(b.Head, moved, StringComparison.Ordinal)));
        Refresh(repo, () => { });
        Check(histories.Commits.Any(c => string.Equals(c.SHA, moved, StringComparison.Ordinal)), "focus follows a named branch when its tip advances");
        Check(string.Equals(repo.UIStates.SelectedHistoryBranch, RELEASE_REF, StringComparison.Ordinal), "refresh does not replace branch identity with its old SHA");
        Refresh(repo, () => histories.IsHistoryFocused = false);
        Check(histories.Commits.Any(c => string.Equals(c.SHA, graph.FeatureTip, StringComparison.Ordinal)), "returning to overview restores unrelated branch history");
        Refresh(repo, () => histories.IsHistoryFocused = true);
        Git(repo.FullPath, "update-ref", "-d", RELEASE_REF);
        repo.RefreshBranches();
        PumpUntil(() => repo.Branches.All(b => !string.Equals(b.FullName, RELEASE_REF, StringComparison.Ordinal)));
        Refresh(repo, () => { });
        Check(histories.Commits.Count == 0, "deleted focused branch cannot silently widen the graph");
        Check(string.Equals(repo.UIStates.SelectedHistoryBranch, RELEASE_REF, StringComparison.Ordinal), "deleted target identity is retained for a clear missing-branch message");
        Check(histories.HistoryScopeDescription.Contains("no longer exists", StringComparison.OrdinalIgnoreCase),
            "deleted selected branch is explained in the compact row tooltip");
        Git(repo.FullPath, "update-ref", RELEASE_REF, moved);
        repo.RefreshBranches();
        PumpUntil(() => repo.Branches.Any(b => string.Equals(b.FullName, RELEASE_REF, StringComparison.Ordinal)));
        Refresh(repo, () => { });
        Check(histories.Commits.Any(c => string.Equals(c.SHA, moved, StringComparison.Ordinal)), "restoring the focused ref restores its history");

        var toRename = repo.Branches.Find(b => string.Equals(b.FullName, RELEASE_REF, StringComparison.Ordinal));
        Git(repo.FullPath, "branch", "-m", "release/1.55.6", "release/renamed");
        repo.RefreshAfterRenameBranch(toRename, "release/renamed");
        Refresh(repo, () => { });
        Check(string.Equals(repo.UIStates.SelectedHistoryBranch, "refs/heads/release/renamed", StringComparison.Ordinal),
            "renaming a selected branch preserves its focus identity");
        Check(histories.Commits.Any(c => string.Equals(c.SHA, moved, StringComparison.Ordinal)), "renamed branch retains its focused history");
        toRename = repo.Branches.Find(b => string.Equals(b.FullName, "refs/heads/release/renamed", StringComparison.Ordinal));
        Git(repo.FullPath, "branch", "-m", "release/renamed", "release/1.55.6");
        repo.RefreshAfterRenameBranch(toRename, "release/1.55.6");
        Refresh(repo, () => { });

        repo.SetBranchFilterMode(master, Models.FilterMode.Included, true, true);
        PumpUntil(() => !histories.IsHistoryFocused && !histories.IsLoading);
        Refresh(repo, () => { });
        CheckSet(histories.Commits.Select(c => c.SHA), graph.MasterHistory, "manual filter selection switches to custom overview history");
        var filteredCommits = histories.Commits.Select(c => c.SHA).ToArray();
        Refresh(repo, () => histories.GraphHighlighting = Models.CommitGraphHighlighting.All);
        CheckSet(histories.Commits.Select(c => c.SHA), filteredCommits, "changing highlight choice preserves existing custom query scope");
        Refresh(repo, () => histories.GraphHighlighting = Models.CommitGraphHighlighting.SelectedBranchOnly);
        CheckSet(histories.Commits.Select(c => c.SHA), filteredCommits, "selected-branch emphasis does not discard manual history filters");
        Refresh(repo, () => histories.IsHistoryFocused = true);
        Check(histories.Commits.Any(c => string.Equals(c.SHA, moved, StringComparison.Ordinal)),
            $"focused view preserves its branch despite custom overview filters (target {repo.UIStates.SelectedHistoryBranch}, refs {string.Join(',', histories.GetHistoryRefs())}, actual {string.Join(',', histories.Commits.Select(c => c.SHA))})");
        Refresh(repo, () => histories.IsHistoryFocused = false);
        CheckSet(histories.Commits.Select(c => c.SHA), graph.MasterHistory, "custom history filters are restored after leaving focus");
        repo.ClearHistoryFilters();
        Refresh(repo, () => histories.IsHistoryFocused = true);
    }

    private static void CheckSettingsMigration(VM.Repository repo)
    {
        var path = Path.Combine(repo.FullPath, "settings-migration");
        Directory.CreateDirectory(path);
        var file = Path.Combine(path, "sourcegit.uistates");
        File.WriteAllText(file, "{\"HistoryDisplayMode\":1,\"GraphHighlighting\":1}");
        var migrated = Models.RepositoryUIStates.Load(path);
        Check(!migrated.IsHistoryFocused && migrated.GraphHighlighting == Models.CommitGraphHighlighting.SelectedBranchOnly,
            "legacy selected-branch display migrates to independent highlighting");
        File.WriteAllText(file, "{\"HistoryDisplayMode\":2,\"GraphHighlighting\":1}");
        migrated = Models.RepositoryUIStates.Load(path);
        Check(migrated.IsHistoryFocused && migrated.GraphHighlighting == Models.CommitGraphHighlighting.SelectedBranchOnly,
            "legacy focused display migrates to focused selected-branch history");
        migrated.IsHistoryFocused = false;
        migrated.Save();
        using var saved = JsonDocument.Parse(File.ReadAllText(file));
        Check(!saved.RootElement.TryGetProperty("HistoryDisplayMode", out _), "new settings omit the obsolete display-mode field");
        var restored = Models.RepositoryUIStates.Load(path);
        Check(!restored.IsHistoryFocused && restored.GraphHighlighting == Models.CommitGraphHighlighting.SelectedBranchOnly,
            "reloading independent settings does not reenable focused history");
    }

    private static void CheckRapidSwitching(VM.Repository repo)
    {
        var histories = repo.Histories;
        var selected = repo.Branches.Find(b => string.Equals(b.FullName, RELEASE_REF, StringComparison.Ordinal));
        var features = repo.Branches.Where(b => b.FullName.StartsWith("refs/heads/feature/", StringComparison.Ordinal)).ToList();
        var index = 0;
        foreach (var feature in features)
        {
            repo.SelectHistoryBranch(feature);
            if (index++ % 5 == 0)
                repo.RefreshBranches();
            File.WriteAllText(Path.Combine(repo.FullPath, "watcher-check.txt"), feature.FullName);
            Dispatcher.UIThread.RunJobs();
        }
        repo.SelectHistoryBranch(selected);
        Refresh(repo, () => { });
        for (var i = 0; i < 25; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(20);
        }
        PumpUntil(() => !histories.IsLoading);
        Check(histories.Commits.Any(c => string.Equals(c.SHA, selected.Head, StringComparison.Ordinal)) &&
            histories.Commits.All(c => !c.Subject.StartsWith("feature ", StringComparison.Ordinal)),
            "watcher-active rapid switching across 30 branches discards stale history refreshes");
        Check(string.Equals(repo.UIStates.SelectedHistoryBranch, RELEASE_REF, StringComparison.Ordinal),
            "watcher refreshes preserve the final selected branch");
    }

    private static void CheckMergedMetadata(List<Models.Commit> commits, Fixture graph, string name)
    {
        CheckSet(commits.Where(c => c.IsMerged).Select(c => c.SHA),
            commits.Select(c => c.SHA).Intersect(graph.MasterHistory, StringComparer.Ordinal), name);
    }

    private static void CheckFocusedEmphasis(VM.Histories histories, string name)
    {
        Check(histories.Commits.All(c => c.IsHighlightedInGraph && c.HistoryOpacity == 1) &&
            histories.Graph.Dots.All(dot => dot.IsHighlighted) && histories.Graph.Paths.All(path => path.IsHighlighted) &&
            histories.Graph.Links.All(link => link.IsHighlighted), name);
    }

    private static Window RenderViews(VM.Repository repo)
    {
        var histories = repo.Histories;
        VM.Preferences.Instance.UseWideDetailsLayoutInHistories = false;
        VM.Preferences.Instance.UseTwoColumnsLayoutInHistories = false;
        var view = new Views.Repository { DataContext = repo };
        var zoom = new LayoutTransformControl { Child = view };
        var window = new Window
        {
            Title = "YAGT Branch History Checks",
            Width = 1200,
            Height = 780,
            Content = zoom,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
        };
        s_renderScreen = DisplayPlacement.PositionOnDisplayOne(window);
        window.Show();
        foreach (var scale in new[] { 1.0, 1.5 })
        {
            zoom.LayoutTransform = new ScaleTransform(scale, scale);
            foreach (var theme in new[] { "Dark", "Light" })
            {
                App.SetTheme(theme, null);
                foreach (var width in new[] { 1200, 800 })
                {
                    window.Width = width;
                    foreach (var presentation in new[]
                    {
                        (Name: "RepositoryOverview", Focused: false, Highlight: Models.CommitGraphHighlighting.All),
                        (Name: "HighlightSelectedBranch", Focused: false, Highlight: Models.CommitGraphHighlighting.SelectedBranchOnly),
                        (Name: "FocusedHistory", Focused: true, Highlight: Models.CommitGraphHighlighting.SelectedBranchOnly),
                    })
                    {
                        Refresh(repo, () =>
                        {
                            histories.IsHistoryFocused = presentation.Focused;
                            histories.GraphHighlighting = presentation.Highlight;
                        });
                        histories.NavigateTo(repo.UIStates.SelectedHistoryBranch.Length > 0
                            ? repo.Branches.Find(b => string.Equals(b.FullName, repo.UIStates.SelectedHistoryBranch, StringComparison.Ordinal)).Head
                            : repo.CurrentBranch.Head);
                        Arrange(window);
                        var controls = view.FindControl<Views.Histories>("DashboardHistories");
                        var branchRow = controls.FindControl<Border>("HistoryFocusBar");
                        Check(branchRow.IsVisible == (presentation.Focused || presentation.Highlight == Models.CommitGraphHighlighting.SelectedBranchOnly),
                            $"compact branch row visibility follows focused or selected-branch state ({presentation.Name}, {theme}, {width}, {scale})");
                        if (branchRow.IsVisible)
                        {
                            Check(branchRow.Bounds.Height <= 36,
                                $"branch controls occupy exactly one row ({presentation.Name}, {theme}, {width}, {scale})");
                            Check(!branchRow.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.StartsWith("History of", StringComparison.Ordinal) ?? false),
                                "compact branch row displays names without a narrative prefix");
                        }
                        RenderControl(window, $"history-{presentation.Name}-{theme}-{width}-{scale * 100}.png");
                        if (presentation.Focused && width == 1200)
                            CheckHistoryControls(repo, view, window, $"{theme}-{scale * 100}");
                    }
                }
            }
        }
        return window;
    }

    private static void CheckHistoryControls(VM.Repository repo, Views.Repository view, Window window, string suffix)
    {
        var histories = view.FindControl<Views.Histories>("DashboardHistories");
        ContextMenu menu = null;
        using var menus = MenuBase.OpenedEvent.AddClassHandler<ContextMenu>((opened, _) => menu = opened);
        var advanced = histories.FindControl<Button>("HistoryAdvancedOptionsButton");
        Check(histories.FindControl<Button>("HistoryDisplayButton") == null && histories.FindControl<Button>("HistoryFocusOptionsButton") == null,
            "compact history exposes no standalone display or settings controls");
        advanced.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpUntil(() => menu?.IsOpen == true);
        var items = menu.Items.OfType<MenuItem>().ToList();
        var focused = items.Single(i => string.Equals(i.Name, "FocusedHistoryMenuItem", StringComparison.Ordinal));
        var selectedBranch = items.Single(i => string.Equals(i.Name, "SelectedBranchHistoryMenuItem", StringComparison.Ordinal));
        var all = items.Single(i => string.Equals(i.Name, "AllHistoryMenuItem", StringComparison.Ordinal));
        var focusedIndex = items.IndexOf(focused);
        var highlightNames = new[]
        {
            "AllHistoryMenuItem",
            "CurrentBranchHistoryMenuItem",
            "SelectedBranchHistoryMenuItem",
            "SelectedCommitsHistoryMenuItem",
            "SelectedCommitsFirstParentHistoryMenuItem",
            "CurrentBranchAndSelectedCommitsHistoryMenuItem",
        };
        Check(focused.Icon != null && focused.IsEnabled && selectedBranch.Icon != null,
            "focused toggle stays usable while the saved overview choice remains checked");
        Check(items.Where(i => highlightNames.Contains(i.Name, StringComparer.Ordinal)).Count() == 6 &&
            items.Where(i => highlightNames.Contains(i.Name, StringComparer.Ordinal)).All(i => !i.IsEnabled),
            "all six overview highlight choices are disabled while focused history is on");
        Check(items.Single(i => string.Equals(i.Name, "MatchingCommitSubjectsHistoryMenuItem", StringComparison.Ordinal)).IsEnabled &&
            items.Single(i => string.Equals(i.Name, "IncludeCurrentHistoryBranchMenuItem", StringComparison.Ordinal)).IsEnabled &&
            items.Single(i => string.Equals(i.Tag as string, "--first-parent", StringComparison.Ordinal)).IsEnabled,
            "subject matching, checked-out branch inclusion, and first-parent remain enabled while focused");
        Check(items.Single(i => string.Equals(i.Tag as string, "--date-order", StringComparison.Ordinal)).IsEnabled &&
            items.Single(i => string.Equals(i.Tag as string, "--topo-order", StringComparison.Ordinal)).IsEnabled &&
            items.Single(i => string.Equals(i.Header as string, App.Text("Repository.HistoriesLayout.Horizontal"), StringComparison.Ordinal)).IsEnabled &&
            items.Single(i => string.Equals(i.Header as string, App.Text("Histories.Header.Author"), StringComparison.Ordinal)).IsEnabled,
            "sorting, layout, and column choices stay enabled in focused history");
        Check(!items.Single(i => string.Equals(i.Tag as string, "--reflog", StringComparison.Ordinal)).IsEnabled,
            "show lost commits remains disabled because it would broaden the focused scope");
        Check(string.Equals(items[focusedIndex + 1].Header as string, App.Text("Histories.HighlightMatchingSubjects"), StringComparison.Ordinal),
            "focused history follows highlight choices without a redundant separator before subject matching");
        Check(items.Count(i => string.Equals(i.Header as string, App.Text("Repository.ShowFirstParentOnly"), StringComparison.Ordinal)) == 1,
            "advanced menu contains a single first-parent setting");
        Check(items.Any(i => i.Header is TextBlock label && string.Equals(label.Text, App.Text("Histories.HistoryScope"), StringComparison.Ordinal)),
            "branch inclusion settings have their own group in the existing menu");
        RenderControl(TopLevel.GetTopLevel(menu), "history-advanced-menu-" + suffix + ".png");
        Refresh(repo, () => focused.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)));
        Check(!repo.Histories.IsHistoryFocused && repo.Histories.GraphHighlighting == Models.CommitGraphHighlighting.SelectedBranchOnly,
            "focused menu checkbox restores full context without changing the highlight preference");
        menu.Close();
        advanced.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpUntil(() => menu?.IsOpen == true);
        items = menu.Items.OfType<MenuItem>().ToList();
        all = items.Single(i => string.Equals(i.Name, "AllHistoryMenuItem", StringComparison.Ordinal));
        selectedBranch = items.Single(i => string.Equals(i.Name, "SelectedBranchHistoryMenuItem", StringComparison.Ordinal));
        Check(items.Where(i => highlightNames.Contains(i.Name, StringComparer.Ordinal)).All(i => i.IsEnabled) && selectedBranch.Icon != null,
            "leaving focus enables overview choices and restores the previously selected radio");
        Refresh(repo, () => all.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)));
        menu.Close();
        Arrange(window);
        Check(!histories.FindControl<Border>("HistoryFocusBar").IsVisible,
            "ordinary overview hides the branch row after choosing a normal highlight option");
        advanced.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpUntil(() => menu?.IsOpen == true);
        items = menu.Items.OfType<MenuItem>().ToList();
        var currentBranch = items.Single(i => string.Equals(i.Name, "CurrentBranchHistoryMenuItem", StringComparison.Ordinal));
        Refresh(repo, () => currentBranch.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)));
        menu.Close();
        advanced.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpUntil(() => menu?.IsOpen == true);
        items = menu.Items.OfType<MenuItem>().ToList();
        focused = items.Single(i => string.Equals(i.Name, "FocusedHistoryMenuItem", StringComparison.Ordinal));
        Refresh(repo, () => focused.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)));
        menu.Close();
        Arrange(window);
        CheckFocusedEmphasis(repo.Histories, "focused branch remains fully emphasized despite saved Current Branch Only preference");
        RenderControl(window, "history-focused-current-preference-" + suffix + ".png");
        advanced.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpUntil(() => menu?.IsOpen == true);
        items = menu.Items.OfType<MenuItem>().ToList();
        currentBranch = items.Single(i => string.Equals(i.Name, "CurrentBranchHistoryMenuItem", StringComparison.Ordinal));
        Check(currentBranch.Icon != null && !currentBranch.IsEnabled &&
            repo.Histories.GraphHighlighting == Models.CommitGraphHighlighting.CurrentBranchOnly,
            "focused menu shows the saved Current Branch Only choice checked and disabled");
        RenderControl(TopLevel.GetTopLevel(menu), "history-focused-current-preference-menu-" + suffix + ".png");
        focused = items.Single(i => string.Equals(i.Name, "FocusedHistoryMenuItem", StringComparison.Ordinal));
        Refresh(repo, () => focused.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)));
        menu.Close();
        advanced.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpUntil(() => menu?.IsOpen == true);
        items = menu.Items.OfType<MenuItem>().ToList();
        selectedBranch = items.Single(i => string.Equals(i.Name, "SelectedBranchHistoryMenuItem", StringComparison.Ordinal));
        Refresh(repo, () => selectedBranch.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)));
        menu.Close();
        Check(repo.Histories.IsSelectedBranchHighlighting && !repo.Histories.IsHistoryFocused,
            "selected branch radio enables emphasis without forcing focused history");
        Refresh(repo, () => repo.Histories.IsHistoryFocused = true);

        var selected = repo.Branches.Find(b => string.Equals(b.FullName, RELEASE_REF, StringComparison.Ordinal));
        Refresh(repo, () => repo.SelectHistoryBranch(repo.CurrentBranch));
        advanced.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpUntil(() => menu?.IsOpen == true);
        items = menu.Items.OfType<MenuItem>().ToList();
        Check(items.All(i => !string.Equals(i.Name, "SelectedBranchHistoryMenuItem", StringComparison.Ordinal)) &&
            items.Single(i => string.Equals(i.Name, "CurrentBranchHistoryMenuItem", StringComparison.Ordinal)) is { Icon: not null, IsEnabled: false },
            "selected=current hides the redundant radio and keeps the equivalent current-branch entry checked but disabled in focus");
        Check(repo.Histories.IsSelectedBranchHighlighting, "coalesced menu preserves follow-selection preference");
        menu.Close();
        Refresh(repo, () => repo.SelectHistoryBranch(selected));

        Popup popup = null;
        using var popups = Popup.IsOpenProperty.Changed.AddClassHandler<Popup>((opened, _) =>
        {
            if (opened.IsOpen)
                popup = opened;
        });
        histories.FindControl<Button>("AddHistoryBranchButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpUntil(() => popup?.IsOpen == true && popup.Child != null);
        Arrange(window);
        var picker = popup.Child.GetVisualDescendants().OfType<Views.HistoryBranchPicker>().Single();
        Check(picker.VisibleBranches.Count > 30, "branch picker includes local and remote branches");
        RenderControl(TopLevel.GetTopLevel(picker), "history-add-branch-" + suffix + ".png");
        var filter = picker.FindControl<TextBox>("FilterBox");
        filter.Text = "remote-only";
        Dispatcher.UIThread.RunJobs();
        Check(picker.VisibleBranches.Count == 1 && string.Equals(picker.VisibleBranches[0].FullName, REMOTE_REF, StringComparison.Ordinal),
            "branch picker filters remote branches");
        filter.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        PumpUntil(() => repo.Histories.AdditionalHistoryBranches.Any(b => string.Equals(b.FullName, REMOTE_REF, StringComparison.Ordinal)));
        Check(!popup.IsOpen, "Enter adds the selected branch and closes the picker");
        Arrange(window);
        RenderControl(window, "history-additional-branch-" + suffix + ".png");
        var remove = histories.GetVisualDescendants().OfType<Button>().Single(b => b.DataContext is Models.Branch branch && string.Equals(branch.FullName, REMOTE_REF, StringComparison.Ordinal));
        remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpUntil(() => repo.Histories.AdditionalHistoryBranches.Count == 0);
        Check(true, "additional branch chip removes its branch");

        Refresh(repo, () =>
        {
            repo.Histories.AddHistoryBranch(repo.CurrentBranch);
            repo.Histories.IncludeCurrentBranchInHistory = true;
        });
        Arrange(window);
        var currentChip = histories.FindControl<Border>("IncludedCurrentHistoryBranchChip");
        Check(currentChip.IsVisible, "explicit and implicit checked-out branch inclusion share one chip");
        currentChip.GetVisualDescendants().OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpUntil(() => !repo.Histories.IncludeCurrentBranchInHistory &&
            !repo.UIStates.AdditionalHistoryBranches.Contains(repo.CurrentBranch.FullName, StringComparer.Ordinal));
        Arrange(window);
        Check(!currentChip.IsVisible, "removing the checked-out chip clears both explicit and implicit inclusion");

        if (string.Equals(suffix, "Dark-100", StringComparison.Ordinal))
            CheckSingleRowOverflow(repo, view, window);
    }

    private static void CheckSingleRowOverflow(VM.Repository repo, Views.Repository view, Window window)
    {
        using var lockWatcher = repo.LockWatcher();
        var histories = view.FindControl<Views.Histories>("DashboardHistories");
        var branches = repo.Branches.Where(b => b.FullName.StartsWith("refs/heads/feature/", StringComparison.Ordinal)).Take(12).ToList();
        var longName = "feature/" + new string('a', 100);
        Git(repo.FullPath, "branch", longName, "master");
        repo.RefreshBranches();
        PumpUntil(() => repo.Branches.Any(b => string.Equals(b.Name, longName, StringComparison.Ordinal)));
        var longBranch = repo.Branches.Find(b => string.Equals(b.Name, longName, StringComparison.Ordinal));
        branches.Add(longBranch);
        foreach (var branch in branches)
            repo.Histories.AddHistoryBranch(branch);
        Refresh(repo, () => repo.Histories.IncludeCurrentBranchInHistory = true);
        var oldWidth = window.Width;
        window.Width = 800;
        Arrange(window);
        var bar = histories.FindControl<Border>("HistoryFocusBar");
        var scroll = bar.GetVisualDescendants().OfType<ScrollViewer>().Single();
        var add = histories.FindControl<Button>("AddHistoryBranchButton");
        var addPosition = add.TranslatePoint(default, histories);
        Check(bar.Bounds.Height <= 36 && scroll.Extent.Width > scroll.Viewport.Width,
            "many included branches overflow horizontally instead of adding another row");
        Check(addPosition.HasValue && addPosition.Value.X + add.Bounds.Width <= histories.Bounds.Width + 1,
            "Add branch remains accessible outside the horizontally overflowing chips");
        Check(histories.FindControl<Border>("IncludedCurrentHistoryBranchChip").IsVisible,
            "included checked-out branch appears as a chip in the same compact row");
        Check(bar.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.DataContext is Models.Branch b && string.Equals(b.FullName, longBranch.FullName, StringComparison.Ordinal))
            .All(t => t.Bounds.Width <= 241), "long branch names ellipsize within the single-row strip");
        RenderControl(window, "history-single-row-overflow-Dark-100.png");
        foreach (var branch in branches)
            repo.Histories.RemoveHistoryBranch(branch);
        Refresh(repo, () => repo.Histories.IncludeCurrentBranchInHistory = false);
        Git(repo.FullPath, "branch", "-D", longName);
        repo.RefreshBranches();
        PumpUntil(() => repo.Branches.All(b => !string.Equals(b.Name, longName, StringComparison.Ordinal)));
        window.Width = oldWidth;
        Arrange(window);
    }

    private static void RenderControl(Control control, string name)
    {
        PumpUntil(() => control.Bounds.Width > 0 && control.Bounds.Height > 0);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(control.Bounds.Width), (int)Math.Ceiling(control.Bounds.Height)));
        bitmap.Render(control);
        var path = Path.Combine(AppContext.BaseDirectory, name);
        bitmap.Save(path);
        Console.WriteLine("SCREENSHOT: " + path);
    }

    private static void CheckPersistence(VM.Repository repo)
    {
        repo.Histories.IsHistoryFocused = true;
        repo.Histories.GraphHighlighting = Models.CommitGraphHighlighting.SelectedBranchOnly;
        repo.Histories.IncludeCurrentBranchInHistory = true;
        repo.UIStates.Save();
        using var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(repo.GitDir, "sourcegit.uistates")));
        Check(saved.RootElement.GetProperty("IsHistoryFocused").GetBoolean(), "independent focused-history checkbox is persisted");
        Check(saved.RootElement.GetProperty("GraphHighlighting").GetInt32() == (int)Models.CommitGraphHighlighting.SelectedBranchOnly,
            "selected-branch highlight preference is persisted independently");
        Check(string.Equals(saved.RootElement.GetProperty("SelectedHistoryBranch").GetString(), RELEASE_REF, StringComparison.Ordinal), "named branch target is persisted");
        Check(saved.RootElement.GetProperty("IncludeCurrentBranchInHistory").GetBoolean(), "checked-out branch inclusion is persisted");
    }

    private static List<Models.Commit> Query(string path, string historyParams, int limit)
    {
        return Await(new Commands.QueryCommits(path, $"-{limit} {historyParams}", false).GetResultAsync());
    }

    private static void Refresh(VM.Repository repo, Action change)
    {
        var previous = repo.Histories.Commits;
        change();
        repo.RefreshCommits();
        PumpUntil(() => !repo.Histories.IsLoading && !ReferenceEquals(previous, repo.Histories.Commits));
        Dispatcher.UIThread.RunJobs();
    }

    private static T Await<T>(Task<T> task)
    {
        PumpUntil(() => task.IsCompleted);
        return task.GetAwaiter().GetResult();
    }

    private static void Arrange(Window window)
    {
        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(20);
        }
        window.Measure(new Size(window.Width, window.Height));
        window.Arrange(new Rect(0, 0, window.Width, window.Height));
        Dispatcher.UIThread.RunJobs();
    }

    private static void PumpUntil(Func<bool> condition)
    {
        var timer = Stopwatch.StartNew();
        do
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(20);
            if (timer.Elapsed.TotalSeconds > 30)
                throw new InvalidOperationException("Timed out waiting for history state");
        } while (!condition());
    }

    private static void CheckSet(IEnumerable<string> actual, IEnumerable<string> expected, string name)
    {
        var set = actual.ToHashSet(StringComparer.Ordinal);
        var wanted = expected.ToHashSet(StringComparer.Ordinal);
        Check(set.SetEquals(wanted), $"{name} (actual {set.Count}, expected {wanted.Count})");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition)
            throw new InvalidOperationException("FAIL: " + name);
        s_checks++;
        Console.WriteLine("PASS: " + name);
    }

    private static string Commit(string path, string tree, string message, long timestamp, params string[] parents)
    {
        var messageFile = Path.Combine(path, "fixture-message.txt");
        File.WriteAllText(messageFile, message + Environment.NewLine);
        var arguments = new List<string> { "commit-tree", tree };
        foreach (var parent in parents)
        {
            arguments.Add("-p");
            arguments.Add(parent);
        }
        arguments.Add("-F");
        arguments.Add(messageFile);
        return Git(path, arguments.ToArray(), timestamp: timestamp);
    }

    private static string Git(string path, string command, string input)
    {
        return Git(path, [command], input: input);
    }

    private static string Git(string path, params string[] arguments)
    {
        return Git(path, arguments, input: null);
    }

    private static string Git(string path, string[] arguments, string input = null, long? timestamp = null)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = path,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = input != null,
        };
        foreach (var arg in arguments)
            start.ArgumentList.Add(arg);
        if (timestamp.HasValue)
        {
            start.Environment["GIT_AUTHOR_DATE"] = $"@{timestamp.Value} +0000";
            start.Environment["GIT_COMMITTER_DATE"] = $"@{timestamp.Value} +0000";
        }
        using var process = Process.Start(start);
        if (input != null)
        {
            process.StandardInput.Write(input);
            process.StandardInput.Close();
        }
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(output, error);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error.Result}");
        return output.Result.Trim();
    }

    private sealed record Fixture(string Tree, string Root, string Common, string ReleaseOne, string ReleaseTwo,
        string MergedSide, string Merge, string ReleaseTip, string MasterOne, string RepeatedSubject, string MasterTip,
        string RemoteOnly, string Upstream, string UnrelatedTag, string FeatureTip)
    {
        public string[] ReleaseHistory => [Root, Common, ReleaseOne, ReleaseTwo, MergedSide, Merge, ReleaseTip];
        public string[] ReleaseFirstParentHistory => [Root, Common, ReleaseOne, ReleaseTwo, Merge, ReleaseTip];
        public string[] MasterHistory => [Root, Common, MasterOne, RepeatedSubject, MasterTip];
    }

    private const string RELEASE_REF = "refs/heads/release/1.55.6";
    private const string REMOTE_REF = "refs/remotes/origin/remote-only";
    private static PixelRect s_renderScreen;
    private static int s_checks;
}
