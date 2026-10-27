using System.Linq;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's SSH key helper (<c>Views/SSHKeyHelper.axaml</c>), a 640 ×
    /// 400 sheet as upstream's dialog: the key pairs in <c>~/.ssh</c> beside
    /// the picked one's path, fingerprint and public key; adding generates
    /// one with <c>ssh-keygen</c> (upstream's generator, a sheet over it),
    /// removing deletes both files once confirmed.
    /// </summary>
    public static class SSHKeyHelper
    {
        /// <summary>The sheet showing, if any: for captures.</summary>
        public static Window Shown { get; private set; }

        public static void Show()
        {
            if (Shown != null)
                return;
            var vm = new ViewModels.SSHKeyHelper();
            var title = App.Text("SSHKeyHelper.Title");
            var sheet = Shown = new Window(App.MainWindow, title, 640, 400);
            sheet.Closed += () => Shown = null;

            var column = new Column { Padding = Spacing.Lg, Gap = Spacing.Md, AlignItems = Align.Stretch };
            column.Grow = 1;
            column.Add(new Text(title) { Style = TextStyle.Headline });

            var row = new Row { Gap = Spacing.Lg, AlignItems = Align.Stretch };
            row.Grow = 1;
            row.MinHeight = 0;
            var left = new Column { Gap = Spacing.Xs, AlignItems = Align.Stretch };
            left.Width = 200;
            left.Shrink = 0;
            var keys = new ListView<Models.SSHKeyPair>(k => k.PrivateKeyPath, k =>
            {
                var line = new Row { Gap = Spacing.Sm, AlignItems = Align.Center, Padding = new Thickness(Spacing.None, Spacing.Xs) };
                line.Add(new Icon(Icons.Get("Password")) { Size = 14 });
                var name = new Text(k.Name) { MaxLines = 1 };
                name.Shrink = 1;
                name.MinWidth = 0;
                line.Add(name);
                return line;
            }, itemAnimations: false)
            {
                Style = ListStyle.Framed,
                EstimatedRowHeight = 26,
                Items = vm.Keys,
            };
            keys.Grow = 1;
            keys.MinHeight = 0;
            left.Add(keys);
            var buttons = new Row { AlignItems = Align.Center, Gap = Icons.ButtonGap(Spacing.None) };
            var add = Settings.Small("Plus", App.Text("SSHKeyHelper.Generator"));
            add.IsEnabled = true;
            add.Click += () => Generate(sheet, vm);
            buttons.Add(add);
            var remove = Settings.Small("Minus", "Remove");
            remove.Click += () => _ = Delete(vm);
            buttons.Add(remove);
            left.Add(buttons);
            row.Add(left);

            var right = new Column { AlignItems = Align.Stretch, Justify = Justify.Center };
            right.Grow = 1;
            right.MinWidth = 0;
            row.Add(right);
            column.Add(row);

            Node shown = null;
            Bind.OneWay(vm, nameof(vm.SelectedKey), () => vm.SelectedKey, key =>
            {
                shown?.Destroy();
                shown = key == null
                    ? new Icon(Icons.Get("Empty")) { Size = 64, Color = Color.SecondaryLabel, AlignSelf = Align.Center }
                    : Detail(key);
                right.Add(shown);
                remove.IsEnabled = key != null;
                if (keys.SelectedItem != key)
                    keys.SelectedItems = key != null ? [key] : [];
            }, owner: sheet);
            keys.SelectionChanged += selected => vm.SelectedKey = selected.Count > 0 ? selected[0] : null;

            var footer = new Row { Justify = Justify.End };
            var close = new Button(Popups.ButtonText("Close")) { Role = ButtonRole.Default };
            // `Closed` is the user's closing only, not this.
            close.Click += () =>
            {
                sheet.Destroy();
                Shown = null;
            };
            footer.Add(close);
            column.Add(footer);
            sheet.Content = column;
        }

        /// <summary>Upstream's key form: its path and fingerprint, which can
        /// be selected, and its public key to copy.</summary>
        private static Node Detail(Models.SSHKeyPair key)
        {
            var column = new Column { Gap = Spacing.Xs, AlignItems = Align.Stretch };
            column.Grow = 1;
            void Add(string label, Node value, bool first = false)
            {
                var heading = new Text(App.Text(label)) { Weight = FontWeight.Bold, Color = Color.SecondaryLabel };
                if (!first)
                    heading.Margin = new Thickness(Spacing.Md, Spacing.None, Spacing.None, Spacing.None);
                column.Add(heading);
                column.Add(value);
            }
            Add("SSHKeyHelper.Path", new Text(key.PrivateKeyPath) { Selectable = true }, first: true);
            Add("SSHKeyHelper.Fingerprint", new Text(key.Fingerprint) { Selectable = true, Style = TextStyle.Monospace });
            var publicKey = new TextArea { Value = key.RawPublicKey, ReadOnly = true, Style = TextStyle.Monospace };
            publicKey.Height = 80;
            Add("SSHKeyHelper.PublicKey", publicKey);
            return column;
        }

        /// <summary>Upstream's question before deleting both files.</summary>
        private static async System.Threading.Tasks.Task Delete(ViewModels.SSHKeyHelper vm)
        {
            if (vm.SelectedKey is not { } key)
                return;
            var message = $"• {key.PrivateKeyPath}\n• {key.PublicKeyPath}";
            var buttons = new[] { Popups.ButtonText("Yes"), Popups.ButtonText("No") };
            if (await Dialogs.Alert(Shown, App.Text("SSHKeyHelper.ConfirmDeletion"), message, buttons, AlertStyle.Warning, destructive: 0) == 0)
                vm.DeleteSelected();
        }

        /// <summary>Upstream's generator, a sheet over the helper: the key's
        /// type, name and email, and an optional passphrase typed twice. Its
        /// checks show under the form when Generate finds them.</summary>
        private static void Generate(Window owner, ViewModels.SSHKeyHelper vm)
        {
            vm.OpenGenerator();
            var generator = vm.Generator;
            var title = App.Text("SSHKeyHelper.Generator");
            var sheet = new Window(owner, title, 500);
            sheet.Closed += vm.CloseGenerator;

            var column = new Column { Padding = Spacing.Lg, Gap = Spacing.Sm, AlignItems = Align.Stretch };
            column.Add(new Text(title) { Style = TextStyle.Headline });
            var types = Models.SSHKeyType.Supported;
            var type = new Select(App.Text("SSHKeyHelper.Generator.Type"))
            {
                Options = types.Select(t => $"{t.Name}  ({t.Description})").ToList(),
                MinWidth = 0,
            };
            type.SelectedIndex = System.Math.Max(0, types.IndexOf(generator.Type));
            type.Changed += i => generator.Type = types[i];
            column.Add(Settings.Field(App.Text("SSHKeyHelper.Generator.Type"), type));
            var name = new TextInput { Placeholder = App.Text("SSHKeyHelper.Generator.Name.Placeholder") };
            Bind.Value(name, generator, nameof(generator.Name), () => generator.Name, v => generator.Name = v);
            column.Add(Settings.Field(App.Text("SSHKeyHelper.Generator.Name"), name));
            var email = new TextInput { Placeholder = App.Text("SSHKeyHelper.Generator.Email.Placeholder") };
            Bind.Value(email, generator, nameof(generator.Email), () => generator.Email, v => generator.Email = v);
            column.Add(Settings.Field(App.Text("SSHKeyHelper.Generator.Email"), email));
            var passphrase = new PasswordInput();
            Bind.Value(passphrase, generator, nameof(generator.Passphrase), () => generator.Passphrase, v => generator.Passphrase = v);
            var passphraseRow = Settings.Field(App.Text("SSHKeyHelper.Generator.Passphrase"), passphrase);
            column.Add(passphraseRow);
            var confirm = new PasswordInput();
            Bind.Value(confirm, generator, nameof(generator.ConfirmedPassphrase), () => generator.ConfirmedPassphrase, v => generator.ConfirmedPassphrase = v);
            var confirmRow = Settings.Field(App.Text("SSHKeyHelper.Generator.ConfirmPassphrase"), confirm);
            column.Add(confirmRow);
            var use = new Checkbox(App.Text("SSHKeyHelper.Generator.UsePassphrase"));
            Bind.Checked(use, generator, nameof(generator.UsePassphrase), () => generator.UsePassphrase, v => generator.UsePassphrase = v);
            Bind.OneWay(generator, nameof(generator.UsePassphrase), () => generator.UsePassphrase,
                on => passphraseRow.IsVisible = confirmRow.IsVisible = on, owner: sheet);
            column.Add(Settings.Field("", use));

            var errors = new Text { Color = Color.Error, IsVisible = false };
            column.Add(errors);
            var buttons = new Row { Justify = Justify.End, Gap = Spacing.Sm };
            var cancel = new Button(Popups.ButtonText("Cancel"));
            cancel.Click += sheet.Destroy;
            var start = new Button(Popups.ButtonText("SSHKeyHelper.Generator.Start")) { Role = ButtonRole.Default };
            start.Click += () =>
            {
                vm.Generate();
                if (vm.Generator == null)
                {
                    sheet.Destroy();
                    return;
                }
                var messages = generator.GetErrors()
                    .Select(e => e.ErrorMessage)
                    .Append(generator.ErrorMessage)
                    .Where(m => !string.IsNullOrEmpty(m));
                errors.Value = string.Join("\n", messages);
                errors.IsVisible = true;
            };
            buttons.Add(cancel);
            buttons.Add(start);
            column.Add(buttons);
            sheet.Content = column;
        }
    }
}
