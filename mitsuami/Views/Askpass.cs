using System;
using System.Security.Cryptography;
using System.Text;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's askpass (<c>Views/Askpass.axaml</c>): git and ssh run the
    /// app to ask for a password or passphrase, and read it from its
    /// output. A window of its own with what they ask, the platform's
    /// password field, OK and Cancel. Upstream's show-password toggle is
    /// left to the platform's field.
    /// </summary>
    public static class Askpass
    {
        public static void Show(string prompt)
        {
            var window = new Window(App.Text("Askpass"), 400);
            var column = new Column { Padding = Metrics.Panel, Gap = Metrics.PanelGap, AlignItems = Align.Stretch };
            column.Add(new Text(prompt));
            var field = new PasswordInput { AccessibleName = prompt };
            column.Add(field);

            var buttons = new Row { Gap = Metrics.DialogButtonGap, Justify = Justify.End };
            var cancel = new Button(Popups.ButtonText("Cancel")) { Role = ButtonRole.Cancel };
            var sure = new Button(Popups.ButtonText("Sure")) { Role = ButtonRole.Default };
            buttons.Add(cancel);
            buttons.Add(sure);
            column.Add(buttons);
            window.Content = column;

            // What's typed goes to git on the output, and nowhere else.
            var answered = false;
            // Return can come from the field and the default button both.
            void Enter()
            {
                if (answered)
                    return;
                answered = true;
                var bytes = Encoding.UTF8.GetBytes(field.Value ?? string.Empty);
                try
                {
                    var output = Console.OpenStandardOutput();
                    output.Write(bytes, 0, bytes.Length);
                    output.WriteByte((byte)'\n');
                    output.Flush();
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(bytes);
                }
                App.Quit(0);
            }
            void Refuse()
            {
                if (answered)
                    return;
                answered = true;
                Console.Out.WriteLine("No passphrase entered.");
                App.Quit(-1);
            }
            sure.Click += Enter;
            field.Submit += Enter;
            cancel.Click += Refuse;
            window.Closed += Refuse;
        }
    }
}
