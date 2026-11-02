using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace SourceGit.Tests
{
    /// <summary>The merge editor over the real editor, on a real merge's conflicts: choosing in
    /// the window, saving, and what git then has.</summary>
    public static class MergeEditorTests
    {
        public static IEnumerable<(string, Func<Task>)> All() =>
        [
            ("merge editor: chooses each conflict, saves and stages", ChoosesAndSaves),
            ("merge editor: both sides, and undoing a choice", BothAndUndo),
            ("merge editor: can't save with a conflict open", NoSaveWhileOpen),
            ("merge editor: its close button asks once something's chosen", AsksBeforeClosing),
            ("merge editor: keeps a CRLF file's line endings", KeepsCrlf),
            ("merge editor: reads git's diff3 style, with the base shown", Diff3),
        ];

        /// <summary>A file changed on two branches in two places far enough apart for git to call
        /// them two conflicts, merging.</summary>
        private static (TestRepo Repo, ViewModels.MergeConflictEditor Vm) Conflicted(string eol = "\n", string style = null)
        {
            var repo = new TestRepo();
            string Lines(params string[] lines) => string.Join(eol, lines) + eol;
            repo.Write("c.txt", Lines("top", "one", "m1", "m2", "m3", "m4", "m5", "two", "end"));
            repo.Git("add c.txt");
            repo.Git("commit -q -m base");
            repo.Git("checkout -q -b other");
            repo.Write("c.txt", Lines("top", "theirs one", "m1", "m2", "m3", "m4", "m5", "theirs two", "end"));
            repo.Git("commit -q -am other");
            repo.Git("checkout -q main");
            repo.Write("c.txt", Lines("top", "mine one", "m1", "m2", "m3", "m4", "m5", "mine two", "end"));
            repo.Git("commit -q -am mine");
            if (style != null)
                repo.Git($"config merge.conflictStyle {style}");
            repo.GitAllowingFailure("merge -q other");
            var vm = new ViewModels.Repository(false, repo.Path, repo.Path + "/.git");
            return (repo, new ViewModels.MergeConflictEditor(vm, null, "c.txt"));
        }

        private sealed class Page : IDisposable
        {
            public readonly List<(string Name, JsonNode Payload)> Events = [];
            public readonly Mitsuamia.Window Window;

            public Page(ViewModels.MergeConflictEditor vm)
            {
                var repo = new ViewModels.Repository(false, System.IO.Path.GetTempPath(), System.IO.Path.GetTempPath());
                Views.MergeEditor.Show(repo, vm);
                Window = Views.MergeEditor.Shown;
                Views.MergeEditor.Code.PluginEvent += (name, json) => Events.Add((name, JsonNode.Parse(json)));
            }

            public int Count(string name) => Events.Count(e => e.Name == name);

            public JsonNode Last(string name) => Events.Last(e => e.Name == name).Payload;

            public async Task At(int index, bool resolved)
            {
                await Check.Until($"the cursor at conflict {index}", () =>
                    Count("conflict_at") > 0 && Last("conflict_at")["index"].GetValue<int>() == index
                    && Last("conflict_at")["resolved"].GetValue<bool>() == resolved);
            }

            public void Dispose()
            {
                // Closed by the user, it's gone already, and `Shown` with it.
                if (Views.MergeEditor.Shown == Window)
                    Window.Destroy();
            }
        }

        private static async Task ChoosesAndSaves()
        {
            var (repo, vm) = Conflicted();
            using (repo)
            using (var page = new Page(vm))
            {
                await page.At(0, false);
                Check.Equal(2, vm.UnsolvedCount, "conflicts");

                Host.Press(page.Window, Views.Popups.ButtonText("MergeConflictEditor.UseMine"));
                await page.At(0, true);
                Check.Equal(1, vm.UnsolvedCount, "after mine");

                Host.Press(page.Window, App.Text("MergeConflictEditor.NextConflict"));
                await page.At(1, false);
                Host.Press(page.Window, Views.Popups.ButtonText("MergeConflictEditor.UseTheirs"));
                await page.At(1, true);
                Check.Equal(0, vm.UnsolvedCount, "after theirs");

                Host.Press(page.Window, Views.Popups.ButtonText("MergeConflictEditor.SaveAndStage"));
                await Check.Until("the window to close", () => Views.MergeEditor.Shown == null);
                Check.Equal("top\nmine one\nm1\nm2\nm3\nm4\nm5\ntheirs two\nend\n", repo.Read("c.txt"), "the file");
                Check.Equal("M  c.txt", repo.Git("status --porcelain").Trim(), "git's status");
            }
        }

        private static async Task BothAndUndo()
        {
            var (repo, vm) = Conflicted();
            using (repo)
            using (var page = new Page(vm))
            {
                await page.At(0, false);
                Views.MergeEditor.Code.CallPlugin("sourcegit-conflict", "choose_here", "{\"choice\":\"both_theirs_first\"}");
                await page.At(0, true);
                Check.That(vm.ResultLines.Any(l => l.Content == "theirs one"), "theirs in the result");
                var theirs = vm.ResultLines.FindIndex(l => l.Content == "theirs one");
                Check.Equal(theirs + 1, vm.ResultLines.FindIndex(l => l.Content == "mine one"), "mine after theirs");

                Host.Press(page.Window, Views.Popups.ButtonText("MergeConflictEditor.Undo"));
                await page.At(0, false);
                Check.Equal(2, vm.UnsolvedCount, "undone");
            }
        }

        private static async Task NoSaveWhileOpen()
        {
            var (repo, vm) = Conflicted();
            using (repo)
            using (var page = new Page(vm))
            {
                await page.At(0, false);
                var before = repo.Read("c.txt");
                Check.That(!await vm.SaveAndStageAsync(), "it saved with conflicts open");
                Check.Equal(before, repo.Read("c.txt"), "the file");
            }
        }

        private static async Task AsksBeforeClosing()
        {
            var questions = 0;
            var answer = false;
            var real = Views.MergeEditor.Confirm;
            Views.MergeEditor.Confirm = (_, _) =>
            {
                questions++;
                return Task.FromResult(answer);
            };
            try
            {
                // Nothing chosen: it just closes.
                var (first, untouched) = Conflicted();
                using (first)
                using (var page = new Page(untouched))
                {
                    await page.At(0, false);
                    var closed = false;
                    page.Window.Closed += () => closed = true;
                    Host.Close(page.Window);
                    await Check.Until("the window to close", () => closed);
                    Check.Equal(0, questions, "questions");
                }

                var (repo, vm) = Conflicted();
                using (repo)
                using (var page = new Page(vm))
                {
                    await page.At(0, false);
                    Host.Press(page.Window, Views.Popups.ButtonText("MergeConflictEditor.UseMine"));
                    await page.At(0, true);
                    var closed = false;
                    page.Window.Closed += () => closed = true;

                    Host.Close(page.Window);
                    await Check.Until("the question", () => questions == 1);
                    await Task.Delay(50);
                    Check.That(!closed, "it closed on No");
                    Host.Press(page.Window, Views.Popups.ButtonText("MergeConflictEditor.Undo"));
                    await page.At(0, false);
                    Host.Press(page.Window, Views.Popups.ButtonText("MergeConflictEditor.UseMine"));
                    await page.At(0, true);

                    answer = true;
                    Host.Close(page.Window);
                    await Check.Until("the window to close", () => closed);
                    Check.Equal(2, questions, "questions");
                }
            }
            finally
            {
                Views.MergeEditor.Confirm = real;
            }
        }

        private static async Task KeepsCrlf()
        {
            var (repo, vm) = Conflicted("\r\n");
            using (repo)
            using (var page = new Page(vm))
            {
                await page.At(0, false);
                Host.Press(page.Window, Views.Popups.ButtonText("MergeConflictEditor.UseMine"));
                await page.At(0, true);
                Host.Press(page.Window, App.Text("MergeConflictEditor.NextConflict"));
                await page.At(1, false);
                Host.Press(page.Window, Views.Popups.ButtonText("MergeConflictEditor.UseMine"));
                await page.At(1, true);
                Host.Press(page.Window, Views.Popups.ButtonText("MergeConflictEditor.SaveAndStage"));
                await Check.Until("the window to close", () => Views.MergeEditor.Shown == null);
                Check.Equal("top\r\nmine one\r\nm1\r\nm2\r\nm3\r\nm4\r\nm5\r\nmine two\r\nend\r\n", repo.Read("c.txt"), "the file");
            }
        }

        private static async Task Diff3()
        {
            var (repo, vm) = Conflicted(style: "diff3");
            using (repo)
            using (var page = new Page(vm))
            {
                await page.At(0, false);
                Check.Equal(vm.OursLines.Count, vm.ResultLines.Count, "panes' rows");
                Check.Equal(2, vm.ConflictRegions.Count, "regions");
                Host.Press(page.Window, Views.Popups.ButtonText("MergeConflictEditor.UseMine"));
                await page.At(0, true);
                Host.Press(page.Window, App.Text("MergeConflictEditor.NextConflict"));
                await page.At(1, false);
                Host.Press(page.Window, Views.Popups.ButtonText("MergeConflictEditor.UseTheirs"));
                await page.At(1, true);
                Host.Press(page.Window, Views.Popups.ButtonText("MergeConflictEditor.SaveAndStage"));
                await Check.Until("the window to close", () => Views.MergeEditor.Shown == null);
                Check.Equal("top\nmine one\nm1\nm2\nm3\nm4\nm5\ntheirs two\nend\n", repo.Read("c.txt"), "the file");
            }
        }
    }
}
