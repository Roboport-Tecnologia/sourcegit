using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's notifications (git's errors, and what succeeded), which
    /// it floats over the page. Each is shown in turn, for the page showing
    /// (another page's wait till it's chosen), by <see cref="Present"/>:
    /// the one place to change once mitsuami has the platform's own
    /// notifications.
    /// </summary>
    public static class Notifications
    {
        /// <summary>The notification's sheet showing, if any: for captures.</summary>
        public static Window Shown { get; private set; }

        private static readonly Queue<Models.Notification> s_pending = new();
        private static bool s_presenting;

        /// <summary>The page's notifications, if it's the one showing.</summary>
        public static async void Show(Window window, ViewModels.Launcher vm, ViewModels.LauncherPage page)
        {
            if (page != vm.ActivePage || page.Notifications.Count == 0)
                return;
            foreach (var notification in page.Notifications)
                s_pending.Enqueue(notification);
            page.Notifications.Clear();
            if (s_presenting)
                return;
            s_presenting = true;
            while (s_pending.TryDequeue(out var notification))
                await Present(Popups.Shown ?? window, notification);
            s_presenting = false;
        }

        /// <summary>
        /// Upstream's card as a sheet, on the popup's sheet if one shows (a
        /// failed push keeps it): its icon and "Error" or "Notice", a copy
        /// button, then the message as upstream's command log shows it
        /// (monospace, wrapped, scrolled past a height), which can be
        /// selected; Sure closes it, as Escape does.
        /// </summary>
        private static Task Present(Window owner, Models.Notification notification)
        {
            var done = new TaskCompletionSource();
            var error = notification.IsError;
            var title = Popups.ButtonText(error ? "Launcher.Error" : "Launcher.Info");
            var sheet = Shown = new Window(owner, title, 560);

            var column = new Column { Padding = Metrics.Panel, Gap = Metrics.PanelGap, AlignItems = Align.Stretch };
            var header = new Row { AlignItems = Align.Center, Gap = Spacing.Sm };
            header.Add(new Icon(Icons.Get(error ? "Error" : "Info")) { Size = 14, Color = error ? Color.Error : Color.Success });
            var heading = new Text(title) { Style = TextStyle.Headline };
            heading.Grow = 1;
            header.Add(heading);
            var message = notification.Message.Trim();
            header.Add(CommitDetail.CopyButton(message));
            column.Add(header);

            var scroll = new ScrollView();
            scroll.MaxHeight = 360;
            scroll.Content.Add(new Text(message) { Style = TextStyle.Monospace, Selectable = true });
            column.Add(scroll);

            var buttons = new Row { Justify = Justify.End };
            var sure = new Button(Popups.ButtonText("Sure")) { Role = ButtonRole.Default };
            buttons.Add(sure);
            column.Add(buttons);
            sheet.Content = column;

            void Close()
            {
                if (done.Task.IsCompleted)
                    return;
                done.SetResult();
                Shown = null;
                sheet.Destroy();
            }
            sure.Click += Close;
            sheet.Closed += Close;
            return done.Task;
        }
    }
}
