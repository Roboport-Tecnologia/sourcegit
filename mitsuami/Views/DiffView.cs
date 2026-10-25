using System;
using System.ComponentModel;
using System.Linq;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's diff view (<c>Views/DiffView.axaml</c>, <c>ImageDiffView</c>):
    /// the file's title, then its diff. Images show side by side, old and
    /// new with their sizes; binaries and LFS objects their old and new
    /// sizes; submodules their old and new commits; no change says so.
    /// Text waits for the code view (bemtvi), and so do upstream's swipe,
    /// blend and difference modes for images, which composite pixels.
    /// Until a file is picked, upstream's welcome.
    /// </summary>
    public static class DiffView
    {
        /// <summary>The diff of what <paramref name="property"/> gives (a
        /// <see cref="ViewModels.DiffContext"/>, or nothing for the welcome).</summary>
        public static Node Create(INotifyPropertyChanged source, string property, Func<object> diff)
        {
            var holder = new Column { AlignItems = Align.Stretch };
            holder.Grow = 1;
            holder.MinWidth = 100;
            holder.MinHeight = 0;
            Node shown = null;
            Bind.OneWay(source, property, diff, value =>
            {
                shown?.Destroy();
                shown = value is ViewModels.DiffContext context ? Diff(context) : Welcome();
                holder.Add(shown);
            }, owner: holder);
            return holder;
        }

        private static Node Welcome()
        {
            var column = new Column { AlignItems = Align.Center, Justify = Justify.Center, Gap = 16 };
            column.Grow = 1;
            column.Add(new Icon(Icons.Get("Diff")) { Size = 64, Color = Color.SecondaryLabel });
            column.Add(new Text(App.Text("Diff.Welcome")) { Style = TextStyle.Title, Weight = FontWeight.Bold, Color = Color.SecondaryLabel });
            return column;
        }

        private static Node Diff(ViewModels.DiffContext context)
        {
            var column = new Column { AlignItems = Align.Stretch, Gap = 4 };
            column.Grow = 1;
            column.MinHeight = 0;
            var header = new Row { AlignItems = Align.Center, Gap = 6, Padding = new Thickness(4, 4) };
            header.Add(new Icon(Icons.Get("File")) { Size = 12 });
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
            header.Add(merger);
            column.Add(header);

            var body = new Column { AlignItems = Align.Stretch };
            body.Grow = 1;
            body.MinHeight = 0;
            column.Add(body);
            Node shown = null;
            Bind.OneWay(context, nameof(context.Content), () => context.Content, content =>
            {
                shown?.Destroy();
                shown = Content(content);
                body.Add(shown);
            }, owner: body);
            return column;
        }

        private static Node Content(object content)
        {
            var column = new Column { AlignItems = Align.Center, Justify = Justify.Center, Gap = 12 };
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
                    column.Padding = 12;
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
                default:
                    column.Add(new Icon(Icons.Get("Diff")) { Size = 64, Color = Color.SecondaryLabel });
                    column.Add(new Text("Text diffs aren't ported to mitsuami yet: they need the code view.") { Style = TextStyle.Caption, Color = Color.SecondaryLabel });
                    break;
            }
            return column;
        }

        /// <summary>Upstream's side by side: each image as large as its
        /// pixels, shrunk to fit, under its old or new mark and sizes.</summary>
        private static Node Images(Models.ImageDiff diff)
        {
            var row = new Row { AlignItems = Align.Stretch, Gap = 16, Padding = 8 };
            row.Grow = 1;
            row.MinHeight = 0;
            row.Add(Side(App.Text("Diff.Old"), diff.Old, diff.OldImageSize, diff.OldFileSize));
            row.Add(Side(App.Text("Diff.New"), diff.New, diff.NewImageSize, diff.NewFileSize));
            return row;

            static Node Side(string label, Avalonia.Media.Imaging.Bitmap bitmap, string size, long fileSize)
            {
                var column = new Column { AlignItems = Align.Center, Gap = 8 };
                column.Grow = 1;
                column.MinWidth = 0;
                var info = new Row { AlignItems = Align.Center, Gap = 8 };
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
            var column = new Column { AlignItems = Align.Stretch, Gap = 4 };
            foreach (var (label, size) in new[] { (App.Text("Diff.Old"), old), (App.Text("Diff.New"), now) })
            {
                var row = new Row { AlignItems = Align.Center, Gap = 16, Justify = Justify.SpaceBetween };
                row.Add(new Text(label) { Style = TextStyle.Caption, Weight = FontWeight.Bold });
                row.Add(new Text(Bytes(size)) { Style = TextStyle.Title, Color = Color.SecondaryLabel });
                column.Add(row);
            }
            return column;
        }

        private static Node Commit(string label, Models.RevisionSubmodule revision)
        {
            var column = new Column { Gap = 4 };
            column.Add(new Text(label) { Style = TextStyle.Caption, Weight = FontWeight.Bold, Color = Color.SecondaryLabel });
            if (revision.Commit is { } commit)
            {
                var top = new Row { AlignItems = Align.Center, Gap = 8 };
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
