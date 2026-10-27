using System;
using System.Globalization;

namespace OrchidUI
{
    // Plain colour maths with no WPF dependency, so the theme pipeline can be tested anywhere.
    internal struct Rgba
    {
        public readonly byte A, R, G, B;

        public Rgba(byte a, byte r, byte g, byte b) { A = a; R = r; G = g; B = b; }

        public static readonly Rgba White = new Rgba(255, 255, 255, 255);
        public static readonly Rgba Black = new Rgba(255, 0, 0, 0);
        public static readonly Rgba Transparent = new Rgba(0, 0, 0, 0);

        public Rgba WithAlpha(byte a) { return new Rgba(a, R, G, B); }
        public Rgba Opaque() { return new Rgba(255, R, G, B); }

        public static Rgba Mix(Rgba a, Rgba b, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return new Rgba(
                (byte)Math.Round(a.A + (b.A - a.A) * t),
                (byte)Math.Round(a.R + (b.R - a.R) * t),
                (byte)Math.Round(a.G + (b.G - a.G) * t),
                (byte)Math.Round(a.B + (b.B - a.B) * t));
        }

        public double Luminance
        {
            get
            {
                Func<byte, double> lin = v =>
                {
                    double s = v / 255.0;
                    return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
                };
                return 0.2126 * lin(R) + 0.7152 * lin(G) + 0.0722 * lin(B);
            }
        }

        public bool IsDark { get { return Luminance < 0.35; } }

        public static bool TryParse(string text, out Rgba color)
        {
            color = White;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string s = text.Trim();
            if (s.Equals("transparent", StringComparison.OrdinalIgnoreCase)) { color = Transparent; return true; }
            if (s.StartsWith("#")) s = s.Substring(1);
            if (s.Length == 3 || s.Length == 4)
            {
                var expanded = new char[s.Length * 2];
                for (int i = 0; i < s.Length; i++) { expanded[i * 2] = s[i]; expanded[i * 2 + 1] = s[i]; }
                s = new string(expanded);
            }
            if (s.Length != 6 && s.Length != 8) return false;
            uint v;
            if (!uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)) return false;
            color = s.Length == 6
                ? new Rgba(255, (byte)(v >> 16), (byte)(v >> 8), (byte)v)
                : new Rgba((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
            return true;
        }

        public static Rgba Parse(string text, Rgba fallback)
        {
            Rgba c;
            return TryParse(text, out c) ? c : fallback;
        }

        public string ToHex(bool alpha)
        {
            string rgb = R.ToString("X2") + G.ToString("X2") + B.ToString("X2");
            return "#" + (alpha ? A.ToString("X2") : "") + rgb;
        }

        public string Argb { get { return ToHex(true); } }

        public override string ToString() { return Argb; }
    }
}
