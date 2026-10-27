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

        /// <summary>Between a form's fields.</summary>
        public static Length FormGap => Spacing.Sm;

        /// <summary>Between a field's label and its control beside it.</summary>
        public static Length FieldGap => Spacing.Md;

        /// <summary>Between a field's label and its control under it.</summary>
        public static Length LabelGap => Spacing.Xs;

        /// <summary>Between the controls of one field: a path and its
        /// button, a check and its number, a slider and its value.</summary>
        public static Length ControlGap => Spacing.Sm;

        /// <summary>Between a label's icon (or spinner) and its text, and
        /// the labels in a line: a branch, a commit, a stash, a warning.</summary>
        public static Length IconGap => Spacing.Sm;

        /// <summary>Between a sheet's push buttons (Cancel, OK).</summary>
        public static Length DialogButtonGap => Spacing.Sm;

        /// <summary>Above and below a list's row: the platform's lists
        /// make a row at least their own height, and content-sized rows
        /// with nothing around them crowd each other.</summary>
        public static Length RowInset => Spacing.Xs;

        /// <summary>A list row's padding: <see cref="RowInset"/> above and
        /// below, and its own at the sides.</summary>
        public static Thickness ListRow(Spacing start = Spacing.None, Spacing end = Spacing.None) =>
            new(RowInset, end, RowInset, start);

        /// <summary>Above and below a row with a button that shows while
        /// the pointer is over it (the branches' and tags' filter), and its
        /// list's other rows: room for the button, which takes none in the
        /// row (<see cref="Views.FilterModeSwitch"/>). On WinUI the
        /// borderless icon pull-down is 29 points, and a line of text with
        /// <see cref="RowInset"/> 27; with this, 35.</summary>
        public static Length HoverRowInset => Spacing.Sm;

        /// <summary>As <see cref="ListRow"/>, with <see cref="HoverRowInset"/>
        /// above and below.</summary>
        public static Thickness HoverRow(Spacing start = Spacing.None, Spacing end = Spacing.None) =>
            new(HoverRowInset, end, HoverRowInset, start);

        /// <summary>Between what's in a list's row: its icon, its text.</summary>
        public static Length RowGap => Spacing.Sm;

        /// <summary>A bar over a list, or over a page (bisecting, an
        /// operation in progress).</summary>
        public static Thickness Bar => new(Spacing.Xs, Spacing.Sm);

        /// <summary>The toolbar bands at the top of the windows. On macOS
        /// the borderless buttons' icons sat close to the band's bottom
        /// line, so there they have more room under them.</summary>
        public static Thickness Band => Mac
            ? new(Spacing.Xs, Spacing.Sm, Spacing.Md, Spacing.Sm)
            : new(Spacing.Xs, Spacing.Sm);

        /// <summary>Between icon-only borderless buttons side by side.
        /// AppKit's have no padding of their own, so there it's the medium
        /// step, or their icons nearly touch.</summary>
        public static Length ButtonGap => Mac ? Spacing.Md : Spacing.Xs;

        /// <summary>As <see cref="ButtonGap"/>, in the toolbar bands; on
        /// macOS <see cref="SpaceToolbar"/> doubles it.</summary>
        public static Length ToolbarGap => Mac ? Spacing.Lg : Spacing.Sm;

        /// <summary>Sets a toolbar row's items apart, once they're all in:
        /// <see cref="ToolbarGap"/>, and on macOS as much again after each
        /// but the last, twice the large step in all. A length has no
        /// multiple of a step, so the second is a margin.</summary>
        public static void SpaceToolbar(Container row)
        {
            row.Gap = ToolbarGap;
            if (!Mac)
                return;
            for (var i = 0; i < row.Children.Count - 1; i++)
                row.Children[i].Margin = new Thickness(Spacing.None, ToolbarGap, Spacing.None, Spacing.None);
        }
    }
}
