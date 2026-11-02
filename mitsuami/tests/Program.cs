using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGit.Tests
{
    /// <summary>Runs every test on this thread, the UI's, turning the UI while each waits.
    /// An argument runs only the tests with it in their name.</summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            // Before anything reads them: the user's preferences and git config stay out of it.
            var home = Directory.CreateTempSubdirectory("sourcegit-tests-").FullName;
            Environment.SetEnvironmentVariable("HOME", home);
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", Path.Combine(home, ".config"));
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", Path.Combine(home, ".local/share"));
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", Path.Combine(home, ".cache"));
            Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", Path.Combine(home, "gitconfig"));
            Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", "1");
            File.WriteAllText(Path.Combine(home, "gitconfig"), "[user]\n\tname = Test\n\temail = test@example.com\n[init]\n\tdefaultBranch = main\n");

            Native.OS.SetupBasicDirectories();
            if (!Native.OS.BasicDirectories.ConfigDir.StartsWith(home, StringComparison.Ordinal))
                throw new InvalidOperationException($"Tests would use the user's preferences: {Native.OS.BasicDirectories.ConfigDir}");
            Native.OS.SetupApp(new Avalonia.AppBuilder());
            Native.OS.GitExecutable = Native.OS.FindGitExecutable();
            ViewModels.Preferences.Instance.UseFullTextDiff = true;
            App.SetLocale("en_US");
            Models.AvatarManager.Instance.Start();
            Host.Start();

            var tests = new List<(string Name, Func<Task> Run)>();
            tests.AddRange(ChunkTests.All());
            tests.AddRange(DiffViewTests.All());
            tests.AddRange(BlameTests.All());
            tests.AddRange(MessageTests.All());
            tests.AddRange(DetailsWindowTests.All());
            tests.AddRange(MergeEditorTests.All());

            var failed = 0;
            var ran = 0;
            foreach (var (name, run) in tests)
            {
                if (args.Length > 0 && !args.Any(name.Contains))
                    continue;
                ran++;
                var task = Pump(run);
                if (task.IsCompletedSuccessfully)
                {
                    Console.WriteLine($"  ok    {name}");
                }
                else
                {
                    failed++;
                    Console.WriteLine($"  FAIL  {name}\n        {task.Exception?.InnerException?.Message}");
                    Console.WriteLine(task.Exception?.InnerException?.StackTrace);
                }
            }
            Console.WriteLine($"{ran - failed} passed, {failed} failed");
            try
            {
                Directory.Delete(home, true);
            }
            catch (IOException)
            {
                // An editor still closing: it's in the system's temporary directory.
            }
            // Editors and watchers still run on other threads: nothing else ends them.
            Environment.Exit(failed == 0 && ran > 0 ? 0 : 1);
            return 1;
        }

        private static Task Pump(Func<Task> run)
        {
            var task = run();
            while (!task.IsCompleted)
            {
                Host.Settle();
                Thread.Sleep(1);
            }
            return task;
        }
    }
}
