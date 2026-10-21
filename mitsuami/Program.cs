using System;
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
            // On macOS, it also fixes `PATH`, so git and tools are found.
            Native.OS.SetupApp(new Avalonia.AppBuilder());

            s_startupRepo = args.Length > 0 ? args[0] : null;
            Application.Run("com.sourcegit-scm.SourceGit", "SourceGit", Start);
        }

        private static string s_startupRepo;

        private static void Start()
        {
            var pref = ViewModels.Preferences.Instance;
            App.SetLocale(pref.Locale);

            Views.Launcher.Create(App.CreateLauncher(s_startupRepo));
            _ = ViewModels.Welcome.Instance.UpdateStatusAsync(false, null);
        }
    }
}
