using System;
using System.Collections.Generic;
using System.Linq;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// What upstream's settings windows share (<c>Views/Preferences.axaml</c>,
    /// <c>Views/RepositoryConfigure.axaml</c>): forms of labelled fields,
    /// right-aligned as upstream's and the platforms' settings are, and lists
    /// beside the picked item's fields. Read-only for now: the fields show
    /// the settings, text can be selected, and nothing changes them;
    /// <see cref="Editable"/> turns them on once saving is ported.
    /// </summary>
    public static class Settings
    {
        public const bool Editable = false;

        public const float LabelWidth = 200;

        public static Column Page()
        {
            var page = new Column { Padding = 16, Gap = 8, AlignItems = Align.Stretch };
            page.Grow = 1;
            return page;
        }

        /// <summary>A label, right-aligned in the labels' column, beside its
        /// control; an empty label lines the control up under the others. By
        /// a tall control (<paramref name="top"/>), at its top.</summary>
        public static Row Field(string label, Node control, bool top = false)
        {
            var row = new Row { Gap = 12, AlignItems = top ? Align.Start : Align.Center };
            var side = new Row { Justify = Justify.End };
            side.Width = LabelWidth;
            side.Shrink = 0;
            if (!string.IsNullOrEmpty(label))
                side.Add(new Text(label.Trim()) { MaxLines = 1, Margin = new Thickness(top ? 4 : 0, 0, 0, 0) });
            row.Add(side);
            control.Grow = 1;
            control.MinWidth = 0;
            row.Add(control);
            return row;
        }

        public static TextInput TextValue(string value, string placeholder = "")
        {
            var input = new TextInput { Value = value ?? "", Placeholder = placeholder ?? "", ReadOnly = !Editable };
            return input;
        }

        public static TextArea TextBlock(string value, int lines)
        {
            var area = new TextArea { Value = value ?? "", ReadOnly = !Editable };
            area.Height = lines * 17 + 10;
            return area;
        }

        public static Checkbox Check(string label, bool value, string tip = null)
        {
            var check = new Checkbox(label) { IsChecked = value, IsEnabled = Editable };
            if (tip != null)
                check.ToolTip = tip;
            return check;
        }

        /// <summary>A drop-down of these options, the one at
        /// <paramref name="index"/> chosen (the first if none is).</summary>
        public static Select Choice(string label, IEnumerable<string> options, int index)
        {
            var list = options.ToList();
            var select = new Select(label) { Options = list, MinWidth = 0, IsEnabled = Editable };
            if (list.Count > 0)
                select.SelectedIndex = Math.Clamp(index, 0, list.Count - 1);
            return select;
        }

        /// <summary>Upstream's path fields: the path, and a button that asks
        /// the platform's file or folder dialog.</summary>
        public static Row Path(string value, string placeholder = "")
        {
            var row = new Row { Gap = 4, AlignItems = Align.Center };
            var input = TextValue(value, placeholder);
            input.Grow = 1;
            input.MinWidth = 0;
            row.Add(input);
            row.Add(new Button("…") { IsEnabled = Editable });
            return row;
        }

        /// <summary>A field's text with upstream's info mark after it, whose
        /// tooltip says what the field takes.</summary>
        public static Row WithTip(Node control, string tip)
        {
            var row = new Row { Gap = 6, AlignItems = Align.Center };
            control.Grow = 1;
            control.MinWidth = 0;
            row.Add(control);
            row.Add(new Icon(Icons.Get("Info")) { Size = 14, Color = Color.SecondaryLabel, ToolTip = tip });
            return row;
        }

        /// <summary>Upstream's section headings: an icon, bold text, and a
        /// line under them.</summary>
        public static Node Section(string icon, string title)
        {
            var column = new Column { Gap = 8, AlignItems = Align.Stretch, Margin = new Thickness(8, 0, 0, 0) };
            var heading = new Row { Gap = 6, AlignItems = Align.Center };
            heading.Add(new Icon(Icons.Get(icon)) { Size = 12 });
            heading.Add(new Text(title) { Weight = FontWeight.Bold });
            column.Add(heading);
            column.Add(new Separator(Orientation.Horizontal));
            return column;
        }

        /// <summary>A label over its control, as upstream's item forms are.</summary>
        public static Column Stacked(string label, Node control)
        {
            var column = new Column { Gap = 4, AlignItems = Align.Stretch };
            column.Add(new Text(label.Trim()));
            column.Add(control);
            return column;
        }

        /// <summary>
        /// Upstream's list of items beside the picked one's fields: a framed
        /// list 200 wide with its add, remove and move buttons under it, and
        /// upstream's empty icon while none is picked.
        /// </summary>
        public static Node ListDetail<T>(IReadOnlyList<T> items, Func<T, string> name, string icon, Func<T, Node> detail,
            bool moves = false, Node add = null) where T : class
        {
            var row = new Row { Gap = 16, AlignItems = Align.Stretch, Padding = 16 };
            row.Grow = 1;
            row.MinHeight = 360;

            var left = new Column { AlignItems = Align.Stretch, Gap = 4 };
            left.Width = 200;
            left.Shrink = 0;
            var keys = new System.Runtime.CompilerServices.ConditionalWeakTable<T, string>();
            var next = 0;
            var list = new ListView<T>(item => keys.GetValue(item, _ => (++next).ToString()), item =>
            {
                var line = new Row { Gap = 6, AlignItems = Align.Center, Padding = new Thickness(0, 4) };
                line.Height = 26;
                line.Add(new Icon(Icons.Get(icon)) { Size = 12 });
                var text = new Text(name(item)) { MaxLines = 1 };
                text.Shrink = 1;
                text.MinWidth = 0;
                line.Add(text);
                return line;
            }, itemAnimations: false)
            {
                Style = ListStyle.Framed,
                EstimatedRowHeight = 26,
                Items = items,
            };
            list.Grow = 1;
            list.MinHeight = 0;
            left.Add(list);
            var buttons = new Row { AlignItems = Align.Center };
            // Upstream's have only icons, and no names to read out.
            buttons.Add(add ?? Small("Plus", "Add"));
            buttons.Add(Small("Minus", "Remove"));
            var gap = new Row();
            gap.Grow = 1;
            buttons.Add(gap);
            if (moves)
            {
                buttons.Add(Small("Up", "Move Up"));
                buttons.Add(Small("Down", "Move Down"));
            }
            left.Add(buttons);
            row.Add(left);

            var right = new Column { AlignItems = Align.Stretch, Justify = Justify.Center };
            right.Grow = 1;
            right.MinWidth = 0;
            row.Add(right);
            Node shown = null;
            void Show(T item)
            {
                shown?.Destroy();
                if (item == null)
                {
                    shown = new Icon(Icons.Get("Empty")) { Size = 64, Color = Color.SecondaryLabel, AlignSelf = Align.Center };
                }
                else
                {
                    var fields = new Column { Gap = 12, AlignItems = Align.Stretch, Justify = Justify.Start };
                    fields.Grow = 1;
                    fields.Add(detail(item));
                    shown = fields;
                }
                right.Add(shown);
            }
            list.SelectionChanged += selected => Show(selected.Count > 0 ? selected[0] : null);
            Show(null);
            return row;
        }

        /// <summary>Upstream's small icon buttons under a list.</summary>
        public static Button Small(string icon, string name) =>
            new(name)
            {
                Icon = Icons.Get(icon, 14),
                IconOnly = true,
                Style = ButtonStyle.Borderless,
                ToolTip = name,
                IsEnabled = Editable,
            };

        /// <summary>Upstream's custom action form, the same in both windows.</summary>
        public static Node CustomAction(Models.CustomAction action)
        {
            var column = new Column { Gap = 12, AlignItems = Align.Stretch };
            column.Add(Stacked(App.Text("Configure.CustomAction.Name"), TextValue(action.Name)));
            column.Add(Stacked(App.Text("Configure.CustomAction.Scope"), Choice(App.Text("Configure.CustomAction.Scope"),
                new[] { "Repository", "Commit", "Branch", "Tag", "Remote", "File" }.Select(s => App.Text($"Configure.CustomAction.Scope.{s}")),
                (int)action.Scope)));
            var controls = new Button($"{App.Text("Configure.CustomAction.InputControls.Edit")} ({action.Controls.Count})")
            {
                Icon = Icons.Get("Edit", 14),
                IsEnabled = Editable,
            };
            var controlsRow = new Row();
            controlsRow.Add(controls);
            column.Add(Stacked(App.Text("Configure.CustomAction.InputControls"), controlsRow));
            column.Add(Stacked(App.Text("Configure.CustomAction.Executable"), Path(action.Executable)));
            column.Add(Stacked(App.Text("Configure.CustomAction.Arguments"),
                WithTip(TextValue(action.Arguments), App.Text("Configure.CustomAction.Arguments.Tip"))));
            column.Add(Check(App.Text("Configure.CustomAction.WaitForExit"), action.WaitForExit));
            return column;
        }

        /// <summary>A window's tabs, each a page; titled as upstream's tab
        /// headers, in the platform's case.</summary>
        public static Tabs Tabs(params (string Key, Node Page)[] pages)
        {
            var tabs = new Tabs();
            tabs.Grow = 1;
            foreach (var (key, page) in pages)
                tabs.Add(Popups.ButtonText(key), page);
            return tabs;
        }
    }
}
