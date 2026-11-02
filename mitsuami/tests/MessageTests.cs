using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using Mitsuamia;

namespace SourceGit.Tests
{
    /// <summary>The commit message's editor, on the real plugin in a real single-file session:
    /// the app's text reaches it, and what's typed comes back.</summary>
    public static class MessageTests
    {
        public static IEnumerable<(string, Func<Task>)> All() =>
        [
            ("message: what's typed reaches the app, in each mode", Typed),
            ("message: the app's text isn't told back", NoEcho),
            ("message: what was typed before the app's new text is dropped", StaleDropped),
            ("message: :q starts again on what was typed", Quits),
        ];

        private sealed class Page : IDisposable
        {
            public readonly Views.MessageEditor Editor;
            public readonly List<(string Name, JsonNode Payload)> Events = [];
            public readonly List<string> Changes = [];
            private readonly Window _window;

            public Page(string text)
            {
                _window = new Window("Message", 400, 200);
                var column = new Column { AlignItems = Align.Stretch };
                Editor = new Views.MessageEditor(text, "Enter a message");
                Editor.Code.PluginEvent += (name, json) => Events.Add((name, JsonNode.Parse(json)));
                Editor.Changed += Changes.Add;
                column.Add(Editor.Node);
                _window.Content = column;
            }

            public Task Shown(int times = 1) =>
                Check.Until("the message to show", () => Events.Count(e => e.Name == "shown") >= times);

            public void Type(string keys) => Host.Command(Editor.Code.EditorId, $"normal! {keys}");

            public void Dispose() => _window.Destroy();
        }

        private static async Task Typed()
        {
            // Modeless and vim fire different events as they type. Helix is mappings, which
            // `:normal!` skips, so its typing is in the plugin's spec, with real keys.
            try
            {
                foreach (var mode in new[] { "modeless", "vim" })
                {
                    PortSettings.Instance.EditorMode = mode;
                    using var page = new Page("");
                    await page.Shown();
                    page.Type("ifix");
                    await Check.Until($"{mode}: the app to hear it", () => page.Editor.Text == "fix");
                    Check.Equal("fix", page.Changes.Last(), mode);
                }
            }
            finally
            {
                PortSettings.Instance.EditorMode = "modeless";
            }
        }

        private static async Task NoEcho()
        {
            using var page = new Page("subject\n\nbody");
            await page.Shown();
            page.Editor.Text = "";
            await page.Shown(2);
            page.Editor.Text = "from a template";
            await page.Shown(3);
            await Task.Delay(100);
            Check.Equal(0, page.Changes.Count, "changes told back");
        }

        private static async Task StaleDropped()
        {
            using var page = new Page("");
            await page.Shown();
            // Typed, then the app clears it as the event is on its way.
            page.Type("ifix");
            page.Editor.Text = "cleared";
            await page.Shown(2);
            await Task.Delay(100);
            Check.Equal("cleared", page.Editor.Text);
        }

        private static async Task Quits()
        {
            using var page = new Page("");
            await page.Shown();
            page.Type("ifix");
            await Check.Until("the app to hear it", () => page.Editor.Text == "fix");
            Host.Command(page.Editor.Code.EditorId, "q");
            await page.Shown(2);
            Check.Equal("fix", page.Editor.Text);
            // The new editor has it: typing goes on from it, and what it says is the whole text.
            page.Type("A!");
            await Check.Until("the new editor's text", () => page.Editor.Text == "fix!");
        }
    }
}
