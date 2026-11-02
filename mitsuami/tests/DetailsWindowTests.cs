using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SourceGit.Tests
{
    /// <summary>A commit's details opened in a window, on real git: they stay on the file that
    /// was picked, not the first one.</summary>
    public static class DetailsWindowTests
    {
        private const string Deep = "dir/sub/b.txt";

        public static IEnumerable<(string, Func<Task>)> All() =>
        [
            ("details window: keeps the change picked", KeepsChange),
            ("details window: keeps the file picked", KeepsFile),
            ("details window: finds the file picked when its tab opens later", KeepsFileForLater),
            ("details window: keeps the change picked of two commits compared", KeepsCompared),
        ];

        private static async Task<(TestRepo Repo, ViewModels.Repository Vm, Models.Commit Commit)> Repo()
        {
            var repo = new TestRepo();
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(repo.Path, "dir/sub"));
            repo.Write("a.txt", "a\n");
            repo.Write(Deep, "b\n");
            repo.Git("add a.txt dir");
            repo.Git("commit -q -m second");
            var commit = await new Commands.QuerySingleCommit(repo.Path, "HEAD").GetResultAsync();
            return (repo, new ViewModels.Repository(false, repo.Path, repo.Path + "/.git"), commit);
        }

        private static async Task<ViewModels.CommitDetail> Detail(ViewModels.Repository vm, Models.Commit commit, int tab)
        {
            var detail = new ViewModels.CommitDetail(vm, new ViewModels.CommitDetailSharedData()) { ActiveTabIndex = tab, Commit = commit };
            await Check.Until("the changes", () => detail.Changes is { Count: 2 } && detail.ChangeSelection.Count == 1);
            return detail;
        }

        private static async Task KeepsChange()
        {
            var (repo, vm, commit) = await Repo();
            using (repo)
            {
                var detail = await Detail(vm, commit, 1);
                Check.Equal("a.txt", detail.ChangeSelection.Changes[0].Path, "picked first");
                detail.ChangeSelection = new(detail.VisibleChanges.Where(c => c.Path == Deep).ToList());

                await Views.HistoryWindows.ShowDetails(vm, detail);
                var window = Views.HistoryWindows.Shown;
                try
                {
                    var shown = (ViewModels.CommitDetail)Views.HistoryWindows.ShownDetails;
                    await Check.Until("the changes", () => shown.Changes is { Count: 2 });
                    Check.Equal(Deep, shown.ChangeSelection.Changes.Single().Path, "the change");
                    await Check.Until("its diff", () => shown.DiffContext != null);
                    Check.Equal(Deep, shown.DiffContext.Title, "the diff");
                    Check.Equal(Deep, detail.ChangeSelection.Changes.Single().Path, "the original's");
                }
                finally
                {
                    window.Destroy();
                }
            }
        }

        private static async Task Picked(ViewModels.CommitDetail detail)
        {
            var found = await detail.GetRevisionFilesUnderFolderAsync(Deep);
            await detail.ViewRevisionFileAsync(found.Single());
        }

        private static async Task KeepsFile()
        {
            var (repo, vm, commit) = await Repo();
            using (repo)
            {
                var detail = await Detail(vm, commit, 2);
                await Picked(detail);

                await Views.HistoryWindows.ShowDetails(vm, detail);
                var window = Views.HistoryWindows.Shown;
                try
                {
                    var shown = (ViewModels.CommitDetail)Views.HistoryWindows.ShownDetails;
                    Check.Equal(2, shown.ActiveTabIndex, "tab");
                    await Check.Until($"the content, at '{shown.ViewRevisionFilePath}'", () => shown.ViewRevisionFileContent is Models.RevisionTextFile { Content: "b\n" });
                    await Check.Until("the tree to pick it", () => Views.RevisionFiles.Newest.PickedPath == Deep);
                }
                finally
                {
                    window.Destroy();
                }
            }
        }

        private static async Task KeepsFileForLater()
        {
            var (repo, vm, commit) = await Repo();
            using (repo)
            {
                var detail = await Detail(vm, commit, 2);
                await Picked(detail);
                detail.ActiveTabIndex = 0;

                await Views.HistoryWindows.ShowDetails(vm, detail);
                var window = Views.HistoryWindows.Shown;
                try
                {
                    var shown = (ViewModels.CommitDetail)Views.HistoryWindows.ShownDetails;
                    shown.ActiveTabIndex = 2;
                    await Check.Until("the tree to pick it", () => Views.RevisionFiles.Newest.PickedPath == Deep);
                }
                finally
                {
                    window.Destroy();
                }
            }
        }

        private static async Task KeepsCompared()
        {
            var (repo, vm, commit) = await Repo();
            using (repo)
            {
                var first = await new Commands.QuerySingleCommit(repo.Path, "HEAD~").GetResultAsync();
                var compare = new ViewModels.RevisionCompare(vm, first, commit);
                await Check.Until("the changes", () => compare.ChangeSelection.Count == 1);
                compare.ChangeSelection = new(compare.VisibleChanges.Where(c => c.Path == Deep).ToList());

                await Views.HistoryWindows.ShowDetails(vm, compare);
                var window = Views.HistoryWindows.Shown;
                try
                {
                    var shown = (ViewModels.RevisionCompare)Views.HistoryWindows.ShownDetails;
                    await Check.Until("the changes", () => shown.VisibleChanges is { Count: 2 });
                    Check.Equal(Deep, shown.ChangeSelection.Changes.Single().Path, "the change");
                }
                finally
                {
                    window.Destroy();
                }
            }
        }
    }
}
