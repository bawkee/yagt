using System.Diagnostics;
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
using Views = SourceGit.Views;
using VM = SourceGit.ViewModels;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var fixture = Path.Combine(Path.GetTempPath(), "yagt-layout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        SourceGit.Native.OS.BasicDirectories.ConfigDir = fixture;
        SourceGit.Native.OS.BasicDirectories.CacheDir = fixture;
        VM.Repository repo = null;
        Window window = null;
        try
        {
            Check(new VM.Preferences().UseWideDetailsLayoutInHistories, "Wide Details is the default for new preferences");
            File.WriteAllText(Path.Combine(fixture, "preference.json"), "{\"UseTwoColumnsLayoutInHistories\":false}");
            App.BuildAvaloniaApp().SetupWithoutStarting();
            App.SetLocale("en_US");
            Git(fixture, "init", "-b", "main");
            Git(fixture, "config", "user.name", "Layout Test");
            Git(fixture, "config", "user.email", "layout@example.com");
            Git(fixture, "config", "commit.gpgsign", "false");
            Git(fixture, "config", "core.hooksPath", fixture);
            Git(fixture, "commit", "--allow-empty", "-m", "matching subject");
            Git(fixture, "commit", "--allow-empty", "-m", "matching subject");
            for (var i = 0; i < 150; i++)
                Git(fixture, "branch", $"branch-{i:D3}");

            repo = new VM.Repository(false, fixture, Path.Combine(fixture, ".git"));
            repo.Open();
            var pref = VM.Preferences.Instance;
            pref.SetCanModify();
            Check(!pref.UseWideDetailsLayoutInHistories, "existing Vertical preferences are preserved");
            pref.UseWideDetailsLayoutInHistories = true;
            var view = new Views.Repository { DataContext = repo };
            var zoom = new LayoutTransformControl { Child = view };
            window = new Window { Title = "YAGT Layout Checks", Width = 1100, Height = 800, Content = zoom };
            window.Show();
            PumpUntil(() => !repo.Histories.IsLoading && repo.Histories.Commits.Count == 2 && repo.LocalBranchesCount == 151);
            repo.Histories.SelectedCommits = [repo.Histories.Commits[0]];
            PumpUntil(() => repo.Histories.DetailContext is VM.CommitDetail);

            foreach (var scale in new[] { 1.0, 1.5 })
            {
                zoom.LayoutTransform = new ScaleTransform(scale, scale);
                foreach (var theme in new[] { "Dark", "Light" })
                {
                    App.SetTheme(theme, null);
                    foreach (var width in new[] { 1100, 800 })
                    {
                        window.Width = width;
                        CheckLayouts(repo, view, window);
                        if (args.Contains("--render", StringComparer.Ordinal))
                            Render(window, $"wide-details-{theme}-{width}-{scale * 100}.png");
                        CheckShortSidebar(repo, view, window);
                        if (args.Contains("--render", StringComparer.Ordinal))
                            Render(window, $"short-history-{theme}-{width}-{scale * 100}.png");
                    }
                }
            }

            CheckMenu(view, window);
            pref.UseWideDetailsLayoutInHistories = true;
            pref.Save();
            using var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixture, "preference.json")));
            Check(saved.RootElement.GetProperty("UseWideDetailsLayoutInHistories").GetBoolean(), "selected layout is persisted");
            Check(!saved.RootElement.GetProperty("UseTwoColumnsLayoutInHistories").GetBoolean(), "saved layouts are mutually exclusive");
            Console.WriteLine($"PASS: {s_checks} layout checks");
        }
        finally
        {
            window?.Close();
            repo?.Close();
            var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(fixture).StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Fixture must stay inside the temporary directory");
            foreach (var file in Directory.EnumerateFiles(fixture, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(fixture, true);
        }
    }

    private static void CheckLayouts(VM.Repository repo, Views.Repository view, Window window)
    {
        var pref = VM.Preferences.Instance;
        var layout = view.FindControl<Views.RepositoryLayout>("MainBody");
        var details = view.FindControl<Views.HistoriesDetails>("HistoryDetails");
        var histories = view.FindControl<Views.Histories>("DashboardHistories");
        var sidebar = view.FindControl<Grid>("Sidebar");
        var sidebarScroller = view.FindControl<ScrollViewer>("SidebarScroller");
        var search = view.FindControl<Views.CommitSearchView>("CommitSearchPanel");
        var list = histories.FindControl<Views.HistoriesCommitList>("CommitListContainer");
        var selected = repo.Histories.SelectedCommits[0];
        var detailContext = repo.Histories.DetailContext;
        var parent = sidebar.Parent;
        var searchParent = search.Parent;

        repo.Histories.TopArea = new GridLength(1, GridUnitType.Star);
        repo.Histories.BottomArea = new GridLength(1, GridUnitType.Star);
        repo.Histories.IsCollapseDetails = false;
        pref.UseWideDetailsLayoutInHistories = true;
        Arrange(window);
        Check(layout.IsWideDetailsActive, "saved Wide Details layout is active");
        Check(Near(details.Bounds.Width, layout.Bounds.Width), "wide details span the whole content area");
        Check(Near(sidebarScroller.Bounds.Height + 3 + details.Bounds.Height, layout.Bounds.Height), $"sidebar ends above wide details (sidebar={sidebarScroller.Bounds.Height}, details={details.Bounds.Height}, total={layout.Bounds.Height}, row={Grid.GetRow(sidebarScroller)}, span={Grid.GetRowSpan(sidebarScroller)})");
        Check(list.Bounds.Width < details.Bounds.Width - 100, "sidebar reserves width beside history");
        repo.Histories.HighlightMatchingCommitSubjects = true;
        Arrange(window);
        Check(list.GetVisualDescendants().OfType<Border>().Any(x => x.Classes.Contains("same_subject")), "matching subject highlighting survives the layout change");

        var detailWidth = details.Bounds.Width;
        var historyWidth = list.Bounds.Width;
        var sidebarWidth = view.SidebarWidth;
        layout.ColumnDefinitions[0].Width = new GridLength(sidebarWidth.Value + 40);
        Arrange(window);
        Check(Near(view.SidebarWidth.Value, sidebarWidth.Value + 40), "sidebar resizing updates saved width");
        Check(Near(details.Bounds.Width, detailWidth) && Near(list.Bounds.Width, historyWidth - 40), $"sidebar resizing leaves wide details unchanged (details={detailWidth}->{details.Bounds.Width}, history={historyWidth}->{list.Bounds.Width})");
        view.SidebarWidth = sidebarWidth;

        repo.Histories.IsCollapseDetails = true;
        Arrange(window);
        Check(Near(details.Bounds.Height, 28) && !details.IsDetailsPanelExpanded, "details collapse to a full width tab bar");
        var commitDetail = details.GetVisualDescendants().OfType<Views.CommitDetail>().Single();
        var tabs = commitDetail.GetVisualDescendants().OfType<TabControl>().First();
        var header = (Border)((TabItem)tabs.Items[0]).Header;
        header.RaiseEvent(new PointerPressedEventArgs(header, new Pointer(1, PointerType.Mouse, true), window, default, 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None));
        Arrange(window);
        Check(!repo.Histories.IsCollapseDetails && details.IsDetailsPanelExpanded, "clicking a collapsed detail tab expands it");

        view.IsNormalSidebarVisible = false;
        Arrange(window);
        Check(Near(list.Bounds.Width, details.Bounds.Width), "hidden sidebar releases history width");
        view.IsCommitSearchPanelVisible = true;
        repo.Histories.IsSearchingCommits = true;
        Arrange(window);
        Check(ReferenceEquals(search.DataContext, repo.Histories.SearchCommitContext), "search retains its context");
        var input = search.FindControl<TextBox>("TxtSearchCommitsBox");
        input.Text = "matching subject";
        var query = repo.Histories.SearchCommitContext.StartSearchAsync();
        PumpUntil(() => query.IsCompleted);
        Check(query.GetAwaiter().GetResult() && repo.Histories.SearchCommitContext.Results.Count == 2, "asynchronous commit search completes");
        var results = search.FindControl<ListBox>("SearchResultsBox");
        results.SelectedIndex = 0;
        var result = results.SelectedItem;
        input.Focus();

        pref.UseTwoColumnsLayoutInHistories = true;
        Arrange(window);
        Check(!pref.UseWideDetailsLayoutInHistories, "Horizontal disables Wide Details");
        Check(Near(details.Bounds.Height, layout.Bounds.Height), "Horizontal places details beside history");
        Check(ReferenceEquals(search.Parent, searchParent) && ReferenceEquals(results.SelectedItem, result), "layout switch preserves search parent and selection");
        Check(input.IsKeyboardFocusWithin, "layout switch preserves search focus");
        pref.UseTwoColumnsLayoutInHistories = false;
        Arrange(window);
        Check(details.Bounds.Width < layout.Bounds.Width - 100, "Vertical retains the full height sidebar");
        Check(Near(sidebarScroller.Bounds.Height, layout.Bounds.Height), "Vertical sidebar spans both panes");
        pref.UseWideDetailsLayoutInHistories = true;
        Arrange(window);
        Check(ReferenceEquals(sidebar.Parent, parent) && ReferenceEquals(search.Parent, searchParent), "all layouts retain sidebar and search parents");
        Check(ReferenceEquals(repo.Histories.DetailContext, detailContext) && ReferenceEquals(repo.Histories.SelectedCommits[0], selected), "layout switching preserves commit and detail state");

        repo.Histories.IsSearchingCommits = false;
        view.IsCommitSearchPanelVisible = false;
        view.IsNormalSidebarVisible = true;
        foreach (var page in new[] { 1, 2, 0 })
        {
            repo.SelectedViewIndex = page;
            Arrange(window);
            Check(details.IsVisible == (page == 0), "details follow the selected repository page");
            Check(Near(sidebarScroller.Bounds.Height, page == 0 ? layout.RowDefinitions[0].ActualHeight : layout.Bounds.Height), "sidebar follows page height");
            Check(ReferenceEquals(sidebar.Parent, parent), "page switches retain sidebar parent");
        }
    }

    private static void CheckShortSidebar(VM.Repository repo, Views.Repository view, Window window)
    {
        var layout = view.FindControl<Views.RepositoryLayout>("MainBody");
        var scroller = view.FindControl<ScrollViewer>("SidebarScroller");
        var host = view.FindControl<Grid>("SidebarHost");
        var groups = view.FindControl<Grid>("LeftSidebarGroups");
        var tree = view.FindControl<Views.BranchTree>("LocalBranchTree");
        repo.Histories.TopArea = new GridLength(100);
        repo.Histories.BottomArea = new GridLength(1, GridUnitType.Star);
        VM.Preferences.Instance.UseWideDetailsLayoutInHistories = true;
        Arrange(window);
        Check(Near(scroller.Bounds.Height, 100), "history supports its minimum height");
        Check(scroller.Extent.Height > scroller.Viewport.Height, "short sidebar has a scrollable extent");
        Check(tree.Height >= 120 && tree.Height < 151 * 24, "large branch tree retains a finite viewport");
        foreach (var header in groups.Children.OfType<ToggleButton>())
        {
            header.BringIntoView();
            Arrange(window);
            var point = header.TranslatePoint(default, scroller)!.Value;
            Check(point.Y >= -1 && point.Y + header.Bounds.Height <= scroller.Bounds.Height + 1, "every sidebar group can be scrolled into view");
        }

        view.IsNormalSidebarVisible = false;
        view.IsCommitSearchPanelVisible = true;
        repo.Histories.IsSearchingCommits = true;
        Arrange(window);
        Check(host.Bounds.Height >= 300 && scroller.Extent.Height > scroller.Viewport.Height, "short search panel can scroll through its controls");
        Check(Near(layout.RowDefinitions[0].ActualHeight, 100), "search does not force details out of view");
        repo.Histories.IsSearchingCommits = false;
        view.IsCommitSearchPanelVisible = false;
        view.IsNormalSidebarVisible = true;
        scroller.Offset = default;
        Arrange(window);
    }

    private static void CheckMenu(Views.Repository view, Window window)
    {
        var histories = view.FindControl<Views.Histories>("DashboardHistories");
        var button = histories.GetVisualDescendants().OfType<Button>()
            .Single(x => string.Equals(x.GetValue(ToolTip.TipProperty) as string, App.Text("Histories.AdvancedOptions"), StringComparison.Ordinal));
        ContextMenu opened = null;
        using var subscription = MenuBase.OpenedEvent.AddClassHandler<ContextMenu>((menu, _) => opened = menu);
        foreach (var name in new[] { "Vertical", "Horizontal", "WideDetails" })
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => opened?.IsOpen == true);
            var item = opened.Items.OfType<MenuItem>().Single(x => string.Equals(x.Header as string, App.Text("Repository.HistoriesLayout." + name), StringComparison.Ordinal));
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            opened.Close();
            Arrange(window);
            var pref = VM.Preferences.Instance;
            Check(pref.UseTwoColumnsLayoutInHistories == string.Equals(name, "Horizontal", StringComparison.Ordinal) &&
                pref.UseWideDetailsLayoutInHistories == string.Equals(name, "WideDetails", StringComparison.Ordinal), "menu selects " + name);
        }
    }

    private static void Render(Window window, string name)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height));
        bitmap.Render(window);
        bitmap.Save(Path.Combine(AppContext.BaseDirectory, name));
    }

    private static bool Near(double a, double b) => Math.Abs(a - b) <= 1;

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
            if (timer.Elapsed.TotalSeconds > 20)
                throw new Exception("Timed out waiting for UI state");
        } while (!condition());
    }

    private static void Check(bool condition, string name)
    {
        if (!condition)
            throw new Exception("FAIL: " + name);
        s_checks++;
    }

    private static void Git(string path, params string[] args)
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
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(output, error);
        if (process.ExitCode != 0)
            throw new Exception(error.Result);
    }

    private static int s_checks;
}
