using System;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's <c>CommitStatusIndicator</c>: before a commit's labels, a
    /// 5-point dot when the current branch is ahead of its upstream by it
    /// (in the accent colour) or behind by it (in the label colour), 9
    /// points wide with its gap; nothing otherwise.
    /// </summary>
    public sealed class CommitStatusDot : DrawnWidget
    {
        private Color? _color;

        public CommitStatusDot() : base("CommitStatus", 9, 5)
        {
            IsVisible = false;
        }

        public void Show(Models.Commit commit, Models.Branch current)
        {
            Color? color = current switch
            {
                not null when current.Ahead.Contains(commit.SHA) => Color.Accent,
                not null when current.Behind.Contains(commit.SHA) => Color.Label,
                _ => null,
            };
            if (color == _color)
                return;
            _color = color;
            IsVisible = color != null;
            Redraw();
        }

        protected override void Draw(Canvas canvas)
        {
            if (_color is { } color)
                canvas.FillEllipse(0, 0, 5, 5, color);
        }
    }

    /// <summary>
    /// Upstream's <c>BisectStateIndicator</c>: while bisecting, a commit
    /// marked good, bad or skipped shows a label 32 by 16 points (and 4
    /// after it): the bisect icon, a line, then the mark's icon on a tint
    /// of its colour (green, red, grey), outlined in it. The outline, line
    /// and tint are drawn; the icons are the platform's, as the ref labels'.
    /// </summary>
    public sealed class BisectMark
    {
        private readonly Row _label = new() { AlignItems = Align.Center, Margin = new Thickness(Spacing.None, Spacing.Xs, Spacing.None, Spacing.None) };
        private readonly Icon _state = new(Icons.Get("Good")) { Size = 10, Color = Color.Label, Margin = new Thickness(Spacing.None, Spacing.None, Spacing.None, Spacing.Sm) };
        private readonly Backdrop _backdrop = new() { Inset = new Thickness(0) };
        private Models.BisectCommitFlag _flag = Models.BisectCommitFlag.None;

        public BisectMark()
        {
            _label.Width = 32;
            _label.Height = 16;
            _label.Add(_backdrop);
            _label.Add(new Icon(Icons.Get("Bisect")) { Size = 10, Color = Color.Label, Margin = new Thickness(Spacing.None, Spacing.None, Spacing.None, Spacing.Xs) });
            _label.Add(_state);
            _label.IsVisible = false;
        }

        public Node View => _label;

        public void Show(Models.Commit commit, Models.Bisect bisect)
        {
            var flag = Models.BisectCommitFlag.None;
            if (bisect != null)
            {
                if (bisect.Bads.Contains(commit.SHA))
                    flag = Models.BisectCommitFlag.Bad;
                else if (bisect.Goods.Contains(commit.SHA))
                    flag = Models.BisectCommitFlag.Good;
                else if (bisect.Skipped.Contains(commit.SHA))
                    flag = Models.BisectCommitFlag.Skipped;
            }
            if (flag == _flag)
                return;
            _flag = flag;
            _label.IsVisible = flag != Models.BisectCommitFlag.None;
            if (flag == Models.BisectCommitFlag.None)
                return;
            _state.Vector = Icons.Get(flag switch
            {
                Models.BisectCommitFlag.Good => "Good",
                Models.BisectCommitFlag.Bad => "Bad",
                _ => "Skip",
            });
            _label.ToolTip = flag.ToString();
            _backdrop.Show(flag switch
            {
                Models.BisectCommitFlag.Good => (0, 128, 0),
                Models.BisectCommitFlag.Bad => (255, 0, 0),
                _ => (128, 128, 128),
            });
        }

        private sealed class Backdrop() : DrawnWidget("BisectMark", 0, 0)
        {
            private (byte R, byte G, byte B) _color;

            public void Show((byte R, byte G, byte B) color)
            {
                _color = color;
                Redraw();
            }

            protected override void Draw(Canvas canvas)
            {
                var (r, g, b) = _color;
                var (w, h) = (canvas.Width, canvas.Height);
                canvas.FillPath(FormattableString.Invariant($"M16 0H{w - 2}Q{w} 0 {w} 2V{h - 2}Q{w} {h} {w - 2} {h}H16Z"), Color.Rgba(r, g, b, 51));
                canvas.StrokePath("M16.5 0V16", Color.Rgba(r, g, b));
                canvas.StrokeRect(0.5f, 0.5f, w - 1, h - 1, Color.Rgba(r, g, b), 1, 2);
            }
        }
    }
}
