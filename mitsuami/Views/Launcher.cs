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

            var window = new Window(vm.Title, 1100, 720) { Content = root };
            Bind.OneWay(vm, nameof(vm.Title), () => vm.Title, title => window.Title = title);
            // Upstream closes the repositories when the window goes, which
            // saves each one's UI state.
            window.Closed += vm.CloseAll;

            // The titles follow the pages and their names.
            void ShowTitles()
            {
                tabs.Titles = vm.Pages.Select(Title).ToArray();
                tabs.SelectedIndex = vm.Pages.IndexOf(vm.ActivePage);
            }
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
            }
            void NodeRenamed(object sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(ViewModels.RepositoryNode.Name))
                    ShowTitles();
            }

            Bind.OneWay(vm, nameof(vm.ActivePage), () => vm.ActivePage, page =>
            {
                tabs.SelectedIndex = vm.Pages.IndexOf(page);
                ShowPage(content, page);
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
