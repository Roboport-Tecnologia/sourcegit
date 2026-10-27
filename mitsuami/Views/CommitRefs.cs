using System;
using System.Collections.Generic;
using System.Linq;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's <c>CommitRefsPresenter</c>: a commit's branches and tags
    /// before its subject, each a label 16 points high in its lane's colour
    /// (tags and commits off the highlighted lanes in grey). The outline,
    /// the tint and the lines between parts are drawn; the icons and names
    /// are the platform's, so they're its font and read out.
    /// </summary>
    public sealed class CommitRefs
    {
        private readonly Row _row = new() { AlignItems = Align.Center, Gap = Spacing.Xs };
        private string _shown;

        public Node View => _row;

        /// <param name="useGraphColor">False: grey labels, the head's
        /// outlined, as the commit's details show them.</param>
        public void Show(Models.Commit commit, bool hasSingleRemote, bool useGraphColor = true)
        {
            var prefs = ViewModels.Preferences.Instance;
            var compact = prefs.UseCompactBranchNamesInGraph;
            var shown = string.Join('\n', commit.Decorators.Select(d => $"{d.Type} {d.Name}"))
                + $"\n{compact} {prefs.ShowTagsInGraph} {hasSingleRemote} {useGraphColor && commit.IsHighlightedInGraph} {commit.Color}";
            if (shown == _shown)
                return;
            _shown = shown;

            foreach (var child in _row.Children.ToArray())
                child.Destroy();

            var refs = commit.Decorators;
            var useLane = useGraphColor && commit.IsHighlightedInGraph;
            var lane = useLane && Models.CommitGraph.Pens.Count > 0
                ? Models.CommitGraph.Pens[commit.Color % Models.CommitGraph.Pens.Count].Color
                : new Avalonia.Media.Color(255, 128, 128, 128);
            var gray = new Avalonia.Media.Color(255, 128, 128, 128);
            var skipped = new HashSet<int>();
            for (var i = 0; i < refs.Count; i++)
            {
                if (skipped.Contains(i))
                    continue;
                var decorator = refs[i];
                if (!prefs.ShowTagsInGraph && decorator.Type == Models.DecoratorType.Tag)
                    continue;

                // A local branch's remote branches of the same name join its
                // label, as the remote's icon (and name, with several remotes).
                var remotes = new List<string>();
                if (compact && decorator.Type is Models.DecoratorType.CurrentBranchHead or Models.DecoratorType.LocalBranchHead)
                {
                    for (var j = i + 1; j < refs.Count; j++)
                    {
                        var test = refs[j];
                        var slash = test.Name.IndexOf('/');
                        if (test.Type != Models.DecoratorType.RemoteBranchHead || slash < 1 || slash == test.Name.Length - 1)
                            continue;
                        if (decorator.Name.Equals(test.Name[(slash + 1)..], StringComparison.Ordinal))
                        {
                            remotes.Add(test.Name[..slash]);
                            skipped.Add(j);
                        }
                    }
                }

                var color = decorator.Type == Models.DecoratorType.Tag ? gray : lane;
                var isHead = decorator.Type is Models.DecoratorType.CurrentBranchHead or Models.DecoratorType.CurrentCommitHead;
                _row.Add(Label(decorator, Color.Rgba(color.R, color.G, color.B), isHead && useLane, isHead, remotes, hasSingleRemote));
            }
            _row.IsVisible = _row.Children.Count > 0;
        }

        private static Node Label(Models.Decorator decorator, Color color, bool filled, bool isHead, List<string> remotes, bool hasSingleRemote)
        {
            var label = new Row { AlignItems = Align.Center };
            label.Height = 16;
            label.ToolTip = decorator.Name;

            var backdrop = new Backdrop(color, filled, isHead) { Inset = new Thickness(0) };
            label.Add(backdrop);

            var icon = new Icon(Icons.Get(decorator.Type switch
            {
                Models.DecoratorType.CurrentBranchHead or Models.DecoratorType.CurrentCommitHead => "Head",
                Models.DecoratorType.RemoteBranchHead => "Remote",
                Models.DecoratorType.Tag => "Tag",
                _ => "Branch",
            }))
            // Upstream's icons are the names' colour.
            { Size = 10, Margin = new Thickness(Spacing.None, Spacing.Xs), Color = Color.Label };
            label.Add(icon);

            // Upstream's name is 12 points, a head's 13 and bold.
            var name = new Text(decorator.Name)
            {
                Style = isHead ? TextStyle.Body : TextStyle.Callout,
                Weight = isHead ? FontWeight.Bold : FontWeight.Regular,
                MaxLines = 1,
                Margin = new Thickness(Spacing.None, Spacing.Xs),
            };
            label.Add(name);

            foreach (var remote in remotes)
            {
                label.Add(new Divider(color));
                label.Add(new Icon(Icons.Get("Remote")) { Size = 10, Margin = new Thickness(Spacing.None, Spacing.Xs, Spacing.None, Spacing.Xs), Color = Color.Label });
                if (hasSingleRemote)
                    break;
                label.Add(new Text(remote) { Style = TextStyle.Callout, Weight = FontWeight.Bold, MaxLines = 1, Margin = new Thickness(Spacing.None, Spacing.Sm, Spacing.None, Spacing.None) });
            }
            return label;
        }

        /// <summary>Behind a label: its outline, the line after its icon,
        /// and a tint behind its name; a head's is filled.</summary>
        private sealed class Backdrop(Color color, bool filled, bool isHead) : DrawnWidget("RefLabel", 0, 0)
        {
            protected override void Draw(Canvas canvas)
            {
                var (w, h) = (canvas.Width, canvas.Height);
                if (filled)
                    canvas.FillRect(0, 0, w, h, Tint(0.6), 4);
                else if (!isHead)
                    canvas.FillPath(FormattableString.Invariant($"M20 0H{w - 4}Q{w} 0 {w} 4V{h - 4}Q{w} {h} {w - 4} {h}H20Q16 {h} 16 {h - 4}V4Q16 0 20 0Z"), Tint(0.2));
                canvas.StrokePath(FormattableString.Invariant($"M16.5 0V{h}"), color);
                canvas.StrokeRect(0.5f, 0.5f, w - 1, h - 1, color, 1, 4);
            }

            private Color Tint(double opacity) => Color.Rgba(color.R, color.G, color.B, (byte)(255 * opacity));
        }

        /// <summary>The line before a remote's part of a label.</summary>
        private sealed class Divider : DrawnWidget
        {
            private readonly Color _color;

            public Divider(Color color) : base("RefDivider", 1, 16)
            {
                _color = color;
            }

            protected override void Draw(Canvas canvas) => canvas.FillRect(0, 0, 1, canvas.Height, _color);
        }
    }
}
