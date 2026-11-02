using System;
using System.IO;
using System.Text.Json.Nodes;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// A commit message in the code view's editor, by the <c>sourcegit-message</c> plugin. The
    /// text is here, and the editor's copy follows it. Its file is empty and only there because
    /// an editor that edits needs one: named so bemtvi knows the kind of file, in its own
    /// temporary directory, gone with the node.
    /// </summary>
    public sealed class MessageEditor
    {
        public Node Node => _code.Node;

        /// <summary>What was typed.</summary>
        public event Action<string> Changed;

        /// <summary>A chord the plugin passes on, as the editor takes every key:
        /// <c>commit</c>, <c>commit_stage</c>, <c>commit_push</c>, <c>stage_all</c> or
        /// <c>unstage_all</c>.</summary>
        public event Action<string> Action;

        /// <summary>The message. Setting it replaces what's in the editor.</summary>
        public string Text
        {
            get => _text;
            set
            {
                value ??= "";
                if (value == _text)
                    return;
                _text = value;
                _revision++;
                _code.ShowMessage(Json());
            }
        }

        /// <summary>The editor, for tests.</summary>
        internal CodeView Code => _code;

        private readonly CodeView _code;
        private readonly string _placeholder;
        private string _text;
        // Counts the texts sent to the editor. What it says it typed on an older one was in
        // flight as this replaced it (a commit clearing the message), and is dropped.
        private int _revision;

        public MessageEditor(string text, string placeholder)
        {
            _text = text ?? "";
            _placeholder = placeholder;
            var directory = Directory.CreateTempSubdirectory("sourcegit-message-").FullName;
            var file = Path.Combine(directory, "COMMIT_EDITMSG");
            File.WriteAllText(file, "");
            _code = new CodeView(file);
            _code.PluginEvent += OnPluginEvent;
            _code.Node.Forgotten += () =>
            {
                try
                {
                    Directory.Delete(directory, true);
                }
                catch (IOException e)
                {
                    Native.OS.LogException(e);
                }
            };
            _code.ShowMessage(Json());
        }

        private string Json() => new JsonObject
        {
            ["text"] = _text,
            ["revision"] = _revision,
            ["placeholder"] = _placeholder,
        }.ToJsonString();

        private void OnPluginEvent(string name, string json)
        {
            var payload = JsonNode.Parse(json);
            switch (name)
            {
                case "message_changed":
                    if (payload["revision"].GetValue<int>() < _revision)
                        return;
                    _text = payload["text"].GetValue<string>();
                    _code.KeepMessage(Json());
                    Changed?.Invoke(_text);
                    break;
                case "message_action":
                    Action?.Invoke(payload["action"].GetValue<string>());
                    break;
            }
        }
    }
}
