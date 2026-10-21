using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream shows a page's popup inside the page, over its content, with
    /// OK and Cancel under the form. Here it's a sheet on the window: what
    /// each platform's own Git clients use for a dialog about the window.
    /// </summary>
    public static class Popups
    {
        /// <summary>Shows the active page's popup while it has one.</summary>
        public static void Attach(Window window, ViewModels.Launcher vm)
        {
            Window sheet = null;
            ViewModels.LauncherPage shown = null;

            void Show()
            {
                var page = vm.ActivePage;
                if (page?.Popup == null || (page == shown && sheet != null))
                {
                    if (page?.Popup == null)
                        Close();
                    return;
                }

                Close();
                shown = page;
                sheet = Sheet(window, page, page.Popup);
                // Escape or the close button: what Cancel does.
                sheet.Closed += () =>
                {
                    sheet = null;
                    page.CancelPopup();
                };
            }

            void Close()
            {
                if (sheet == null)
                    return;
                var closing = sheet;
                sheet = null;
                shown = null;
                closing.Destroy();
            }

            void Watch(ViewModels.LauncherPage page) => page.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(page.Popup) && page == vm.ActivePage)
                    Show();
            };

            foreach (var page in vm.Pages)
                Watch(page);
            vm.Pages.CollectionChanged += (_, e) =>
            {
                foreach (var page in e.NewItems?.OfType<ViewModels.LauncherPage>() ?? [])
                    Watch(page);
            };
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(vm.ActivePage))
                    Show();
            };
            Show();
        }

        private static Window Sheet(Window owner, ViewModels.LauncherPage page, ViewModels.Popup popup)
        {
            var form = Form(popup, out var title, out var ported);
            var sheet = new Window(owner, title, 520);

            var column = new Column { Padding = 20, Gap = 12, AlignItems = Align.Stretch };
            column.Add(new Text(title) { Style = TextStyle.Headline });
            column.Add(form(sheet));

            // Upstream's progress: git's last line while the popup runs.
            var progress = new Row { Gap = 8, AlignItems = Align.Center };
            var progressText = new Text { Style = TextStyle.Caption };
            progressText.Grow = 1;
            progress.Add(new Spinner());
            progress.Add(progressText);
            Bind.OneWay(popup, nameof(popup.InProgress), () => popup.InProgress, v => progress.IsVisible = v, owner: sheet);
            Bind.OneWay(popup, nameof(popup.ProgressDescription), () => popup.ProgressDescription,
                v => progressText.Value = v, owner: sheet);
            column.Add(progress);

            var errors = new Text { Style = TextStyle.Caption, IsVisible = false };
            column.Add(errors);

            var buttons = new Row { Gap = 8, Justify = Justify.End };
            var cancel = new Button(ButtonText("Cancel")) { Role = ButtonRole.Cancel };
            cancel.Click += page.CancelPopup;
            var sure = new Button(ButtonText("Sure")) { Role = ButtonRole.Default, IsEnabled = ported };
            sure.Click += async () =>
            {
                await page.ProcessPopupAsync();
                // Still showing: it didn't validate, or git failed.
                if (page.Popup == popup)
                    ShowErrors(errors, popup);
            };
            Bind.OneWay(popup, nameof(popup.InProgress), () => popup.InProgress, running =>
            {
                cancel.IsEnabled = !running;
                sure.IsEnabled = ported && !running;
            }, owner: sheet);
            buttons.Add(cancel);
            buttons.Add(sure);
            column.Add(buttons);

            sheet.Content = column;
            return sheet;
        }

        private static void ShowErrors(Text errors, ViewModels.Popup popup)
        {
            var messages = popup.GetErrors(null).Select(e => e.ErrorMessage).Where(m => !string.IsNullOrEmpty(m)).ToArray();
            errors.Value = string.Join("\n", messages);
            errors.IsVisible = messages.Length > 0;
        }

        /// <summary>Upstream's buttons are in capitals, its theme's style;
        /// native buttons are in the platform's case.</summary>
        public static string ButtonText(string key)
        {
            var text = App.Text(key);
            return text == text.ToUpperInvariant() ? CultureInfo.CurrentCulture.TextInfo.ToTitleCase(text.ToLowerInvariant()) : text;
        }

        /// <summary>Each popup's form, as upstream's view for it. Those not
        /// ported yet say so, and can only be cancelled.</summary>
        private static Func<Window, Node> Form(ViewModels.Popup popup, out string title, out bool ported)
        {
            ported = true;
            switch (popup)
            {
                case ViewModels.Clone clone:
                    title = App.Text("Clone");
                    return sheet => CloneForm(sheet, clone);
                case ViewModels.CreateGroup group:
                    title = App.Text("Welcome.AddRootFolder");
                    return sheet => Fields(sheet, (App.Text("Name"), TextField(sheet, group, nameof(group.Name), () => group.Name, v => group.Name = v)));
                case ViewModels.OpenLocalRepository open:
                    title = App.Text("OpenLocalRepository");
                    return sheet => Fields(sheet,
                        (App.Text("OpenLocalRepository.Path"), FolderField(sheet, open, nameof(open.RepoPath), () => open.RepoPath, v => open.RepoPath = v)),
                        (App.Text("OpenLocalRepository.Group"), GroupSelect(sheet, open, open.Groups, () => open.SelectedGroupIndex, v => open.SelectedGroupIndex = v)),
                        (App.Text("OpenLocalRepository.Bookmark"), BookmarkField(open, nameof(open.Bookmark), () => open.Bookmark, v => open.Bookmark = v)));
                case ViewModels.EditRepositoryNode edit:
                    title = App.Text(edit.IsRepository ? "EditRepositoryNode.TitleForRepository" : "EditRepositoryNode.TitleForGroup");
                    return sheet => Fields(sheet,
                        (App.Text("EditRepositoryNode.Target"), new Text(edit.Target)),
                        (App.Text("EditRepositoryNode.Name"), TextField(sheet, edit, nameof(edit.Name), () => edit.Name, v => edit.Name = v)),
                        (App.Text("EditRepositoryNode.Bookmark"), BookmarkField(edit, nameof(edit.Bookmark), () => edit.Bookmark, v => edit.Bookmark = v)));
                case ViewModels.MoveRepositoryNode move:
                    title = App.Text("MoveRepositoryNode");
                    return sheet => MoveForm(move);
                case ViewModels.DeleteRepositoryNode delete:
                    title = App.Text(delete.Node.IsRepository ? "DeleteRepositoryNode.TitleForRepository" : "DeleteRepositoryNode.TitleForGroup");
                    return sheet => DeleteForm(sheet, delete);
                case ViewModels.ScanRepositories scan:
                    title = App.Text("ScanRepositories");
                    return sheet => ScanForm(sheet, scan);
                default:
                    title = popup.GetType().Name;
                    ported = false;
                    return _ => new Text($"The {popup.GetType().Name} popup isn't ported to mitsuami yet.");
            }
        }

        private static Node CloneForm(Window sheet, ViewModels.Clone vm)
        {
            var sshKey = Field(App.Text("SSHKey"),
                FileField(sheet, vm, nameof(vm.SSHKey), () => vm.SSHKey, v => vm.SSHKey = v, App.Text("SSHKey.Placeholder")));
            Bind.OneWay(vm, nameof(vm.UseSSH), () => vm.UseSSH, v => sshKey.IsVisible = v, owner: sshKey);

            var submodules = new Checkbox(App.Text("Clone.RecurseSubmodules"));
            Bind.Checked(submodules, vm, nameof(vm.InitAndUpdateSubmodules), () => vm.InitAndUpdateSubmodules, v => vm.InitAndUpdateSubmodules = v);

            var form = Fields(sheet,
                (App.Text("Clone.RemoteURL"), TextField(sheet, vm, nameof(vm.Remote), () => vm.Remote, v => vm.Remote = v)));
            form.Add(sshKey);
            form.Add(Field(App.Text("Clone.ParentFolder"), FolderField(sheet, vm, nameof(vm.ParentFolder), () => vm.ParentFolder, v => vm.ParentFolder = v)));
            form.Add(Field(App.Text("Clone.LocalName"), TextField(sheet, vm, nameof(vm.Local), () => vm.Local, v => vm.Local = v, App.Text("Clone.LocalName.Placeholder"))));
            form.Add(Field(App.Text("Clone.AdditionalParam"), TextField(sheet, vm, nameof(vm.ExtraArgs), () => vm.ExtraArgs, v => vm.ExtraArgs = v, App.Text("Clone.AdditionalParam.Placeholder"))));
            form.Add(Field(App.Text("Clone.Group"), GroupSelect(sheet, vm, vm.Groups, () => vm.SelectedGroupIndex, v => vm.SelectedGroupIndex = v)));
            form.Add(Field(App.Text("Clone.Bookmark"), BookmarkField(vm, nameof(vm.Bookmark), () => vm.Bookmark, v => vm.Bookmark = v)));
            form.Add(Field("", submodules));
            return form;
        }

        private static Node ScanForm(Window sheet, ViewModels.ScanRepositories vm)
        {
            var dirs = new Select(App.Text("ScanRepositories.RootDir"))
            {
                Options = vm.ScanDirs.Select(d => string.IsNullOrEmpty(d.desc) ? d.path : $"{d.path}  ({d.desc})").ToArray(),
            };
            Bind.OneWay(vm, nameof(vm.Selected), () => vm.ScanDirs.IndexOf(vm.Selected), i => dirs.SelectedIndex = Math.Max(i, 0), owner: dirs);
            dirs.Changed += i => vm.Selected = vm.ScanDirs[i];

            var useCustom = new Checkbox(App.Text("ScanRepositories.UseCustomDir"));
            Bind.Checked(useCustom, vm, nameof(vm.UseCustomDir), () => vm.UseCustomDir, v => vm.UseCustomDir = v);

            var custom = Field("", FolderField(sheet, vm, nameof(vm.CustomDir), () => vm.CustomDir, v => vm.CustomDir = v));
            Bind.OneWay(vm, nameof(vm.UseCustomDir), () => vm.UseCustomDir, v =>
            {
                custom.IsVisible = v;
                dirs.IsEnabled = !v;
            }, owner: custom);

            var form = Fields(sheet, (App.Text("ScanRepositories.RootDir"), dirs));
            form.Add(Field("", useCustom));
            form.Add(custom);
            return form;
        }

        /// <summary>Upstream's move: the new parent, from every group, each
        /// under its own (indented as the tree is), the top first.</summary>
        private static Node MoveForm(ViewModels.MoveRepositoryNode vm)
        {
            var parents = new Select(App.Text("MoveRepositoryNode.Target"))
            {
                Options = vm.Rows.Select(r => new string(' ', r.Depth * 4) + r.Name).ToArray(),
            };
            Bind.OneWay(vm, nameof(vm.Selected), () => vm.Rows.IndexOf(vm.Selected), i => parents.SelectedIndex = Math.Max(i, 0), owner: parents);
            parents.Changed += i => vm.Selected = vm.Rows[i];

            var column = new Column { Gap = 8, AlignItems = Align.Stretch };
            column.Add(new Text(App.Text("MoveRepositoryNode.Target")));
            column.Add(new Text(vm.Target.Name) { Style = TextStyle.Headline });
            column.Add(parents);
            return column;
        }

        /// <summary>Upstream's confirmation: what goes, and that a
        /// repository stays on disk (a group's children go too).</summary>
        private static Node DeleteForm(Window sheet, ViewModels.DeleteRepositoryNode vm)
        {
            var node = vm.Node;
            var form = Fields(sheet, (App.Text("DeleteRepositoryNode.Target"), new Text(node.Name)));
            if (node.IsRepository)
                form.Add(Field(App.Text("DeleteRepositoryNode.Path"), new Text(node.Id) { Color = Color.SecondaryLabel }));
            var tip = App.Text(node.IsRepository ? "DeleteRepositoryNode.TipForRepository" : "DeleteRepositoryNode.TipForGroup");
            form.Add(new Text(tip) { Style = TextStyle.Caption, Color = Color.SecondaryLabel });
            return form;
        }

        /// <summary>
        /// Upstream's bookmark picker is a row of coloured bookmarks drawn by
        /// hand. Here it's the platform's drop-down of the colours' names,
        /// and the bookmark beside it in the one chosen.
        /// </summary>
        private static Row BookmarkField(INotifyPropertyChanged vm, string property, Func<int> get, Action<int> set)
        {
            var row = new Row { Gap = 8, AlignItems = Align.Center };
            var preview = new Icon(Icons.Get("Bookmark")) { Size = 14 };
            var colours = new Select(App.Text("EditRepositoryNode.Bookmark")) { Options = BookmarkNames };
            Bind.OneWay(vm, property, get, b =>
            {
                colours.SelectedIndex = b;
                preview.Color = Welcome.BookmarkColor(b);
            }, owner: row);
            colours.Changed += set;
            row.Add(preview);
            row.Add(colours);
            return row;
        }

        /// <summary>Upstream's bookmarks, in its order: none, then red,
        /// orange, gold, forest green, dark cyan, deep sky blue, purple.</summary>
        private static readonly string[] BookmarkNames = ["None", "Red", "Orange", "Yellow", "Green", "Cyan", "Blue", "Purple"];

        // Building blocks: a label beside each control, as a form's rows.

        private const float LabelWidth = 140;

        private static Column Fields(Window sheet, params (string Label, Node Control)[] rows)
        {
            var column = new Column { Gap = 8, AlignItems = Align.Stretch };
            foreach (var (label, control) in rows)
                column.Add(Field(label, control));
            return column;
        }

        private static Row Field(string label, Node control)
        {
            var row = new Row { Gap = 8, AlignItems = Align.Center };
            var text = new Text(label);
            text.Width = LabelWidth;
            row.Add(text);
            control.Grow = 1;
            row.Add(control);
            return row;
        }

        private static TextInput TextField(Window sheet, INotifyPropertyChanged vm, string property, Func<string> get,
            Action<string> set, string placeholder = "")
        {
            var input = new TextInput { Placeholder = placeholder, AccessibleName = property };
            Bind.Value(input, vm, property, get, set);
            return input;
        }

        /// <summary>A path field with a button that asks the platform's
        /// folder dialog, as upstream's folder fields have.</summary>
        private static Row FolderField(Window sheet, INotifyPropertyChanged vm, string property, Func<string> get, Action<string> set) =>
            PathField(sheet, vm, property, get, set, "", folders: true);

        private static Row FileField(Window sheet, INotifyPropertyChanged vm, string property, Func<string> get,
            Action<string> set, string placeholder) =>
            PathField(sheet, vm, property, get, set, placeholder, folders: false);

        private static Row PathField(Window sheet, INotifyPropertyChanged vm, string property, Func<string> get,
            Action<string> set, string placeholder, bool folders)
        {
            var row = new Row { Gap = 4, AlignItems = Align.Center };
            var input = TextField(sheet, vm, property, get, set, placeholder);
            input.Grow = 1;
            var browse = new Button("…");
            browse.Click += async () =>
            {
                var current = get() ?? "";
                var start = folders ? current : System.IO.Path.GetDirectoryName(current) ?? "";
                var chosen = folders
                    ? await Dialogs.OpenFolder(sheet, startFolder: start)
                    : (await Dialogs.OpenFiles(sheet, startFolder: start)).FirstOrDefault();
                if (chosen != null)
                    set(chosen);
            };
            row.Add(input);
            row.Add(browse);
            return row;
        }

        /// <summary>Upstream's group picker: a group's name, and its path
        /// in the tree as the option's description.</summary>
        private static Select GroupSelect(Window sheet, INotifyPropertyChanged vm, List<ViewModels.TargetGroup> groups,
            Func<int> get, Action<int> set)
        {
            var select = new Select(App.Text("Clone.Group"))
            {
                Options = groups.Select(g => string.IsNullOrEmpty(g.Description) ? g.Name : $"{g.Name}  ({g.Description})").ToArray(),
            };
            Bind.OneWay(vm, "SelectedGroupIndex", get, i => select.SelectedIndex = Math.Max(i, 0), owner: select);
            select.Changed += set;
            return select;
        }
    }
}
