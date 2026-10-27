using System;
using INotifyPropertyChanged = System.ComponentModel.INotifyPropertyChanged;
using PropertyChangedEventArgs = System.ComponentModel.PropertyChangedEventArgs;
using System.Linq;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's details under the history (<c>Views/Histories.axaml</c>'s
    /// bottom area, <c>CommitDetail.axaml</c>, <c>CommitBaseInfo.axaml</c>):
    /// the platform's tabs, Information, Changes and Files. Information
    /// shows what the selection is: a tip with none, the commit with one,
    /// how many with more. Changes lists the commit's changes
    /// (<see cref="CommitChanges"/>), Files its files
    /// (<see cref="RevisionFiles"/>); diffs and text wait for the code view
    /// (bemtvi).
    /// </summary>
    public static class CommitDetail
    {
        public static Node Create(ViewModels.Histories vm) =>
            Create(vm, nameof(vm.DetailContext), () => vm.DetailContext, () => vm.HasSingleRemote, tips: true);

        /// <summary>The details of what <paramref name="context"/> gives: a
        /// commit's view model, a count, or nothing (then the history's tips,
        /// with <paramref name="tips"/>, or upstream's icon alone).</summary>
        public static Node Create(INotifyPropertyChanged source, string property, Func<object> context, Func<bool> hasSingleRemote,
            bool tips)
        {
            var tabs = new Tabs();
            var info = new ScrollView();
            info.Content.Padding = new Thickness(Spacing.None, Spacing.Md, Spacing.Sm, Spacing.Xs);
            // A tab's page is a container, here filled by the scroll view.
            info.Grow = 1;
            info.MinHeight = 0;
            var infoPage = new Column { AlignItems = Align.Stretch };
            infoPage.Add(info);
            tabs.Add(App.Text("CommitDetail.Info"), infoPage);
            var changesPage = Page();
            tabs.Add(App.Text("CommitDetail.Changes"), changesPage);
            var filesPage = Page();
            tabs.Add(App.Text("CommitDetail.Files"), filesPage);

            // The commit shown follows its view model's changes; another
            // selection brings another view model.
            ViewModels.CommitDetail shown = null;
            void Changed(object sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName is nameof(shown.Commit) or nameof(shown.FullMessage) or nameof(shown.Changes) or nameof(shown.SignInfo))
                    Show(info.Content, hasSingleRemote, shown, tips);
                else if (e.PropertyName == nameof(shown.ActiveTabIndex))
                    tabs.SelectedIndex = shown.ActiveTabIndex;
            }
            tabs.Changed += index =>
            {
                if (shown != null)
                    shown.ActiveTabIndex = index;
            };
            Bind.OneWay(source, property, context, context =>
            {
                if (shown != null)
                    shown.PropertyChanged -= Changed;
                var changed = !ReferenceEquals(shown, context);
                shown = context as ViewModels.CommitDetail;
                // A commit's Changes page is built for its view model.
                if (changed)
                {
                    Fill(changesPage, shown == null ? null : CommitChanges.Create(shown));
                    Fill(filesPage, shown == null ? null : RevisionFiles.Create(shown));
                }
                if (shown != null)
                {
                    shown.PropertyChanged += Changed;
                    tabs.SelectedIndex = shown.ActiveTabIndex;
                }
                else
                {
                    tabs.SelectedIndex = 0;
                }
                Show(info.Content, hasSingleRemote, context, tips);
            }, owner: tabs);
            tabs.Forgotten += () =>
            {
                if (shown != null)
                    shown.PropertyChanged -= Changed;
            };

            // Two commits compared show in place of the tabs, as upstream's.
            var details = new Column { AlignItems = Align.Stretch };
            tabs.Grow = 1;
            tabs.MinHeight = 0;
            details.Add(tabs);
            var compare = Page();
            details.Add(compare);
            ViewModels.RevisionCompare compared = null;
            Bind.OneWay(source, property, context, context =>
            {
                var now = context as ViewModels.RevisionCompare;
                tabs.IsVisible = now == null;
                compare.IsVisible = now != null;
                if (!ReferenceEquals(now, compared))
                    Fill(compare, now == null ? null : RevisionCompare.Create(now));
                compared = now;
            }, owner: details);
            return details;
        }

        /// <summary>A tab's page, which a page of the commit fills.</summary>
        private static Column Page()
        {
            var page = new Column { AlignItems = Align.Stretch };
            page.Grow = 1;
            return page;
        }

        private static void Fill(Column page, Node content)
        {
            foreach (var child in page.Children.ToArray())
                child.Destroy();
            if (content != null)
                page.Add(content);
        }


        /// <summary>Fills the Information page for what's selected.</summary>
        private static void Show(Container page, Func<bool> hasSingleRemote, object context, bool tips)
        {
            foreach (var child in page.Children.ToArray())
                child.Destroy();

            switch (context)
            {
                case ViewModels.CommitDetail { Commit: { } commit } detail:
                    BaseInfo(page, hasSingleRemote, detail, commit);
                    Changes(page, detail);
                    break;
                case Models.Count count:
                    page.Add(Placeholder(App.Text("Histories.Selected", count.Value)));
                    break;
                default:
                    var hint = OperatingSystem.IsMacOS() ? "Histories.Tips.MacOS" : "Histories.Tips";
                    page.Add(Placeholder(tips ? $"{App.Text("Histories.Tips.Prefix")} {App.Text(hint)}" : null));
                    break;
            }
        }

        /// <summary>Upstream's empty page: its 128-point detail icon over a
        /// line in the secondary colour.</summary>
        private static Node Placeholder(string text)
        {
            var column = new Column { AlignItems = Align.Center, Gap = Spacing.Lg, Padding = new Thickness(Spacing.Xl, Spacing.None) };
            column.Add(new Icon(Icons.Get("Detail")) { Size = 128, Color = Color.SecondaryLabel });
            if (text != null)
                column.Add(new Text(text) { Color = Color.SecondaryLabel, Weight = FontWeight.Bold });
            return column;
        }

        /// <summary>
        /// Upstream's <c>CommitBaseInfo</c>: the author and the committer
        /// (when they differ) side by side, each with their avatar in a
        /// 96-point column, a line, then the SHA with its buttons, the
        /// parents, the refs and the message, labelled in that column.
        /// </summary>
        private static void BaseInfo(Container page, Func<bool> hasSingleRemote, ViewModels.CommitDetail detail, Models.Commit commit)
        {
            var people = new Row { AlignItems = Align.Start, Padding = new Thickness(Spacing.Sm, Spacing.None) };
            people.Add(Person(App.Text("CommitDetail.Info.Author"), commit.Author, commit.AuthorTime));
            if (commit.IsCommitterVisible)
                people.Add(Person(App.Text("CommitDetail.Info.Committer"), commit.Committer, commit.CommitterTime));
            page.Add(people);
            page.Add(new Separator { Margin = new Thickness(Spacing.Sm, Spacing.None, Spacing.Sm, Spacing.Sm) });

            page.Add(Field(App.Text("CommitDetail.Info.SHA"), ShaRow(detail, commit), center: true));

            if (commit.Parents.Count > 0)
            {
                var parents = new Row { AlignItems = Align.Center, Gap = Spacing.Lg };
                foreach (var parent in commit.Parents)
                {
                    // Upstream's links to the parents, in its dark orange.
                    var link = new Row();
                    link.Add(new Text(parent.Length > 10 ? parent[..10] : parent) { Style = TextStyle.Monospace, Color = Color.Rgba(255, 140, 0) });
                    link.Clicked += () => detail.NavigateTo(parent);
                    link.ToolTip = parent;
                    link.ContextMenu = [new MenuItem(App.Text("Copy"), () => Clipboard.SetText(parent))];
                    ParentTip(link, detail, parent);
                    parents.Add(link);
                }
                page.Add(Field(App.Text("CommitDetail.Info.Parents"), parents));
            }

            if (commit.HasDecorators)
            {
                var refs = new CommitRefs();
                refs.Show(commit, hasSingleRemote(), useGraphColor: false);
                page.Add(Field(App.Text("CommitDetail.Info.Refs"), refs.View));
            }

            // Selectable, as upstream's; the platform's text has its own
            // Copy, and Copy All Text is the menu around it.
            var full = detail.FullMessage?.Message ?? commit.Subject;
            var message = new Text(full.Trim()) { Selectable = true };
            var holder = new Column();
            holder.Add(message);
            holder.ContextMenu = [new MenuItem(App.Text("CopyAllText"), () => Clipboard.SetText(full))];
            page.Add(Field(App.Text("CommitDetail.Info.Message"), holder));
        }

        /// <summary>Upstream's tooltip on a parent: its author, date and
        /// subject, fetched when it's hovered. The platform's tooltip is
        /// text set ahead, so it's fetched as the link shows (its avatar
        /// isn't shown).</summary>
        private static async void ParentTip(Node link, ViewModels.CommitDetail detail, string sha)
        {
            var gone = false;
            link.Forgotten += () => gone = true;
            var commit = await detail.GetCommitAsync(sha);
            if (commit != null && !gone)
                link.ToolTip = $"{commit.Author.Name}  ·  {Models.DateTimeFormat.Format(commit.CommitterTime)}\n\n{commit.Subject}";
        }

        /// <summary>
        /// Upstream's SHA row: the SHA, a copy button, a button opening
        /// the commit on the remotes' sites (a menu of them when there are
        /// several) and the signature's mark, coloured by how it verified,
        /// its details in the tooltip.
        /// </summary>
        private static Node ShaRow(ViewModels.CommitDetail detail, Models.Commit commit)
        {
            var row = new Row { AlignItems = Align.Center };
            row.Add(new Text(commit.SHA) { Style = TextStyle.Monospace });
            var actions = new Row { AlignItems = Align.Center, Gap = Metrics.ButtonGap };
            row.Add(actions);
            actions.Add(CopyButton(commit.SHA));
            actions.Add(ContainsIn(detail));

            if (detail.WebLinks is { Count: > 0 } links)
            {
                var title = App.Text("CommitDetail.Info.WebLinks");
                var icon = Icons.Get("Link", 12);
                Node button;
                if (links.Count == 1)
                {
                    var open = new Button(title) { Icon = icon, IconOnly = true, Style = ButtonStyle.Borderless, IconColor = Color.Accent };
                    open.Click += () => Native.OS.OpenBrowser($"{links[0].URLPrefix}{commit.SHA}");
                    button = open;
                }
                else
                {
                    button = new MenuButton(title)
                    {
                        Icon = icon,
                        IconOnly = true,
                        Style = ButtonStyle.Borderless,
                        IconColor = Color.Accent,
                        Menu = links.Select(link => (MenuEntry)new MenuItem(link.Name, () => Native.OS.OpenBrowser($"{link.URLPrefix}{commit.SHA}"))).ToList(),
                    };
                }
                button.ToolTip = title;
                actions.Add(button);
            }

            if (detail.SignInfo is { } sign)
            {
                Color? color = sign.VerifyResult switch
                {
                    'G' or 'U' => Color.Success,
                    'X' or 'Y' or 'R' => Color.Warning,
                    'B' or 'E' => Color.Error,
                    _ => null,
                };
                if (color is { } c)
                {
                    var mark = new Row { AlignItems = Align.Center, Justify = Justify.Center };
                    mark.Width = 24;
                    mark.Add(new Icon(Icons.Get("Verified")) { Size = 14, Color = c, Label = sign.ToolTip });
                    var tip = sign.ToolTip + "\n";
                    if (sign.HasSigner)
                        tip += $"\n{App.Text("CommitDetail.Info.Signer")}  {sign.Signer}";
                    tip += $"\n{App.Text("CommitDetail.Info.Key")}  {sign.Key}";
                    mark.ToolTip = tip;
                    actions.Add(mark);
                }
            }
            return row;
        }

        /// <summary>
        /// Upstream's "Contains in" button and its flyout, the platform's
        /// popover here: its title, a line, then the branches and tags that
        /// hold the commit as chips that wrap, in 300 to 500 points, up to
        /// 200 high, then scrolling; a spinner while git looks.
        /// </summary>
        private static Node ContainsIn(ViewModels.CommitDetail detail)
        {
            var holder = new Row { AlignItems = Align.Center };
            var label = App.Text("CommitDetail.Info.ContainsIn");
            var button = new Button(label) { Icon = Icons.Get("Milestone", 14, 1), IconOnly = true, Style = ButtonStyle.Borderless, ToolTip = label };
            holder.Add(button);

            var popover = new Popover { AlignItems = Align.Stretch };
            popover.Add(new Text(App.Text("CommitDetail.Info.ContainsIn.Title")) { Color = Color.SecondaryLabel, Weight = FontWeight.Bold, AlignSelf = Align.Center });
            popover.Add(new Separator { Margin = new Thickness(Spacing.Sm, Spacing.None) });
            var scroll = new ScrollView();
            scroll.MinWidth = 300;
            scroll.MaxWidth = 500;
            scroll.MaxHeight = 200;
            var chips = new Row { Wrap = true, AlignItems = Align.Center };
            scroll.Content.Add(chips);
            popover.Add(scroll);
            var spinner = new Spinner { AlignSelf = Align.Center, Margin = new Thickness(Spacing.Sm, Spacing.None) };
            popover.Add(spinner);
            holder.Add(popover);

            button.Click += async () =>
            {
                foreach (var chip in chips.Children.ToArray())
                    chip.Destroy();
                spinner.IsVisible = true;
                scroll.IsVisible = false;
                popover.IsShown = true;
                var refs = await detail.GetRefsContainsThisCommitAsync();
                if (!popover.IsShown)
                    return;
                foreach (var d in refs)
                    chips.Add(Chip(d));
                spinner.IsVisible = false;
                scroll.IsVisible = true;
            };
            return holder;
        }

        /// <summary>A ref holding the commit: 20 points high, outlined with
        /// round ends, its icon and name.</summary>
        private static Node Chip(Models.Decorator decorator)
        {
            var line = new Row { AlignItems = Align.Center, Margin = new Thickness(Spacing.Xs, Spacing.Sm, Spacing.None, Spacing.None) };
            var chip = new Row { AlignItems = Align.Center, Gap = Spacing.Xs, Padding = new Thickness(Spacing.None, Spacing.Sm) };
            chip.Height = 20;
            chip.Add(new ChipOutline { Inset = new Thickness(0) });
            chip.Add(new Icon(Icons.Get(decorator.IsTag ? "Tag" : "Branch")) { Size = 10, Color = Color.Label });
            chip.Add(new Text(decorator.Name) { MaxLines = 1 });
            line.Add(chip);
            return line;
        }

        private sealed class ChipOutline() : DrawnWidget("RefChip", 0, 0)
        {
            protected override void Draw(Canvas canvas) =>
                canvas.StrokeRect(0.5f, 0.5f, canvas.Width - 1, canvas.Height - 1, Color.Separator, 1, (canvas.Height - 1) / 2);
        }

        /// <summary>Upstream's <c>CopyButton</c>: its copy icon, which turns
        /// into a green check for a second once it has copied. The check is
        /// a second button in its place, so the copy icon keeps the
        /// platform's colour.</summary>
        internal static Node CopyButton(string text)
        {
            var holder = new Row();
            var label = App.Text("Copy");
            var copy = new Button(label) { Icon = Icons.Get("Copy", 12), IconOnly = true, Style = ButtonStyle.Borderless, ToolTip = label };
            var check = new Button(label) { Icon = Icons.Get("Check", 14, 1), IconOnly = true, Style = ButtonStyle.Borderless, ToolTip = label, IconColor = Color.Success };
            check.IsVisible = false;
            var copies = 0;
            var gone = false;
            holder.Forgotten += () => gone = true;
            async void Copy()
            {
                Clipboard.SetText(text);
                copy.IsVisible = false;
                check.IsVisible = true;
                var mine = ++copies;
                await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(1));
                if (mine == copies && !gone)
                {
                    check.IsVisible = false;
                    copy.IsVisible = true;
                }
            }
            copy.Click += Copy;
            check.Click += Copy;
            holder.Add(copy);
            holder.Add(check);
            return holder;
        }

        /// <summary>A person's half: their 64-point avatar at the end of
        /// the 96-point column, then their name in the label colour and
        /// their email in the secondary one, then the date, smaller. Each
        /// has upstream's menu to copy it.</summary>
        private static Node Person(string title, Models.User user, ulong time)
        {
            var half = new Row { AlignItems = Align.Start };
            half.Grow = 1;
            half.Width = 0;
            var face = new Column { AlignItems = Align.End };
            face.Width = 96;
            var avatar = new Avatar(64) { User = user };
            face.Add(avatar.View);
            half.Add(face);
            var text = new Column { Margin = new Thickness(Spacing.None, Spacing.Xs, Spacing.None, Spacing.Lg) };
            text.Grow = 1;
            text.Add(new Text(title) { Color = Color.SecondaryLabel, Weight = FontWeight.Bold });
            var who = new Row { AlignItems = Align.Center, Gap = Spacing.Xs, Margin = new Thickness(Spacing.Md, Spacing.None, Spacing.Sm, Spacing.None) };
            who.ToolTip = $"{user.Name} <{user.Email}>";
            who.Add(new Text(user.Name) { MaxLines = 1 });
            who.Add(new Text(user.Email) { Color = Color.SecondaryLabel, MaxLines = 1 });
            who.ContextMenu =
            [
                new MenuItem(App.Text("CommitDetail.Info.CopyName"), () => Clipboard.SetText(user.Name)),
                new MenuItem(App.Text("CommitDetail.Info.CopyEmail"), () => Clipboard.SetText(user.Email)),
                new MenuItem(App.Text("CommitDetail.Info.CopyNameAndEmail"), () => Clipboard.SetText(user.ToString())),
            ];
            text.Add(who);
            var date = Models.DateTimeFormat.Format(time);
            var when = new Row { Margin = new Thickness(Spacing.None, Spacing.None, Spacing.None, Spacing.Xs) };
            when.Add(new Text(date) { Style = TextStyle.Caption, Color = Color.SecondaryLabel });
            when.ContextMenu = [new MenuItem(App.Text("Copy"), () => Clipboard.SetText(date))];
            text.Add(when);
            half.Add(text);
            return half;
        }

        /// <summary>A labelled value: the label right-aligned in 96 points,
        /// bold and secondary, the value 12 after it. <paramref name="center"/>
        /// centres the label on a one-line value taller than its text (the
        /// SHA's row, as high as its buttons).</summary>
        private static Node Field(string label, Node value, bool center = false)
        {
            var row = new Row { AlignItems = center ? Align.Center : Align.Start, Gap = Spacing.Md, Padding = new Thickness(Spacing.Xs, Spacing.None) };
            var name = new Column { AlignItems = Align.End };
            name.Width = 96;
            name.Add(new Text(label) { Color = Color.SecondaryLabel, Weight = FontWeight.Bold });
            row.Add(name);
            var holder = new Column { AlignItems = Align.Start };
            holder.Grow = 1;
            holder.Width = 0;
            holder.Add(value);
            row.Add(holder);
            return row;
        }

        /// <summary>
        /// Upstream's list of the commit's first 100 changes under a line:
        /// each a status mark (its letter on its colour) and the path.
        /// </summary>
        private static void Changes(Container page, ViewModels.CommitDetail detail)
        {
            if (detail.Changes is not { Count: > 0 } changes)
                return;
            page.Add(new Separator { Margin = new Thickness(Spacing.Sm, Spacing.None, Spacing.Sm, Spacing.Sm) });
            var list = new Column { Margin = new Thickness(Spacing.None, Spacing.None, Spacing.Xs, 64) };
            foreach (var change in changes.Take(100))
            {
                var row = new Row { AlignItems = Align.Center, Gap = Spacing.Sm, Padding = new Thickness(Spacing.None, Spacing.None, Spacing.None, Spacing.Lg) };
                row.ToolTip = $"{change.Path} • {change.IndexDesc}";
                row.Add(new StatusMark(change.Index).View);
                row.Add(new Text(change.Path) { MaxLines = 1 });
                list.Add(row);
            }
            page.Add(list);
            if (changes.Count > 100)
                page.Add(new Text(App.Text("CommitDetail.Info.GotoChangesPage")) { Color = Color.SecondaryLabel, Margin = new Thickness(Spacing.None, Spacing.Md, Spacing.Lg, 108) });
        }
    }

    /// <summary>
    /// Upstream's <c>ChangeStatusIcon</c>: a change's state as a letter on
    /// its colour, 14 points square. The square is drawn; the letter is the
    /// platform's text over it.
    /// </summary>
    public sealed class StatusMark
    {
        private static readonly string[] s_letters = ["?", "M", "T", "+", "−", "➜", "❏", "?", "!"];
        private static readonly (byte R, byte G, byte B)[] s_colors =
        [
            (0, 0, 0),
            (218, 165, 32),
            (218, 165, 32),
            (50, 205, 50),
            (255, 99, 71),
            (218, 112, 214),
            (218, 165, 32),
            (135, 206, 235),
            (255, 69, 0),
        ];

        public StatusMark(Models.ChangeState state)
        {
            var i = (int)state;
            var (r, g, b) = s_colors[i];
            var mark = new Row { AlignItems = Align.Center, Justify = Justify.Center };
            mark.Width = 14;
            mark.Height = 14;
            mark.Add(new Square(Color.Rgba(r, g, b, (byte)(i == 0 ? 0 : 255))) { Inset = new Thickness(0) });
            mark.Add(new Text(s_letters[i]) { Style = TextStyle.Caption, Weight = FontWeight.Bold, Color = Color.Rgba(255, 255, 255) });
            View = mark;
        }

        public Node View { get; }

        private sealed class Square(Color color) : DrawnWidget("ChangeStatus", 0, 0)
        {
            protected override void Draw(Canvas canvas) => canvas.FillRect(0, 0, canvas.Width, canvas.Height, color, 2);
        }
    }
}
