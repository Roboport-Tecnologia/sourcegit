using System;

using Mitsuamia;

namespace SourceGit
{
    /// <summary>
    /// The port's spacing, each kind in one place: the platform's steps,
    /// and where a platform needs more or less, the step it gets. Views
    /// take these rather than steps of their own, so a change here moves
    /// every panel, list and bar alike.
    /// </summary>
    public static class Metrics
    {
        private static bool Mac => OperatingSystem.IsMacOS();

        /// <summary>Around a sheet's or a window's content.</summary>
        public static Thickness Panel => Spacing.Lg;

        /// <summary>Between the sections of a sheet or a window.</summary>
        public static Length PanelGap => Spacing.Md;

        /// <summary>Above and below a list's row: the platform's lists
        /// make a row at least their own height, and content-sized rows
        /// with nothing around them crowd each other.</summary>
        public static Length RowInset => Spacing.Xs;

        /// <summary>A list row's padding: <see cref="RowInset"/> above and
        /// below, and its own at the sides.</summary>
        public static Thickness ListRow(Spacing start = Spacing.None, Spacing end = Spacing.None) =>
            new(RowInset, end, RowInset, start);

        /// <summary>Between what's in a list's row: its icon, its text.</summary>
        public static Length RowGap => Spacing.Sm;

        /// <summary>A bar over a list, or over a page (bisecting, an
        /// operation in progress).</summary>
        public static Thickness Bar => new(Spacing.Xs, Spacing.Sm);

        /// <summary>The toolbar bands at the top of the windows.</summary>
        public static Thickness Band => new(Spacing.Xs, Spacing.Sm);

        /// <summary>Between icon-only borderless buttons side by side.
        /// AppKit's have no padding of their own, so there it's the medium
        /// step, or their icons nearly touch.</summary>
        public static Length ButtonGap => Mac ? Spacing.Md : Spacing.Xs;

        /// <summary>As <see cref="ButtonGap"/>, in the toolbar bands.</summary>
        public static Length ToolbarGap => Mac ? Spacing.Lg : Spacing.Sm;
    }
}
