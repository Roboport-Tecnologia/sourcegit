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
    /// beside the picked item's fields. Each field sets its setting as it
    /// changes, as upstream's bindings do; the windows save on closing.
    /// </summary>
    public static class Settings
    {
        public const float LabelWidth = 200;

        /// <summary>Takes a container's children away, to fill it again.</summary>
        public static void Empty(Container container)
        {
            foreach (var child in container.Children.ToList())
                child.Destroy();
        }

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

        public static TextInput TextValue(string value, Action<string> set, string placeholder = "")
        {
            var input = new TextInput { Value = value ?? "", Placeholder = placeholder ?? "" };
            input.Changed += set;
            return input;
        }

        public static TextArea TextBlock(string value, int lines, Action<string> set)
        {
            var area = new TextArea { Value = value ?? "" };
            area.Height = lines * 17 + 10;
            area.Changed += set;
            return area;
        }

        public static Checkbox Check(string label, bool value, Action<bool> set, string tip = null)
        {
            var check = new Checkbox(label) { IsChecked = value };
            if (tip != null)
                check.ToolTip = tip;
            check.Changed += set;
            return check;
        }

        /// <summary>A drop-down of these options, the one at
        /// <paramref name="index"/> chosen (the first if none is).</summary>
        public static Select Choice(string label, IEnumerable<string> options, int index, Action<int> set)
        {
            var list = options.ToList();
            var select = new Select(label) { Options = list, MinWidth = 0 };
            if (list.Count > 0)
                select.SelectedIndex = Math.Clamp(index, 0, list.Count - 1);
            select.Changed += set;
            return select;
        }

        /// <summary>What a path field's button asks for.</summary>
        public enum Pick { File, Folder }

        /// <summary>Upstream's path fields: the path, and a button that asks
        /// the platform's file or folder dialog, over
        /// <paramref name="window"/>.</summary>
        public static Row Path(Func<Window> window, string value, Action<string> set, string placeholder = "",
            Pick pick = Pick.File, string title = "")
        {
            var row = new Row { Gap = 4, AlignItems = Align.Center };
            var input = TextValue(value, set, placeholder);
            input.Grow = 1;
            input.MinWidth = 0;
            row.Add(input);
            var browse = new Button("…") { ToolTip = title };
            browse.Click += async () =>
            {
                var path = pick == Pick.Folder
                    ? await Dialogs.OpenFolder(window(), title)
                    : (await Dialogs.OpenFiles(window(), title)).FirstOrDefault();
                if (string.IsNullOrEmpty(path))
                    return;
                input.Value = path;
                set(path);
            };
            row.Add(browse);
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

        /// <summary>What a list beside its items' fields does to them.</summary>
        public sealed class ListActions<T>
        {
            /// <summary>Adds an item and returns it, to pick it; or null, with
            /// <see cref="AddButton"/> making the add button.</summary>
            public Func<T> Add { get; init; }

            /// <summary>Makes the add button (a menu of samples), given what
            /// picks the item it added.</summary>
            public Func<Action<T>, Node> AddButton { get; init; }

            public Action<T> Remove { get; init; }

            /// <summary>Moves the item at one index to another; null: the
            /// list has no move buttons.</summary>
            public Action<int, int> Move { get; init; }
        }

        /// <summary>
        /// Upstream's list of items beside the picked one's fields: a framed
        /// list 200 wide with its add, remove and move buttons under it, and
        /// upstream's empty icon while none is picked. A row's name follows
        /// the item's <paramref name="nameProperty"/>.
        /// </summary>
        public static Node ListDetail<T>(IReadOnlyList<T> items, Func<T, string> name, string nameProperty, string icon,
            Func<T, Node> detail, ListActions<T> actions) where T : class, System.ComponentModel.INotifyPropertyChanged
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
                Bind.OneWay(item, nameProperty, () => name(item), v => text.Value = v ?? "", owner: line);
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

            var right = new Column { AlignItems = Align.Stretch, Justify = Justify.Center };
            right.Grow = 1;
            right.MinWidth = 0;
            Node shown = null;
            T picked = null;
            var remove = Small("Minus", "Remove", () =>
            {
                if (picked != null)
                    actions.Remove(picked);
            });
            Button up = null, down = null;
            void Show(T item)
            {
                picked = item;
                remove.IsEnabled = item != null;
                if (up != null)
                {
                    var index = item == null ? -1 : items.ToList().IndexOf(item);
                    up.IsEnabled = index > 0;
                    down.IsEnabled = index >= 0 && index < items.Count - 1;
                }
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
            void Pick(T item)
            {
                list.SelectedItem = item;
                if (item != null)
                    list.ScrollTo(item);
                Show(item);
            }
            list.SelectionChanged += selected => Show(selected.Count > 0 ? selected[0] : null);
            // An item gone (removed) takes its fields with it; a move keeps
            // the pick but changes which way it can go.
            list.ItemsChanged += () => Show(picked != null && items.Contains(picked) ? picked : null);

            var buttons = new Row { AlignItems = Align.Center };
            // Upstream's have only icons, and no names to read out.
            buttons.Add(actions.AddButton?.Invoke(Pick) ?? Small("Plus", "Add", () => Pick(actions.Add())));
            buttons.Add(remove);
            var gap = new Row();
            gap.Grow = 1;
            buttons.Add(gap);
            if (actions.Move != null)
            {
                void Move(int by)
                {
                    var index = items.ToList().IndexOf(picked);
                    if (index < 0 || index + by < 0 || index + by >= items.Count)
                        return;
                    actions.Move(index, index + by);
                    Pick(picked);
                }
                up = Small("Up", "Move Up", () => Move(-1));
                down = Small("Down", "Move Down", () => Move(1));
                buttons.Add(up);
                buttons.Add(down);
            }
            left.Add(buttons);
            row.Add(left);
            row.Add(right);
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
            };

        public static Button Small(string icon, string name, Action click)
        {
            var button = new Button(name)
            {
                Icon = Icons.Get(icon, 14),
                IconOnly = true,
                Style = ButtonStyle.Borderless,
                ToolTip = name,
            };
            button.Click += click;
            return button;
        }

        /// <summary>Upstream's custom action form, the same in both windows,
        /// over <paramref name="window"/>.</summary>
        public static Node CustomAction(Func<Window> window, Models.CustomAction action)
        {
            var column = new Column { Gap = 12, AlignItems = Align.Stretch };
            column.Add(Stacked(App.Text("Configure.CustomAction.Name"), TextValue(action.Name, v => action.Name = v)));
            column.Add(Stacked(App.Text("Configure.CustomAction.Scope"), Choice(App.Text("Configure.CustomAction.Scope"),
                new[] { "Repository", "Commit", "Branch", "Tag", "Remote", "File" }.Select(s => App.Text($"Configure.CustomAction.Scope.{s}")),
                (int)action.Scope, i => action.Scope = (Models.CustomActionScope)i)));
            var controls = new Button(ControlsTitle(action)) { Icon = Icons.Get("Edit", 14) };
            controls.Click += () => CustomActionControls.Show(window(), action.Controls);
            void Count(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => controls.Label = ControlsTitle(action);
            action.Controls.CollectionChanged += Count;
            controls.Forgotten += () => action.Controls.CollectionChanged -= Count;
            var controlsRow = new Row();
            controlsRow.Add(controls);
            column.Add(Stacked(App.Text("Configure.CustomAction.InputControls"), controlsRow));
            column.Add(Stacked(App.Text("Configure.CustomAction.Executable"),
                Path(window, action.Executable, v => action.Executable = v, title: "Executable file(script)")));
            column.Add(Stacked(App.Text("Configure.CustomAction.Arguments"),
                WithTip(TextValue(action.Arguments, v => action.Arguments = v), App.Text("Configure.CustomAction.Arguments.Tip"))));
            column.Add(Check(App.Text("Configure.CustomAction.WaitForExit"), action.WaitForExit, v => action.WaitForExit = v));
            return column;
        }

        private static string ControlsTitle(Models.CustomAction action) =>
            $"{App.Text("Configure.CustomAction.InputControls.Edit")} ({action.Controls.Count})";

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
