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

            Application.Run("com.sourcegit-scm.SourceGit", "SourceGit", Start);
        }

        /// <summary>For now, the welcome page in a window of its own: the
        /// launcher, with its tabs, comes next.</summary>
        private static void Start()
        {
            var pref = ViewModels.Preferences.Instance;
            App.SetLocale(pref.Locale);

            var welcome = ViewModels.Welcome.Instance;
            _ = new Window("SourceGit", 800, 560) { Content = Views.Welcome.Create(welcome) };
            _ = welcome.UpdateStatusAsync(false, null);
        }
    }
}
