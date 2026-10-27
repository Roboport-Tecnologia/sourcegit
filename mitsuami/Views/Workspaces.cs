using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's workspace switcher (<c>Launcher.axaml.cs</c>'s
    /// <c>OnOpenWorkspaceMenu</c>) and its <c>ConfigureWorkspace</c> window.
    /// Upstream's is a button before the tabs whose ⌘⌥P opens a menu; here
    /// the menu is the File menu's Workspaces submenu, and ⌘⌥P opens a
    /// palette of them, as ⌘P does the tabs (the user's choice). Configure
    /// Workspaces is a sheet, upstream's dialog, which saves the preferences
    /// as it closes.
    /// </summary>
    public static class Workspaces
    {
        /// <summary>The sheet showing, if any: for captures.</summary>
        public static Window Shown { get; private set; }

        /// <summary>The Workspaces submenu: each workspace, the active one
        /// checked, then Configure Workspaces….</summary>
        public static List<MenuEntry> Menu(ViewModels.Launcher launcher)
        {
            var menu = new List<MenuEntry>();
            foreach (var workspace in ViewModels.Preferences.Instance.Workspaces)
            {
                var name = string.IsNullOrEmpty(workspace.Name) ? "—" : workspace.Name;
                menu.Add(new MenuItem(name, () => launcher.SwitchWorkspace(workspace)) { Checked = workspace.IsActive, Radio = true });
            }
            menu.Add(MenuSeparator.Instance);
            menu.Add(new MenuItem(App.Text("Workspace.Configure"), Configure));
            return menu;
        }

        /// <summary>A workspace's colour, from upstream's ARGB.</summary>
        public static Color ColorOf(ViewModels.Workspace workspace) =>
            Color.Rgba((byte)(workspace.Color >> 16), (byte)(workspace.Color >> 8), (byte)workspace.Color);

        /// <summary>Upstream's workspace icon in the workspace's colour.</summary>
        public static Icon IconOf(ViewModels.Workspace workspace, float size = 14)
        {
            var icon = new Icon(Icons.Get("Workspace")) { Size = size, Color = ColorOf(workspace) };
            Bind.OneWay(workspace, nameof(workspace.Color), () => workspace.Color, _ => icon.Color = ColorOf(workspace), owner: icon);
            return icon;
        }

        /// <summary>
        /// Upstream's <c>ConfigureWorkspace</c>: the workspaces in a framed
        /// list 200 wide, its add, remove and move buttons under it, beside
        /// the picked one's name, default clone folder, colour (the
        /// platform's colour well) and whether its tabs come back on startup.
        /// The active one can't be removed.
        /// </summary>
        public static void Configure()
        {
            if (Shown != null)
                return;
            var vm = new ViewModels.ConfigureWorkspace();
            var title = App.Text("ConfigureWorkspace");
            var sheet = Shown = new Window(App.MainWindow, title, 494);

            var column = new Column { Padding = Spacing.Lg, Gap = Spacing.Md, AlignItems = Align.Stretch };
            column.Add(new Text(title) { Style = TextStyle.Headline });
            var body = new Row { Gap = Spacing.Lg, AlignItems = Align.Stretch };
            body.MinHeight = 340;

            var left = new Column { AlignItems = Align.Stretch, Gap = Spacing.Xs };
            left.Width = 200;
            left.Shrink = 0;
            var keys = new System.Runtime.CompilerServices.ConditionalWeakTable<ViewModels.Workspace, string>();
            var next = 0;
            var list = new ListView<ViewModels.Workspace>(w => keys.GetValue(w, _ => (++next).ToString()), Row, itemAnimations: false)
            {
                Style = ListStyle.Framed,
                EstimatedRowHeight = 26,
                Items = vm.Workspaces.ToList(),
            };
            list.Grow = 1;
            list.MinHeight = 0;
            left.Add(list);

            var buttons = new Row { AlignItems = Align.Center, Gap = Icons.ButtonGap(Spacing.None) };
            var add = Settings.Small("Plus", "Add");
            add.IsEnabled = true;
            add.Click += vm.Add;
            buttons.Add(add);
            var remove = Settings.Small("Minus", "Remove");
            remove.Click += vm.Delete;
            buttons.Add(remove);
            var gap = new Row();
            gap.Grow = 1;
            buttons.Add(gap);
            var up = Settings.Small("Up", "Move Up");
            up.IsEnabled = true;
            up.Click += vm.MoveSelectedUp;
            buttons.Add(up);
            var down = Settings.Small("Down", "Move Down");
            down.IsEnabled = true;
            down.Click += vm.MoveSelectedDown;
            buttons.Add(down);
            left.Add(buttons);
            body.Add(left);

            var right = new Column { AlignItems = Align.Stretch };
            right.Width = 256;
            right.Shrink = 0;
            body.Add(right);
            column.Add(body);

            var footer = new Row { Justify = Justify.End };
            var close = new Button(Popups.ButtonText("Close")) { Role = ButtonRole.Default };
            footer.Add(close);
            column.Add(footer);
            sheet.Content = column;

            // The list follows the view model's, which its buttons change.
            void Changed(object _, NotifyCollectionChangedEventArgs e)
            {
                list.Items = vm.Workspaces.ToList();
                list.SelectedItems = vm.Selected != null ? [vm.Selected] : [];
                AppMenu.Refresh();
            }
            vm.Workspaces.CollectionChanged += Changed;
            list.SelectionChanged += selected => vm.Selected = selected.Count > 0 ? selected[0] : null;

            Node shown = null;
            Bind.OneWay(vm, nameof(vm.Selected), () => vm.Selected, workspace =>
            {
                shown?.Destroy();
                shown = workspace != null ? Detail(sheet, workspace) : null;
                if (shown != null)
                    right.Add(shown);
                up.IsVisible = down.IsVisible = workspace != null;
                if (workspace != null && list.SelectedItem != workspace)
                    list.SelectedItems = [workspace];
            }, owner: sheet);
            Bind.OneWay(vm, nameof(vm.CanDeleteSelected), () => vm.CanDeleteSelected, can => remove.IsEnabled = can, owner: sheet);

            // Upstream saves the preferences as its window closes. `Closed`
            // is the user's closing (Escape), not the Close button's.
            void Done()
            {
                vm.Workspaces.CollectionChanged -= Changed;
                ViewModels.Preferences.Instance.Save();
                AppMenu.Refresh();
                Shown = null;
            }
            sheet.Closed += Done;
            close.Click += () =>
            {
                sheet.Destroy();
                Done();
            };
        }

        /// <summary>Upstream's row: the workspace's icon in its colour, its
        /// name, and a check on the active one.</summary>
        private static Node Row(ViewModels.Workspace workspace)
        {
            var row = new Mitsuamia.Row { Gap = Spacing.Sm, AlignItems = Align.Center, Padding = new Thickness(Spacing.None, Spacing.Xs) };
            row.Height = 26;
            row.Add(IconOf(workspace));
            var name = new Text { MaxLines = 1 };
            name.Grow = 1;
            name.Shrink = 1;
            name.MinWidth = 0;
            Bind.OneWay(workspace, nameof(workspace.Name), () => workspace.Name, v => name.Value = v ?? "", owner: name);
            row.Add(name);
            var check = new Icon(Icons.Get("Check")) { Size = 12 };
            Bind.OneWay(workspace, nameof(workspace.IsActive), () => workspace.IsActive, active => check.IsVisible = active, owner: check);
            row.Add(check);
            return row;
        }

        /// <summary>Upstream's form for the picked workspace: labels over
        /// their fields.</summary>
        private static Node Detail(Window sheet, ViewModels.Workspace workspace)
        {
            var form = new Column { Gap = Spacing.Md, AlignItems = Align.Stretch };

            var name = new TextInput();
            Bind.Value(name, workspace, nameof(workspace.Name), () => workspace.Name, v =>
            {
                workspace.Name = v;
                AppMenu.Refresh();
            });
            form.Add(Settings.Stacked(App.Text("ConfigureWorkspace.Name"), name));

            var folder = new Mitsuamia.Row { Gap = Spacing.Xs, AlignItems = Align.Center };
            var path = new TextInput();
            path.Grow = 1;
            path.MinWidth = 0;
            Bind.Value(path, workspace, nameof(workspace.DefaultCloneDir), () => workspace.DefaultCloneDir, v => workspace.DefaultCloneDir = v);
            folder.Add(path);
            var browse = new Button(App.Text("Preferences.Git.DefaultCloneDir"))
            {
                Icon = Icons.Get("Folder.Open", 14),
                IconOnly = true,
                Style = ButtonStyle.Borderless,
            };
            browse.Click += async () =>
            {
                if (await Dialogs.OpenFolder(sheet, startFolder: workspace.DefaultCloneDir ?? "") is { } picked)
                    workspace.DefaultCloneDir = picked;
            };
            folder.Add(browse);
            form.Add(Settings.Stacked(App.Text("Preferences.Git.DefaultCloneDir"), folder));

            // The platform's colour well, where upstream draws a grid of
            // Fluent's swatches.
            var color = new ColorWell(App.Text("ConfigureWorkspace.Color"));
            Bind.OneWay(workspace, nameof(workspace.Color), () => workspace.Color, _ => color.Value = ColorOf(workspace), owner: color);
            color.Changed += c => workspace.Color = 0xFF000000u | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;
            var colorRow = new Mitsuamia.Row { AlignItems = Align.Center };
            colorRow.Add(color);
            form.Add(Settings.Stacked(App.Text("ConfigureWorkspace.Color"), colorRow));

            var restore = new Checkbox(App.Text("ConfigureWorkspace.Restore"));
            Bind.Checked(restore, workspace, nameof(workspace.RestoreOnStartup), () => workspace.RestoreOnStartup, v => workspace.RestoreOnStartup = v);
            form.Add(restore);
            return form;
        }
    }

    /// <summary>
    /// The workspaces as a palette (upstream has none: its ⌘⌥P opens the
    /// menu): ⌘⌥P lists them, filtered as it's typed, and Return switches
    /// to the one picked.
    /// </summary>
    public class WorkspaceCommandPalette : ViewModels.ICommandPalette
    {
        public WorkspaceCommandPalette(ViewModels.Launcher launcher)
        {
            _launcher = launcher;
            Update();
        }

        public string Filter
        {
            get => _filter;
            set
            {
                if (SetProperty(ref _filter, value))
                    Update();
            }
        }

        public List<ViewModels.Workspace> Visible
        {
            get => _visible;
            private set => SetProperty(ref _visible, value);
        }

        public ViewModels.Workspace Selected
        {
            get => _selected;
            set => SetProperty(ref _selected, value);
        }

        public void Switch()
        {
            var picked = _selected;
            Close();
            if (picked != null)
                _launcher.SwitchWorkspace(picked);
        }

        private void Update()
        {
            var all = ViewModels.Preferences.Instance.Workspaces;
            Visible = string.IsNullOrEmpty(_filter)
                ? all.ToList()
                : all.Where(w => w.Name?.Contains(_filter, StringComparison.OrdinalIgnoreCase) == true).ToList();
            if (_selected == null || !Visible.Contains(_selected))
                Selected = Visible.FirstOrDefault(w => !w.IsActive) ?? Visible.FirstOrDefault();
        }

        private readonly ViewModels.Launcher _launcher;
        private string _filter = string.Empty;
        private List<ViewModels.Workspace> _visible = [];
        private ViewModels.Workspace _selected;
    }
}
