using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    public class QueryRevisionObjects : Command
    {
        public QueryRevisionObjects(string repo, string sha, string parentFolder)
            : this(repo, sha, string.IsNullOrEmpty(parentFolder) ? null : [parentFolder], false)
        {
        }

        public QueryRevisionObjects(string repo, string sha, IReadOnlyCollection<string> paths, bool includeTrees)
        {
            WorkingDirectory = repo;
            Context = repo;

            // `-z` keeps paths unquoted; each record is `<mode> SP <type> SP <object> TAB <path> NUL`.
            var builder = new StringBuilder(1024);
            builder.Append("ls-tree -z ");
            if (includeTrees)
                builder.Append("-t ");

            builder.Append(sha.Quoted());
            if (paths is { Count: > 0 })
            {
                builder.Append(" --");
                foreach (var path in paths)
                    builder.Append(' ').Append(path.Quoted());
            }

            Args = builder.ToString();
        }

        /// <summary>
        ///     Returns the listed objects, or null when Git fails or prints an unexpected record.
        ///     Throws <see cref="OperationCanceledException"/> once <see cref="Command.CancellationToken"/> is cancelled.
        /// </summary>
        public async Task<List<Models.Object>> GetResultAsync()
        {
            using var proc = new Process();
            proc.StartInfo = CreateGitStartInfo(true);

            var started = false;
            Task<string> output = null;
            Task<string> errors = null;
            try
            {
                CancellationToken.ThrowIfCancellationRequested();
                proc.Start();
                started = true;

                output = proc.StandardOutput.ReadToEndAsync(CancellationToken);
                errors = proc.StandardError.ReadToEndAsync(CancellationToken);

                // Pipe reads may ignore the token, so stop waiting as soon as it is cancelled.
                var stdout = await output.WaitAsync(CancellationToken).ConfigureAwait(false);
                await proc.WaitForExitAsync(CancellationToken).ConfigureAwait(false);
                return proc.ExitCode == 0 ? Parse(stdout) : null;
            }
            catch
            {
                CancellationToken.ThrowIfCancellationRequested();
                return null;
            }
            finally
            {
                if (started)
                    await StopAsync(proc).ConfigureAwait(false);

                // A Git wrapper can exit while a descendant keeps the redirected pipes open.
                // Disposing the process closes our handles; never wait for those reads here.
                IgnoreFailure(output);
                IgnoreFailure(errors);
            }
        }

        private static async Task StopAsync(Process proc)
        {
            try
            {
                if (!proc.HasExited)
                {
                    Native.OS.TerminateProcess(proc);
                    if (!proc.HasExited)
                        proc.Kill(true);
                }

                await proc.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch
            {
                // The process may have exited while cancellation was being handled.
            }
        }

        private static void IgnoreFailure(Task task)
        {
            // Closing a cancelled query's pipes can complete its pending reads with an error.
            _ = task?.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        }

        private static List<Models.Object> Parse(string output)
        {
            var records = output.Split('\0');
            if (records[^1].Length > 0)
                return null;

            var objects = new List<Models.Object>(records.Length - 1);
            for (var i = 0; i < records.Length - 1; i++)
            {
                var obj = ParseRecord(records[i]);
                if (obj == null)
                    return null;

                objects.Add(obj);
            }

            return objects;
        }

        private static Models.Object ParseRecord(string record)
        {
            var firstSpace = record.IndexOf(' ');
            var secondSpace = firstSpace < 1 ? -1 : record.IndexOf(' ', firstSpace + 1);
            var tab = secondSpace <= firstSpace + 1 ? -1 : record.IndexOf('\t', secondSpace + 1);
            if (tab <= secondSpace + 1 || tab == record.Length - 1)
                return null;

            var type = record.AsSpan(firstSpace + 1, secondSpace - firstSpace - 1) switch
            {
                "blob" => Models.ObjectType.Blob,
                "tree" => Models.ObjectType.Tree,
                "tag" => Models.ObjectType.Tag,
                "commit" => Models.ObjectType.Commit,
                _ => Models.ObjectType.None,
            };

            if (type == Models.ObjectType.None)
                return null;

            return new Models.Object
            {
                SHA = record.Substring(secondSpace + 1, tab - secondSpace - 1),
                Type = type,
                Path = record.Substring(tab + 1),
            };
        }
    }
}
