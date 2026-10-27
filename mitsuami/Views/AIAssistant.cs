using System.Collections.Generic;
using System.Linq;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's AI assistant (<c>Views/AIAssistant.axaml</c>), a 520 × 400
    /// sheet as upstream's window: the model's answer as it streams in, in a
    /// read-only text area that follows its end; the model to ask, a spinner
    /// while it writes, Use (the message goes in the commit message box) and
    /// Regenerate. Closing it stops the request.
    /// </summary>
    public static class AIAssistant
    {
        /// <summary>The sheet showing, if any: for captures.</summary>
        public static Window Shown { get; private set; }

        /// <summary>Upstream's entry for the staged changes' menu: the one
        /// service, or a submenu of them.</summary>
        public static MenuEntry MenuEntry(ViewModels.Repository repo, List<Models.Change> changes)
        {
            var title = App.Text("ChangeCM.GenerateCommitMessage");
            var services = repo.GetPreferredOpenAIServices();
            if (services.Count == 1)
                return new MenuItem(title, () => Show(repo, services[0], changes));
            return new Submenu(title, services.Select(s => (MenuEntry)new MenuItem(s.Name, () => Show(repo, s, changes))).ToList());
        }

        /// <summary>Upstream's button by the commit message: the one service,
        /// or asked which.</summary>
        public static async void Ask(ViewModels.Repository repo, List<Models.Change> changes)
        {
            var services = repo.GetPreferredOpenAIServices();
            if (services.Count == 1)
            {
                Show(repo, services[0], changes);
                return;
            }
            var buttons = services.Select(s => s.Name).Append(Popups.ButtonText("Cancel")).ToList();
            var chosen = await Dialogs.Alert(App.MainWindow, App.Text("AIAssistant"), "", buttons);
            if (chosen >= 0 && chosen < services.Count)
                Show(repo, services[chosen], changes);
        }

        public static void Show(ViewModels.Repository repo, AI.Service service, List<Models.Change> changes)
        {
            var vm = new ViewModels.AIAssistant(repo, service, changes);
            var title = App.Text("AIAssistant");
            var sheet = Shown = new Window(App.MainWindow, title, 520, 400);
            sheet.Closed += () =>
            {
                Shown = null;
                vm.Cancel();
            };

            var column = new Column { Padding = Spacing.Lg, Gap = Spacing.Md, AlignItems = Align.Stretch };
            column.Grow = 1;
            column.Add(new Text(title) { Style = TextStyle.Headline });
            var answer = new TextArea { ReadOnly = true, FollowsEnd = true, AccessibleName = title };
            answer.Grow = 1;
            answer.MinHeight = 0;
            Bind.OneWay(vm, nameof(vm.Text), () => vm.Text, text => answer.Value = text ?? "", owner: answer);
            column.Add(answer);

            var buttons = new Row { AlignItems = Align.Center, Gap = Spacing.Sm };
            buttons.Add(new Text(Popups.ButtonText("AIAssistant.Model")));
            var model = new Select(App.Text("AIAssistant.Model")) { MinWidth = 0 };
            model.Grow = 1;
            Bind.OneWay(vm, nameof(vm.AvailableModels), () => vm.AvailableModels, models =>
            {
                model.Options = models ?? [];
                model.SelectedIndex = System.Math.Max(0, (models ?? []).IndexOf(vm.CurrentModel));
            }, owner: model);
            model.Changed += i =>
            {
                if (vm.AvailableModels is { } models && i < models.Count && models[i] != vm.CurrentModel)
                {
                    vm.CurrentModel = models[i];
                    _ = vm.GenAsync();
                }
            };
            buttons.Add(model);
            var spinner = new Spinner();
            buttons.Add(spinner);
            var use = new Button(Popups.ButtonText("AIAssistant.Use")) { Role = ButtonRole.Default };
            use.Click += () =>
            {
                if (!string.IsNullOrEmpty(vm.Response))
                    vm.Use(vm.Response);
                sheet.Destroy();
            };
            buttons.Add(use);
            var again = new Button(Popups.ButtonText("AIAssistant.Regen"));
            again.Click += () => _ = vm.GenAsync();
            buttons.Add(again);
            Bind.OneWay(vm, nameof(vm.IsGenerating), () => vm.IsGenerating, busy =>
            {
                spinner.IsVisible = busy;
                again.IsEnabled = !busy;
            }, owner: buttons);
            Bind.OneWay(vm, nameof(vm.Response), () => vm.Response, response => use.IsEnabled = !string.IsNullOrEmpty(response), owner: use);
            column.Add(buttons);
            sheet.Content = column;
            _ = vm.GenAsync();
        }
    }
}
