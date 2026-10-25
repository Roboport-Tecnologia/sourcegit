using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's about window (<c>Views/About.axaml</c>), a small window of
    /// its own as macOS apps' about windows are: the app's icon, its name and
    /// version (a click opens the release notes), upstream's subtitle, the
    /// website and source code, the release date and copyright.
    /// </summary>
    public static partial class About
    {
        private static Window s_shown;

        /// <summary>The window, if it's open: for captures.</summary>
        public static Window Shown => s_shown;

        public static void Show()
        {
            if (s_shown != null)
                return;
            var window = s_shown = new Window(App.Text("About"), 520);
            window.Closed += () => s_shown = null;

            var row = new Row { Padding = new Thickness(18, 24, 18, 8), Gap = 12, AlignItems = Align.Center };
            if (AppIcon() is { } icon)
                row.Add(icon);

            var column = new Column { Gap = 4 };
            column.Grow = 1;
            var title = new Row { Gap = 12, AlignItems = Align.Center };
            title.Add(new Text("SourceGit") { Style = TextStyle.LargeTitle, Weight = FontWeight.Bold });
            var (version, date) = Version();
            var notes = new Button(version) { ToolTip = App.Text("About.ReleaseNotes") };
            notes.Click += () =>
            {
                var tag = version.IndexOf('-') is var end and > 0 ? version[..end] : version;
                Native.OS.OpenBrowser($"https://github.com/sourcegit-scm/sourcegit/releases/tag/{tag}");
            };
            title.Add(notes);
            column.Add(title);
            column.Add(new Text(App.Text("About.SubTitle")) { Style = TextStyle.Title });

            var links = new Row { Gap = 8, Margin = new Thickness(8, 0, 0, 0) };
            links.Add(Link("Remotes", "https://sourcegit-scm.github.io/"));
            links.Add(Link("GitHub", "https://github.com/sourcegit-scm/sourcegit"));
            column.Add(links);

            var spacer = new Row();
            spacer.Height = 20;
            column.Add(spacer);
            if (date != null)
                column.Add(new Text(App.Text("About.ReleaseDate", Models.DateTimeFormat.Format(date.Value, true))) { Color = Color.SecondaryLabel });
            if (Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyCopyrightAttribute>() is { } copyright)
                column.Add(new Text(copyright.Copyright) { Color = Color.SecondaryLabel });
            row.Add(column);
            window.Content = row;
        }

        private static Button Link(string icon, string url)
        {
            var button = new Button(url)
            {
                Icon = Icons.Get(icon, 16),
                IconOnly = true,
                Style = ButtonStyle.Borderless,
                ToolTip = url,
            };
            button.Click += () => Native.OS.OpenBrowser(url);
            return button;
        }

        /// <summary>Upstream's: its friendly version and build date where
        /// the build gave them, else "v" and the assembly's version.</summary>
        private static (string, DateTime?) Version()
        {
            var assembly = Assembly.GetExecutingAssembly();
            string version = null;
            DateTime? date = null;
            foreach (var attr in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
            {
                if (attr.Key.Equals("BuildDate", StringComparison.OrdinalIgnoreCase) && DateTime.TryParse(attr.Value, out var built))
                    date = built;
                else if (attr.Key.Equals("FriendlyVersion", StringComparison.OrdinalIgnoreCase) && FriendlyVersion().IsMatch(attr.Value))
                    version = attr.Value;
            }
            if (version == null && assembly.GetName().Version is { } v)
                version = $"v{v.Major}.{v.Minor:D2}";
            return (version ?? "", date);
        }

        /// <summary>Upstream's icon (the PNG in its <c>App.ico</c>), 100
        /// points, as the platform's image view shows pixels.</summary>
        private static Image AppIcon()
        {
            try
            {
                var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "App.ico"));
                // The icon's first image, which is a PNG.
                var size = BitConverter.ToInt32(bytes, 14);
                var offset = BitConverter.ToInt32(bytes, 18);
                var png = StbImageSharp.ImageResult.FromMemory(bytes[offset..(offset + size)], StbImageSharp.ColorComponents.RedGreenBlueAlpha);
                var image = new Image();
                image.SetPixels(png.Width, png.Height, png.Data, png.Width / 100f);
                return image;
            }
            catch (Exception)
            {
                return null;
            }
        }

        [GeneratedRegex(@"^v\d{4}\.\d{1,2}(?:\-\d+\-[0-9a-f]{8})?(?:\-dirty)?$")]
        private static partial Regex FriendlyVersion();
    }
}
