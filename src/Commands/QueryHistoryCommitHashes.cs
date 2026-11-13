using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    public class QueryHistoryCommitHashes : Command
    {
        public QueryHistoryCommitHashes(string repo, IReadOnlyList<string> refs, IReadOnlyList<Models.Commit> commits, bool firstParentOnly, bool canUseLoadedGraph = false)
        {
            WorkingDirectory = repo;
            Context = repo;

            var builder = new StringBuilder("rev-list ");
            if (firstParentOnly)
                builder.Append("--first-parent ");

            foreach (var reference in refs)
                builder.Append(reference.Quoted()).Append(' ');
            builder.Append("--");
            Args = builder.ToString();

            _hasRefs = refs.Count > 0;
            _candidates = new HashSet<string>(StringComparer.Ordinal);
            foreach (var commit in commits)
                _candidates.Add(commit.SHA);

            // The caller must supply a contiguous, unfiltered topological traversal without history simplification.
            // Then all visible ancestry is connected within the loaded graph whenever every focus tip is visible.
            _canUseLoadedGraph = canUseLoadedGraph;
            foreach (var reference in refs)
            {
                if (!_candidates.Contains(reference))
                {
                    _canUseLoadedGraph = false;
                    break;
                }
            }

            _refs = refs;
            _commits = commits;
            _firstParentOnly = firstParentOnly;
        }

        public async Task<HashSet<string>> GetResultAsync()
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (!_hasRefs || _candidates.Count == 0)
                return result;

            CancellationToken.ThrowIfCancellationRequested();
            if (_canUseLoadedGraph)
            {
                var reachable = new HashSet<string>(_refs, StringComparer.Ordinal);
                foreach (var commit in _commits)
                {
                    CancellationToken.ThrowIfCancellationRequested();
                    if (!reachable.Remove(commit.SHA))
                        continue;

                    result.Add(commit.SHA);
                    var parentCount = _firstParentOnly ? Math.Min(commit.Parents.Count, 1) : commit.Parents.Count;
                    for (var i = 0; i < parentCount; i++)
                        reachable.Add(commit.Parents[i]);
                }

                return result;
            }

            using var proc = new Process();
            proc.StartInfo = CreateGitStartInfo(true);

            try
            {
                proc.Start();
                var stderr = proc.StandardError.ReadToEndAsync();
                var remaining = new HashSet<string>(_candidates, StringComparer.Ordinal);
                var completed = false;
                try
                {
                    while (await proc.StandardOutput.ReadLineAsync(CancellationToken).ConfigureAwait(false) is { } line)
                    {
                        if (!remaining.Remove(line))
                            continue;

                        result.Add(line);
                        if (remaining.Count == 0)
                        {
                            completed = true;
                            break;
                        }
                    }

                    if (completed)
                        StopProcess(proc);

                    await proc.WaitForExitAsync(CancellationToken).ConfigureAwait(false);
                    var error = await stderr.ConfigureAwait(false);
                    if (!completed && proc.ExitCode != 0)
                    {
                        result.Clear();
                        if (RaiseError && !string.IsNullOrWhiteSpace(error))
                            RaiseException(error.Trim());
                    }
                }
                finally
                {
                    StopProcess(proc);
                    await proc.WaitForExitAsync().ConfigureAwait(false);
                    await stderr.ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (CancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                result.Clear();
                if (RaiseError)
                    RaiseException($"Failed to query history reachability. Reason: {e.Message}");
            }

            return result;
        }

        private static void StopProcess(Process proc)
        {
            try
            {
                if (!proc.HasExited)
                    proc.Kill(true);
            }
            catch (InvalidOperationException)
            {
                // The process may exit between checking its state and terminating it.
            }
        }

        private readonly bool _hasRefs;
        private readonly HashSet<string> _candidates;
        private readonly bool _canUseLoadedGraph;
        private readonly IReadOnlyList<string> _refs;
        private readonly IReadOnlyList<Models.Commit> _commits;
        private readonly bool _firstParentOnly;
    }
}
