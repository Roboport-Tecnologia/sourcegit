using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using Mitsuamia;

namespace SourceGit.Tests
{
    /// <summary>The blame window over the real editor, on real git's blame.</summary>
    public static class BlameTests
    {
        public static IEnumerable<(string, Func<Task>)> All() =>
        [
            ("blame: shows the file, says its line's commit, and goes back through history", GoesBack),
        ];

        private static async Task GoesBack()
        {
            PortSettings.Instance.EditorMode = "modeless";
            using var repo = new TestRepo();
            repo.ChangeTwoPlaces();
            repo.Git($"add {TestRepo.File1}");
            repo.Git("commit -q -m second");
            var head = await new Commands.QuerySingleCommit(repo.Path, "HEAD").GetResultAsync();
            var first = repo.Git("rev-parse HEAD~").Trim();

            var live = Views.CodeView.Live.Count;
            Views.Blame.Show(new ViewModels.Blame(repo.Path, TestRepo.File1, head));
            var window = Views.Blame.Shown;
            try
            {
                await Check.Until("the editor to start", () => Views.CodeView.Live.Count > live);
                var events = new List<(string Name, JsonNode Payload)>();
                Views.CodeView.Live[^1].PluginEvent += (name, json) => events.Add((name, JsonNode.Parse(json)));
                await Check.Until("the blame to show", () => events.Any(e => e.Name == "shown"));
                Check.Equal(40, events.Last(e => e.Name == "shown").Payload["lines"].GetValue<int>(), "lines");
                await Check.Until("the first line's commit", () => events.Any(e => e.Name == "blame_line"));
                var line = events.Last(e => e.Name == "blame_line").Payload;
                Check.That(first.StartsWith(line["sha"].GetValue<string>(), StringComparison.Ordinal),
                    $"line 1 is from {first}, it said {line["sha"]}");

                // Line 5 is the second commit's: blame at its commit shows the line it replaced.
                var shown = events.Count(e => e.Name == "shown");
                Host.Press(window, "Blame This Commit");
                await Check.Until("the older blame", () => events.Count(e => e.Name == "shown") > shown);
                Check.Equal(40, events.Last(e => e.Name == "shown").Payload["lines"].GetValue<int>(), "lines");
            }
            finally
            {
                window.Destroy();
            }
        }
    }
}
