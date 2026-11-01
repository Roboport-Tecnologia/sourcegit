using System;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Nodes;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's diff view (<c>Views/DiffView.axaml</c>, <c>ImageDiffView</c>):
    /// the file's title, then its diff. Images show side by side, old and
    /// new with their sizes; binaries and LFS objects their old and new
    /// sizes; submodules their old and new commits; no change says so.
    /// Text shows in the code view (bemtvi). Upstream's swipe,
    /// blend and difference modes for images wait for it, as they composite pixels.
    /// Until a file is picked, upstream's welcome.
    /// </summary>
    public static class DiffView
    {
        /// <summary>The diff of what <paramref name="property"/> gives (a
        /// <see cref="ViewModels.DiffContext"/>, or nothing for the welcome).</summary>
        public static Node Create(INotifyPropertyChanged source, string property, Func<object> diff)
        {
            var pane = new CodePane();
            pane.Node.MinWidth = 100;
            Node shown = null;
            Bind.OneWay(source, property, diff, value =>
            {
                shown?.Destroy();
                if (value is ViewModels.DiffContext context)
                {
                    shown = Diff(context, pane);
                }
                else
                {
                    pane.HideCode();
                    shown = Welcome();
                }
                pane.Top.Add(shown);
            }, owner: pane.Node);
            return pane.Node;
        }

        private static Node Welcome()
        {
            var column = new Column { AlignItems = Align.Center, Justify = Justify.Center, Gap = Spacing.Lg };
            column.Grow = 1;
            column.Add(new Icon(Icons.Get("Diff")) { Size = 64, Color = Color.SecondaryLabel });
            column.Add(new Text(App.Text("Diff.Welcome")) { Style = TextStyle.Title, Weight = FontWeight.Bold, Color = Color.SecondaryLabel });
            return column;
        }

        private static Node Diff(ViewModels.DiffContext context, CodePane pane)
        {
            var column = new Column { AlignItems = Align.Stretch, Gap = Spacing.Xs };
            column.Grow = 1;
            column.MinHeight = 0;
            var header = new Row { AlignItems = Align.Center, Gap = Spacing.Sm, Padding = new Thickness(Spacing.Xs, Spacing.Xs) };
            // The file it's now: a rename's title is its old path, an
            // arrow, then its new one.
            header.Add(FileIcons.File(context.Title.Split(" → ")[^1]));
            var title = new Text(context.Title) { Style = TextStyle.Caption, MaxLines = 1, ToolTip = context.Title };
            title.Grow = 1;
            title.Shrink = 1;
            title.MinWidth = 0;
            header.Add(title);
            if (context.OldMode != context.NewMode && context.OldMode != 0 && context.NewMode != 0)
                header.Add(new Text($"{Convert.ToString(context.OldMode, 8)} → {Convert.ToString(context.NewMode, 8)}") { Style = TextStyle.Caption, Color = Color.SecondaryLabel });
            var merger = new Button(App.Text("Diff.UseMerger"))
            {
                Icon = Icons.Get("OpenWith", 12),
                IconOnly = true,
                Style = ButtonStyle.Borderless,
                ToolTip = App.Text("Diff.UseMerger"),
            };
            merger.Click += context.OpenExternalMergeTool;
            var layout = Toolbar(pane);
            Bind.OneWay(context, nameof(context.IsTextDiff), () => context.IsTextDiff, text => layout.IsVisible = text, owner: layout);
            header.Add(layout);
            header.Add(IgnoreWhitespace(context));
            header.Add(merger);
            column.Add(header);

            var body = new Column { AlignItems = Align.Stretch };
            body.Grow = 1;
            body.MinHeight = 0;
            column.Add(body);
            Node shown = null;
            Bind.OneWay(context, nameof(context.Content), () => context.Content, content =>
            {
                // Upstream's model wraps text by its unified or side by side preference.
                // The plugin lays out the same lines either way (the toolbar).
                if (content is ViewModels.TextDiffContext text)
                {
                    shown?.Destroy();
                    shown = null;
                    body.IsVisible = false;
                    pane.Code().ShowDiff(context.Title.Split(" → ")[^1], text.Data);
                    return;
                }
                // Loading: what's showing stays until the next is here, with no flash.
                if (content == null && pane.ShowsCode)
                    return;
                pane.HideCode();
                body.IsVisible = true;
                shown?.Destroy();
                shown = Content(content);
                body.Add(shown);
            }, owner: body);
            return column;
        }

        /// <summary>Upstream's toolbar for a text diff, none of which asks git again:
        /// going through its changes, with where the cursor is among them, more or fewer lines
        /// around them or all of them, syntax, word wrap with one pane, hidden symbols, side
        /// by side.</summary>
        private static Row Toolbar(CodePane pane)
        {
            var port = PortSettings.Instance;
            var pref = ViewModels.Preferences.Instance;
            var row = new Row { AlignItems = Align.Center, Gap = Spacing.Xs };

            Button Go(string text, string icon, string where)
            {
                var button = IconButton(text, icon);
                button.Click += () => pane.Showing?.GoToChange(where);
                return button;
            }
            var position = new Text("-/-") { Style = TextStyle.Caption, MaxLines = 1 };
            void Heard(string name, string json)
            {
                if (name != "change_position" || JsonNode.Parse(json) is not { } at)
                    return;
                var index = at["at"]?.GetValue<int>() ?? 0;
                position.Value = $"{(index == 0 ? "-" : index.ToString())}/{at["total"]?.GetValue<int>() ?? 0}";
            }
            pane.PluginEvent += Heard;
            row.Forgotten += () => pane.PluginEvent -= Heard;
            row.Add(Go("Diff.First", "Top", "first"));
            row.Add(Go("Diff.Prev", "Up", "prev"));
            row.Add(position);
            row.Add(Go("Diff.Next", "Down", "next"));
            row.Add(Go("Diff.Last", "Bottom", "last"));

            var more = IconButton("Diff.VisualLines.Incr", "Lines.Incr");
            var fewer = IconButton("Diff.VisualLines.Decr", "Lines.Decr");
            void Enable()
            {
                more.IsEnabled = !port.EditorDiffAllLines;
                fewer.IsEnabled = !port.EditorDiffAllLines && port.EditorDiffContext > PortSettings.MinDiffContext;
            }
            more.Click += () =>
            {
                port.EditorDiffContext++;
                Enable();
                CodeView.SettingsChanged();
            };
            fewer.Click += () =>
            {
                port.EditorDiffContext = Math.Max(PortSettings.MinDiffContext, port.EditorDiffContext - 1);
                Enable();
                CodeView.SettingsChanged();
            };
            var all = IconToggle("Diff.VisualLines.All", "Lines.All", port.EditorDiffAllLines);
            all.Changed += on =>
            {
                port.EditorDiffAllLines = on;
                Enable();
                CodeView.SettingsChanged();
            };
            Enable();
            row.Add(more);
            row.Add(fewer);
            row.Add(all);

            ToggleButton Pref(string text, string icon, bool on, Action<bool> set)
            {
                var toggle = IconToggle(text, icon, on);
                toggle.Changed += value =>
                {
                    set(value);
                    CodeView.SettingsChanged();
                };
                return toggle;
            }
            row.Add(Pref("Diff.SyntaxHighlight", "SyntaxHighlight", pref.UseSyntaxHighlighting, v => pref.UseSyntaxHighlighting = v));
            var wrap = Pref("Diff.ToggleWordWrap", "WordWrap", pref.EnableDiffViewWordWrap, v => pref.EnableDiffViewWordWrap = v);
            wrap.IsVisible = !pref.UseSideBySideDiff;
            row.Add(wrap);
            row.Add(Pref("Diff.ShowHiddenSymbols", "HiddenSymbol", pref.ShowHiddenSymbolsInDiffView, v => pref.ShowHiddenSymbolsInDiffView = v));
            row.Add(Pref("Diff.SideBySide", "Layout", pref.UseSideBySideDiff, v =>
            {
                pref.UseSideBySideDiff = v;
                wrap.IsVisible = !v;
            }));
            return row;
        }

        /// <summary>Upstream's: it changes what git sends, so the diff is read again. It
        /// shows where whitespace can matter, as upstream's model says.</summary>
        private static ToggleButton IgnoreWhitespace(ViewModels.DiffContext context)
        {
            var pref = ViewModels.Preferences.Instance;
            var toggle = IconToggle("Diff.IgnoreWhitespace", "Whitespace", pref.IgnoreWhitespaceChangesInDiff);
            toggle.Changed += on =>
            {
                pref.IgnoreWhitespaceChangesInDiff = on;
                context.CheckSettings();
            };
            Bind.OneWay(context, nameof(context.IsIgnoreWhitespaceVisible), () => context.IsIgnoreWhitespaceVisible,
                visible => toggle.IsVisible = visible, owner: toggle);
            return toggle;
        }

        private static Button IconButton(string text, string icon) => new(App.Text(text))
        {
            Icon = Icons.Get(icon, 12),
            IconOnly = true,
            Style = ButtonStyle.Borderless,
            ToolTip = App.Text(text),
        };

        private static ToggleButton IconToggle(string text, string icon, bool on) => new(App.Text(text))
        {
            Icon = Icons.Get(icon, 12),
            IconOnly = true,
            Style = ButtonStyle.Borderless,
            ToolTip = App.Text(text),
            IsChecked = on,
        };

        private static Node Content(object content)
        {
            var column = new Column { AlignItems = Align.Center, Justify = Justify.Center, Gap = Spacing.Md };
            column.Grow = 1;
            column.MinHeight = 0;
            switch (content)
            {
                case Models.ImageDiff image:
                    return Images(image);
                case Models.BinaryDiff binary:
                    column.Add(new Text(App.Text("Diff.Binary")) { Style = TextStyle.Title, Weight = FontWeight.Bold, Color = Color.SecondaryLabel });
                    column.Add(new Icon(Icons.Get("Binary")) { Size = 64, Color = Color.SecondaryLabel });
                    column.Add(Sizes(binary.OldSize, binary.NewSize));
                    break;
                case Models.LFSDiff lfs:
                    column.Add(new Text(App.Text("Diff.LFS")) { Style = TextStyle.Title, Weight = FontWeight.Bold, Color = Color.SecondaryLabel });
                    column.Add(new Icon(Icons.Get("LFS")) { Size = 64, Color = Color.SecondaryLabel });
                    column.Add(Sizes(lfs.Old.Size, lfs.New.Size));
                    break;
                case Models.SubmoduleDiff submodule:
                    column.Justify = Justify.Start;
                    column.AlignItems = Align.Stretch;
                    column.Padding = Spacing.Md;
                    column.Add(new Text(App.Text("Diff.Submodule")) { Style = TextStyle.Title, Weight = FontWeight.Bold, Color = Color.SecondaryLabel });
                    if (submodule.New == null)
                        column.Add(new Text(App.Text("Diff.Submodule.Deleted")) { Weight = FontWeight.Bold, Color = Color.Error });
                    if (submodule.Old != null)
                        column.Add(Commit(App.Text("Diff.Old"), submodule.Old));
                    if (submodule.New != null)
                        column.Add(Commit(App.Text("Diff.New"), submodule.New));
                    break;
                case Models.NoOrEOLChange:
                    column.Add(new Icon(Icons.Get("Check")) { Size = 64, Color = Color.SecondaryLabel });
                    column.Add(new Text(App.Text("Diff.NoChange")) { Style = TextStyle.Title, Weight = FontWeight.Bold, Color = Color.SecondaryLabel });
                    break;
                case null:
                    column.Add(new Spinner());
                    break;
                case Models.EmptyFile:
                    column.Add(new Icon(Icons.Get("File.Empty")) { Size = 64, Color = Color.SecondaryLabel });
                    column.Add(new Text(App.Text("Diff.EmptyFile")) { Style = TextStyle.Title, Weight = FontWeight.Bold, Color = Color.SecondaryLabel });
                    break;
                // Its sizes; upstream's tab with the images waits.
                case ViewModels.LFSImageDiff lfsImage:
                    column.Add(new Text(App.Text("Diff.LFS")) { Style = TextStyle.Title, Weight = FontWeight.Bold, Color = Color.SecondaryLabel });
                    column.Add(new Icon(Icons.Get("LFS")) { Size = 64, Color = Color.SecondaryLabel });
                    column.Add(Sizes(lfsImage.LFS.Old.Size, lfsImage.LFS.New.Size));
                    break;
                default:
                    // On screen: thrown, it left the pane empty.
                    column.Add(new Text($"No view for a diff's {content.GetType().Name}") { Style = TextStyle.Caption, Color = Color.SecondaryLabel });
                    break;
            }
            return column;
        }

        /// <summary>Upstream's side by side: each image as large as its
        /// pixels, shrunk to fit, under its old or new mark and sizes.</summary>
        private static Node Images(Models.ImageDiff diff)
        {
            var row = new Row { AlignItems = Align.Stretch, Gap = Spacing.Lg, Padding = Spacing.Sm };
            row.Grow = 1;
            row.MinHeight = 0;
            row.Add(Side(App.Text("Diff.Old"), diff.Old, diff.OldImageSize, diff.OldFileSize));
            row.Add(Side(App.Text("Diff.New"), diff.New, diff.NewImageSize, diff.NewFileSize));
            return row;

            static Node Side(string label, Avalonia.Media.Imaging.Bitmap bitmap, string size, long fileSize)
            {
                var column = new Column { AlignItems = Align.Center, Gap = Spacing.Sm };
                column.Grow = 1;
                column.MinWidth = 0;
                var info = new Row { AlignItems = Align.Center, Gap = Spacing.Sm };
                info.Add(new Text(label) { Style = TextStyle.Caption, Weight = FontWeight.Bold });
                info.Add(new Text(size) { Style = TextStyle.Caption });
                info.Add(new Text(Bytes(fileSize)) { Style = TextStyle.Caption, Color = Color.SecondaryLabel });
                column.Add(info);
                var area = new Column { AlignItems = Align.Center, Justify = Justify.Center };
                area.Grow = 1;
                area.MinHeight = 0;
                area.AlignSelf = Align.Stretch;
                if (bitmap != null)
                    area.Add(RevisionFiles.Picture(bitmap));
                column.Add(area);
                return column;
            }
        }

        private static Node Sizes(long old, long now)
        {
            var column = new Column { AlignItems = Align.Stretch, Gap = Spacing.Xs };
            foreach (var (label, size) in new[] { (App.Text("Diff.Old"), old), (App.Text("Diff.New"), now) })
            {
                var row = new Row { AlignItems = Align.Center, Gap = Spacing.Lg, Justify = Justify.SpaceBetween };
                row.Add(new Text(label) { Style = TextStyle.Caption, Weight = FontWeight.Bold });
                row.Add(new Text(Bytes(size)) { Style = TextStyle.Title, Color = Color.SecondaryLabel });
                column.Add(row);
            }
            return column;
        }

        private static Node Commit(string label, Models.RevisionSubmodule revision)
        {
            var column = new Column { Gap = Spacing.Xs };
            column.Add(new Text(label) { Style = TextStyle.Caption, Weight = FontWeight.Bold, Color = Color.SecondaryLabel });
            if (revision.Commit is { } commit)
            {
                var top = new Row { AlignItems = Align.Center, Gap = Spacing.Sm };
                top.Add(new Avatar(16, menu: false) { User = commit.Author }.View);
                top.Add(new Text(commit.Author.Name));
                top.Add(new Text(commit.SHA[..10]) { Style = TextStyle.Monospace, Color = Color.Rgba(255, 140, 0), Selectable = true });
                top.Add(new Text(Models.DateTimeFormat.Format(commit.CommitterTime)) { Color = Color.SecondaryLabel });
                column.Add(top);
                column.Add(new Text(revision.FullMessage?.Message ?? commit.Subject) { Selectable = true });
            }
            return column;
        }

        private static string Bytes(long bytes) =>
            bytes switch
            {
                < 1024 => $"{bytes} B",
                < 1024 * 1024 => $"{bytes / 1024.0:F2} KB",
                < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:F2} MB",
                _ => $"{bytes / 1024.0 / 1024 / 1024:F2} GB",
            };
    }
}
