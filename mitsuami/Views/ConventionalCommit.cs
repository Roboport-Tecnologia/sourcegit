using System;
using System.Linq;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's conventional commit message builder
    /// (<c>Views/ConventionalCommitMessageBuilder.axaml</c>), a 600-point
    /// sheet as upstream's window: the type (the repository's own list, if
    /// it has one), scope, short description, detail, breaking changes and
    /// closed issue; Sure puts the message in the commit message box, once
    /// the required fields are filled.
    /// </summary>
    public static class ConventionalCommit
    {
        /// <summary>The sheet showing, if any: for captures.</summary>
        public static Window Shown { get; private set; }

        public static void Show(ViewModels.Repository repo, Action<string> apply)
        {
            var vm = new ViewModels.ConventionalCommitMessageBuilder(repo.Settings.ConventionalTypesOverride, apply);
            var title = App.Text("ConventionalCommit");
            var sheet = Shown = new Window(App.MainWindow, title, 600);
            sheet.Closed += () => Shown = null;

            var column = new Column { Padding = Metrics.Panel, Gap = Metrics.PanelGap, AlignItems = Align.Stretch };
            column.Add(new Text(title) { Style = TextStyle.Headline });
            var types = vm.Types;
            var type = new Select(App.Text("ConventionalCommit.Type"))
            {
                Options = types.Select(t => $"{t.Name} ({t.Type})  {t.Description}").ToList(),
                MinWidth = 0,
            };
            type.SelectedIndex = Math.Max(0, types.IndexOf(vm.SelectedType));
            type.Changed += i => vm.SelectedType = types[i];
            column.Add(Settings.Field(App.Text("ConventionalCommit.Type"), type));
            column.Add(Settings.Field(App.Text("ConventionalCommit.Scope"),
                Field(vm, nameof(vm.Scope), () => vm.Scope, v => vm.Scope = v, App.Text("Optional"))));
            column.Add(Settings.Field(App.Text("ConventionalCommit.ShortDescription"),
                Field(vm, nameof(vm.Description), () => vm.Description, v => vm.Description = v, "")));
            column.Add(Settings.Field(App.Text("ConventionalCommit.Detail"),
                Area(vm, nameof(vm.Detail), () => vm.Detail, v => vm.Detail = v), top: true));
            column.Add(Settings.Field(App.Text("ConventionalCommit.BreakingChanges"),
                Area(vm, nameof(vm.BreakingChanges), () => vm.BreakingChanges, v => vm.BreakingChanges = v), top: true));
            column.Add(Settings.Field(App.Text("ConventionalCommit.ClosedIssue"),
                Field(vm, nameof(vm.ClosedIssue), () => vm.ClosedIssue, v => vm.ClosedIssue = v, App.Text("Optional"))));

            var errors = new Text { Color = Color.Error, IsVisible = false };
            column.Add(errors);
            var buttons = new Row { Justify = Justify.End, Gap = Spacing.Sm };
            var cancel = new Button(Popups.ButtonText("Cancel"));
            cancel.Click += sheet.Destroy;
            buttons.Add(cancel);
            var sure = new Button(Popups.ButtonText("Sure")) { Role = ButtonRole.Default };
            sure.Click += () =>
            {
                if (vm.Apply())
                {
                    sheet.Destroy();
                    return;
                }
                errors.Value = string.Join("\n", vm.GetErrors().Select(e => e.ErrorMessage).Where(m => !string.IsNullOrEmpty(m)));
                errors.IsVisible = true;
            };
            buttons.Add(sure);
            column.Add(buttons);
            sheet.Content = column;
        }

        private static TextInput Field(System.ComponentModel.INotifyPropertyChanged vm, string property, Func<string> get, Action<string> set, string placeholder)
        {
            var input = new TextInput { Placeholder = placeholder };
            Bind.Value(input, vm, property, get, set);
            return input;
        }

        /// <summary>Upstream's 96-point boxes for the longer texts.</summary>
        private static TextArea Area(System.ComponentModel.INotifyPropertyChanged vm, string property, Func<string> get, Action<string> set)
        {
            var area = new TextArea { Placeholder = App.Text("Optional") };
            area.Height = 96;
            Bind.Value(area, vm, property, get, set);
            return area;
        }
    }
}
