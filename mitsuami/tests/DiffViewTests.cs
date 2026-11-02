using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;

using Mitsuamia;

namespace SourceGit.Tests
{
    /// <summary>The real <c>DiffView</c> over the real editor and plugins: a change from git,
    /// through <c>DiffJson</c> and the plugin, back as a chunk to <c>git apply</c>; its panes
    /// and its toolbar.</summary>
    public static class DiffViewTests
    {
        public static IEnumerable<(string, Func<Task>)> All() =>
        [
            ("diff view: the plugin takes what git said, and stages the run under the cursor", StageUnderCursor),
            ("diff view: stages the selected lines", StageSelected),
            ("diff view: stages from a pane of side by side, by the toolbar", StageSideBySide),
            ("diff view: text past ASCII reaches the plugin and comes back", AwkwardText),
            ("diff view: a commit's diff can't stage", CommitCannotStage),
            ("diff view: unstages what's staged", UnstageStaged),
            ("diff view: the toolbar goes through the changes", ToolbarGoes),
            ("diff view: a pane keeps its editor from file to file", PaneKeepsItsEditor),
        ];

        private sealed partial class Shown : ObservableObject
        {
            public object Diff { get => _diff; set => SetProperty(ref _diff, value); }

            private object _diff;
        }

        /// <summary>A window with a diff view on a real repository, and what its editor says.</summary>
        private sealed class Page : IDisposable
        {
            public readonly TestRepo Repo = new();
            public readonly Window Window;
            public readonly List<(string Name, JsonNode Payload)> Events = [];
            public CodeViewRef Code;

            private readonly Shown _shown = new();
            private readonly ViewModels.Repository _vm;

            public Page(Action<TestRepo> change)
            {
                change(Repo);
                _vm = new ViewModels.Repository(false, Repo.Path, Repo.Path + "/.git");
                Window = new Window("test", 1000, 700);
                var column = new Column { AlignItems = Align.Stretch };
                column.Grow = 1;
                column.Add(Views.DiffView.Create(_shown, nameof(_shown.Diff), () => _shown.Diff, _vm));
                Window.Content = column;
            }

            /// <summary>Shows a diff, and waits for the plugin to say so.</summary>
            public async Task Show(Models.DiffOption option)
            {
                var before = Count("shown");
                _shown.Diff = new ViewModels.DiffContext(Repo.Path, option);
                await Check.Until("the editor to start", () => Views.CodeView.Live.Count > 0);
                if (Code == null)
                {
                    Code = new CodeViewRef(Views.CodeView.Live[^1]);
                    Code.View.PluginEvent += (name, json) => Events.Add((name, JsonNode.Parse(json)));
                }
                await Check.Until("the diff to show", () => Count("shown") > before || Count("error") > 0);
                Check.Equal(0, Count("error"), "errors from the plugin");
            }

            /// <summary>Shows what the editor won't say anything about.</summary>
            public Task ShowOther(ViewModels.DiffContext context)
            {
                _shown.Diff = context;
                return Task.CompletedTask;
            }

            public async Task ShowLocal(string file, bool unstaged) =>
                await Show(new Models.DiffOption(await Repo.Change(file), unstaged));

            public int Count(string name) => Events.Count(e => e.Name == name);

            public JsonNode Last(string name) => Events.Last(e => e.Name == name).Payload;

            public void Act(string action) =>
                Code.View.CallPlugin("sourcegit-diff", "act_on", new JsonObject { ["action"] = action }.ToJsonString());

            public void Dispose()
            {
                Window.Destroy();
                Repo.Dispose();
            }
        }

        public sealed class CodeViewRef(Views.CodeView view)
        {
            public Views.CodeView View { get; } = view;
        }

        /// <summary>Each test its mode: an editor reads it as it starts.</summary>
        private static void Mode(string mode) => PortSettings.Instance.EditorMode = mode;

        private static async Task StageUnderCursor()
        {
            Mode("modeless");
            using var page = new Page(r => r.ChangeTwoPlaces());
            await page.ShowLocal(TestRepo.File1, true);
            Check.Equal(2, page.Last("shown")["hunks"].GetValue<int>(), "hunks");
            page.Act("stage");
            await Check.Until("the stage", () => page.Repo.Staged() != "");
            Check.Has(page.Repo.Staged(), "+line five");
            Check.HasNot(page.Repo.Staged(), "line thirty");
        }

        private static async Task StageSelected()
        {
            Mode("vim");
            using var page = new Page(r =>
            {
                var lines = TestRepo.Numbered().ToList();
                lines.InsertRange(10, ["added a", "added b", "added c"]);
                r.Write(TestRepo.File1, string.Join('\n', lines) + "\n");
            });
            await page.ShowLocal(TestRepo.File1, true);
            // The cursor starts on the first change: `added a`.
            Host.Command(page.Code.View.EditorId, "normal! Vj");
            await Task.Delay(100);
            page.Act("stage");
            await Check.Until("the stage", () => page.Repo.Staged() != "");
            Check.Has(page.Repo.Staged(), "+added a");
            Check.Has(page.Repo.Staged(), "+added b");
            Check.HasNot(page.Repo.Staged(), "added c");
        }

        private static async Task StageSideBySide()
        {
            Mode("modeless");
            using var page = new Page(r => r.ChangeTwoPlaces());
            await page.ShowLocal(TestRepo.File1, true);
            var shown = page.Count("shown");
            Host.Press(page.Window, App.Text("Diff.SideBySide"));
            await Check.Until("two panes", () => page.Count("shown") > shown);
            try
            {
                page.Act("stage");
                await Check.Until("the stage", () => page.Repo.Staged() != "");
                // The old pane is the one focused, which takes the old side's lines.
                Check.Has(page.Repo.Staged(), "-line 5");
                Check.HasNot(page.Repo.Staged(), "line thirty");
            }
            finally
            {
                // A preference: the next test starts on one pane.
                Host.Press(page.Window, App.Text("Diff.SideBySide"));
            }
        }

        private static async Task AwkwardText()
        {
            Mode("modeless");
            using var page = new Page(r =>
            {
                r.Write("text.txt", "olá\n\ttabbed \"quoted\" \\ back\nend");
                r.Git("add text.txt");
                r.Git("commit -q -m text");
                r.Write("text.txt", "olá mundo 日本語\n\ttabbed \"quoted\" \\ back\nend");
            });
            await page.ShowLocal("text.txt", true);
            page.Act("stage");
            await Check.Until("the stage", () => page.Repo.Staged() != "");
            Check.Equal("olá mundo 日本語\n\ttabbed \"quoted\" \\ back\nend", page.Repo.Git("show :text.txt"), "what's in the index");
        }

        private static async Task CommitCannotStage()
        {
            Mode("modeless");
            using var page = new Page(r =>
            {
                r.ChangeTwoPlaces();
                r.Git($"add {TestRepo.File1}");
                r.Git("commit -q -m second");
            });
            var commit = await new Commands.QuerySingleCommit(page.Repo.Path, "HEAD").GetResultAsync();
            var change = (await new Commands.CompareRevisions(page.Repo.Path, "HEAD~", "HEAD").ReadAsync()).First();
            await page.Show(new Models.DiffOption(commit, change));
            page.Act("stage");
            await Task.Delay(200);
            Check.Equal(0, page.Count("chunk"), "chunks sent");
            Check.Equal("", page.Repo.Staged(), "what's staged");
        }

        private static async Task UnstageStaged()
        {
            Mode("modeless");
            using var page = new Page(r =>
            {
                r.ChangeTwoPlaces();
                r.Git($"add {TestRepo.File1}");
            });
            await page.ShowLocal(TestRepo.File1, false);
            page.Act("unstage");
            await Check.Until("the unstage", () => page.Repo.Unstaged() != "");
            Check.Has(page.Repo.Unstaged(), "+line five");
            Check.Has(page.Repo.Staged(), "+line thirty");
            Check.HasNot(page.Repo.Staged(), "line five");
        }

        private static async Task ToolbarGoes()
        {
            Mode("modeless");
            using var page = new Page(r => r.ChangeTwoPlaces());
            await page.ShowLocal(TestRepo.File1, true);
            await Check.Until("where the cursor is", () => page.Count("change_position") > 0);
            Check.Equal(2, page.Last("change_position")["total"].GetValue<int>(), "changes");
            Check.Equal(1, page.Last("change_position")["at"].GetValue<int>(), "the change it starts at");

            Host.Press(page.Window, App.Text("Diff.Next"));
            await Check.Until("the next change", () => page.Last("change_position")["at"].GetValue<int>() == 2);
            Host.Press(page.Window, App.Text("Diff.Prev"));
            await Check.Until("the previous change", () => page.Last("change_position")["at"].GetValue<int>() == 1);
            Host.Press(page.Window, App.Text("Diff.Last"));
            await Check.Until("the last change", () => page.Last("change_position")["at"].GetValue<int>() == 2);
            Host.Press(page.Window, App.Text("Diff.First"));
            await Check.Until("the first change", () => page.Last("change_position")["at"].GetValue<int>() == 1);
        }

        private static async Task PaneKeepsItsEditor()
        {
            Mode("modeless");
            using var page = new Page(r =>
            {
                r.ChangeTwoPlaces();
                r.Write("other.txt", "other\n");
                r.Write("image.bin", "\0\u0001binary\0");
            });
            var live = Views.CodeView.Live.Count;
            await page.ShowLocal(TestRepo.File1, true);
            var editor = page.Code.View;
            Check.Equal(live + 1, Views.CodeView.Live.Count, "editors");

            await page.ShowLocal("other.txt", true);
            Check.Equal("other.txt", page.Last("shown")["path"].GetValue<string>(), "what shows");
            Check.Equal(live + 1, Views.CodeView.Live.Count, "editors");

            // What isn't text hides the editor, and it's still there for what is.
            page.Events.Clear();
            var option = new Models.DiffOption(await page.Repo.Change("image.bin"), true);
            Check.That(editor.Node.Frame.Height > 0, "the editor was hidden");
            var context = new ViewModels.DiffContext(page.Repo.Path, option);
            await page.ShowOther(context);
            await Check.Until("the editor to hide", () => editor.Node.Frame.Height == 0);

            await page.ShowLocal(TestRepo.File1, true);
            await Check.Until("the editor to come back", () => editor.Node.Frame.Height > 0);
            Check.Equal(live + 1, Views.CodeView.Live.Count, "editors");
            Check.That(ReferenceEquals(editor, Views.CodeView.Live[^1]), "another editor started");
        }
    }
}
