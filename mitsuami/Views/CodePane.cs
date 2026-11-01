using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// A pane's editor, kept from one file to the next: starting one for each pick is what
    /// switching files would otherwise cost, and a grammar it was installing would go with
    /// it. A node can't move once placed, so a pane is <see cref="Top"/>, for a header and
    /// whatever isn't text, over the editor, which stays there hidden.
    /// </summary>
    public sealed class CodePane
    {
        public Column Node { get; }

        /// <summary>What's above the editor, or all there is while it's hidden.</summary>
        public Column Top { get; }

        /// <summary>Whether the editor is what shows.</summary>
        public bool ShowsCode { get; private set; }

        /// <summary>What a plugin in its editor said (its name and JSON).</summary>
        public event System.Action<string, string> PluginEvent;

        /// <summary>The editor, if it has started and shows.</summary>
        public CodeView Showing => ShowsCode ? _code : null;

        private CodeView _code;

        public CodePane()
        {
            Node = new Column { AlignItems = Align.Stretch };
            Node.Grow = 1;
            Node.MinHeight = 0;
            Node.MinWidth = 0;
            Top = new Column { AlignItems = Align.Stretch };
            Top.Grow = 1;
            Top.MinHeight = 0;
            Node.Add(Top);
        }

        /// <summary>Shows the editor, started if this is its first time.</summary>
        public CodeView Code()
        {
            if (_code == null)
            {
                _code = new CodeView();
                _code.PluginEvent += (name, json) => PluginEvent?.Invoke(name, json);
                Node.Add(_code.Node);
            }
            _code.Node.IsVisible = true;
            Top.Grow = 0;
            ShowsCode = true;
            return _code;
        }

        /// <summary>Hides the editor, for what isn't text.</summary>
        public void HideCode()
        {
            if (_code != null)
                _code.Node.IsVisible = false;
            Top.Grow = 1;
            ShowsCode = false;
        }
    }
}
