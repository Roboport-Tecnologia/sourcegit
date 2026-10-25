using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's keyboard shortcuts reference (<c>Views/Hotkeys.axaml</c>),
    /// a window of its own: the app menus' shortcuts, each menu a section,
    /// then the local changes' own keys, written as the platform writes them
    /// (⌘⇧↑ on macOS, Ctrl+Shift+Up elsewhere). Only keys that work are
    /// listed: upstream's zoom and command palettes aren't ported.
    /// </summary>
    public static class Hotkeys
    {
        private static Window s_shown;

        /// <summary>The window, if it's open: for captures.</summary>
        public static Window Shown => s_shown;

        public static void Show()
        {
            if (s_shown != null)
                return;
            var window = s_shown = new Window(App.Text("Hotkeys"), 520, 600);
            window.Closed += () => s_shown = null;
            var scroll = new ScrollView();
            scroll.Grow = 1;
            scroll.MinHeight = 0;
            var column = scroll.Content;
            column.Padding = 20;
            column.Gap = 6;
            column.AlignItems = Align.Stretch;

            var sections = AppMenu.Sections()
                .Select(s => (s.Title == "SourceGit" ? Popups.ButtonText("Hotkeys.Global") : s.Title,
                    s.Commands.Where(c => c?.Key != null).Select(c => (c.Description ?? c.Title, c.Key.Value)).ToList()))
                .ToList();
            sections.Add((App.Text("WorkingCopy"),
            [
                (App.Text("Hotkeys.Repo.Commit"), Shortcut.Of(Key.Enter).WithPrimary()),
                (App.Text("Hotkeys.Repo.CommitWithAutoStage"), Shortcut.Of(Key.Enter).WithPrimary().WithShift()),
                (App.Text("Hotkeys.Repo.CommitAndPush"), Shortcut.Of(Key.Enter).WithPrimary().WithAlt()),
            ]));
            var first = true;
            foreach (var (title, keys) in sections)
            {
                if (keys.Count == 0)
                    continue;
                var heading = new Text(title) { Weight = FontWeight.Bold, Color = Color.SecondaryLabel };
                if (!first)
                    heading.Margin = new Thickness(14, 0, 0, 0);
                first = false;
                column.Add(heading);
                foreach (var (name, key) in keys)
                {
                    var row = new Row { Gap = 12, AlignItems = Align.Center };
                    var keyText = new Text(Format(key)) { Weight = FontWeight.Semibold };
                    keyText.Width = 150;
                    keyText.Shrink = 0;
                    row.Add(keyText);
                    var text = new Text(name) { MaxLines = 1 };
                    text.Shrink = 1;
                    text.MinWidth = 0;
                    row.Add(text);
                    column.Add(row);
                }
            }
            var page = new Column { AlignItems = Align.Stretch };
            page.Grow = 1;
            page.Add(scroll);
            window.Content = page;
        }

        /// <summary>A shortcut as the platform writes it: macOS's symbols in
        /// its order (⌥⇧⌘), else "Ctrl+Alt+Shift+" and the key.</summary>
        public static string Format(Shortcut key)
        {
            var mac = OperatingSystem.IsMacOS();
            var name = key.Key switch
            {
                Key.Char => key.Code == ',' ? "," : char.ToUpperInvariant((char)key.Code).ToString(),
                Key.Enter => mac ? "↩" : "Enter",
                Key.Tab => mac ? "⇥" : "Tab",
                Key.Up => mac ? "↑" : "Up",
                Key.Down => mac ? "↓" : "Down",
                Key.Left => mac ? "←" : "Left",
                Key.Right => mac ? "→" : "Right",
                Key.F => $"F{key.Code}",
                var other => other.ToString(),
            };
            if (mac)
            {
                var symbols = new StringBuilder();
                if (key.Alt) symbols.Append('⌥');
                if (key.Shift) symbols.Append('⇧');
                if (key.Primary) symbols.Append('⌘');
                return symbols + name;
            }
            var parts = new List<string>();
            if (key.Primary) parts.Add("Ctrl");
            if (key.Alt) parts.Add("Alt");
            if (key.Shift) parts.Add("Shift");
            parts.Add(name);
            return string.Join('+', parts);
        }
    }
}
