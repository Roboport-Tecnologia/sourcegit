using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SourceGit.Tests
{
    /// <summary><c>DiffView.ApplyChunk</c> on real git: what ends up staged, unstaged and in
    /// the file, for the indexes the plugin sends.</summary>
    public static class ChunkTests
    {
        public static IEnumerable<(string, Func<Task>)> All() =>
        [
            ("chunk: stage takes its hunk and leaves the other", StageOneHunk),
            ("chunk: stage takes one added line of a run", StageOneLineOfARun),
            ("chunk: unstage gives back its hunk", UnstageOneHunk),
            ("chunk: discard changes the file, and only there", DiscardOneHunk),
            ("chunk: stage from the new side of side by side", StageNewSide),
            ("chunk: stage from the old side of side by side", StageOldSide),
            ("chunk: stage part of a new file", StagePartOfNewFile),
            ("chunk: text git and C# could differ on", StageAwkwardText),
            ("chunk: what doesn't fit the change does nothing", WhatDoesNotFit),
            ("chunk: a range of no changes does nothing", NoChangesInRange),
        ];

        /// <summary>The first and last index in <c>TextDiff.Lines</c> with this text and kind.</summary>
        private static int Index(Models.TextDiff diff, string text, Models.TextDiffLineType type) =>
            diff.Lines.FindIndex(l => l.Type == type && l.Content.TrimEnd('\r') == text) is var i and >= 0
                ? i
                : throw new InvalidOperationException($"No {type} line '{text}'");

        private static (int, int) Change(Models.TextDiff diff, string old, string now) =>
            (Index(diff, old, Models.TextDiffLineType.Deleted), Index(diff, now, Models.TextDiffLineType.Added));

        private static async Task StageOneHunk()
        {
            using var repo = new TestRepo();
            repo.ChangeTwoPlaces();
            var (option, diff) = await repo.Diff(TestRepo.File1, true);
            var (first, last) = Change(diff, "line 5", "line five");
            Check.That(await Views.DiffView.ApplyChunk(repo.Path, option, diff, "stage", first, last, true, false), "git refused it");
            Check.Has(repo.Staged(), "+line five");
            Check.HasNot(repo.Staged(), "line thirty");
            Check.Has(repo.Unstaged(), "+line thirty");
            Check.HasNot(repo.Unstaged(), "line five");
        }

        private static async Task StageOneLineOfARun()
        {
            using var repo = new TestRepo();
            var lines = TestRepo.Numbered().ToList();
            lines.InsertRange(10, ["added a", "added b", "added c"]);
            repo.Write(TestRepo.File1, string.Join('\n', lines) + "\n");
            var (option, diff) = await repo.Diff(TestRepo.File1, true);
            var b = Index(diff, "added b", Models.TextDiffLineType.Added);
            Check.That(await Views.DiffView.ApplyChunk(repo.Path, option, diff, "stage", b, b, true, false), "git refused it");
            Check.Has(repo.Staged(), "+added b");
            Check.HasNot(repo.Staged(), "added a");
            Check.HasNot(repo.Staged(), "added c");
        }

        private static async Task UnstageOneHunk()
        {
            using var repo = new TestRepo();
            repo.ChangeTwoPlaces();
            repo.Git($"add {TestRepo.File1}");
            var (option, diff) = await repo.Diff(TestRepo.File1, false);
            var (first, last) = Change(diff, "line 30", "line thirty");
            Check.That(await Views.DiffView.ApplyChunk(repo.Path, option, diff, "unstage", first, last, true, false), "git refused it");
            Check.Has(repo.Staged(), "+line five");
            Check.HasNot(repo.Staged(), "line thirty");
            Check.Has(repo.Unstaged(), "+line thirty");
            Check.Has(repo.Read(TestRepo.File1), "line thirty");
        }

        private static async Task DiscardOneHunk()
        {
            using var repo = new TestRepo();
            repo.ChangeTwoPlaces();
            var (option, diff) = await repo.Diff(TestRepo.File1, true);
            var (first, last) = Change(diff, "line 5", "line five");
            Check.That(await Views.DiffView.ApplyChunk(repo.Path, option, diff, "discard", first, last, true, false), "git refused it");
            var text = repo.Read(TestRepo.File1);
            Check.Has(text, "line 5\n");
            Check.HasNot(text, "line five");
            Check.Has(text, "line thirty");
            Check.Equal("", repo.Staged(), "what's staged");
        }

        private static async Task StageNewSide()
        {
            using var repo = new TestRepo();
            repo.ChangeTwoPlaces();
            var (option, diff) = await repo.Diff(TestRepo.File1, true);
            var (first, last) = Change(diff, "line 30", "line thirty");
            Check.That(await Views.DiffView.ApplyChunk(repo.Path, option, diff, "stage", first, last, false, false), "git refused it");
            Check.Has(repo.Staged(), "+line thirty");
            Check.HasNot(repo.Staged(), "line five");
        }

        private static async Task StageOldSide()
        {
            using var repo = new TestRepo();
            repo.ChangeTwoPlaces();
            var (option, diff) = await repo.Diff(TestRepo.File1, true);
            var (first, last) = Change(diff, "line 30", "line thirty");
            Check.That(await Views.DiffView.ApplyChunk(repo.Path, option, diff, "stage", first, last, false, true), "git refused it");
            // The old side's lines only: line 30 goes, and its replacement stays behind.
            Check.Has(repo.Staged(), "-line 30");
            Check.HasNot(repo.Staged(), "+line thirty");
            Check.HasNot(repo.Staged(), "line five");
        }

        private static async Task StagePartOfNewFile()
        {
            using var repo = new TestRepo();
            repo.Write("new.txt", "one\ntwo\nthree\n");
            var (option, diff) = await repo.Diff("new.txt", true);
            var two = Index(diff, "two", Models.TextDiffLineType.Added);
            Check.That(await Views.DiffView.ApplyChunk(repo.Path, option, diff, "stage", two, two, true, false), "git refused it");
            var staged = repo.Git("show :new.txt");
            Check.Equal("two\n", staged, "what's in the index");
        }

        private static async Task StageAwkwardText()
        {
            using var repo = new TestRepo();
            // No line end at the end of the file, and bytes past ASCII.
            repo.Write("text.txt", "olá\nmiddle\nend");
            repo.Git("add text.txt");
            repo.Git("commit -q -m text");
            repo.Write("text.txt", "olá mundo 日本語\nmiddle\nend changed");
            var (option, diff) = await repo.Diff("text.txt", true);
            var first = Index(diff, "olá", Models.TextDiffLineType.Deleted);
            var last = Index(diff, "olá mundo 日本語", Models.TextDiffLineType.Added);
            Check.That(await Views.DiffView.ApplyChunk(repo.Path, option, diff, "stage", first, last, true, false), "git refused the first");
            Check.Equal("olá mundo 日本語\nmiddle\nend", repo.Git("show :text.txt"), "what's in the index");

            (option, diff) = await repo.Diff("text.txt", true);
            first = Index(diff, "end", Models.TextDiffLineType.Deleted);
            last = Index(diff, "end changed", Models.TextDiffLineType.Added);
            Check.That(await Views.DiffView.ApplyChunk(repo.Path, option, diff, "stage", first, last, true, false), "git refused the last");
            Check.Equal("olá mundo 日本語\nmiddle\nend changed", repo.Git("show :text.txt"), "what's in the index");
        }

        private static async Task WhatDoesNotFit()
        {
            using var repo = new TestRepo();
            repo.ChangeTwoPlaces();
            var (unstaged, diff) = await repo.Diff(TestRepo.File1, true);
            var (first, last) = Change(diff, "line 5", "line five");
            Check.That(!await Views.DiffView.ApplyChunk(repo.Path, unstaged, diff, "unstage", first, last, true, false), "unstaged what wasn't staged");

            repo.Git($"add {TestRepo.File1}");
            var (staged, stagedDiff) = await repo.Diff(TestRepo.File1, false);
            (first, last) = Change(stagedDiff, "line 5", "line five");
            Check.That(!await Views.DiffView.ApplyChunk(repo.Path, staged, stagedDiff, "stage", first, last, true, false), "staged what was staged");
            Check.That(!await Views.DiffView.ApplyChunk(repo.Path, staged, stagedDiff, "discard", first, last, true, false), "discarded what was staged");

            repo.Git("commit -q -m second");
            var commit = await new Commands.QuerySingleCommit(repo.Path, "HEAD").GetResultAsync();
            var ofCommit = new Models.DiffOption(commit, new Models.Change { Path = TestRepo.File1 });
            Check.That(!await Views.DiffView.ApplyChunk(repo.Path, ofCommit, stagedDiff, "stage", first, last, true, false), "staged a commit's change");
        }

        private static async Task NoChangesInRange()
        {
            using var repo = new TestRepo();
            repo.ChangeTwoPlaces();
            var (option, diff) = await repo.Diff(TestRepo.File1, true);
            var context = diff.Lines.FindIndex(l => l.Type == Models.TextDiffLineType.Normal);
            Check.That(!await Views.DiffView.ApplyChunk(repo.Path, option, diff, "stage", context, context, true, false), "a line of context applied");
            Check.Equal("", repo.Staged(), "what's staged");
        }
    }
}
