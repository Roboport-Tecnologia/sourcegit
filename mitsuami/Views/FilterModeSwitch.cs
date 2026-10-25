using System;
using System.Collections.Generic;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's <c>FilterModeSwitchButton</c> at a branch's or tag's row's
    /// end: a borderless pull-down whose icon says whether the history shows
    /// only it (a filter), hides it (a closed eye) or neither (an eye), with
    /// upstream's menu to change that. Shown while the pointer is over the
    /// row or a filter is set, as upstream's.
    /// </summary>
    public static class FilterModeSwitch
    {
        /// <param name="source">What has the mode (a branch tree's node, a
        /// tag's item), which upstream changes in place.</param>
        public static Node Create(Container row, System.ComponentModel.INotifyPropertyChanged source, Func<Models.FilterMode> mode,
            Action<Models.FilterMode> set)
        {
            var button = new MenuButton(App.Text("Repository.FilterCommits"))
            {
                IconOnly = true,
                Style = ButtonStyle.Borderless,
                ToolTip = App.Text("Repository.FilterCommits"),
                MenuOnOpen = () => Menu(mode(), set),
            };
            button.Shrink = 0;
            var over = false;
            void Show()
            {
                var now = mode();
                button.Icon = Icons.Get(now switch
                {
                    Models.FilterMode.Included => "Filter",
                    Models.FilterMode.Excluded => "EyeClose",
                    _ => "Eye",
                }, 12);
                button.IsVisible = over || now != Models.FilterMode.None;
            }
            row.Hover += o =>
            {
                over = o;
                Show();
            };
            if (source != null)
                Bind.OneWay(source, "FilterMode", mode, _ => Show(), owner: button);
            else
                Show();
            return button;
        }

        private static IReadOnlyList<MenuEntry> Menu(Models.FilterMode current, Action<Models.FilterMode> set)
        {
            var menu = new List<MenuEntry>();
            if (current != Models.FilterMode.None)
            {
                menu.Add(new MenuItem(App.Text("Repository.FilterCommits.Default"), () => set(Models.FilterMode.None)));
                menu.Add(MenuSeparator.Instance);
            }
            menu.Add(new MenuItem(App.Text("Repository.FilterCommits.Include"), () => set(Models.FilterMode.Included))
            { Enabled = current != Models.FilterMode.Included });
            menu.Add(new MenuItem(App.Text("Repository.FilterCommits.Exclude"), () => set(Models.FilterMode.Excluded))
            { Enabled = current != Models.FilterMode.Excluded });
            return menu;
        }
    }
}
