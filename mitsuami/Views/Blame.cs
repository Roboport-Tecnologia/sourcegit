using System.Threading.Tasks;
using System.Text.Json.Nodes;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's blame window (<c>Views/Blame.axaml</c>), 1280 × 720 as upstream's least
    /// size: the file over its blame in the code view, then ignoring whitespace, back and
    /// forward through what was blamed, the revision and the one before it. Upstream's margin
    /// is drawn, so a click on it goes to a commit and Ctrl-click blames there; bemtvi's
    /// gutter takes no clicks, so a click selects a line, and the footer's buttons, or
    /// the editor's keys, act on it.
    /// </summary>
    public static class Blame
    {
        /// <summary>The window last opened, while it's open: for captures.</summary>
        public static Window Shown { get; private set; }

        /// <summary>As the local changes' menus ask: the file as HEAD has it.</summary>
        public static async Task ShowAtHead(ViewModels.Repository repo, string file)
        {
            var commit = await new Commands.QuerySingleCommit(repo.FullPath, "HEAD").GetResultAsync();
            if (commit != null)
                Show(new ViewModels.Blame(repo.FullPath, file, commit));
        }

        public static void Show(ViewModels.Blame vm)
        {
            var window = Shown = new Window(App.Text("Blame"), 1280, 720);
            window.Closed += () =>
            {
                if (Shown == window)
                    Shown = null;
            };
            var column = new Column { AlignItems = Align.Stretch, Gap = Spacing.Sm, Padding = Spacing.Md };
            column.Grow = 1;
            column.Add(Heading(vm));
            column.Add(new Separator());
            string sha = null;
            string file = null;
            var line = new Text { Style = TextStyle.Caption, MaxLines = 1, Color = Color.SecondaryLabel };
            line.Grow = 1;
            line.Shrink = 1;
            line.MinWidth = 0;
            var go = FooterButton("Go to Commit", "Commit");
            var here = FooterButton("Blame This Commit", "Blame");
            go.IsEnabled = false;
            here.IsEnabled = false;
            go.Click += () => vm.NavigateToCommit(file, sha, false);
            here.Click += () => vm.NavigateToCommit(file, sha, true);
            void Picked(string name, string json)
            {
                if (JsonNode.Parse(json) is not { } payload)
                    return;
                var picked = payload["sha"]?.GetValue<string>();
                var from = payload["file"]?.GetValue<string>();
                if (picked == null || from == null)
                    return;
                switch (name)
                {
                    case "blame_line":
                        sha = picked;
                        file = from;
                        go.IsEnabled = true;
                        here.IsEnabled = true;
                        var message = vm.GetCommitMessage(picked);
                        line.Value = $"{picked}  {message.Split('\n')[0]}";
                        line.ToolTip = message;
                        break;
                    case "blame_goto":
                        vm.NavigateToCommit(from, picked, false);
                        break;
                    case "blame_here":
                        vm.NavigateToCommit(from, picked, true);
                        break;
                }
            }
            column.Add(Body(vm, Picked));
            var picked = new Row { AlignItems = Align.Center, Gap = Spacing.Sm };
            picked.Add(line);
            picked.Add(go);
            picked.Add(here);
            column.Add(picked);
            column.Add(Options(vm));
            window.Content = column;
        }

        private static Button FooterButton(string text, string icon) => new(text)
        {
            Icon = Icons.Get(icon, 12),
            Style = ButtonStyle.Borderless,
            ToolTip = text,
        };

        private static Row Heading(ViewModels.Blame vm)
        {
            var heading = new Row { AlignItems = Align.Center, Gap = Spacing.Sm };
            heading.Add(new Icon(Icons.Get("File")) { Size = 14 });
            var text = new Text { Weight = FontWeight.Bold, Selectable = true };
            Bind.OneWay(vm, nameof(vm.File), () => vm.File, f => text.Value = f ?? "", owner: text);
            heading.Add(text);
            return heading;
        }

        /// <summary>The blame, upstream's spinner until it's read, and its message for a
        /// binary file. One editor for all that's blamed here, so moving through commits
        /// keeps it.</summary>
        private static Column Body(ViewModels.Blame vm, System.Action<string, string> picked)
        {
            var body = new Column { AlignItems = Align.Stretch, Justify = Justify.Center };
            body.Grow = 1;
            body.MinHeight = 0;
            CodeView code = null;
            Node other = new Spinner();
            body.Add(other);
            Bind.OneWay(vm, nameof(vm.Data), () => vm.Data, data =>
            {
                if (data == null)
                    return;
                other?.Destroy();
                other = null;
                if (data.IsBinary)
                {
                    code?.Node.Destroy();
                    code = null;
                    var column = new Column { AlignItems = Align.Center, Gap = Spacing.Md };
                    column.Add(new Icon(Icons.Get("Error")) { Size = 64, Color = Color.SecondaryLabel });
                    column.Add(new Text(App.Text("Blame.TypeNotSupported")) { Style = TextStyle.Title, Weight = FontWeight.Bold, Color = Color.SecondaryLabel });
                    other = column;
                    body.Add(other);
                    return;
                }
                if (code == null)
                {
                    code = new CodeView();
                    code.PluginEvent += picked;
                    body.Add(code.Node);
                }
                code.ShowBlame(vm.File, data);
            }, owner: body);
            return body;
        }

        private static Row Options(ViewModels.Blame vm)
        {
            var row = new Row { AlignItems = Align.Center, Gap = Spacing.Sm };
            var ignore = new Checkbox(App.Text("Blame.IgnoreWhitespace")) { IsChecked = vm.IgnoreWhitespace, ToolTip = "-w" };
            ignore.Changed += on => vm.IgnoreWhitespace = on;
            ignore.Grow = 1;
            row.Add(ignore);
            var back = IconOnly("Back", "TriangleLeft");
            back.Click += vm.Back;
            Bind.OneWay(vm, nameof(vm.CanBack), () => vm.CanBack, can => back.IsEnabled = can, owner: back);
            row.Add(back);
            var forward = IconOnly("Forward", "TriangleRight");
            forward.Click += vm.Forward;
            Bind.OneWay(vm, nameof(vm.CanForward), () => vm.CanForward, can => forward.IsEnabled = can, owner: forward);
            row.Add(forward);
            var revision = new Text { MaxLines = 1 };
            revision.Width = 520;
            Bind.OneWay(vm, nameof(vm.Revision), () => vm.Revision, commit =>
                revision.Value = commit == null ? "" : $"{commit.SHA[..10]}  {commit.Subject}", owner: revision);
            row.Add(revision);
            var previous = IconOnly(App.Text("Blame.BlameOnPreviousRevision"), "GotoParent");
            previous.Click += vm.GotoPrevRevision;
            Bind.OneWay(vm, nameof(vm.PrevRevision), () => vm.PrevRevision, commit => previous.IsEnabled = commit != null, owner: previous);
            row.Add(previous);
            return row;
        }

        private static Button IconOnly(string text, string icon) => new(text)
        {
            Icon = Icons.Get(icon, 12),
            IconOnly = true,
            Style = ButtonStyle.Borderless,
            ToolTip = text,
        };
    }
}
