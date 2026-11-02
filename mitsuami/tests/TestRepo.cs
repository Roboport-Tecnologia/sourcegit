using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SourceGit.Tests
{
    /// <summary>A real repository: 40 numbered lines in <c>app.txt</c>, committed, to change.</summary>
    public sealed class TestRepo : IDisposable
    {
        public const string File1 = "app.txt";

        public string Path { get; } = Directory.CreateTempSubdirectory("sourcegit-repo-").FullName.Replace('\\', '/');

        public static string[] Numbered() => Enumerable.Range(1, 40).Select(i => $"line {i}").ToArray();

        public TestRepo()
        {
            Git("init -q");
            Write(File1, string.Join('\n', Numbered()) + "\n");
            Git($"add {File1}");
            Git("commit -q -m first");
        }

        public void Write(string file, string text) =>
            File.WriteAllText(System.IO.Path.Combine(Path, file), text);

        public string Read(string file) => File.ReadAllText(System.IO.Path.Combine(Path, file));

        /// <summary>Lines 5 and 30 changed, two far apart hunks.</summary>
        public void ChangeTwoPlaces()
        {
            var lines = Numbered();
            lines[4] = "line five";
            lines[29] = "line thirty";
            Write(File1, string.Join('\n', lines) + "\n");
        }

        public string Git(string args)
        {
            var start = new ProcessStartInfo("git", args)
            {
                WorkingDirectory = Path,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var git = Process.Start(start);
            var output = git.StandardOutput.ReadToEndAsync();
            var error = git.StandardError.ReadToEndAsync();
            git.WaitForExit();
            if (git.ExitCode != 0)
                throw new InvalidOperationException($"git {args}: {error.Result}");
            return output.Result;
        }

        /// <summary>For what's expected to fail, as a merge with conflicts does.</summary>
        public void GitAllowingFailure(string args)
        {
            try
            {
                Git(args);
            }
            catch (InvalidOperationException)
            {
            }
        }

        public string Staged() => Git("diff --cached");

        public string Unstaged() => Git("diff");

        public async Task<Models.Change> Change(string file)
        {
            var changes = await new Commands.QueryLocalChanges(Path).GetResultAsync();
            return changes.First(c => c.Path == file);
        }

        /// <summary>The diff as the port asks for it: both versions whole.</summary>
        public async Task<(Models.DiffOption Option, Models.TextDiff Diff)> Diff(string file, bool unstaged)
        {
            var option = new Models.DiffOption(await Change(file), unstaged);
            var result = await new Commands.Diff(Path, option, 999999999, false, false).ReadAsync();
            return (option, result.TextDiff);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch (IOException)
            {
                // A watcher still closing: it's in the system's temporary directory.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
