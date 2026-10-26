using System;
using System.Collections.Generic;
using System.IO;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's local changes page (<c>Views/WorkingCopy.axaml</c>): on the
    /// left, a field filtering the changes over the unstaged and the staged
    /// ones, each list under its toolbar; on the right, the selected file's
    /// diff (<see cref="DiffView"/>) over the commit message and the
    /// commit's options and buttons.
    /// </summary>
    public static class WorkingCopy
    {
        public static Node Create(ViewModels.WorkingCopy vm)
        {
            // Upstream's left column is a preference, 300 points until the
            // user drags it, and at least 300; the right at least 470.
            var layout = ViewModels.Preferences.Instance.Layout;
            var split = new Split { Position = layout.WorkingCopyLeftWidth.Value };
            split.Grow = 1;
            split.Changed += width => layout.WorkingCopyLeftWidth = new Avalonia.Controls.GridLength(width);
            var left = Left(vm);
            left.MinWidth = 300;
            split.Add(left);
            var right = Right(vm);
            right.MinWidth = 470;
            split.Add(right);

            // Upstream's commit keys: ⌘↵ commits, ⌘⇧↵ stages everything
            // first, ⌘⌥↵ pushes after.
            var page = new Column { AlignItems = Align.Stretch };
            page.Grow = 1;
            page.Add(split);
            page.OnKey(Shortcut.Of(Key.Enter).WithPrimary(), () => Commit(vm, false, false));
            page.OnKey(Shortcut.Of(Key.Enter).WithPrimary().WithShift(), () => Commit(vm, true, false));
            page.OnKey(Shortcut.Of(Key.Enter).WithPrimary().WithAlt(), () => Commit(vm, false, true));
            // ⌘⇧S stages everything shown, ⌘⇧U unstages it, as upstream's
            // buttons' hot keys.
            page.OnKey(StageAllKey, () => _ = vm.StageChangesAsync(vm.VisibleUnstaged, null));
            page.OnKey(UnstageAllKey, () => _ = vm.UnstageChangesAsync(vm.VisibleStaged, null));
            return page;
        }

        private static void Commit(ViewModels.WorkingCopy vm, bool autoStage, bool autoPush)
        {
            if (!vm.IsCommitting && !string.IsNullOrWhiteSpace(vm.CommitMessage))
                _ = vm.CommitAsync(autoStage, autoPush);
        }

        private static Container Left(ViewModels.WorkingCopy vm)
        {
            var prefs = ViewModels.Preferences.Instance;
            var column = new Column { AlignItems = Align.Stretch };

            // Upstream's filter row, 36 points high: the field, discarding
            // everything, and the lists' view mode.
            var top = new Row { AlignItems = Align.Center, Gap = 4, Padding = new Thickness(0, 4) };
            top.Height = 36;
            top.Shrink = 0;
            // Upstream's sidebar toggle: the repository's sidebar hides
            // while the local changes show.
            var layout = prefs.Layout;
            var sidebar = new Button(App.Text("WorkingCopy.CollapseSidebar")) { IconOnly = true, Style = ButtonStyle.Borderless };
            sidebar.Width = 32;
            Bind.OneWay(layout, nameof(layout.IsSidebarCollapsedInLocalChanges), () => layout.IsSidebarCollapsedInLocalChanges, collapsed =>
            {
                sidebar.Icon = Icons.Get(collapsed ? "Sidebar.Collapsed" : "Sidebar.Expanded", 15);
                sidebar.ToolTip = App.Text(collapsed ? "WorkingCopy.ExpandSidebar" : "WorkingCopy.CollapseSidebar");
            }, owner: sidebar);
            sidebar.Click += () => layout.IsSidebarCollapsedInLocalChanges = !layout.IsSidebarCollapsedInLocalChanges;
            top.Add(sidebar);
            var filter = new SearchInput { Placeholder = App.Text("WorkingCopy.FilterChanges") };
            filter.Grow = 1;
            Bind.Search(filter, vm, nameof(vm.Filter), () => vm.Filter, v => vm.Filter = v, owner: top);
            top.Add(filter);
            top.Add(IconButton("Undo", "WorkingCopy.DiscardAll", vm.DiscardAllChanges));
            top.Add(ChangeCollection.ModeButton());
            column.Add(top);
            column.Add(new Separator());

            var unstaged = Changes(vm, true);
            var staged = Changes(vm, false);

            // Upstream's unstaged toolbar: the count, a spinner while
            // staging, then the assume-unchanged files, untracked files
            // shown or not, the merge tool for every conflict, staging the
            // selection and staging everything.
            var unstagedBar = Bar(vm, "WorkingCopy.Unstaged", nameof(vm.UnstagedCountInfo), () => vm.UnstagedCountInfo,
                nameof(vm.IsStaging), () => vm.IsStaging);
            unstagedBar.Add(IconButton("File.Ignore", "WorkingCopy.Unstaged.ViewAssumeUnchanged", () => AssumeUnchanged.Show(vm.Repository)));
            var untracked = new ToggleButton(App.Text("WorkingCopy.IncludeUntracked"))
            {
                IconOnly = true,
                Style = ButtonStyle.Borderless,
                ToolTip = App.Text("WorkingCopy.IncludeUntracked"),
            };
            untracked.Width = 28;
            Bind.OneWay(vm, nameof(vm.IncludeUntracked), () => vm.IncludeUntracked, on =>
            {
                untracked.IsChecked = on;
                untracked.Icon = Icons.Get(on ? "Eye" : "EyeClose", 14);
            }, owner: untracked);
            untracked.Changed += on => vm.IncludeUntracked = on;
            unstagedBar.Add(untracked);
            var conflicts = IconButton("Conflict", "WorkingCopy.Conflicts.OpenExternalMergeToolAllConflicts",
                () => _ = vm.UseExternalMergeToolAsync(null));
            Bind.OneWay(vm, nameof(vm.HasUnsolvedConflicts), () => vm.HasUnsolvedConflicts, v => conflicts.IsVisible = v, owner: conflicts);
            unstagedBar.Add(conflicts);
            unstagedBar.Add(IconButton("Down", "WorkingCopy.Unstaged.Stage", () => Stage(vm, unstaged)));
            unstagedBar.Add(IconButton("DoubleDown", "WorkingCopy.Unstaged.StageAll", () => _ = vm.StageChangesAsync(vm.VisibleUnstaged, null), StageAllKey));

            var stagedBar = Bar(vm, "WorkingCopy.Staged", nameof(vm.StagedCountInfo), () => vm.StagedCountInfo,
                nameof(vm.IsUnstaging), () => vm.IsUnstaging);
            stagedBar.Add(IconButton("Up", "WorkingCopy.Staged.Unstage", () => Unstage(vm, staged)));
            stagedBar.Add(IconButton("DoubleUp", "WorkingCopy.Staged.UnstageAll", () => _ = vm.UnstageChangesAsync(vm.VisibleStaged, null), UnstageAllKey));

            // The two lists share the height, as upstream's rows do, with
            // the platform's divider between them.
            var lists = new Split { Orientation = Orientation.Vertical };
            lists.Grow = 1;
            lists.Add(Pane(unstagedBar, unstaged.View));
            lists.Add(Pane(stagedBar, staged.View));
            column.Add(lists);
            // Halfway once the page has a size, as the history's split: the
            // divider is in points, and the page is built hidden.
            var gone = false;
            var halved = false;
            lists.Forgotten += () => gone = true;
            var repo = vm.Repository;
            Bind.OneWay(repo, nameof(repo.SelectedViewIndex), () => repo.SelectedViewIndex, i =>
            {
                if (i == 1 && !halved)
                    Halve();
            }, owner: lists);
            return column;

            async void Halve()
            {
                halved = true;
                for (var tries = 0; tries < 100; tries++)
                {
                    await System.Threading.Tasks.Task.Delay(50);
                    if (gone)
                        return;
                    if (lists.Frame.Height is var height and > 0)
                    {
                        lists.Position = Math.Floor(height / 2);
                        return;
                    }
                }
                halved = false;
            }

            static Container Pane(Node bar, Node list)
            {
                var pane = new Column { AlignItems = Align.Stretch };
                pane.MinHeight = 100;
                pane.Add(bar);
                pane.Add(new Separator());
                list.Grow = 1;
                list.MinHeight = 0;
                pane.Add(list);
                return pane;
            }
        }

        /// <summary>Upstream's 28-point toolbar over a list: the changes
        /// icon, the list's name and count in bold, both in the secondary
        /// colour, and a spinner while it works; its buttons go at the end.</summary>
        private static Row Bar(ViewModels.WorkingCopy vm, string title, string countProperty, Func<string> count,
            string busyProperty, Func<bool> busy)
        {
            var bar = new Row { AlignItems = Align.Center, Gap = 4, Padding = new Thickness(0, 4, 0, 8) };
            bar.Height = 28;
            bar.Shrink = 0;
            bar.Add(new Icon(Icons.Get("Changes")) { Size = 14, Color = Color.SecondaryLabel });
            var text = new Text { Weight = FontWeight.Bold, Color = Color.SecondaryLabel };
            Bind.OneWay(vm, countProperty, count, c => text.Value = $"{App.Text(title)} {c}", owner: bar);
            bar.Add(text);
            var spinner = new Spinner();
            Bind.OneWay(vm, busyProperty, busy, v => spinner.IsVisible = v, owner: spinner);
            bar.Add(spinner);
            var space = new Column();
            space.Grow = 1;
            bar.Add(space);
            return bar;
        }

        /// <summary>One of the two lists, bound both ways to its changes and
        /// selection, with upstream's keys and menus.</summary>
        private static ChangeCollection Changes(ViewModels.WorkingCopy vm, bool unstaged)
        {
            var prefs = ViewModels.Preferences.Instance;
            var list = new ChangeCollection(unstaged);
            var owner = list.View;
            Bind.OneWay(prefs, nameof(prefs.ChangeViewMode), () => prefs.ChangeViewMode, m => list.Mode = m, owner: owner);
            Bind.OneWay(prefs, nameof(prefs.EnableCompactFoldersInChangesTree), () => prefs.EnableCompactFoldersInChangesTree,
                v => list.CompactFolders = v, owner: owner);
            if (unstaged)
            {
                Bind.OneWay(vm, nameof(vm.VisibleUnstaged), () => vm.VisibleUnstaged, c => list.Changes = c, owner: owner);
                Bind.OneWay(vm, nameof(vm.SelectedUnstaged), () => vm.SelectedUnstaged, s => list.Selection = s, owner: owner);
                list.SelectionChanged += s => vm.SelectedUnstaged = s;
                list.Activated += () => Stage(vm, list);
                list.Menu = s => ChangeMenus.Unstaged(vm, s, list.NextWithoutSelection);
            }
            else
            {
                Bind.OneWay(vm, nameof(vm.VisibleStaged), () => vm.VisibleStaged, c => list.Changes = c, owner: owner);
                Bind.OneWay(vm, nameof(vm.SelectedStaged), () => vm.SelectedStaged, s => list.Selection = s, owner: owner);
                list.SelectionChanged += s => vm.SelectedStaged = s;
                list.Activated += () => Unstage(vm, list);
                list.Menu = s => ChangeMenus.Staged(vm, s, list.NextWithoutSelection);
            }

            // Upstream's keys: Space (and Return, the list's activation)
            // stages or unstages; Delete and Backspace discard unstaged
            // changes; ⌘O opens a file; ⌘C and ⌘⇧C copy paths.
            ViewModels.ChangeSelection Selected() => unstaged ? vm.SelectedUnstaged : vm.SelectedStaged;
            owner.OnKey(Shortcut.Of(' '), () =>
            {
                if (unstaged)
                    Stage(vm, list);
                else
                    Unstage(vm, list);
            });
            if (unstaged)
            {
                void Discard()
                {
                    if (vm.SelectedUnstaged is { Count: > 0 } selection)
                        vm.Discard(selection.Changes, list.NextWithoutSelection());
                }
                owner.OnKey(Shortcut.Of(Key.Delete), Discard);
                owner.OnKey(Shortcut.Of(Key.Backspace), Discard);
            }
            owner.OnKey(Shortcut.Of('o').WithPrimary(), () => ChangeMenus.OpenWithDefaultEditor(vm.Repository, Selected()));
            owner.OnKey(Shortcut.Of('c').WithPrimary(), () => ChangeMenus.CopyPaths(vm.Repository, Selected(), false));
            owner.OnKey(Shortcut.Of('c').WithPrimary().WithShift(), () => ChangeMenus.CopyPaths(vm.Repository, Selected(), true));
            return list;
        }

        private static void Stage(ViewModels.WorkingCopy vm, ChangeCollection list)
        {
            if (vm.SelectedUnstaged is { Count: > 0 } selection)
                _ = vm.StageChangesAsync(selection.Changes, list.NextWithoutSelection());
        }

        private static void Unstage(ViewModels.WorkingCopy vm, ChangeCollection list)
        {
            if (vm.SelectedStaged is { Count: > 0 } selection)
                _ = vm.UnstageChangesAsync(selection.Changes, list.NextWithoutSelection());
        }

        private static Container Right(ViewModels.WorkingCopy vm)
        {
            var column = new Column { AlignItems = Align.Stretch, Gap = 4, Padding = new Thickness(4, 6, 0, 4) };
            column.Add(Detail(vm));
            column.Add(MessageBox(vm));
            column.Add(Options(vm));
            return column;
        }

        /// <summary>Upstream's detail: the picked file's diff
        /// (<see cref="DiffView"/>), or its conflict.</summary>
        private static Node Detail(ViewModels.WorkingCopy vm)
        {
            var detail = new Column { AlignItems = Align.Stretch };
            detail.Grow = 1;
            detail.MinHeight = 100;
            var diff = DiffView.Create(vm, nameof(vm.DetailContext), () => vm.DetailContext as ViewModels.DiffContext);
            detail.Add(diff);
            Node conflict = null;
            Bind.OneWay(vm, nameof(vm.DetailContext), () => vm.DetailContext, context =>
            {
                conflict?.Destroy();
                conflict = null;
                if (context is ViewModels.Conflict c)
                {
                    conflict = Conflict.Create(vm.Repository, c);
                    detail.Add(conflict);
                }
                diff.IsVisible = conflict == null;
            }, owner: detail);
            return detail;
        }

        /// <summary>
        /// Upstream's commit message box, 128 points high: the platform's
        /// text view, then a 24-point footer with the message's helpers
        /// (templates and recent messages, the AI assistant, conventional
        /// commits) and the subject's length against the guide, with a
        /// warning past it. Upstream's guide line in the text and its
        /// suggestions aren't ported: a native text view has neither.
        /// </summary>
        private static Node MessageBox(ViewModels.WorkingCopy vm)
        {
            var repo = vm.Repository;
            var prefs = ViewModels.Preferences.Instance;
            var box = new Column { AlignItems = Align.Stretch, Gap = 4 };
            box.Height = 128;
            box.Shrink = 0;

            // Upstream's placeholder, on every platform: mitsuami-extras'.
            var editor = new TextAreaWithPlaceholder(App.Text("CommitMessageTextBox.Placeholder"));
            editor.Grow = 1;
            editor.MinHeight = 0;
            var message = editor.Area;
            message.AccessibleName = App.Text("WorkingCopy.CommitMessageHelper");
            Bind.Value(message, vm, nameof(vm.CommitMessage), () => vm.CommitMessage, v => vm.CommitMessage = v);
            box.Add(editor);

            var footer = new Row { AlignItems = Align.Center, Gap = 4 };
            footer.Height = 24;
            footer.Shrink = 0;
            var helper = new MenuButton(App.Text("WorkingCopy.CommitMessageHelper"))
            {
                Icon = Icons.Get("Menu", 12),
                IconOnly = true,
                Style = ButtonStyle.Borderless,
                ToolTip = App.Text("WorkingCopy.CommitMessageHelper"),
            };
            helper.Width = 28;
            footer.Add(helper);
            // Upstream builds it as it opens; a menu button's menu is set
            // ahead, so it's built again whenever what's in it can change:
            // after a commit, and as the page shows.
            async void BuildHelper() => helper.Menu = await HelperMenu(vm, BuildHelper);
            BuildHelper();
            Bind.OneWay(vm, nameof(vm.IsCommitting), () => vm.IsCommitting, committing =>
            {
                if (!committing)
                    BuildHelper();
            }, owner: helper);
            Bind.OneWay(repo, nameof(repo.SelectedViewIndex), () => repo.SelectedViewIndex, i =>
            {
                if (i == 1)
                    BuildHelper();
            }, owner: helper);

            footer.Add(IconButton("AIAssist", "AIAssistant.Tip", () =>
            {
                if (vm.Staged == null || vm.Staged.Count == 0)
                    repo.SendNotification("No files added to commit!", true);
                else if (repo.GetPreferredOpenAIServices().Count == 0)
                    repo.SendNotification("Bad configuration for OpenAI", true);
                else
                    AIAssistant.Ask(repo, vm.Staged);
            }));
            footer.Add(IconButton("CommitMessageGenerator", "ConventionalCommit",
                () => ConventionalCommit.Show(repo, text => vm.CommitMessage = text)));

            var space = new Column();
            space.Grow = 1;
            footer.Add(space);
            footer.Add(new Text(App.Text("CommitMessageTextBox.SubjectCount")) { Style = TextStyle.Caption, Color = Color.SecondaryLabel });
            var count = new Text { Style = TextStyle.Caption };
            footer.Add(count);
            var warning = new Icon(Icons.Get("Error")) { Size = 10, Color = Color.Warning };
            footer.Add(warning);
            void ShowCount()
            {
                var length = SubjectLength(vm.CommitMessage);
                var guide = prefs.SubjectGuideLength;
                count.Value = $"{length}/{guide}";
                count.Color = length > guide ? Color.Warning : Color.Label;
                warning.IsVisible = length > guide;
            }
            Bind.OneWay(vm, nameof(vm.CommitMessage), () => vm.CommitMessage, _ => ShowCount(), owner: footer);
            Bind.OneWay(prefs, nameof(prefs.SubjectGuideLength), () => prefs.SubjectGuideLength, _ => ShowCount(), owner: footer);
            box.Add(footer);
            return box;
        }

        /// <summary>Upstream's <c>CommitMessageTextBox</c>'s subject length:
        /// the first paragraph's lines, trimmed at their ends, joined by a
        /// space each.</summary>
        private static int SubjectLength(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;
            var length = 0;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd();
                if (string.IsNullOrWhiteSpace(line))
                {
                    if (length > 0)
                        break;
                    continue;
                }
                length = length > 0 ? length + line.Length + 1 : line.Length;
            }
            return length;
        }

        /// <summary>Upstream's message helper: the repository's commit
        /// templates and git's <c>commit.template</c>, then the recent
        /// messages, one line each, and clearing them.</summary>
        private static async System.Threading.Tasks.Task<IReadOnlyList<MenuEntry>> HelperMenu(ViewModels.WorkingCopy vm, Action rebuild)
        {
            var repo = vm.Repository;
            var menu = new List<MenuEntry>();
            var gitTemplate = await new Commands.Config(repo.FullPath).GetAsync("commit.template");
            var templates = repo.Settings.CommitTemplates;
            if (templates.Count == 0 && string.IsNullOrEmpty(gitTemplate))
            {
                menu.Add(new MenuItem(App.Text("WorkingCopy.NoCommitTemplates"), () => { }) { Enabled = false });
            }
            else
            {
                foreach (var template in templates)
                    menu.Add(new MenuItem(App.Text("WorkingCopy.UseCommitTemplate", template.Name), () => vm.ApplyCommitMessageTemplate(template)));
                if (!string.IsNullOrEmpty(gitTemplate))
                {
                    if (!Path.IsPathRooted(gitTemplate))
                        gitTemplate = Native.OS.GetAbsPath(repo.FullPath, gitTemplate);
                    var friendlyName = gitTemplate;
                    if (!OperatingSystem.IsWindows())
                    {
                        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                        var prefixLen = home.EndsWith('/') ? home.Length - 1 : home.Length;
                        if (gitTemplate.StartsWith(home, StringComparison.Ordinal))
                            friendlyName = $"~{gitTemplate.AsSpan(prefixLen)}";
                    }
                    var file = gitTemplate;
                    menu.Add(new MenuItem(App.Text("WorkingCopy.UseCommitTemplate", friendlyName), () =>
                    {
                        if (File.Exists(file))
                            vm.CommitMessage = File.ReadAllText(file);
                    }));
                }
            }
            menu.Add(MenuSeparator.Instance);

            var recent = repo.UIStates.RecentCommitMessages;
            if (recent.Count == 0)
            {
                menu.Add(new MenuItem(App.Text("WorkingCopy.NoCommitHistories"), () => { }) { Enabled = false });
            }
            else
            {
                foreach (var item in recent)
                {
                    var text = item.Trim();
                    menu.Add(new MenuItem(text.ReplaceLineEndings(" "), () => vm.CommitMessage = text));
                }
                menu.Add(MenuSeparator.Instance);
                menu.Add(new MenuItem(App.Text("WorkingCopy.ClearCommitHistories"), async () =>
                {
                    await vm.ClearCommitMessageHistoryAsync();
                    rebuild();
                }));
            }
            return menu;
        }

        /// <summary>
        /// Upstream's options row, 40 points high: sign-off, no verify,
        /// amend (and resetting the author while amending), a spinner while
        /// committing, then Continue while an operation's in progress, else
        /// Commit, and Commit &amp; Push when there are remotes and it isn't
        /// an amend. Continue and Commit are upstream's primary buttons:
        /// prominent, not the window's default (Return in a field mustn't
        /// commit).
        /// </summary>
        private static Node Options(ViewModels.WorkingCopy vm)
        {
            var row = new Row { AlignItems = Align.Center, Gap = 12 };
            row.Height = 40;
            row.Shrink = 0;
            row.Add(Check(vm, nameof(vm.EnableSignOff), "WorkingCopy.SignOff", "--signoff", () => vm.EnableSignOff, v => vm.EnableSignOff = v));
            row.Add(Check(vm, nameof(vm.NoVerifyOnCommit), "WorkingCopy.NoVerify", "--no-verify", () => vm.NoVerifyOnCommit, v => vm.NoVerifyOnCommit = v));
            row.Add(Check(vm, nameof(vm.UseAmend), "WorkingCopy.Amend", "--amend", () => vm.UseAmend, v => vm.UseAmend = v));
            var resetAuthor = Check(vm, nameof(vm.ResetAuthor), "WorkingCopy.ResetAuthor", "--reset-author", () => vm.ResetAuthor, v => vm.ResetAuthor = v);
            Bind.OneWay(vm, nameof(vm.UseAmend), () => vm.UseAmend, v => resetAuthor.IsVisible = v, owner: resetAuthor);
            row.Add(resetAuthor);

            var space = new Column();
            space.Grow = 1;
            row.Add(space);
            var spinner = new Spinner();
            Bind.OneWay(vm, nameof(vm.IsCommitting), () => vm.IsCommitting, v => spinner.IsVisible = v, owner: spinner);
            row.Add(spinner);

            var buttons = new Row { AlignItems = Align.Center, Gap = 8 };
            // Upstream's split button: its menu commits the message as the
            // operation stopped to let it edit.
            var proceed = new SplitButton(Popups.ButtonText("Repository.Continue")) { Style = ButtonStyle.Prominent };
            proceed.Click += () => _ = vm.ContinueMergeAsync();
            bool? canCommitToEdit = null;
            buttons.Add(proceed);
            var primary = OperatingSystem.IsMacOS() ? "⌘" : "Ctrl+";
            var commit = new Button(Popups.ButtonText("WorkingCopy.Commit"))
            {
                Style = ButtonStyle.Prominent,
                ToolTip = $"{primary}↵ {App.Text("WorkingCopy.CommitTip")}\n{primary}⇧↵ {App.Text("WorkingCopy.CommitWithAutoStage")}",
            };
            commit.Click += () => Commit(vm, false, false);
            buttons.Add(commit);
            var commitAndPush = new Button(Popups.ButtonText("WorkingCopy.CommitAndPush")) { ToolTip = $"{primary}⌥↵" };
            commitAndPush.Click += () => Commit(vm, false, true);
            buttons.Add(commitAndPush);
            row.Add(buttons);

            void Show()
            {
                var inProgress = vm.InProgressContext != null;
                var canCommit = !vm.IsCommitting && !string.IsNullOrWhiteSpace(vm.CommitMessage);
                proceed.IsVisible = inProgress;
                proceed.IsEnabled = !vm.IsCommitting && !vm.HasUnsolvedConflicts;
                if (canCommitToEdit != canCommit)
                {
                    canCommitToEdit = canCommit;
                    proceed.Menu = [new MenuItem(App.Text("WorkingCopy.CommitToEdit"), () => Commit(vm, false, false)) { Enabled = canCommit }];
                }
                commit.IsVisible = !inProgress;
                commit.IsEnabled = canCommit;
                commitAndPush.IsVisible = vm.HasRemotes && !vm.UseAmend && !inProgress;
                commitAndPush.IsEnabled = canCommit;
            }
            foreach (var property in new[] { nameof(vm.InProgressContext), nameof(vm.IsCommitting), nameof(vm.CommitMessage),
                nameof(vm.HasUnsolvedConflicts), nameof(vm.HasRemotes), nameof(vm.UseAmend) })
                Bind.OneWay(vm, property, () => 0, _ => Show(), owner: buttons);
            return row;
        }

        private static Checkbox Check(ViewModels.WorkingCopy vm, string property, string label, string tip, Func<bool> get, Action<bool> set)
        {
            var box = new Checkbox(App.Text(label)) { ToolTip = tip };
            Bind.Checked(box, vm, property, get, set);
            return box;
        }

        /// <summary>A toolbar's button: upstream's icon, 14 points, its
        /// tooltip the text; disabled when there's no command yet.</summary>
        private static readonly Shortcut StageAllKey = Shortcut.Of('s').WithPrimary().WithShift();
        private static readonly Shortcut UnstageAllKey = Shortcut.Of('u').WithPrimary().WithShift();

        // A key, if given, follows the name in the tip, as upstream's.
        private static Button IconButton(string icon, string text, Action command, Shortcut? key = null)
        {
            var button = new Button(App.Text(text))
            {
                Icon = Icons.Get(icon, 14),
                IconOnly = true,
                Style = ButtonStyle.Borderless,
                ToolTip = key is { } k ? $"{App.Text(text)}  {Hotkeys.Format(k)}" : App.Text(text),
                IsEnabled = command != null,
            };
            button.Width = 28;
            if (command != null)
                button.Click += command;
            return button;
        }
    }
}
