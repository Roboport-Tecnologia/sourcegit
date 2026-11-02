using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's merge editor (<c>Views/MergeConflictEditor.axaml</c>), on its own view model:
    /// the file and the conflicts left, mine and theirs over the result in the code view, then
    /// saving and staging. Upstream's buttons over the chunk under the pointer would draw over
    /// the editor, so here they're above it, and act on the conflict the cursor is in; the
    /// plugin's menu and keys do the same.
    /// </summary>
    public static class MergeEditor
    {
        /// <summary>The window last opened, while it's open: for tests.</summary>
        public static Window Shown { get; private set; }

        /// <summary>What it shows, for tests.</summary>
        internal static CodeView Code { get; private set; }

        public static void Show(ViewModels.Repository repo, ViewModels.MergeConflictEditor vm)
        {
            var window = Shown = new Window(App.Text("MergeConflictEditor.Title"), 1280, 800);
            window.Closed += () =>
            {
                if (Shown == window)
                    Shown = null;
            };
            // Upstream asks once something was chosen, as nothing's written until it's saved.
            window.CanClose = () => CanClose(window, vm);

            // Closing it from here reports no `Closed`.
            void Close()
            {
                if (Shown == window)
                    Shown = null;
                window.Destroy();
            }
            var column = new Column { AlignItems = Align.Stretch, Gap = Spacing.Sm, Padding = Spacing.Md };
            column.Grow = 1;

            var code = Code = new CodeView();
            // The conflict the cursor is in, for the buttons; -1 for none.
            var at = -1;
            var atResolved = false;

            var mine = Choice("MergeConflictEditor.UseMine", Models.ConflictResolution.UseOurs);
            var theirs = Choice("MergeConflictEditor.UseTheirs", Models.ConflictResolution.UseTheirs);
            var both = new MenuButton(Popups.ButtonText("MergeConflictEditor.UseBoth"))
            {
                Menu =
                [
                    new MenuItem(App.Text("MergeConflictEditor.AcceptBoth.MineFirst"), () => Choose(at, Models.ConflictResolution.UseBothMineFirst)),
                    new MenuItem(App.Text("MergeConflictEditor.AcceptBoth.TheirsFirst"), () => Choose(at, Models.ConflictResolution.UseBothTheirsFirst)),
                ],
            };
            var undo = Choice("MergeConflictEditor.Undo", Models.ConflictResolution.None);

            Button Choice(string text, Models.ConflictResolution resolution)
            {
                var button = new Button(Popups.ButtonText(text));
                button.Click += () => Choose(at, resolution);
                return button;
            }

            void Choose(int index, Models.ConflictResolution resolution)
            {
                if (index < 0 || index >= vm.ConflictRegions.Count)
                    return;
                vm.SelectedChunk = new Models.ConflictSelectedChunk(0, 0, index, Models.ConflictPanelType.Result, vm.ConflictRegions[index].IsResolved);
                vm.Resolve(resolution);
                vm.SelectedChunk = null;
                code.ShowConflict(Json(vm));
            }

            void ShowButtons()
            {
                mine.IsEnabled = theirs.IsEnabled = both.IsEnabled = at >= 0 && !atResolved;
                undo.IsEnabled = at >= 0 && atResolved;
            }
            ShowButtons();

            var top = new Row { AlignItems = Align.Center, Gap = Spacing.Sm };
            top.Add(new Icon(Icons.Get("File")) { Size = 14 });
            var path = new Text(vm.FilePath) { MaxLines = 1 };
            path.Grow = 1;
            path.Shrink = 1;
            path.MinWidth = 0;
            top.Add(path);
            foreach (var choice in new Node[] { mine, theirs, both, undo })
                top.Add(choice);
            var prev = IconButton("Up", "MergeConflictEditor.PrevConflict", "prev");
            var next = IconButton("Down", "MergeConflictEditor.NextConflict", "next");
            Button IconButton(string icon, string tip, string where)
            {
                var button = new Button(App.Text(tip))
                {
                    Icon = Icons.Get(icon, 12),
                    IconOnly = true,
                    Style = ButtonStyle.Borderless,
                    ToolTip = App.Text(tip),
                };
                button.Click += () => code.CallPlugin("sourcegit-conflict", "go_to", new JsonObject { ["where"] = where }.ToJsonString());
                return button;
            }
            top.Add(prev);
            top.Add(next);
            var left = new Text();
            top.Add(left);
            column.Add(top);
            column.Add(new Separator());

            // Each side's commit, over its half of the panes.
            var sides = new Row { AlignItems = Align.Center, Gap = Spacing.Sm };
            sides.Add(SideOf("MergeConflictEditor.Mine", repo, vm.Mine));
            sides.Add(SideOf("MergeConflictEditor.Theirs", repo, vm.Theirs));
            column.Add(sides);

            code.Node.Grow = 1;
            code.Node.MinHeight = 0;
            column.Add(code.Node);

            var error = new Text { Color = Color.Error, MaxLines = 2 };
            error.Shrink = 1;
            error.MinWidth = 0;
            var buttons = new Row { AlignItems = Align.Center, Gap = Metrics.DialogButtonGap };
            var space = new Column();
            space.Grow = 1;
            buttons.Add(error);
            buttons.Add(space);
            var close = new Button(Popups.ButtonText("Close")) { Role = ButtonRole.Cancel };
            close.Click += async () =>
            {
                if (await CanClose(window, vm))
                    Close();
            };
            var save = new Button(Popups.ButtonText("MergeConflictEditor.SaveAndStage")) { Role = ButtonRole.Default };
            save.Click += async () =>
            {
                if (await vm.SaveAndStageAsync())
                    Close();
            };
            buttons.Add(close);
            buttons.Add(save);
            column.Add(buttons);
            window.Content = column;

            Bind.OneWay(vm, nameof(vm.UnsolvedCount), () => vm.UnsolvedCount, count =>
            {
                left.Value = count == 0
                    ? App.Text("MergeConflictEditor.AllResolved")
                    : App.Text("MergeConflictEditor.ConflictsRemaining", count);
                prev.IsEnabled = next.IsEnabled = count > 0;
                save.IsEnabled = count == 0;
            }, owner: left);
            Bind.OneWay(vm, nameof(vm.Error), () => vm.Error, text => error.Value = text ?? "", owner: error);

            code.PluginEvent += (name, json) =>
            {
                var payload = JsonNode.Parse(json);
                switch (name)
                {
                    case "conflict_at":
                        at = payload["index"].GetValue<int>();
                        atResolved = payload["resolved"].GetValue<bool>();
                        ShowButtons();
                        break;
                    case "conflict_choose":
                        Choose(payload["index"].GetValue<int>(), payload["choice"].GetValue<string>() switch
                        {
                            "ours" => Models.ConflictResolution.UseOurs,
                            "theirs" => Models.ConflictResolution.UseTheirs,
                            "both_mine_first" => Models.ConflictResolution.UseBothMineFirst,
                            "both_theirs_first" => Models.ConflictResolution.UseBothTheirsFirst,
                            _ => Models.ConflictResolution.None,
                        });
                        break;
                }
            };
            code.ShowConflict(Json(vm));
        }

        /// <summary>A field, so tests can answer without a dialog.</summary>
        internal static Func<Window, string, Task<bool>> Confirm { get; set; } = async (window, question) =>
            await Dialogs.Alert(window, question, "", [Popups.ButtonText("Sure"), Popups.ButtonText("Cancel")],
                AlertStyle.Warning, destructive: 0) == 0;

        private static async Task<bool> CanClose(Window window, ViewModels.MergeConflictEditor vm) =>
            vm.UnsolvedCount == vm.ConflictRegions.Count
            || await Confirm(window, App.Text("MergeConflictEditor.UnsavedChanges"));

        private static Node SideOf(string label, ViewModels.Repository repo, object side)
        {
            var row = new Row { AlignItems = Align.Center, Gap = Spacing.Xs };
            row.Grow = 1;
            row.Basis = 0;
            row.MinWidth = 0;
            row.Add(new Text(App.Text(label)) { Weight = FontWeight.Bold, Color = Color.SecondaryLabel });
            var what = new Row { AlignItems = Align.Center, Gap = Spacing.Xs };
            what.Shrink = 1;
            what.MinWidth = 0;
            Conflict.Side(what, repo, side);
            row.Add(what);
            return row;
        }

        /// <summary>What <c>sourcegit-conflict</c> takes. Rows are 1-based, and a region's are
        /// from upstream's line states, which go with the rows it gives.</summary>
        internal static string Json(ViewModels.MergeConflictEditor vm)
        {
            var regions = new JsonArray();
            var first = 0;
            for (var i = 0; i < vm.ResultLines.Count; i++)
            {
                var state = vm.GetLineState(i);
                if (state is Models.ConflictLineState.ConflictBlockStart or Models.ConflictLineState.ResolvedBlockStart)
                    first = i + 1;
                else if (state is Models.ConflictLineState.ConflictBlockEnd or Models.ConflictLineState.ResolvedBlockEnd)
                    regions.Add(new JsonObject
                    {
                        ["first"] = first,
                        ["last"] = i + 1,
                        ["resolved"] = state == Models.ConflictLineState.ResolvedBlockEnd,
                    });
            }
            return new JsonObject
            {
                ["path"] = vm.FilePath,
                ["ours"] = Pane(vm.OursLines),
                ["theirs"] = Pane(vm.TheirsLines),
                ["result"] = Pane(vm.ResultLines),
                ["regions"] = regions,
                ["labels"] = new JsonObject
                {
                    ["ours"] = Popups.ButtonText("MergeConflictEditor.UseMine"),
                    ["theirs"] = Popups.ButtonText("MergeConflictEditor.UseTheirs"),
                    ["both_mine_first"] = App.Text("MergeConflictEditor.AcceptBoth.MineFirst"),
                    ["both_theirs_first"] = App.Text("MergeConflictEditor.AcceptBoth.TheirsFirst"),
                    ["undo"] = Popups.ButtonText("MergeConflictEditor.Undo"),
                    ["result"] = App.Text("MergeConflictEditor.Result"),
                    ["mine_pane"] = App.Text("MergeConflictEditor.Mine"),
                    ["theirs_pane"] = App.Text("MergeConflictEditor.Theirs"),
                },
            }.ToJsonString();
        }

        private static JsonArray Pane(List<Models.ConflictLine> lines)
        {
            var out_ = new JsonArray();
            foreach (var line in lines)
            {
                out_.Add(new JsonObject
                {
                    ["kind"] = line.Type switch
                    {
                        Models.ConflictLineType.Common => "common",
                        Models.ConflictLineType.Marker => "marker",
                        Models.ConflictLineType.Ours => "ours",
                        Models.ConflictLineType.Theirs => "theirs",
                        _ => "none",
                    },
                    // A CRLF file's lines end in a CR here, which a line of the editor can't
                    // hold. Saving takes them from the file, so it keeps them.
                    ["text"] = line.Content.TrimEnd('\r'),
                    ["number"] = line.LineNumber,
                });
            }
            return out_;
        }
    }
}
