using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    public class QueryCommitSubjectMatches : Command
    {
        public bool IsTruncated { get; private set; }
        public Models.CommitSubjectMatch SelectedCommit { get; }

        public QueryCommitSubjectMatches(string repo, Models.Commit commit)
        {
            WorkingDirectory = repo;
            Context = repo;
            _commit = commit;
            SelectedCommit = new Models.CommitSubjectMatch() { SHA = commit.SHA };
        }

        public async Task<List<Models.CommitSubjectMatch>> GetResultAsync()
        {
            var matches = new List<Models.CommitSubjectMatch>();
            if (_commit.Subject == null)
                return matches;

            // A subject can fold several message lines together. Use a literal prefix only
            // as a prefilter, then compare %s exactly to exclude body and partial matches.
            var end = _commit.Subject.IndexOfAny([' ', '"', '\\']);
            var prefix = end < 0 ? _commit.Subject : _commit.Subject.Substring(0, end);
            var grep = prefix.Length > 0 ? $"--fixed-strings --grep={prefix.Quoted()} " : string.Empty;
            Args = $"log --no-show-signature --no-color --format=%H%x00%s {grep}--branches --remotes --";
            var success = await ReadLinesAsync(line =>
            {
                var separator = line.IndexOf('\0');
                if (separator <= 0 || !line.AsSpan(separator + 1).Equals(_commit.Subject.AsSpan(), StringComparison.Ordinal))
                    return;

                var sha = line.Substring(0, separator);
                if (sha.Equals(_commit.SHA, StringComparison.Ordinal))
                    return;

                if (matches.Count < MAX_MATCHES)
                    matches.Add(new Models.CommitSubjectMatch() { SHA = sha });
                else
                    IsTruncated = true;
            }).ConfigureAwait(false);

            if (!success)
                return null;

            if (!await QueryBranchesAsync(SelectedCommit).ConfigureAwait(false))
                return null;

            foreach (var match in matches)
            {
                if (!await QueryBranchesAsync(match).ConfigureAwait(false))
                    return null;
            }

            return matches;
        }

        private async Task<bool> QueryBranchesAsync(Models.CommitSubjectMatch match)
        {
            Args = $"for-each-ref --format=\"%(refname)%00%(symref)\" --contains={match.SHA.Quoted()} refs/heads refs/remotes";
            return await ReadLinesAsync(line =>
            {
                var separator = line.IndexOf('\0');
                if (separator <= 0 || separator != line.Length - 1)
                    return;

                var name = line.Substring(0, separator);
                if (name.StartsWith("refs/heads/", StringComparison.Ordinal))
                    match.Branches.Add(name.Substring(11));
                else if (name.StartsWith("refs/remotes/", StringComparison.Ordinal))
                    match.Branches.Add(name.Substring(13));
            }).ConfigureAwait(false);
        }

        private async Task<bool> ReadLinesAsync(Action<string> onLine)
        {
            CancellationToken.ThrowIfCancellationRequested();

            using var proc = new Process();
            proc.StartInfo = CreateGitStartInfo(true);
            proc.Start();

            // Read-only queries must stop their git process when selection changes.
            using var registration = CancellationToken.Register(() =>
            {
                try
                {
                    if (!proc.HasExited)
                        proc.Kill(true);
                }
                catch (InvalidOperationException)
                {
                    // The process has already exited.
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // The process may exit between checking HasExited and killing it.
                }
            });

            var errors = proc.StandardError.ReadToEndAsync();
            while (await proc.StandardOutput.ReadLineAsync(CancellationToken).ConfigureAwait(false) is { } line)
                onLine(line);

            await proc.WaitForExitAsync(CancellationToken).ConfigureAwait(false);
            await errors.ConfigureAwait(false);
            CancellationToken.ThrowIfCancellationRequested();
            return proc.ExitCode == 0;
        }

        private const int MAX_MATCHES = 100;
        private readonly Models.Commit _commit;
    }
}
