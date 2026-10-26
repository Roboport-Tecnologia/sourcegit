using System;
using System.ComponentModel;

using Avalonia.Collections;
using Mitsuamia;

using static SourceGit.Views.Settings;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's custom action controls (<c>Views/ConfigureCustomActionControls.axaml</c>):
    /// a sheet over the settings window where upstream has a dialog, 560
    /// wide: the controls beside the picked one's fields, which follow its
    /// type as upstream's do.
    /// </summary>
    public static class CustomActionControls
    {
        public static void Show(Window over, AvaloniaList<Models.CustomActionControl> controls)
        {
            var vm = new ViewModels.ConfigureCustomActionControls(controls);
            var title = App.Text("ConfigureCustomActionControls");
            var sheet = new Window(over, title, 560);

            var column = new Column { Padding = 16, Gap = 12, AlignItems = Align.Stretch };
            column.Add(new Text(title) { Style = TextStyle.Headline });
            var list = ListDetail(controls, c => c.Label, nameof(Models.CustomActionControl.Label), "Parameter", Control,
                new ListActions<Models.CustomActionControl>
                {
                    Add = () =>
                    {
                        vm.Add();
                        return vm.Edit;
                    },
                    Remove = control =>
                    {
                        vm.Edit = control;
                        vm.Remove();
                    },
                    Move = controls.Move,
                });
            column.Add(list);
            var buttons = new Row { Justify = Justify.End };
            var close = new Button(Popups.ButtonText("Close")) { Role = ButtonRole.Default };
            close.Click += sheet.Destroy;
            buttons.Add(close);
            column.Add(buttons);
            sheet.Content = column;
        }

        /// <summary>A control's fields: its label, type and description,
        /// then what its type takes.</summary>
        private static Node Control(Models.CustomActionControl control)
        {
            var column = new Column { Gap = 12, AlignItems = Align.Stretch };
            column.Add(Stacked(App.Text("ConfigureCustomActionControls.Label"), TextValue(control.Label, v => control.Label = v)));
            string[] types = ["TextBox", "Path Selector", "CheckBox", "ComboBox", "Local Branch Selector", "Remote Branch Selector"];
            column.Add(Stacked(App.Text("ConfigureCustomActionControls.Type"), Choice(App.Text("ConfigureCustomActionControls.Type"),
                types, (int)control.Type, i => control.Type = (Models.CustomActionControlType)i)));
            column.Add(Stacked(App.Text("ConfigureCustomActionControls.Description"),
                TextValue(control.Description, v => control.Description = v)));
            var typed = new Column { Gap = 12, AlignItems = Align.Stretch };
            column.Add(typed);
            Node shown = null;
            void Show()
            {
                shown?.Destroy();
                shown = Typed(control);
                typed.Add(shown);
            }
            Show();
            void OnType(object sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(Models.CustomActionControl.Type))
                    Application.Post(Show);
            }
            control.PropertyChanged += OnType;
            column.Forgotten += () => control.PropertyChanged -= OnType;
            return column;
        }

        /// <summary>Upstream's fields for each type: a value (the checked
        /// value, the options or the default) with its tip, a text box's
        /// formatter, and the flag a check box, path or remote branch takes.</summary>
        private static Node Typed(Models.CustomActionControl control)
        {
            var type = control.Type;
            var column = new Column { Gap = 12, AlignItems = Align.Stretch };
            var valueLabel = type switch
            {
                Models.CustomActionControlType.CheckBox => "ConfigureCustomActionControls.CheckedValue",
                Models.CustomActionControlType.ComboBox => "ConfigureCustomActionControls.Options",
                Models.CustomActionControlType.TextBox or Models.CustomActionControlType.PathSelector => "ConfigureCustomActionControls.DefaultValue",
                _ => null,
            };
            var tip = type switch
            {
                Models.CustomActionControlType.CheckBox => "ConfigureCustomActionControls.CheckedValue.Tip",
                Models.CustomActionControlType.ComboBox => "ConfigureCustomActionControls.Options.Tip",
                Models.CustomActionControlType.TextBox or Models.CustomActionControlType.PathSelector => "ConfigureCustomActionControls.StringValue.Tip",
                _ => null,
            };
            if (valueLabel != null)
            {
                var value = Stacked(App.Text(valueLabel), TextValue(control.StringValue, v => control.StringValue = v));
                value.Add(Tip(App.Text(tip)));
                column.Add(value);
            }
            if (type == Models.CustomActionControlType.TextBox)
            {
                var formatter = Stacked(App.Text("ConfigureCustomActionControls.StringFormatter"),
                    TextValue(control.StringFormatter, v => control.StringFormatter = v));
                formatter.Add(Tip(App.Text("ConfigureCustomActionControls.StringFormatter.Tip")));
                column.Add(formatter);
            }
            var flag = type switch
            {
                Models.CustomActionControlType.CheckBox => "ConfigureCustomActionControls.DefaultValue",
                Models.CustomActionControlType.PathSelector => "ConfigureCustomActionControls.IsFolder",
                Models.CustomActionControlType.RemoteBranchSelector => "ConfigureCustomActionControls.UseFriendlyName",
                _ => null,
            };
            if (flag != null)
                column.Add(Check(App.Text(flag).TrimEnd(':', ' '), control.BoolValue, v => control.BoolValue = v));
            return column;
        }

        private static Text Tip(string text) =>
            new(text) { Style = TextStyle.Caption, Color = Color.SecondaryLabel };
    }
}
