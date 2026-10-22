using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

using Avalonia;
using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's commit graph (<c>Views/CommitGraph.cs</c>), cut into the
    /// table's rows. Upstream draws the graph once over its whole list; a
    /// native table has cells, so each row's first cell draws the part of
    /// the graph in its row, as native Git clients' tables do. Rows meet
    /// with no room between them (AppKit's tables have no vertical spacing),
    /// so the lines join from row to row.
    /// </summary>
    public sealed class CommitGraphRows
    {
        /// <summary>Upstream's row height.</summary>
        public const float RowHeight = 26;

        /// <summary>A line or a cubic curve, in points from the graph's top,
        /// going down.</summary>
        private readonly record struct Piece(Point P0, Point C1, Point C2, Point P3, bool IsLine, int Color, bool IsHighlighted);

        private readonly Dictionary<int, List<Piece>> _pieces = [];
        private readonly Dictionary<int, List<Models.CommitGraph.Dot>> _dots = [];

        public CommitGraphRows(Models.CommitGraph graph)
        {
            if (graph == null)
                return;

            const double h = RowHeight;
            foreach (var link in graph.Links)
            {
                var start = new Point(link.Start.X, link.Start.Y * h);
                var end = new Point(link.End.X, link.End.Y * h);
                Quad(start, new Point(link.Control.X, link.Control.Y * h), end, link.Color, link.IsHighlighted);
            }

            // Upstream's lines: straight down a lane, a quadratic curve out
            // to a lane on the right, a cubic one back in from it.
            foreach (var path in graph.Paths)
            {
                var last = new Point(path.Points[0].X, path.Points[0].Y * h);
                var size = path.Points.Count;
                for (var i = 1; i < size; i++)
                {
                    var cur = new Point(path.Points[i].X, path.Points[i].Y * h);
                    if (cur.X > last.X)
                    {
                        Quad(last, new Point(cur.X, last.Y), cur, path.Color, path.IsHighlighted);
                    }
                    else if (cur.X < last.X)
                    {
                        if (i < size - 1)
                        {
                            var midY = (last.Y + cur.Y) / 2;
                            Add(new Piece(last, new Point(last.X, midY + 4), new Point(cur.X, midY - 4), cur, false, path.Color, path.IsHighlighted));
                        }
                        else
                        {
                            Quad(last, new Point(last.X, cur.Y), cur, path.Color, path.IsHighlighted);
                        }
                    }
                    else
                    {
                        Add(new Piece(last, last, cur, cur, true, path.Color, path.IsHighlighted));
                    }
                    last = cur;
                }
            }

            foreach (var dot in graph.Dots)
            {
                var row = (int)Math.Floor(dot.Center.Y);
                if (!_dots.TryGetValue(row, out var dots))
                    _dots[row] = dots = [];
                dots.Add(dot);
            }
        }

        private void Quad(Point p0, Point c, Point p3, int color, bool isHighlighted)
        {
            var c1 = new Point(p0.X + (c.X - p0.X) * 2 / 3, p0.Y + (c.Y - p0.Y) * 2 / 3);
            var c2 = new Point(p3.X + (c.X - p3.X) * 2 / 3, p3.Y + (c.Y - p3.Y) * 2 / 3);
            Add(new Piece(p0, c1, c2, p3, false, color, isHighlighted));
        }

        /// <summary>Files a piece under every row it crosses.</summary>
        private void Add(Piece piece)
        {
            var top = Math.Min(piece.P0.Y, piece.P3.Y);
            var bottom = Math.Max(piece.P0.Y, piece.P3.Y);
            var first = (int)Math.Floor(top / RowHeight);
            var last = Math.Max(first, (int)Math.Ceiling(bottom / RowHeight) - 1);
            for (var row = first; row <= last; row++)
            {
                if (!_pieces.TryGetValue(row, out var pieces))
                    _pieces[row] = pieces = [];
                pieces.Add(piece);
            }
        }

        /// <summary>Draws row <paramref name="row"/>'s part of the graph,
        /// its top at the canvas's.</summary>
        public void Draw(Canvas canvas, int row)
        {
            var pens = Models.CommitGraph.Pens;
            if (pens.Count == 0)
                return;

            var thickness = (float)pens[0].Thickness;
            var top = row * (double)RowHeight;
            var bottom = top + RowHeight;
            var grayedLine = Color.Rgba(128, 128, 128, 102);

            // A path per pen, of every piece's part in the row.
            if (_pieces.TryGetValue(row, out var pieces))
            {
                var paths = new Dictionary<(int, bool), StringBuilder>();
                foreach (var piece in pieces)
                {
                    if (Clip(piece, top, bottom) is not { } part)
                        continue;
                    var key = (piece.IsHighlighted ? piece.Color : -1, piece.IsHighlighted);
                    if (!paths.TryGetValue(key, out var data))
                        paths[key] = data = new StringBuilder();
                    Append(data, "M", part.P0, top);
                    if (piece.IsLine)
                    {
                        Append(data, "L", part.P3, top);
                    }
                    else
                    {
                        Append(data, "C", part.C1, top);
                        Append(data, "", part.C2, top);
                        Append(data, "", part.P3, top);
                    }
                }
                foreach (var ((color, highlighted), data) in paths)
                    canvas.StrokePath(data.ToString(), highlighted ? PenColor(color) : grayedLine, thickness);
            }

            if (_dots.TryGetValue(row, out var dots))
            {
                // Upstream's dots are filled with the list's background.
                var fill = Color.ControlBackground;
                var grayed = Color.Rgba(128, 128, 128);
                foreach (var dot in dots)
                {
                    var x = (float)dot.Center.X;
                    var y = (float)(dot.Center.Y * RowHeight - top);
                    var pen = dot.IsHighlighted ? PenColor(dot.Color) : grayed;
                    switch (dot.Type)
                    {
                        case Models.CommitGraph.DotType.Head:
                            canvas.FillEllipse(x - 6, y - 6, 12, 12, fill);
                            canvas.StrokeEllipse(x - 6, y - 6, 12, 12, pen, thickness);
                            canvas.FillEllipse(x - 3, y - 3, 6, 6, pen);
                            break;
                        case Models.CommitGraph.DotType.Merge:
                            canvas.FillEllipse(x - 6, y - 6, 12, 12, pen);
                            canvas.StrokePath(FormattableString.Invariant($"M{x} {y - 3}V{y + 3}M{x - 3} {y}H{x + 3}"), fill, 2);
                            break;
                        default:
                            canvas.FillEllipse(x - 3, y - 3, 6, 6, fill);
                            canvas.StrokeEllipse(x - 3, y - 3, 6, 6, pen, thickness);
                            break;
                    }
                }
            }
        }

        private static Color PenColor(int index)
        {
            var pens = Models.CommitGraph.Pens;
            var c = pens[index % pens.Count].Color;
            return Color.Rgba(c.R, c.G, c.B, c.A);
        }

        private static void Append(StringBuilder data, string command, Point p, double top) =>
            data.Append(command).Append(p.X.ToString("0.##", CultureInfo.InvariantCulture)).Append(' ')
                .Append((p.Y - top).ToString("0.##", CultureInfo.InvariantCulture)).Append(' ');

        /// <summary>The part of a piece between two heights, or null if it
        /// has none. Every piece goes down as it goes on, so its height at
        /// a point of the curve tells where it is.</summary>
        private static Piece? Clip(Piece piece, double top, double bottom)
        {
            if (piece.P3.Y <= top || piece.P0.Y >= bottom)
                return null;
            var t0 = piece.P0.Y < top ? At(piece, top) : 0;
            var t1 = piece.P3.Y > bottom ? At(piece, bottom) : 1;
            if (t0 == 0 && t1 == 1)
                return piece;
            if (piece.IsLine)
            {
                var a = Lerp(piece.P0, piece.P3, t0);
                var b = Lerp(piece.P0, piece.P3, t1);
                return piece with { P0 = a, C1 = a, C2 = b, P3 = b };
            }
            var (_, right) = Split(piece, t0);
            var (part, _) = Split(right, t0 < 1 ? (t1 - t0) / (1 - t0) : 0);
            return part;
        }

        /// <summary>Where along a piece it's at height <paramref name="y"/>.</summary>
        private static double At(Piece piece, double y)
        {
            if (piece.IsLine)
                return (y - piece.P0.Y) / (piece.P3.Y - piece.P0.Y);
            double lo = 0, hi = 1;
            for (var i = 0; i < 24; i++)
            {
                var mid = (lo + hi) / 2;
                if (Cubic(piece, mid).Y < y)
                    lo = mid;
                else
                    hi = mid;
            }
            return (lo + hi) / 2;
        }

        private static Point Lerp(Point a, Point b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

        private static Point Cubic(Piece p, double t)
        {
            var (ab, bc, cd) = (Lerp(p.P0, p.C1, t), Lerp(p.C1, p.C2, t), Lerp(p.C2, p.P3, t));
            return Lerp(Lerp(ab, bc, t), Lerp(bc, cd, t), t);
        }

        /// <summary>De Casteljau's split of a curve at <paramref name="t"/>.</summary>
        private static (Piece, Piece) Split(Piece p, double t)
        {
            var (ab, bc, cd) = (Lerp(p.P0, p.C1, t), Lerp(p.C1, p.C2, t), Lerp(p.C2, p.P3, t));
            var (abc, bcd) = (Lerp(ab, bc, t), Lerp(bc, cd, t));
            var mid = Lerp(abc, bcd, t);
            return (p with { C1 = ab, C2 = abc, P3 = mid }, p with { P0 = mid, C1 = bcd, C2 = cd });
        }
    }

    /// <summary>A row's part of the commit graph, as wide as its lanes
    /// (upstream's <c>LeftMargin</c>) and as high as the row.</summary>
    public sealed class CommitGraphCell : DrawnWidget
    {
        private readonly Func<CommitGraphRows> _graph;
        private int _row;

        public CommitGraphCell(Func<CommitGraphRows> graph) : base("CommitGraph", 0, CommitGraphRows.RowHeight)
        {
            _graph = graph;
        }

        /// <summary>Shows the graph at this row, with lanes this wide.</summary>
        public void Show(int row, double width)
        {
            _row = row;
            SetSize((float)width, CommitGraphRows.RowHeight);
            Redraw();
        }

        protected override void Draw(Canvas canvas) => _graph().Draw(canvas, _row);
    }
}
