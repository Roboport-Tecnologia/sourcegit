using System;
using System.Linq;
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

        /// <summary>For now, loads the preferences and the welcome page's
        /// repositories, and says what it found: the views come next.</summary>
        private static void Start()
        {
            var pref = ViewModels.Preferences.Instance;
            App.SetLocale(pref.Locale);

            var rows = ViewModels.Welcome.Instance.Rows;
            Console.WriteLine($"Preferences from {Native.OS.BasicDirectories.ConfigDir}");
            Console.WriteLine($"{rows.Count} rows on the welcome page");
            foreach (var row in rows.Take(10))
                Console.WriteLine($"  {new string(' ', row.Depth * 2)}{(row.IsRepository ? "" : "[group] ")}{row.Name}  {row.Id}");

            var text = new Text(App.Text("Welcome.Search")) { Style = TextStyle.Headline };
            var column = new Column { Padding = 20, Gap = 8 };
            column.Add(text);
            column.Add(new Text($"{rows.Count(r => r.IsRepository)} repositories, {rows.Count(r => !r.IsRepository)} groups"));
            _ = new Window("SourceGit", 360, 120) { Content = column };
        }
    }
}
