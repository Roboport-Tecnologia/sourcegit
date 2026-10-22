using System;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's <c>BookmarkSelector</c>: a bookmark icon per colour, in a
    /// row, the chosen one marked; choosing one unmarks the others. Here
    /// they're the platform's toggle buttons, each icon in its colour, the
    /// chosen one pressed, so they look, move through with the keyboard
    /// and read out as the platform's own.
    /// </summary>
    public sealed class BookmarkSelector : Row
    {
        /// <summary>Upstream's bookmarks, in its order: none, then red,
        /// orange, gold, forest green, dark cyan, deep sky blue, purple.</summary>
        public static readonly string[] Names = ["None", "Red", "Orange", "Yellow", "Green", "Cyan", "Blue", "Purple"];

        private readonly ToggleButton[] _buttons = new ToggleButton[Names.Length];
        private int _bookmark;

        public BookmarkSelector()
        {
            Gap = Spacing.Xs;
            AlignItems = Align.Center;
            var icon = Icons.Get("Bookmark");
            for (var i = 0; i < Names.Length; i++)
            {
                var bookmark = i;
                var button = new ToggleButton(Names[i])
                {
                    Icon = icon,
                    IconOnly = true,
                    IconColor = Welcome.BookmarkColor(i),
                    ToolTip = Names[i],
                    IsChecked = i == 0,
                };
                // Pressing the chosen one again keeps it chosen.
                button.Changed += _ => Choose(bookmark);
                _buttons[i] = button;
                Add(button);
            }
        }

        public int Bookmark
        {
            get => _bookmark;
            set
            {
                if (value < 0 || value >= Names.Length)
                    return;
                _bookmark = value;
                for (var i = 0; i < _buttons.Length; i++)
                    _buttons[i].IsChecked = i == value;
            }
        }

        /// <summary>The user chose a bookmark.</summary>
        public event Action<int>? Changed;

        private void Choose(int bookmark)
        {
            var changed = bookmark != _bookmark;
            Bookmark = bookmark;
            if (changed)
                Changed?.Invoke(bookmark);
        }
    }
}
