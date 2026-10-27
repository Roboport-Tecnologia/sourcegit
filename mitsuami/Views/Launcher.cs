using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// The main window, upstream's <c>Views/Launcher.axaml</c>: a tab per
    /// page (the welcome page, or an open repository), as the platform's
    /// document tabs, and the chosen page under them.
    /// </summary>
    public static class Launcher
    {
        public static Window Create(ViewModels.Launcher vm)
        {
            // In the title's row, as Safari's compact tabs: upstream's tab bar
            // shares its row with the window's buttons too.
            var tabs = new DocumentTabs
            {
                Closable = true,
                Reorderable = true,
                NewTabButton = true,
                Style = DocumentTabsStyle.TitleRow,
            };
            var content = new Column { AlignItems = Align.Stretch };
            content.Grow = 1;

            // Fills the window, so the page's list has room to grow into: a
            // list asks for no height of its own.
            var root = new Column { AlignItems = Align.Stretch };
            root.Grow = 1;
            root.Add(tabs);
            root.Add(content);

            // Upstream's size, as it last had it (1280 by 720 at first). On
            // Windows at least as wide as the local changes need: XAML's
            // check boxes are at least 120 wide, and the commit options
            // pushed Commit & Push and the toolbar's end out of a narrower
            // window.
            var layout = ViewModels.Preferences.Instance.Layout;
            var width = OperatingSystem.IsWindows() ? Math.Max(layout.LauncherWidth, 1320) : layout.LauncherWidth;
            var window = new Window(WindowTitle(), (float)width, (float)layout.LauncherHeight) { Content = root };
            // Upstream closes the repositories when the window goes, which
            // saves each one's UI state, then saves the preferences (the
            // tabs open, the workspaces).
            window.Closed += () =>
            {
                vm.CloseAll();
                ViewModels.Preferences.Instance.Save();
            };

            // The titles follow the pages and their names.
            void ShowTitles()
            {
                tabs.Titles = vm.Pages.Select(Title).ToArray();
                tabs.SelectedIndex = vm.Pages.IndexOf(vm.ActivePage);
                window.Title = WindowTitle();
            }
            // Upstream's StringConverters.ToWindowTitle: the page and the
            // workspace it's in.
            string WindowTitle()
            {
                var page = vm.ActivePage?.Node.Name;
                var workspace = vm.ActiveWorkspace?.Name;
                return $"{(string.IsNullOrEmpty(page) ? "Repositories" : page)} - {(string.IsNullOrEmpty(workspace) ? "Unnamed" : workspace)}";
            }
            void WorkspaceRenamed(object sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(ViewModels.Workspace.Name))
                    window.Title = WindowTitle();
            }
            var workspace = vm.ActiveWorkspace;
            if (workspace != null)
                workspace.PropertyChanged += WorkspaceRenamed;
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(vm.ActiveWorkspace))
                    return;
                if (workspace != null)
                    workspace.PropertyChanged -= WorkspaceRenamed;
                workspace = vm.ActiveWorkspace;
                if (workspace != null)
                    workspace.PropertyChanged += WorkspaceRenamed;
                window.Title = WindowTitle();
            };
            foreach (var page in vm.Pages)
                Watch(page);
            vm.Pages.CollectionChanged += (_, e) =>
            {
                foreach (var page in e.NewItems?.OfType<ViewModels.LauncherPage>() ?? [])
                    Watch(page);
                ShowTitles();
            };
            void Watch(ViewModels.LauncherPage page)
            {
                page.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName is nameof(page.Node) or nameof(page.Data))
                        ShowTitles();
                    if (page == vm.ActivePage && e.PropertyName == nameof(page.Data))
                        ShowPage(content, page);
                };
                page.Node.PropertyChanged += NodeRenamed;
                // Posted: the list can't change while it tells of a change.
                page.Notifications.CollectionChanged += (_, _) =>
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => Notifications.Show(window, vm, page));
            }
            void NodeRenamed(object sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(ViewModels.RepositoryNode.Name))
                    ShowTitles();
            }

            Bind.OneWay(vm, nameof(vm.ActivePage), () => vm.ActivePage, page =>
            {
                tabs.SelectedIndex = vm.Pages.IndexOf(page);
                window.Title = WindowTitle();
                ShowPage(content, page);
                if (page != null)
                    Notifications.Show(window, vm, page);
            });
            ShowTitles();

            tabs.Chosen += index => vm.ActivePage = vm.Pages[index];
            tabs.CloseRequested += index => vm.CloseTab(vm.Pages[index]);
            // The platform shows the tab moved already; the pages follow.
            tabs.Moved += (from, to) => vm.MoveTab(vm.Pages[from], vm.Pages[to]);
            tabs.NewTabRequested += vm.AddNewTab;

            Popups.Attach(window, vm);
            return window;
        }

        /// <summary>Upstream's tab titles: the repository's name, or the
        /// welcome page's own.</summary>
        private static string Title(ViewModels.LauncherPage page) =>
            page.Node.IsRepository ? page.Node.Name : App.Text("PageTabBar.Welcome.Title");

        private static void ShowPage(Mitsuamia.Container content, ViewModels.LauncherPage page)
        {
            foreach (var child in content.Children.ToArray())
                child.Destroy();
            if (page == null)
                return;

            switch (page.Data)
            {
                case ViewModels.Welcome welcome:
                    content.Add(Welcome.Create(welcome));
                    break;
                case ViewModels.Repository repo:
                    content.Add(Repository.Create(repo));
                    break;
            }
        }
    }
}
