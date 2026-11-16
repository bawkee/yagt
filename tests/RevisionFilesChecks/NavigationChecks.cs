using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SourceGit;
using M = SourceGit.Models;
using V = SourceGit.Views;
using VM = SourceGit.ViewModels;

internal static class NavigationChecks
{
    public static void Run()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "yagt-navigation-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        var repoPath = Path.Combine(fixture, "repo");
        Directory.CreateDirectory(repoPath);
        var previousTrace = Environment.GetEnvironmentVariable("GIT_TRACE2_EVENT");
        var tracePath = Path.Combine(fixture, "git-trace.jsonl");
        VM.Repository repo = null;
        Window window = null;
        Exception failure = null;
        var completed = false;
        using var stop = new CancellationTokenSource();
        try
        {
            SourceGit.Native.OS.BasicDirectories.ConfigDir = fixture;
            SourceGit.Native.OS.BasicDirectories.CacheDir = fixture;
            File.WriteAllText(Path.Combine(fixture, "preference.json"), "{}");
            var expected = CreateRepository(repoPath, fixture);
            App.BuildAvaloniaApp().SetupWithoutStarting();
            App.SetLocale("en_US");
            App.SetTheme("Dark", null);
            Environment.SetEnvironmentVariable("GIT_TRACE2_EVENT", tracePath);

            repo = new VM.Repository(false, repoPath, Path.Combine(repoPath, ".git"));
            repo.Open();
            var view = new V.Repository { DataContext = repo };
            window = new Window { Title = "YAGT Files Navigation Checks", Width = 1100, Height = 800, Content = view };
            window.Show();
            var owner = window;
            var repository = repo;
            var checks = 0;
            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    checks = await RunAsync(repository, view, owner, tracePath, expected);
                }
                catch (Exception e)
                {
                    failure = e;
                }
                finally
                {
                    try
                    {
                        owner.Close();
                        repository.Close();
                    }
                    catch (Exception e)
                    {
                        failure ??= e;
                    }
                    finally
                    {
                        window = null;
                        repo = null;
                        stop.Cancel();
                    }
                }
            });

            // Exercise the native dispatcher instead of draining all priorities with RunJobs().
            Dispatcher.UIThread.MainLoop(stop.Token);
            if (failure != null)
                throw new InvalidOperationException($"Navigation checks failed; fixture retained at {fixture}", failure);

            completed = true;
            Console.WriteLine($"PASS: {checks} native Files navigation checks");
        }
        finally
        {
            window?.Close();
            repo?.Close();
            Environment.SetEnvironmentVariable("GIT_TRACE2_EVENT", previousTrace);
            if (completed)
                DeleteFixture(fixture);
        }
    }

    private static async Task<int> RunAsync(VM.Repository repo, V.Repository view, Window window, string tracePath,
        Dictionary<string, ExpectedRevision> expected)
    {
        var checks = 0;
        await WaitAsync(() => !repo.Histories.IsLoading && repo.Histories.Commits.Count == expected.Count, "history load");
        var list = view.GetVisualDescendants().OfType<V.HistoriesCommitList>().Single();
        list.SelectedItem = repo.Histories.Commits[0];
        await WaitAsync(() => repo.Histories.DetailContext is VM.CommitDetail, "initial detail");
        var detail = (VM.CommitDetail)repo.Histories.DetailContext;
        detail.ActiveTabIndex = 2;
        await WaitAsync(() => HasCurrentTree(view, detail, expected), "initial Files tree");
        await Task.Delay(250);

        Check(SynchronizationContext.Current is AvaloniaSynchronizationContext, "native dispatcher context is installed");
        await CheckBurstAsync(false);

        var tree = Tree(view);
        tree.FindControl<V.RevisionFileRowsListBox>("RowsList").SelectedItem =
            tree.Rows.Single(x => x.Backend.Path.Equals("file.cs", StringComparison.Ordinal));
        await WaitAsync(() => HasCurrentPreview(detail, expected), "selected text preview");
        await CheckBurstAsync(true);

        var before = TreeQueries(tracePath).Count;
        detail.ActiveTabIndex = 0;
        SelectNext(list, repo);
        detail = (VM.CommitDetail)repo.Histories.DetailContext;
        await Task.Delay(350);
        Check(TreeQueries(tracePath).Count == before, "hidden Files tab performs no tree query");
        detail.ActiveTabIndex = 2;
        await WaitAsync(() => HasCurrentTree(view, detail, expected) && HasCurrentPreview(detail, expected), "reopened Files tab");
        Check(true, "tab reopening restores the latest commit and preview");

        SelectNext(list, repo);
        detail = (VM.CommitDetail)repo.Histories.DetailContext;
        tree = Tree(view);
        var search = view.GetVisualDescendants().OfType<V.RevisionFiles>().Single().FindControl<TextBox>("TxtSearchRevisionFiles");
        search.Text = "file.cs";
        await tree.SetSearchResultAsync("file.cs");
        await Task.Delay(350);
        Check(tree.Rows.Count == 1 && tree.Rows[0].Backend.Path.Equals("file.cs", StringComparison.Ordinal) &&
            HasCurrentPreview(detail, expected), "queued revision refresh cannot overwrite an explicit file search");
        search.Text = string.Empty;
        await WaitAsync(() => HasCurrentTree(view, detail, expected) && HasCurrentPreview(detail, expected), "cleared search");
        Check(true, "clearing search restores current tree and selection");

        var validCommit = detail.Commit;
        before = TreeQueries(tracePath).Count;
        detail.Commit = new M.Commit { SHA = new string('1', 40), Subject = "Missing revision" };
        await Task.Delay(900);
        Check(TreeQueries(tracePath).Count == before + 2, "failed tree read retries once without entering a refresh loop");
        detail.Commit = validCommit;
        await WaitAsync(() => HasCurrentTree(view, detail, expected) && HasCurrentPreview(detail, expected), "recovery after failed read");
        Check(true, "a failed read does not block the next valid revision");

        before = TreeQueries(tracePath).Count;
        window.Content = null;
        SelectNext(list, repo);
        detail = (VM.CommitDetail)repo.Histories.DetailContext;
        await Task.Delay(350);
        Check(TreeQueries(tracePath).Count == before, "detached Files view performs no tree query");
        window.Content = view;
        await WaitAsync(() => HasCurrentTree(view, detail, expected) && HasCurrentPreview(detail, expected), "reattached Files view");
        Check(true, "reattaching resumes latest tree and preview");
        return checks;

        async Task CheckBurstAsync(bool withPreview)
        {
            list.Focus();
            var startCount = TreeQueries(tracePath).Count;
            var watch = Stopwatch.StartNew();
            var direction = Key.Down;
            var changes = 0;
            while (watch.ElapsedMilliseconds < 10000)
            {
                await Task.Delay(25);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var index = list.SelectedIndex;
                    if (index == 0)
                        direction = Key.Down;
                    else if (index == repo.Histories.Commits.Count - 1)
                        direction = Key.Up;

                    var previous = ((VM.CommitDetail)repo.Histories.DetailContext).Commit.SHA;
                    list.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = direction });
                    detail = (VM.CommitDetail)repo.Histories.DetailContext;
                    if (!detail.Commit.SHA.Equals(previous, StringComparison.Ordinal))
                        changes++;
                }, DispatcherPriority.Input);
            }

            var released = DateTimeOffset.UtcNow;
            var mode = withPreview ? "text preview" : "blank preview";
            Check(changes >= 100, $"{mode}: sustained native arrow events change history selection");
            Check(TreeQueries(tracePath).Count == startCount, $"{mode}: no tree query starts during sustained navigation");
            await Task.Delay(140);
            Check(TreeQueries(tracePath).Count == startCount, $"{mode}: no tree query starts in the first 140 ms after release");
            await WaitAsync(() => TreeQueries(tracePath).Count > startCount && HasCurrentTree(view, detail, expected) &&
                (!withPreview || HasCurrentPreview(detail, expected)), $"{mode}: final settled selection");

            var queries = TreeQueries(tracePath);
            var delay = (queries[startCount] - released).TotalMilliseconds;
            Check(delay >= 180, $"{mode}: final tree query waits for the 200 ms debounce ({delay:F0} ms observed)");
            await Task.Delay(150);
            Check(TreeQueries(tracePath).Count == startCount + 1, $"{mode}: exactly one tree query loads the final selection");
            Check(HasCurrentTree(view, detail, expected) && (!withPreview || HasCurrentPreview(detail, expected)),
                $"{mode}: current tree and preview recover after release");

            SelectNext(list, repo);
            detail = (VM.CommitDetail)repo.Histories.DetailContext;
            await WaitAsync(() => HasCurrentTree(view, detail, expected) && (!withPreview || HasCurrentPreview(detail, expected)),
                $"{mode}: subsequent selection");
            Check(true, $"{mode}: subsequent navigation still loads");
        }

        void Check(bool condition, string description)
        {
            if (!condition)
                throw new InvalidOperationException("FAIL: " + description);
            checks++;
            Console.WriteLine("PASS: " + description);
        }
    }

    private static void SelectNext(V.HistoriesCommitList list, VM.Repository repo)
    {
        var current = (VM.CommitDetail)repo.Histories.DetailContext;
        var index = repo.Histories.Commits.IndexOf(current.Commit);
        list.SelectedItem = repo.Histories.Commits[(index + 1) % repo.Histories.Commits.Count];
    }

    private static V.RevisionFileTreeView Tree(Control view) => view.GetVisualDescendants().OfType<V.RevisionFileTreeView>().FirstOrDefault();

    private static bool HasCurrentTree(Control view, VM.CommitDetail detail, Dictionary<string, ExpectedRevision> expected)
    {
        var tree = Tree(view);
        return tree != null && tree.Rows.Any(x => x.Backend.Path.Equals(expected[detail.Commit.SHA].Marker, StringComparison.Ordinal));
    }

    private static bool HasCurrentPreview(VM.CommitDetail detail, Dictionary<string, ExpectedRevision> expected) =>
        detail.ViewRevisionFileContent is M.RevisionTextFile text && text.Content.Equals(expected[detail.Commit.SHA].Content, StringComparison.Ordinal);

    private static async Task WaitAsync(Func<bool> condition, string description)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.ElapsedMilliseconds > 15000)
                throw new TimeoutException("Timed out waiting for " + description);
            await Task.Delay(10);
        }
    }

    private static List<DateTimeOffset> TreeQueries(string tracePath)
    {
        var queries = new List<DateTimeOffset>();
        if (!File.Exists(tracePath))
            return queries;

        using var stream = new FileStream(tracePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.TryGetProperty("event", out var kind) && kind.ValueEquals("start") &&
                    root.TryGetProperty("argv", out var argv) &&
                    argv.EnumerateArray().Any(x => x.ValueEquals("ls-tree")))
                {
                    queries.Add(DateTimeOffset.Parse(root.GetProperty("time").GetString(), CultureInfo.InvariantCulture));
                }
            }
            catch (JsonException)
            {
                // Another Git process may still be appending the last record.
            }
        }

        return queries;
    }

    private static Dictionary<string, ExpectedRevision> CreateRepository(string path, string fixture)
    {
        Git(path, "init", "-b", "main");
        Git(path, "config", "user.name", "Navigation Check");
        Git(path, "config", "user.email", "navigation@example.com");
        Git(path, "config", "commit.gpgsign", "false");
        Git(path, "config", "core.autocrlf", "false");
        Git(path, "config", "core.hooksPath", fixture);
        var expected = new Dictionary<string, ExpectedRevision>(StringComparer.Ordinal);
        for (var i = 0; i < 12; i++)
        {
            if (i > 0)
                File.Delete(Path.Combine(path, $"revision-{i - 1:D2}.txt"));
            var marker = $"revision-{i:D2}.txt";
            var content = new StringBuilder($"// revision {i}\nnamespace Checks;\n");
            for (var line = 0; line < 5000; line++)
                content.AppendLine($"public class Item{line} {{ public string Value => \"revision {i}\"; }}");
            File.WriteAllText(Path.Combine(path, marker), i.ToString(CultureInfo.InvariantCulture));
            File.WriteAllText(Path.Combine(path, "file.cs"), content.ToString());
            Git(path, "add", "-A");
            Git(path, "commit", "-m", $"Fixture {i}");
            var sha = Git(path, "rev-parse", "HEAD").Trim();
            expected.Add(sha, new ExpectedRevision(marker, Git(path, "show", $"{sha}:file.cs")));
        }

        return expected;
    }

    private static string Git(string path, params string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = path,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start);
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(output, errors);
        if (process.ExitCode != 0)
            throw new InvalidOperationException(errors.Result);
        return output.Result;
    }

    private static void DeleteFixture(string fixture)
    {
        var temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(fixture).StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Fixture must remain inside the temporary directory");
        foreach (var file in Directory.EnumerateFiles(fixture, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(fixture, true);
    }

    private sealed record ExpectedRevision(string Marker, string Content);
}
