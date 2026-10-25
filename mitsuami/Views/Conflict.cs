using System.Linq;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's <c>Views/Conflict.axaml</c>, the detail of a conflicted
    /// file among the local changes, in a box with upstream's 4-point
    /// corners: the conflict icon (64 points) and "Conflicts", the file's
    /// marker in a red pill and what conflicts, a box saying which commit
    /// is mine and which theirs, then Use Theirs, Use Mine and Merge (for a
    /// text file), whose arrow offers the external merge tool. Once
    /// resolved: a green check, "Resolved" and that the file can be staged.
    /// </summary>
    public static class Conflict
    {
        public static Node Create(ViewModels.Repository repo, ViewModels.Conflict vm)
        {
            var box = new Column { AlignItems = Align.Center, Justify = Justify.Center };
            box.Grow = 1;
            box.Add(new Outline(Color.Separator, 4) { Inset = new Thickness(0) });

            var conflicted = new Column { AlignItems = Align.Center };
            conflicted.Add(new Icon(Icons.Get("Conflict")) { Size = 64, Color = Color.SecondaryLabel });
            conflicted.Add(new Text(App.Text("WorkingCopy.Conflicts"))
            {
                Style = TextStyle.Title,
                Weight = FontWeight.Bold,
                Color = Color.SecondaryLabel,
                Margin = new Thickness(16, 0),
            });

            var what = new Row { AlignItems = Align.Center, Gap = 8, Margin = new Thickness(0, 0, 8, 0) };
            var marker = new Row { AlignItems = Align.Center, Padding = new Thickness(0, 8) };
            marker.Height = 16;
            marker.Add(new Pill { Inset = new Thickness(0) });
            marker.Add(new Text(vm.Marker) { Style = TextStyle.Caption, Weight = FontWeight.Bold, Color = Color.Rgba(255, 255, 255) });
            what.Add(marker);
            what.Add(new Text(vm.Description));
            conflicted.Add(what);

            // Upstream's grid: the labels right-aligned in a column of
            // their own, each row 32 points high.
            var sides = new Row { AlignItems = Align.Stretch, Gap = 16, Padding = new Thickness(8, 8, 8, 16), Margin = new Thickness(0, 16) };
            sides.Add(new Outline(Color.Separator, 4) { Inset = new Thickness(0) });
            var labels = new Column { AlignItems = Align.End };
            var values = new Column { AlignItems = Align.Start };
            values.Grow = 1;
            values.MinWidth = 0;
            foreach (var label in new[] { "MINE", "THEIRS" })
            {
                var text = new Text(label) { Weight = FontWeight.Bold, Color = Color.SecondaryLabel };
                var line = new Row { AlignItems = Align.Center };
                line.Height = 32;
                line.Add(text);
                labels.Add(line);
            }
            var mine = new Row { AlignItems = Align.Center, Gap = 4 };
            mine.Height = 32;
            var theirs = new Row { AlignItems = Align.Center, Gap = 4 };
            theirs.Height = 32;
            values.Add(mine);
            values.Add(theirs);
            sides.Add(labels);
            sides.Add(values);
            conflicted.Add(sides);

            var buttons = new Row { AlignItems = Align.Center, Gap = 8, Margin = new Thickness(8, 0, 0, 0) };
            var useTheirs = new Button(Popups.ButtonText("WorkingCopy.Conflicts.UseTheirs")) { Icon = Icons.Get("Incoming", 12) };
            useTheirs.Click += () => _ = vm.UseTheirsAsync();
            buttons.Add(useTheirs);
            var useMine = new Button(Popups.ButtonText("WorkingCopy.Conflicts.UseMine")) { Icon = Icons.Get("Local", 12) };
            useMine.Click += () => _ = vm.UseMineAsync();
            buttons.Add(useMine);
            // Upstream's merge editor waits for bemtvi.
            var merge = new SplitButton(Popups.ButtonText("WorkingCopy.Conflicts.Merge")) { Icon = Icons.Get("Conflict", 12), Style = ButtonStyle.Prominent };
            merge.Click += () => _ = Dialogs.Alert(null, "The merge editor isn't ported to mitsuami yet.", "", [App.Text("Sure")]);
            merge.Menu = [new MenuItem(App.Text("WorkingCopy.Conflicts.MergeExternal"), () => _ = vm.MergeExternalAsync())];
            buttons.Add(merge);
            conflicted.Add(buttons);
            box.Add(conflicted);

            var resolved = new Column { AlignItems = Align.Center };
            resolved.Add(new Icon(Icons.Get("Check")) { Size = 64, Color = Color.Rgba(0, 128, 0) });
            resolved.Add(new Text(App.Text("WorkingCopy.Conflicts.Resolved"))
            {
                Style = TextStyle.Title,
                Weight = FontWeight.Bold,
                Color = Color.SecondaryLabel,
                Margin = new Thickness(16, 0, 8, 0),
            });
            resolved.Add(new Text(App.Text("WorkingCopy.CanStageTip")) { Color = Color.SecondaryLabel });
            box.Add(resolved);

            Bind.OneWay(vm, nameof(vm.State), () => vm.State, state =>
            {
                conflicted.IsVisible = state != Models.ConflictFileState.Resolved;
                resolved.IsVisible = state == Models.ConflictFileState.Resolved;
                merge.IsVisible = state == Models.ConflictFileState.UnmergedText;
            }, owner: box);
            Bind.OneWay(vm, nameof(vm.Mine), () => vm.Mine, side => Side(mine, repo, side), owner: mine);
            Bind.OneWay(vm, nameof(vm.Theirs), () => vm.Theirs, side => Side(theirs, repo, side), owner: theirs);
            return box;
        }

        /// <summary>A side: a commit's refs, its short SHA (a link to it in
        /// upstream's orange) and subject; or what it is, after the changes
        /// icon ("Stash or Patch").</summary>
        private static void Side(Row row, ViewModels.Repository repo, object side)
        {
            foreach (var child in row.Children.ToArray())
                child.Destroy();
            switch (side)
            {
                case Models.Commit commit:
                    var refs = new CommitRefs();
                    refs.Show(commit, repo.Remotes.Count == 1, useGraphColor: false);
                    row.Add(refs.View);
                    var sha = new Text(commit.SHA.Length > 10 ? commit.SHA[..10] : commit.SHA)
                    {
                        Style = TextStyle.Monospace,
                        Color = Color.Rgba(255, 140, 0),
                        ToolTip = commit.SHA,
                    };
                    var link = new Row();
                    link.Add(sha);
                    link.Clicked += () => repo.NavigateToCommit(commit.SHA);
                    row.Add(link);
                    var subject = new Text(commit.Subject) { MaxLines = 1 };
                    subject.Shrink = 1;
                    subject.MinWidth = 0;
                    row.Add(subject);
                    break;
                case string text:
                    row.Add(new Icon(Icons.Get("Changes")) { Size = 12, Color = Color.Label });
                    row.Add(new Text(text));
                    break;
            }
        }

        /// <summary>A box's outline, in its corners' radius.</summary>
        private sealed class Outline(Color color, float radius) : DrawnWidget("ConflictBox", 0, 0)
        {
            protected override void Draw(Canvas canvas) =>
                canvas.StrokeRect(0.5f, 0.5f, canvas.Width - 1, canvas.Height - 1, color, 1, radius);
        }

        /// <summary>Behind the file's marker: upstream's red pill.</summary>
        private sealed class Pill() : DrawnWidget("ConflictMarker", 0, 0)
        {
            protected override void Draw(Canvas canvas) =>
                canvas.FillRect(0, 0, canvas.Width, canvas.Height, Color.Rgba(255, 0, 0), canvas.Height / 2);
        }
    }
}
