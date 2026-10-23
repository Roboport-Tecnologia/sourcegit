using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Mitsuamia;

namespace SourceGit.Views
{
    /// <summary>
    /// Upstream's <c>Avatar</c>: a user's picture, <c>Size</c> points
    /// square with corners rounded by a sixteenth of it. Until the
    /// <c>AvatarManager</c> has one (or when there's none), upstream's
    /// fallback: their initials on a gradient picked from them, or with the
    /// preference, GitHub's pattern from their email's hash. All of it is
    /// pixels in the platform's image view, the corners rounded in them;
    /// the initials are the platform's text over it.
    /// </summary>
    public sealed class Avatar : Models.IAvatarHost
    {
        /// <param name="menu">Upstream's menu to fetch it again or load
        /// one; the history's rows have none.</param>
        public Avatar(float size, bool menu = true)
        {
            _size = size;
            var view = new Row { AlignItems = Align.Center, Justify = Justify.Center };
            view.Width = size;
            view.Height = size;
            _image = new Image { Inset = new Thickness(0) };
            view.Add(_image);
            // Upstream's are 65% of the side, in white; the platform's
            // text styles are the sizes here.
            _initials = new Text { Style = size >= 32 ? TextStyle.LargeTitle : TextStyle.Caption, Color = Color.Rgba(255, 255, 255), Weight = FontWeight.Bold };
            _initials.IsVisible = false;
            view.Add(_initials);
            if (menu)
                view.ContextMenu =
                [
                    new MenuItem(App.Text("Avatar.Refetch"), () =>
                    {
                        if (_user != null)
                            Models.AvatarManager.Instance.Request(_user.Email, true);
                    }),
                    new MenuItem(App.Text("Avatar.Load"), Load),
                ];
            View = view;

            Models.AvatarManager.Instance.Subscribe(this);
            void Changed(object sender, System.ComponentModel.PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(ViewModels.Preferences.UseGitHubStyleAvatar) && _bitmap == null)
                    Show();
            }
            ViewModels.Preferences.Instance.PropertyChanged += Changed;
            view.Forgotten += () =>
            {
                Models.AvatarManager.Instance.Unsubscribe(this);
                ViewModels.Preferences.Instance.PropertyChanged -= Changed;
            };
        }

        public Node View { get; }

        public Models.User User
        {
            get => _user;
            set
            {
                if (ReferenceEquals(_user, value))
                    return;
                _user = value;
                _bitmap = value == null ? null : Models.AvatarManager.Instance.Request(value.Email, false);
                Show();
            }
        }

        public void OnAvatarResourceChanged(string email, Avalonia.Media.Imaging.Bitmap image)
        {
            if (email.Equals(_user?.Email, StringComparison.Ordinal))
            {
                _bitmap = image;
                Show();
            }
        }

        private async void Load()
        {
            if (_user == null)
                return;
            var email = _user.Email;
            var files = await Dialogs.OpenFiles(null, App.Text("Avatar.Load"));
            if (files.Length == 1)
                Models.AvatarManager.Instance.SetFromLocal(email, files[0]);
        }

        private void Show()
        {
            _initials.IsVisible = false;
            if (_user == null)
            {
                _image.IsVisible = false;
                return;
            }
            _image.IsVisible = true;

            if (_bitmap is { Format: var format } bitmap && format == Avalonia.Platform.PixelFormats.Rgba8888)
            {
                var (w, h) = (bitmap.PixelSize.Width, bitmap.PixelSize.Height);
                var rgba = (byte[])bitmap.Pixels.Clone();
                var scale = Math.Max(w, h) / _size;
                RoundCorners(rgba, w, h, Corner * scale);
                _image.SetPixels(w, h, rgba, scale);
                return;
            }

            // Fallbacks are drawn at twice the points: sharp on Retina,
            // scaled down elsewhere.
            var side = (int)Math.Ceiling(_size * 2);
            var pixels = new byte[side * side * 4];
            if (ViewModels.Preferences.Instance.UseGitHubStyleAvatar)
            {
                Identicon(pixels, side, _user.Email);
            }
            else
            {
                var initials = Initials(_user.Name);
                var sum = 0;
                foreach (var c in initials)
                    sum += Math.Abs(c);
                var (top, bottom) = s_gradients[sum % s_gradients.Length];
                for (var y = 0; y < side; y++)
                {
                    var t = side > 1 ? (float)y / (side - 1) : 0;
                    for (var x = 0; x < side; x++)
                        Put(pixels, (y * side + x) * 4, Mix(top, bottom, t));
                }
                _initials.Value = initials;
                _initials.IsVisible = true;
            }
            RoundCorners(pixels, side, side, Corner * 2);
            _image.SetPixels(side, side, pixels, 2);
        }

        private float Corner => Math.Max(2, _size / 16);

        /// <summary>Upstream's GitHub-style pattern: on white with a faint
        /// outline, a 5 × 5 grid mirrored about its middle column, in the
        /// colour of the email's hash's first bytes, its cells on where the
        /// next bytes are odd.</summary>
        private static void Identicon(byte[] pixels, int side, string email)
        {
            var lowered = email.ToLower(CultureInfo.CurrentCulture).Trim();
            var hash = MD5.HashData(Encoding.Default.GetBytes(lowered));
            var color = (hash[0], hash[1], hash[2]);
            var offset = side / 10.0;
            var step = (side - offset * 2) / 5.0;
            for (var y = 0; y < side; y++)
            {
                for (var x = 0; x < side; x++)
                {
                    var c = ((byte)255, (byte)255, (byte)255);
                    var (col, row) = ((int)Math.Floor((x - offset) / step), (int)Math.Floor((y - offset) / step));
                    if (col is >= 0 and < 5 && row is >= 0 and < 5)
                    {
                        // Columns 2, 3, 4 are bits 0, 1, 2 of the row's
                        // three; columns 1 and 0 mirror 3 and 4.
                        var bit = row * 3 + (col >= 2 ? col - 2 : 2 - col);
                        if (hash[bit + 1] % 2 == 1)
                            c = color;
                    }
                    // Upstream's outline: black at 30%, 0.65 points.
                    var edge = Math.Min(Math.Min(x, y), Math.Min(side - 1 - x, side - 1 - y));
                    if (edge < 1)
                        c = Mix(c, (0, 0, 0), 0.3f);
                    Put(pixels, (y * side + x) * 4, c);
                }
            }
        }

        /// <summary>Upstream's: the first letters of the first and last
        /// words when both are letters or digits, else the first one.</summary>
        private static string Initials(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "?";
            var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var chars = new List<char>();
            foreach (var part in parts)
                chars.Add(part[0]);
            if (chars.Count >= 2 && char.IsAsciiLetterOrDigit(chars[0]) && char.IsAsciiLetterOrDigit(chars[^1]))
                return $"{chars[0]}{chars[^1]}";
            return name.Substring(0, 1);
        }

        /// <summary>Clears what's outside rounded corners of
        /// <paramref name="radius"/> pixels, antialiased.</summary>
        private static void RoundCorners(byte[] rgba, int w, int h, float radius)
        {
            var r = (int)Math.Ceiling(radius);
            for (var y = 0; y < Math.Min(r, h); y++)
            {
                for (var x = 0; x < Math.Min(r, w); x++)
                {
                    // Coverage of the pixel's centre by the corner's circle,
                    // a pixel's width of antialiasing.
                    var dx = radius - (x + 0.5f);
                    var dy = radius - (y + 0.5f);
                    var cover = Math.Clamp(radius - MathF.Sqrt(dx * dx + dy * dy) + 0.5f, 0, 1);
                    if (cover >= 1)
                        continue;
                    foreach (var (px, py) in new[] { (x, y), (w - 1 - x, y), (x, h - 1 - y), (w - 1 - x, h - 1 - y) })
                    {
                        var i = (py * w + px) * 4 + 3;
                        rgba[i] = (byte)(rgba[i] * cover);
                    }
                }
            }
        }

        private static (byte, byte, byte) Mix((byte R, byte G, byte B) a, (byte R, byte G, byte B) b, float t) =>
            ((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

        private static void Put(byte[] pixels, int i, (byte R, byte G, byte B) c)
        {
            pixels[i] = c.R;
            pixels[i + 1] = c.G;
            pixels[i + 2] = c.B;
            pixels[i + 3] = 255;
        }

        // Upstream's: Orange, DodgerBlue, LimeGreen, Orchid and Tomato,
        // each to a lighter tint, top to bottom.
        private static readonly ((byte, byte, byte), (byte, byte, byte))[] s_gradients =
        [
            ((255, 165, 0), (255, 213, 134)),
            ((30, 144, 255), (135, 206, 250)),
            ((50, 205, 50), (124, 241, 124)),
            ((218, 112, 214), (248, 161, 245)),
            ((255, 99, 71), (252, 165, 150)),
        ];

        private readonly float _size;
        private readonly Image _image;
        private readonly Text _initials;
        private Models.User _user;
        private Avalonia.Media.Imaging.Bitmap _bitmap;
    }
}
