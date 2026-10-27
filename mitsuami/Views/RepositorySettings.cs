using System;
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
    /// Each field sets its setting as it changes; closing saves them, as
    /// upstream's <c>SaveAsync</c> on closing.
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
            Window Over() => sheet;
            // Its Close button destroys it, which doesn't say it closed.
            var saved = false;
            async void Save()
            {
                if (saved)
                    return;
                saved = true;
                Shown = null;
                await vm.SaveAsync();
            }
            sheet.Closed += Save;

            var column = new Column { Padding = Spacing.Lg, Gap = Spacing.Md, AlignItems = Align.Stretch };
            column.Add(new Text(title) { Style = TextStyle.Headline });
            column.Add(Tabs(
                ("Configure.Git", Git(vm, Over)),
                ("Configure.CommitMessageTemplate", ListDetail(vm.CommitTemplates, t => t.Name, nameof(Models.CommitTemplate.Name), "Code", Template,
                    new ListActions<Models.CommitTemplate>
                    {
                        Add = () =>
                        {
                            vm.AddCommitTemplate();
                            return vm.SelectedCommitTemplate;
                        },
                        Remove = t =>
                        {
                            vm.SelectedCommitTemplate = t;
                            vm.RemoveSelectedCommitTemplate();
                        },
                    })),
                ("Configure.IssueTracker", ListDetail(vm.IssueTrackers, t => t.Name, nameof(Models.IssueTracker.Name), "Issue", Tracker,
                    new ListActions<Models.IssueTracker>
                    {
                        AddButton = pick => AddTracker(vm, pick),
                        Remove = t =>
                        {
                            vm.SelectedIssueTracker = t;
                            vm.RemoveIssueTracker();
                        },
                    })),
                ("Configure.CustomAction", ListDetail(vm.CustomActions, a => a.Name, nameof(Models.CustomAction.Name), "Action",
                    a => CustomAction(Over, a), new ListActions<Models.CustomAction>
                    {
                        Add = () =>
                        {
                            vm.AddNewCustomAction();
                            return vm.SelectedCustomAction;
                        },
                        Remove = a =>
                        {
                            vm.SelectedCustomAction = a;
                            vm.RemoveSelectedCustomAction();
                        },
                        Move = vm.CustomActions.Move,
                    })),
                ("Configure.OpenAI", Ai(vm))));
            var buttons = new Row { Justify = Justify.End };
            var close = new Button(Popups.ButtonText("Close")) { Role = ButtonRole.Default };
            close.Click += () =>
            {
                Save();
                sheet.Destroy();
            };
            buttons.Add(close);
            column.Add(buttons);
            sheet.Content = column;
        }

        private static Node Git(ViewModels.RepositoryConfigure vm, Func<Window> over)
        {
            var page = Page();
            page.Add(Field(App.Text("Configure.User"), TextValue(vm.UserName, v => vm.UserName = v, App.Text("Configure.User.Placeholder"))));
            page.Add(Field(App.Text("Configure.Email"), TextValue(vm.UserEmail, v => vm.UserEmail = v, App.Text("Configure.Email.Placeholder"))));
            page.Add(Field(App.Text("Configure.Git.DefaultRemote"),
                Choice(App.Text("Configure.Git.DefaultRemote"), vm.Remotes, vm.Remotes.IndexOf(vm.DefaultRemote), i => vm.DefaultRemote = vm.Remotes[i])));
            page.Add(Field(App.Text("Configure.Git.PreferredMergeMode"), Choice(App.Text("Configure.Git.PreferredMergeMode"),
                Models.MergeMode.Supported.Select(m => $"{m.Name}  ({m.Desc})"), vm.PreferredMergeMode, i => vm.PreferredMergeMode = i)));
            page.Add(Field(App.Text("Configure.Proxy"), TextValue(vm.HttpProxy, v => vm.HttpProxy = v, App.Text("Configure.Proxy.Placeholder"))));
            page.Add(Field(App.Text("Preferences.GPG.UserKey"), TextValue(vm.GPGUserSigningKey, v => vm.GPGUserSigningKey = v, App.Text("Preferences.GPG.UserKey.Placeholder"))));
            page.Add(Field(App.Text("Configure.Git.ConventionalTypesOverride"),
                Path(over, vm.ConventionalTypesOverride, v => vm.ConventionalTypesOverride = v, title: "Conventional Commit Types")));
            page.Add(Field("", Check(App.Text("Preferences.GPG.CommitEnabled"), vm.GPGCommitSigningEnabled, v => vm.GPGCommitSigningEnabled = v, "commit.gpgsign=true")));
            page.Add(Field("", Check(App.Text("Preferences.GPG.TagEnabled"), vm.GPGTagSigningEnabled, v => vm.GPGTagSigningEnabled = v, "tag.gpgsign=true")));
            page.Add(Field("", Check(App.Text("Preferences.Git.EnablePruneOnFetch"), vm.EnablePruneOnFetch, v => vm.EnablePruneOnFetch = v, "fetch.prune=true")));
            page.Add(Field("", Check(App.Text("Configure.Git.EnableRecursiveWhenAutoUpdatingSubmodules"), vm.EnableRecursiveWhenAutoUpdatingSubmodules,
                v => vm.EnableRecursiveWhenAutoUpdatingSubmodules = v)));
            page.Add(Field("", Check(App.Text("Configure.Git.AskBeforeAutoUpdatingSubmodules"), vm.AskBeforeAutoUpdatingSubmodules,
                v => vm.AskBeforeAutoUpdatingSubmodules = v)));
            return page;
        }

        /// <summary>Upstream's template form: its name and its text, which
        /// upstream edits in its commit message box.</summary>
        private static Node Template(Models.CommitTemplate template)
        {
            var column = new Column { Gap = Spacing.Md, AlignItems = Align.Stretch };
            column.Add(Stacked(App.Text("Configure.CommitMessageTemplate.Name"), TextValue(template.Name, v => template.Name = v)));
            column.Add(Stacked(App.Text("Configure.CommitMessageTemplate.Content"),
                WithTip(TextBlock(template.Content, 10, v => template.Content = v), App.Text("Configure.CommitMessageTemplate.BuiltinVars"))));
            return column;
        }

        /// <summary>Upstream's issue tracker form: its name, the pattern
        /// (marked while it's not a valid one), the link it makes, and
        /// whether it's shared through the repository's files.</summary>
        private static Node Tracker(Models.IssueTracker tracker)
        {
            var column = new Column { Gap = Spacing.Md, AlignItems = Align.Stretch };
            column.Add(Stacked(App.Text("Configure.IssueTracker.RuleName"), TextValue(tracker.Name, v => tracker.Name = v)));
            var regex = new Row { Gap = Spacing.Sm, AlignItems = Align.Center };
            var input = TextValue(tracker.RegexString, v => tracker.RegexString = v);
            input.Grow = 1;
            regex.Add(input);
            var invalid = new Icon(Icons.Get("Error")) { Size = 12, Color = Color.Error, IsVisible = !tracker.IsRegexValid };
            regex.Add(invalid);
            Bind.OneWay(tracker, nameof(Models.IssueTracker.IsRegexValid), () => tracker.IsRegexValid, valid => invalid.IsVisible = !valid, owner: regex);
            column.Add(Stacked(App.Text("Configure.IssueTracker.Regex"), regex));
            var url = Stacked(App.Text("Configure.IssueTracker.URLTemplate"), TextValue(tracker.URLTemplate, v => tracker.URLTemplate = v));
            url.Add(new Text(App.Text("Configure.IssueTracker.URLTemplate.Tip")) { Style = TextStyle.Caption, Color = Color.SecondaryLabel });
            column.Add(url);
            column.Add(Check(App.Text("Configure.IssueTracker.Share"), tracker.IsShared, v => tracker.IsShared = v));
            return column;
        }

        /// <summary>Upstream's add button for trackers: a menu of a new rule
        /// and samples for the common hosts, linked to this repository's
        /// remote where one is on that host.</summary>
        private static Node AddTracker(ViewModels.RepositoryConfigure vm, Action<Models.IssueTracker> pick)
        {
            void Add(string name, string regex, string url)
            {
                vm.AddIssueTracker(name, regex, url);
                pick(vm.SelectedIssueTracker);
            }
            string Remote(string host, string suffix, string fallback)
            {
                foreach (var remote in vm.GetRemoteVisitUrls())
                {
                    if (host == null || remote.Contains(host, StringComparison.Ordinal))
                        return remote + suffix;
                }
                return fallback;
            }
            return new MenuButton(App.Text("Configure.IssueTracker.NewRule"))
            {
                Icon = Icons.Get("Plus", 14),
                IconOnly = true,
                Style = ButtonStyle.Borderless,
                Menu =
                [
                    new MenuItem(App.Text("Configure.IssueTracker.NewRule"), () => Add("New Issue Tracker", @"#(\d+)", "https://xxx/$1")),
                    MenuSeparator.Instance,
                    new MenuItem(App.Text("Configure.IssueTracker.AddSampleGitHub"), () =>
                        Add("GitHub Issue", @"#(\d+)", Remote("github.com", "/issues/$1", "https://github.com/username/repository/issues/$1"))),
                    new MenuItem(App.Text("Configure.IssueTracker.AddSampleJira"), () =>
                        Add("Jira Tracker", @"PROJ-(\d+)", "https://jira.yourcompany.com/browse/PROJ-$1")),
                    new MenuItem(App.Text("Configure.IssueTracker.AddSampleAzure"), () =>
                        Add("Azure DevOps Tracker", @"#(\d+)", "https://dev.azure.com/yourcompany/workspace/_workitems/edit/$1")),
                    new MenuItem(App.Text("Configure.IssueTracker.AddSampleGitLabIssue"), () =>
                        Add("GitLab Issue", @"#(\d+)", Remote(null, "/-/issues/$1", "https://gitlab.com/username/repository/-/issues/$1"))),
                    new MenuItem(App.Text("Configure.IssueTracker.AddSampleGitLabMergeRequest"), () =>
                        Add("GitLab MR", @"!(\d+)", Remote(null, "/-/merge_requests/$1", "https://gitlab.com/username/repository/-/merge_requests/$1"))),
                    new MenuItem(App.Text("Configure.IssueTracker.AddSampleGiteeIssue"), () =>
                        Add("Gitee Issue", @"#([0-9A-Z]{6,10})", Remote("gitee.com", "/issues/$1", "https://gitee.com/username/repository/issues/$1"))),
                    new MenuItem(App.Text("Configure.IssueTracker.AddSampleGiteePullRequest"), () =>
                        Add("Gitee Pull Request", @"!(\d+)", Remote("gitee.com", "/pulls/$1", "https://gitee.com/username/repository/pulls/$1"))),
                    new MenuItem(App.Text("Configure.IssueTracker.AddSampleGerritChangeIdCommit"), () =>
                        Add("Gerrit Change-Id", @"(I[A-Za-z0-9]{40})", "https://gerrit.yourcompany.com/q/$1")),
                ],
            };
        }

        private static Node Ai(ViewModels.RepositoryConfigure vm)
        {
            var page = Page();
            var services = vm.AvailableOpenAIServices;
            page.Add(Field(App.Text("Configure.OpenAI.Preferred"),
                Choice(App.Text("Configure.OpenAI.Preferred"), services, services.IndexOf(vm.PreferredOpenAIService), i => vm.PreferredOpenAIService = services[i])));
            page.Add(Field("", new Text(App.Text("Configure.OpenAI.Preferred.Tip")) { Style = TextStyle.Caption, Color = Color.SecondaryLabel }));
            return page;
        }
    }
}
