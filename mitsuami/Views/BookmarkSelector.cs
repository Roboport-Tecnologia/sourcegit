using System;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's <c>BookmarkSelector</c>: the bookmarks in a row, 26 points
    /// apart, each a 14-point bookmark icon in its colour, the chosen one in
    /// a rounded frame in the accent colour; a click chooses one. No
    /// platform has such a control, so it's drawn, the same everywhere.
    /// To assistive technology it's a slider over the colours' names.
    /// </summary>
    public sealed class BookmarkSelector : DrawnWidget
    {
        /// <summary>Upstream's bookmarks, in its order: none, then red,
        /// orange, gold, forest green, dark cyan, deep sky blue, purple.</summary>
        public static readonly string[] Names = ["None", "Red", "Orange", "Yellow", "Green", "Cyan", "Blue", "Purple"];

        private static readonly VectorIcon s_icon = Icons.Get("Bookmark");

        private int _bookmark;

        // Upstream's measure: eight icons with 12 points between them, and
        // 2 on each side.
        public BookmarkSelector() : base(nameof(BookmarkSelector), 8 * 14 + 7 * 12 + 4, 24)
        {
            Show();
        }

        public int Bookmark
        {
            get => _bookmark;
            set
            {
                if (value == _bookmark || value < 0 || value >= Names.Length)
                    return;
                _bookmark = value;
                Show();
            }
        }

        /// <summary>The user chose a bookmark.</summary>
        public event Action<int>? Changed;

        protected override void Draw(Canvas canvas)
        {
            for (var i = 0; i < Names.Length; i++)
            {
                var (x, y, width, height) = HitBox(i);
                if (i == _bookmark)
                    canvas.StrokeRect(x, y, width, height, Color.Accent, 1, 3);
                // Centred in its box, where upstream's sits.
                canvas.FillIcon(s_icon, x + 2, 5, 14, Welcome.BookmarkColor(i));
            }
        }

        protected override void OnPointerPressed(float x, float y)
        {
            for (var i = 0; i < Names.Length; i++)
            {
                var (bx, by, width, height) = HitBox(i);
                if (x >= bx && x < bx + width && y >= by && y < by + height)
                {
                    Choose(i);
                    return;
                }
            }
        }

        protected override void OnAction(DrawnActions action, string? value)
        {
            switch (action)
            {
                case DrawnActions.Increment:
                    Choose(Math.Min(_bookmark + 1, Names.Length - 1));
                    break;
                case DrawnActions.Decrement:
                    Choose(Math.Max(_bookmark - 1, 0));
                    break;
                case DrawnActions.SetValue when Array.FindIndex(Names, n => string.Equals(n, value?.Trim(), StringComparison.OrdinalIgnoreCase)) is >= 0 and var i:
                    Choose(i);
                    break;
            }
        }

        private void Choose(int bookmark)
        {
            if (bookmark == _bookmark)
                return;
            Bookmark = bookmark;
            Changed?.Invoke(bookmark);
        }

        private void Show()
        {
            SetSemantics(DrawnRole.Slider, Names[_bookmark], DrawnActions.SetValue | DrawnActions.Increment | DrawnActions.Decrement);
            Redraw();
        }

        // Upstream's: 18 by 20 points around each icon.
        private static (float X, float Y, float Width, float Height) HitBox(int i) => (2 + 26 * i - 2.5f, 2.5f, 18, 20);
    }
}
