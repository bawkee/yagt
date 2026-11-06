using System;
using System.IO;

namespace SourceGit.Native
{
    internal static class ConfigurationMigration
    {
        public static void ImportIfNeeded(string sourceDir, string targetDir)
        {
            if (Directory.Exists(targetDir))
                return;

            Directory.CreateDirectory(targetDir);
            if (!Directory.Exists(sourceDir))
                return;

            try
            {
                // Copy configuration only. Caches and running-instance locks belong to each app.
                foreach (var file in Directory.EnumerateFiles(sourceDir))
                {
                    var name = Path.GetFileName(file);
                    if (name.Equals("process.lock", StringComparison.Ordinal))
                        continue;

                    try
                    {
                        File.Copy(file, Path.Combine(targetDir, name));
                    }
                    catch (IOException)
                    {
                        // An unavailable upstream file must not prevent YAGT from starting.
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Leave inaccessible files in the upstream directory.
                    }
                }
            }
            catch (IOException)
            {
                // Configuration import is best effort; never change the upstream files.
            }
            catch (UnauthorizedAccessException)
            {
                // Continue with YAGT's default configuration.
            }
        }
    }
}
