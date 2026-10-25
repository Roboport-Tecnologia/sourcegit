using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

using Mitsuamia;

namespace SourceGit
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            // Upstream's startup, before its Avalonia app (App.axaml.cs).
            Native.OS.SetupBasicDirectories();
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Native.OS.LogException(e.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (_, e) => e.SetObserved();

            // Git runs the app as its editors during an interactive rebase:
            // they write the todo list and messages the rebase window saved,
            // with no window.
            if (TryLaunchAsRebaseTodoEditor(args, out var exitTodo))
                Environment.Exit(exitTodo);
            if (TryLaunchAsRebaseMessageEditor(args, out var exitMessage))
                Environment.Exit(exitMessage);
            // On macOS, it also fixes `PATH`, so git and tools are found.
            Native.OS.SetupApp(new Avalonia.AppBuilder());

            // ssh runs the app to ask for a passphrase (SSH_ASKPASS, which
            // upstream's commands set with SOURCEGIT_LAUNCH_AS_ASKPASS): its
            // window alone, and the answer on the output.
            if (Environment.GetEnvironmentVariable("SOURCEGIT_LAUNCH_AS_ASKPASS") == "TRUE" && args.Length > 0)
            {
                var prompt = args[0];
                Application.Run("com.sourcegit-scm.SourceGit", "SourceGit", () =>
                {
                    App.SetLocale(ViewModels.Preferences.Instance.Locale);
                    Views.Askpass.Show(prompt);
                });
                return;
            }

            s_startupRepo = args.Length > 0 ? args[0] : null;
            Application.Run("com.sourcegit-scm.SourceGit", "SourceGit", Start);
        }

        private static string s_startupRepo;

        // Upstream's (App.axaml.cs), as they are.
        private static bool TryLaunchAsRebaseTodoEditor(string[] args, out int exitCode)
        {
            exitCode = -1;

            if (args.Length <= 1 || !args[0].Equals("--rebase-todo-editor", StringComparison.Ordinal))
                return false;

            var file = args[1].Replace('\\', '/').Trim('\"').Trim();
            var filename = Path.GetFileName(file);
            if (!filename.Equals("git-rebase-todo", StringComparison.OrdinalIgnoreCase))
                return true;

            var dirInfo = new DirectoryInfo(Path.GetDirectoryName(file)!);
            if (!dirInfo.Exists || !dirInfo.Name.Equals("rebase-merge", StringComparison.Ordinal))
                return true;

            var jobsFile = Path.Combine(dirInfo.Parent!.FullName, "sourcegit.interactive_rebase");
            if (!File.Exists(jobsFile))
                return true;

            using var stream = File.OpenRead(jobsFile);
            var collection = JsonSerializer.Deserialize(stream, JsonCodeGen.Default.InteractiveRebaseJobCollection);
            collection.WriteTodoList(file);
            exitCode = 0;
            return true;
        }

        private static bool TryLaunchAsRebaseMessageEditor(string[] args, out int exitCode)
        {
            exitCode = -1;

            if (args.Length <= 1 || !args[0].Equals("--rebase-message-editor", StringComparison.Ordinal))
                return false;

            exitCode = 0;

            var file = args[1].Replace('\\', '/').Trim('\"').Trim();
            var filename = Path.GetFileName(file);
            if (!filename.Equals("COMMIT_EDITMSG", StringComparison.OrdinalIgnoreCase))
                return true;

            var gitDir = Path.GetDirectoryName(file)!;
            var origHeadFile = Path.Combine(gitDir, "rebase-merge", "orig-head");
            var ontoFile = Path.Combine(gitDir, "rebase-merge", "onto");
            var doneFile = Path.Combine(gitDir, "rebase-merge", "done");
            var jobsFile = Path.Combine(gitDir, "sourcegit.interactive_rebase");
            if (!File.Exists(ontoFile) || !File.Exists(origHeadFile) || !File.Exists(doneFile) || !File.Exists(jobsFile))
                return true;

            var origHead = File.ReadAllText(origHeadFile).Trim();
            var onto = File.ReadAllText(ontoFile).Trim();
            using var stream = File.OpenRead(jobsFile);
            var collection = JsonSerializer.Deserialize(stream, JsonCodeGen.Default.InteractiveRebaseJobCollection);
            if (collection.Onto.StartsWith(onto, StringComparison.OrdinalIgnoreCase) && collection.OrigHead.StartsWith(origHead, StringComparison.OrdinalIgnoreCase))
                collection.WriteCommitMessage(doneFile, file);

            return true;
        }

        private static void Start()
        {
            var pref = ViewModels.Preferences.Instance;
            App.SetLocale(pref.Locale);
            // Upstream's theme sets the graph's colours; the platform's
            // appearance is the theme here, so they're upstream's defaults.
            Models.CommitGraph.SetDefaultPens();
            // The editors and IDEs installed, for the "Open" menus.
            Native.OS.SetupExternalTools();
            // Fetched avatars come to the UI thread.
            Models.AvatarManager.Instance.Start();

            App.CreateLauncher(s_startupRepo);
            _ = ViewModels.Welcome.Instance.UpdateStatusAsync(false, null);
        }
    }
}
