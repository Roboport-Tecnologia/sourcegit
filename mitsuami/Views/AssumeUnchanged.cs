using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's assume-unchanged manager (<c>Views/AssumeUnchangedManager.axaml</c>),
    /// a sheet on the main window where upstream has a window of its own:
    /// the files git assumes unchanged, framed, each with a button that
    /// stops assuming it; upstream's empty icon when there are none.
    /// </summary>
    public static class AssumeUnchanged
    {
        /// <summary>The sheet showing, if any: for captures.</summary>
        public static Window Shown { get; private set; }

        public static void Show(ViewModels.Repository repo)
        {
            var vm = new ViewModels.AssumeUnchangedManager(repo);
            var title = Popups.ButtonText("AssumeUnchanged");
            var sheet = Shown = new Window(App.MainWindow, title, 600, 400);
            sheet.Closed += () => Shown = null;

            // Upstream's fixed size: the list fills it.
            var column = new Column { Padding = 16, Gap = 12, AlignItems = Align.Stretch };
            column.Grow = 1;
            column.Add(new Text(title) { Style = TextStyle.Headline });

            var area = new Column { AlignItems = Align.Stretch, Justify = Justify.Center, Gap = 16 };
            area.Grow = 1;
            area.MinHeight = 0;
            var files = new ListView<string>(file => file, file => Row(vm, file), itemAnimations: false)
            {
                Style = ListStyle.Framed,
                EstimatedRowHeight = 26,
                Items = vm.Files,
            };
            files.Grow = 1;
            files.MinHeight = 0;
            area.Add(files);
            var empty = new Icon(Icons.Get("Empty")) { Size = 48, Color = Color.SecondaryLabel, AlignSelf = Align.Center };
            area.Add(empty);
            var none = new Text(Popups.ButtonText("AssumeUnchanged.Empty")) { Color = Color.SecondaryLabel, AlignSelf = Align.Center };
            area.Add(none);
            column.Add(area);

            // The files come in after the sheet shows, and leave one by one.
            void Count()
            {
                files.IsVisible = vm.Files.Count > 0;
                empty.IsVisible = none.IsVisible = vm.Files.Count == 0;
            }
            Count();
            vm.Files.CollectionChanged += (_, _) => Count();

            var buttons = new Row { Justify = Justify.End };
            var close = new Button(Popups.ButtonText("Close")) { Role = ButtonRole.Default };
            close.Click += sheet.Destroy;
            buttons.Add(close);
            column.Add(buttons);
            sheet.Content = column;
        }

        /// <summary>Upstream's row: the file's icon, its path, and the
        /// button that stops assuming it unchanged.</summary>
        private static Node Row(ViewModels.AssumeUnchangedManager vm, string file)
        {
            var row = new Row { AlignItems = Align.Center, Gap = 4, Padding = new Thickness(0, 4, 0, 8) };
            row.Height = 26;
            row.Add(new Icon(Icons.Get("File")) { Size = 14 });
            var path = new Text(file) { MaxLines = 1 };
            path.Grow = 1;
            path.Shrink = 1;
            path.MinWidth = 0;
            row.Add(path);
            // Upstream's button has no text; "Remove" is upstream's word
            // elsewhere, for the platform's assistive technology.
            var remove = new Button(App.Text("Worktree.Remove"))
            {
                Icon = Icons.Get("Clear", 14),
                IconOnly = true,
                Style = ButtonStyle.Borderless,
            };
            remove.Width = 28;
            remove.Click += () => _ = vm.RemoveAsync(file);
            row.Add(remove);
            return row;
        }
    }
}
