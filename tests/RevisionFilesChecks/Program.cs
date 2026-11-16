using System.Diagnostics;
using System.Globalization;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0)
            return RunFakeGitAsync(args).GetAwaiter().GetResult();

        try
        {
            RunQueryChecksAsync().GetAwaiter().GetResult();
            NavigationChecks.Run();
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            return 1;
        }
    }

    private static async Task RunQueryChecksAsync()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "yagt-revision-query-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        var executable = Environment.ProcessPath;
        if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Run this check through dotnet run so its app host can act as the fake Git executable.");

        var previousGit = SourceGit.Native.OS.GitExecutable;
        var previousDirectory = Environment.GetEnvironmentVariable(CHILD_DIRECTORY);
        var previousMode = Environment.GetEnvironmentVariable(CHILD_MODE);
        Task active = null;
        try
        {
            Environment.SetEnvironmentVariable(CHILD_DIRECTORY, fixture);
            SourceGit.Native.OS.GitExecutable = executable;
            Environment.SetEnvironmentVariable(CHILD_MODE, "reply");
            var objects = await new SourceGit.Commands.QueryRevisionObjects(fixture, REVISION, null)
                .GetResultAsync().ConfigureAwait(false);
            Check(objects is { Count: 1 } && objects[0].Path.Equals("file.txt", StringComparison.Ordinal), "query parses a successful listing");

            Environment.SetEnvironmentVariable(CHILD_MODE, "sleep");
            using (var cancellation = new CancellationTokenSource())
            {
                active = new SourceGit.Commands.QueryRevisionObjects(fixture, REVISION, null) { CancellationToken = cancellation.Token }
                    .GetResultAsync();
                await WaitForFileAsync(Path.Combine(fixture, "sleeper.pid")).ConfigureAwait(false);
                await ExpectCancellationAsync(active, cancellation).ConfigureAwait(false);
                Check(!IsTrackedProcessAlive(Path.Combine(fixture, "sleeper.pid")), "cancelled Git process is reaped");
            }

            Environment.SetEnvironmentVariable(CHILD_MODE, "inherited");
            var state = new SourceGit.ViewModels.RevisionFileTreeState();
            using (var cancellation = new CancellationTokenSource())
            {
                active = state.LoadAsync(fixture, REVISION, Array.Empty<string>(), cancellation.Token);
                await WaitForFileAsync(Path.Combine(fixture, "descendant.pid")).ConfigureAwait(false);
                await WaitUntilAsync(() => !IsTrackedProcessAlive(Path.Combine(fixture, "launcher.pid")), "Git wrapper did not exit").ConfigureAwait(false);
                Check(IsTrackedProcessAlive(Path.Combine(fixture, "descendant.pid")), "descendant keeps stdout and stderr open after Git wrapper exits");

                await ExpectCancellationAsync(active, cancellation).ConfigureAwait(false);
                Environment.SetEnvironmentVariable(CHILD_MODE, "reply");
                var recovered = await state.LoadAsync(fixture, REVISION, Array.Empty<string>(), CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                Check(recovered is { Count: 1 }, "next tree request completes before descendant cleanup");
            }
        }
        finally
        {
            CleanupChildren(fixture);
            if (active != null)
            {
                try
                {
                    await active.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                }
                catch
                {
                    // A cancellation regression must still clean up its test processes.
                }
            }

            Check(!Directory.EnumerateFiles(fixture, "*.pid").Any(IsTrackedProcessAlive), "no fake Git processes remain after cleanup");
            SourceGit.Native.OS.GitExecutable = previousGit;
            Environment.SetEnvironmentVariable(CHILD_DIRECTORY, previousDirectory);
            Environment.SetEnvironmentVariable(CHILD_MODE, previousMode);
            var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(fixture).StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Query fixture must stay inside the temporary directory.");
            Directory.Delete(fixture, true);
        }
    }

    private static async Task ExpectCancellationAsync(Task task, CancellationTokenSource cancellation)
    {
        var timer = Stopwatch.StartNew();
        cancellation.Cancel();
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            throw new InvalidOperationException("Query completed without observing cancellation.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Check(timer.Elapsed < TimeSpan.FromSeconds(3), "cancellation completes promptly even with inherited pipes");
        }
    }

    private static async Task<int> RunFakeGitAsync(string[] args)
    {
        if (args.Contains("--version", StringComparer.Ordinal))
        {
            Console.WriteLine("git version 2.39.2");
            return 0;
        }

        if (args.Contains("flow", StringComparer.Ordinal))
            return 0;

        var fixture = Environment.GetEnvironmentVariable(CHILD_DIRECTORY);
        if (args.Contains("--hold-pipes", StringComparer.Ordinal))
        {
            WriteMarker(Path.Combine(fixture, "descendant.pid"));
            await Task.Delay(30000).ConfigureAwait(false);
            return 0;
        }

        if (!args.Contains("ls-tree", StringComparer.Ordinal))
            return 1;

        switch (Environment.GetEnvironmentVariable(CHILD_MODE))
        {
            case "reply":
                Console.Write($"100644 blob {REVISION}\tfile.txt\0");
                return 0;
            case "sleep":
                WriteMarker(Path.Combine(fixture, "sleeper.pid"));
                await Task.Delay(30000).ConfigureAwait(false);
                return 0;
            case "inherited":
                WriteMarker(Path.Combine(fixture, "launcher.pid"));
                var start = new ProcessStartInfo(Environment.ProcessPath)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                };
                start.ArgumentList.Add("--hold-pipes");
                using (var child = Process.Start(start))
                    child.StandardInput.Close();
                return 0;
            default:
                return 1;
        }
    }

    private static async Task WaitForFileAsync(string path)
    {
        await WaitUntilAsync(() => File.Exists(path) && File.ReadAllLines(path).Length == 2, "Fake Git process did not start").ConfigureAwait(false);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string message)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(5))
                throw new TimeoutException(message);
            await Task.Delay(10).ConfigureAwait(false);
        }
    }

    private static void WriteMarker(string path)
    {
        using var process = Process.GetCurrentProcess();
        var pending = path + ".tmp";
        File.WriteAllLines(pending, [process.Id.ToString(CultureInfo.InvariantCulture), process.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture)]);
        File.Move(pending, path);
    }

    private static bool IsTrackedProcessAlive(string marker)
    {
        var record = File.ReadAllLines(marker);
        if (record.Length != 2)
            return false;
        try
        {
            using var process = Process.GetProcessById(int.Parse(record[0], CultureInfo.InvariantCulture));
            return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == long.Parse(record[1], CultureInfo.InvariantCulture);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void CleanupChildren(string fixture)
    {
        foreach (var marker in Directory.EnumerateFiles(fixture, "*.pid"))
        {
            if (!IsTrackedProcessAlive(marker))
                continue;
            try
            {
                var record = File.ReadAllLines(marker);
                using var process = Process.GetProcessById(int.Parse(record[0], CultureInfo.InvariantCulture));
                process.Kill(true);
                process.WaitForExit(2000);
            }
            catch (ArgumentException)
            {
                // The process exited after its marker was checked.
            }
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }

    private const string CHILD_DIRECTORY = "YAGT_REVISION_FILES_CHECK_DIRECTORY";
    private const string CHILD_MODE = "YAGT_REVISION_FILES_CHECK_MODE";
    private const string REVISION = "1111111111111111111111111111111111111111";
}
