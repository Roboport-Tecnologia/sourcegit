using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// The code view: an embedded bemtvi, from <c>codeview/</c>. Its features are
    /// Lua plugins in <c>codeview/editor/</c>, this class only carries calls to them
    /// as JSON. An editor that quits (<c>:q</c>) comes back with what it showed: the
    /// pane it's in is the app's to close.
    /// </summary>
    public sealed unsafe partial class CodeView
    {
        private const string Lib = "mitsuami_ffi";

        [StructLayout(LayoutKind.Sequential)]
        private struct Str
        {
            public byte* Ptr;
            public nuint Len;
        }

        [LibraryImport(Lib, EntryPoint = "sourcegit_codeview_create")]
        private static partial uint Create(Str config, delegate* unmanaged[Cdecl]<nint, Str, Str, void> onEvent, nint user);

        [LibraryImport(Lib, EntryPoint = "sourcegit_codeview_call")]
        [return: MarshalAs(UnmanagedType.U1)]
        private static partial bool Call(uint id, Str module, Str function, Str json, delegate* unmanaged[Cdecl]<nint, byte, Str, void> done, nint user);

        // MITSUAMI_FFI names the library while developing, as for mitsuamia:
        // the same file, so the same library.
        [ModuleInitializer]
        internal static void Resolve()
        {
            NativeLibrary.SetDllImportResolver(typeof(CodeView).Assembly, (name, assembly, path) =>
            {
                if (name != Lib)
                    return 0;
                var file = Environment.GetEnvironmentVariable("MITSUAMI_FFI");
                if (file != null && NativeLibrary.TryLoad(file, out var handle))
                    return handle;
                return NativeLibrary.TryLoad(name, assembly, path, out handle) ? handle : 0;
            });
        }

        /// <summary>Fills what its parent has left.</summary>
        public Node Node { get; }

        private static readonly List<CodeView> s_live = [];

        // Across editors, as each diff has its own: a failed install isn't run again for
        // every file. Until the next launch, which may have the network back.
        private static readonly HashSet<string> s_triedGrammars = [];

        private ForeignNode _editor;
        private GCHandle _self;
        private (string Module, string Function, string Json)? _showing;

        public CodeView()
        {
            var holder = new Column { AlignItems = Align.Stretch };
            holder.Grow = 1;
            holder.MinHeight = 0;
            holder.MinWidth = 0;
            Node = holder;
            _self = GCHandle.Alloc(this);
            s_live.Add(this);
            holder.Forgotten += () =>
            {
                s_live.Remove(this);
                _editor = null;
                _self.Free();
            };
            Start();
        }

        /// <summary>Upstream's text diff, by the <c>sourcegit-diff</c> plugin.</summary>
        /// <param name="actions">What can be done to its lines, as the plugin's menu shows them
        /// (<c>stage</c>, <c>unstage</c> or <c>discard</c>, with their labels).</param>
        public void ShowDiff(string path, Models.TextDiff diff, params (string Action, string Label)[] actions)
        {
            var spec = JsonNode.Parse(DiffJson(path, diff)).AsObject();
            var entries = new JsonArray();
            foreach (var (action, label) in actions)
                entries.Add(new JsonObject { ["action"] = action, ["label"] = label });
            spec["actions"] = entries;
            _showing = ("sourcegit-diff", "show", spec.ToJsonString());
            Send();
        }

        /// <summary>A file as a revision has it, by the <c>sourcegit-file</c> plugin.</summary>
        public void ShowFile(string path, string text)
        {
            _showing = ("sourcegit-file", "show", new JsonObject { ["path"] = path, ["text"] = text }.ToJsonString());
            Send();
        }

        /// <summary>The diff's cursor to a change: first, prev, next or last.</summary>
        public void GoToChange(string where) =>
            CallPlugin("sourcegit-diff", "go_to", new JsonObject { ["where"] = where }.ToJsonString());

        /// <summary>What a plugin said (its name and JSON), on the UI thread.</summary>
        public event Action<string, string> PluginEvent;

        /// <summary>Upstream's blame, by the <c>sourcegit-blame</c> plugin.</summary>
        public void ShowBlame(string path, Models.BlameData blame)
        {
            var lines = new JsonArray();
            foreach (var line in blame.LineInfos)
            {
                lines.Add(new JsonObject
                {
                    ["sha"] = line.CommitSHA,
                    ["author"] = line.Author,
                    ["time"] = Models.DateTimeFormat.Format(line.Timestamp, true),
                    ["file"] = line.File,
                    ["first"] = line.IsFirstInGroup,
                });
            }
            _showing = ("sourcegit-blame", "show", new JsonObject { ["path"] = path, ["text"] = blame.Content, ["lines"] = lines }.ToJsonString());
            Send();
        }

        /// <summary>What <c>sourcegit-diff</c> takes; <c>codeview/fixtures/</c>
        /// has examples. Its spans are 0-based, end-exclusive bytes, where
        /// upstream's are inclusive UTF-16 units.</summary>
        internal static string DiffJson(string path, Models.TextDiff diff)
        {
            var lines = new JsonArray();
            foreach (var line in diff.Lines)
            {
                var entry = new JsonObject
                {
                    ["kind"] = line.Type switch
                    {
                        Models.TextDiffLineType.Added => "added",
                        Models.TextDiffLineType.Deleted => "deleted",
                        Models.TextDiffLineType.Indicator => "indicator",
                        _ => "normal",
                    },
                    // Git's text may end in a CR, which a line of the editor can't hold.
                    ["text"] = line.Content.TrimEnd('\r', '\n'),
                    ["old"] = line.OldLineNumber,
                    ["new"] = line.NewLineNumber,
                };
                if (line.Highlights.Count > 0)
                {
                    var spans = new JsonArray();
                    foreach (var range in line.Highlights)
                    {
                        var end = Math.Min(range.End + 1, line.Content.Length);
                        var start = Math.Min(range.Start, end);
                        var first = Encoding.UTF8.GetByteCount(line.Content.AsSpan(0, start));
                        spans.Add(new JsonArray(first, Encoding.UTF8.GetByteCount(line.Content.AsSpan(0, end))));
                    }
                    entry["spans"] = spans;
                }
                lines.Add(entry);
            }
            return new JsonObject { ["path"] = path, ["lines"] = lines }.ToJsonString();
        }

        /// <summary>The Code View preferences changed: the editors showing
        /// follow them at once.</summary>
        public static void SettingsChanged()
        {
            foreach (var view in s_live)
                view.ApplySettings();
        }

        /// <summary>What <c>sourcegit-settings</c> takes.</summary>
        internal static string SettingsJson()
        {
            var pref = ViewModels.Preferences.Instance;
            var port = PortSettings.Instance;
            return new JsonObject
            {
                ["mode"] = port.EditorMode,
                ["font"] = pref.MonospaceFontFamily ?? "",
                ["font_size"] = pref.EditorFontSize,
                ["colorscheme"] = port.EditorColorscheme,
                ["tab_width"] = pref.EditorTabWidth,
                ["relative_numbers"] = port.EditorRelativeNumbers,
            }.ToJsonString();
        }

        /// <summary>What <c>sourcegit-grammars</c> takes.</summary>
        internal static string GrammarsJson()
        {
            var tried = new JsonArray();
            foreach (var lang in s_triedGrammars)
                tried.Add(lang);
            return new JsonObject { ["enabled"] = PortSettings.Instance.EditorInstallGrammars, ["tried"] = tried }.ToJsonString();
        }

        private void ApplySettings()
        {
            CallPlugin("sourcegit-settings", "apply", SettingsJson());
            CallPlugin("sourcegit-grammars", "configure", GrammarsJson());
            CallPlugin("sourcegit-diff", "layout", LayoutJson());
        }

        /// <summary>What <c>sourcegit-diff</c>'s layout takes: the diff view's
        /// toolbar, which the editors showing follow at once.</summary>
        internal static string LayoutJson()
        {
            var port = PortSettings.Instance;
            return new JsonObject
            {
                ["context"] = port.EditorDiffContext,
                ["all"] = port.EditorDiffAllLines,
                ["side_by_side"] = ViewModels.Preferences.Instance.UseSideBySideDiff,
                ["wrap"] = ViewModels.Preferences.Instance.EnableDiffViewWordWrap,
                ["syntax"] = ViewModels.Preferences.Instance.UseSyntaxHighlighting,
                ["hidden_symbols"] = ViewModels.Preferences.Instance.ShowHiddenSymbolsInDiffView,
            }.ToJsonString();
        }

        private void Start()
        {
            var config = Path.Combine(AppContext.BaseDirectory, "codeview-editor");
            var id = WithStr(config, s => Create(s, &Heard, GCHandle.ToIntPtr(_self)));
            _editor = new ForeignNode(id);
            _editor.Grow = 1;
            _editor.MinHeight = 0;
            ((Column)Node).Add(_editor);
            ApplySettings();
            Send();
        }

        private void Send()
        {
            if (_showing is { } call)
                CallPlugin(call.Module, call.Function, call.Json);
        }

        private void CallPlugin(string module, string function, string json)
        {
            if (_editor == null)
                return;
            var id = _editor.Id;
            ForeignNode.Check(WithStr(module, m => WithStr(function, f => WithStr(json, j =>
                Call(id, m, f, j, &Done, 0)))));
        }

        private static T WithStr<T>(string text, Func<Str, T> f)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            fixed (byte* p = bytes)
                return f(new Str { Ptr = p, Len = (nuint)bytes.Length });
        }

        private static string Text(Str s) => Encoding.UTF8.GetString(s.Ptr, (int)s.Len);

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static void Heard(nint user, Str name, Str json)
        {
            var view = (CodeView)GCHandle.FromIntPtr(user).Target;
            var what = Text(name);
            var payload = Text(json);
            // After the editor's own call, which is still on the stack.
            Application.Post(() => view.OnEvent(what, payload));
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static void Done(nint user, byte ok, Str text)
        {
            if (ok == 0)
                Native.OS.LogException(new InvalidOperationException($"Code view: {Text(text)}"));
        }

        private void OnEvent(string name, string json)
        {
            if (_editor == null)
                return;
            PluginEvent?.Invoke(name, json);
            switch (name)
            {
                case "exit":
                    _editor.Destroy();
                    _editor = null;
                    Start();
                    break;
                // The others remember this language had no grammar.
                case "grammar_ready":
                    foreach (var other in s_live)
                    {
                        if (other != this)
                            other.CallPlugin("sourcegit-grammars", "ready", json);
                    }
                    break;
                case "grammar_install":
                    if (JsonNode.Parse(json)?["lang"]?.GetValue<string>() is { } lang)
                        s_triedGrammars.Add(lang);
                    break;
                case "error":
                    Native.OS.LogException(new InvalidOperationException($"Code view: {json}"));
                    break;
            }
        }
    }
}
