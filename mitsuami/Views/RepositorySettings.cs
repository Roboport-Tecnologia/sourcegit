using System.Linq;

using Mitsuamia;

using static SourceGit.Views.Settings;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's repository settings (<c>Views/RepositoryConfigure.axaml</c>),
    /// a sheet on the main window where upstream has a dialog, from the
    /// rail's settings button: the repository's git settings, commit
    /// message templates, issue trackers, custom actions and AI service.
    /// Read-only for now (<see cref="Settings.Editable"/>): upstream saves on
    /// closing (<c>SaveAsync</c>), which isn't called.
    /// </summary>
    public static class RepositorySettings
    {
        /// <summary>The sheet showing, if any: for captures.</summary>
        public static Window Shown { get; private set; }

        public static void Show(ViewModels.Repository repo)
        {
            var vm = new ViewModels.RepositoryConfigure(repo);
            var title = App.Text("Configure");
            var sheet = Shown = new Window(App.MainWindow, title, 720);
            sheet.Closed += () => Shown = null;

            var column = new Column { Padding = 16, Gap = 12, AlignItems = Align.Stretch };
            column.Add(new Text(title) { Style = TextStyle.Headline });
            column.Add(Tabs(
                ("Configure.Git", Git(vm)),
                ("Configure.CommitMessageTemplate", ListDetail(vm.CommitTemplates, t => t.Name, "Code", Template)),
                ("Configure.IssueTracker", ListDetail(vm.IssueTrackers, t => t.Name, "Issue", Tracker, add: AddTracker())),
                ("Configure.CustomAction", ListDetail(vm.CustomActions, a => a.Name, "Action", CustomAction, moves: true)),
                ("Configure.OpenAI", Ai(vm))));
            var buttons = new Row { Justify = Justify.End };
            var close = new Button(Popups.ButtonText("Close")) { Role = ButtonRole.Default };
            close.Click += sheet.Destroy;
            buttons.Add(close);
            column.Add(buttons);
            sheet.Content = column;
        }

        private static Node Git(ViewModels.RepositoryConfigure vm)
        {
            var page = Page();
            page.Add(Field(App.Text("Configure.User"), TextValue(vm.UserName, App.Text("Configure.User.Placeholder"))));
            page.Add(Field(App.Text("Configure.Email"), TextValue(vm.UserEmail, App.Text("Configure.Email.Placeholder"))));
            page.Add(Field(App.Text("Configure.Git.DefaultRemote"),
                Choice(App.Text("Configure.Git.DefaultRemote"), vm.Remotes, vm.Remotes.IndexOf(vm.DefaultRemote))));
            page.Add(Field(App.Text("Configure.Git.PreferredMergeMode"), Choice(App.Text("Configure.Git.PreferredMergeMode"),
                Models.MergeMode.Supported.Select(m => $"{m.Name}  ({m.Desc})"), vm.PreferredMergeMode)));
            page.Add(Field(App.Text("Configure.Proxy"), TextValue(vm.HttpProxy, App.Text("Configure.Proxy.Placeholder"))));
            page.Add(Field(App.Text("Preferences.GPG.UserKey"), TextValue(vm.GPGUserSigningKey, App.Text("Preferences.GPG.UserKey.Placeholder"))));
            page.Add(Field(App.Text("Configure.Git.ConventionalTypesOverride"), Path(vm.ConventionalTypesOverride)));
            page.Add(Field("", Check(App.Text("Preferences.GPG.CommitEnabled"), vm.GPGCommitSigningEnabled, "commit.gpgsign=true")));
            page.Add(Field("", Check(App.Text("Preferences.GPG.TagEnabled"), vm.GPGTagSigningEnabled, "tag.gpgsign=true")));
            page.Add(Field("", Check(App.Text("Preferences.Git.EnablePruneOnFetch"), vm.EnablePruneOnFetch, "fetch.prune=true")));
            page.Add(Field("", Check(App.Text("Configure.Git.EnableRecursiveWhenAutoUpdatingSubmodules"), vm.EnableRecursiveWhenAutoUpdatingSubmodules)));
            page.Add(Field("", Check(App.Text("Configure.Git.AskBeforeAutoUpdatingSubmodules"), vm.AskBeforeAutoUpdatingSubmodules)));
            return page;
        }

        /// <summary>Upstream's template form: its name and its text, which
        /// upstream edits in its commit message box.</summary>
        private static Node Template(Models.CommitTemplate template)
        {
            var column = new Column { Gap = 12, AlignItems = Align.Stretch };
            column.Add(Stacked(App.Text("Configure.CommitMessageTemplate.Name"), TextValue(template.Name)));
            column.Add(Stacked(App.Text("Configure.CommitMessageTemplate.Content"),
                WithTip(TextBlock(template.Content, 10), App.Text("Configure.CommitMessageTemplate.BuiltinVars"))));
            return column;
        }

        /// <summary>Upstream's issue tracker form: its name, the pattern
        /// (marked when it's not a valid one), the link it makes, and
        /// whether it's shared through the repository's files.</summary>
        private static Node Tracker(Models.IssueTracker tracker)
        {
            var column = new Column { Gap = 12, AlignItems = Align.Stretch };
            column.Add(Stacked(App.Text("Configure.IssueTracker.RuleName"), TextValue(tracker.Name)));
            Node regex = TextValue(tracker.RegexString);
            if (!tracker.IsRegexValid)
            {
                var row = new Row { Gap = 6, AlignItems = Align.Center };
                regex.Grow = 1;
                row.Add(regex);
                row.Add(new Icon(Icons.Get("Error")) { Size = 12, Color = Color.Error });
                regex = row;
            }
            column.Add(Stacked(App.Text("Configure.IssueTracker.Regex"), regex));
            var url = Stacked(App.Text("Configure.IssueTracker.URLTemplate"), TextValue(tracker.URLTemplate));
            url.Add(new Text(App.Text("Configure.IssueTracker.URLTemplate.Tip")) { Style = TextStyle.Caption, Color = Color.SecondaryLabel });
            column.Add(url);
            column.Add(Check(App.Text("Configure.IssueTracker.Share"), tracker.IsShared));
            return column;
        }

        /// <summary>Upstream's add button for trackers: a menu of a new rule
        /// and samples for the common hosts.</summary>
        private static Node AddTracker() =>
            new MenuButton(App.Text("Configure.IssueTracker.NewRule"))
            {
                Icon = Icons.Get("Plus", 14),
                IconOnly = true,
                Style = ButtonStyle.Borderless,
                IsEnabled = Editable,
            };

        private static Node Ai(ViewModels.RepositoryConfigure vm)
        {
            var page = Page();
            var services = vm.AvailableOpenAIServices;
            page.Add(Field(App.Text("Configure.OpenAI.Preferred"),
                Choice(App.Text("Configure.OpenAI.Preferred"), services, services.IndexOf(vm.PreferredOpenAIService))));
            page.Add(Field("", new Text(App.Text("Configure.OpenAI.Preferred.Tip")) { Style = TextStyle.Caption, Color = Color.SecondaryLabel }));
            return page;
        }
    }
}
