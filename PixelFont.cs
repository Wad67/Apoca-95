using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace Chicago95
{
    // Text the way the desktop draws it: a bitmap font strike (Cronyx Helvetica 11 px, the font Chicago95 shows as
    // "Helvetica 8"), baked by tools/make_font.py into an atlas, drawn glyph by glyph with point filtering at a
    // whole-number scale, so every pixel is the font's own: no smoothing, no font lookup.
    internal sealed class PixelFont
    {
        private struct Glyph { public Rect Uv; public int W, H, XOff, YOff, Advance; }

        private static PixelFont _regular, _bold;
        public static PixelFont Regular => _regular ?? (_regular = new PixelFont("Chicago95.fonts.helv11"));
        public static PixelFont Bold => _bold ?? (_bold = new PixelFont("Chicago95.fonts.helv11b"));
        // the console's: More Perfect DOS VGA, 9 x 16 cells
        private static PixelFont _dos;
        public static PixelFont Dos => _dos ?? (_dos = new PixelFont("Chicago95.fonts.dos16"));

        private readonly Texture2D _atlas;
        private readonly Dictionary<char, Glyph> _glyphs = new Dictionary<char, Glyph>();
        public readonly int Ascent, Descent, CapTop, CapHeight;
        public int LineHeight => Ascent + Descent;

        private PixelFont(string resource)
        {
            var asm = Assembly.GetExecutingAssembly();
            using (var png = asm.GetManifestResourceStream(resource + ".png"))
            using (var buffer = new MemoryStream())
            {
                png.CopyTo(buffer);
                _atlas = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
                ImageConversion.LoadImage(_atlas, buffer.ToArray(), true);
            }
            float aw = _atlas.width, ah = _atlas.height;
            using (var txt = new StreamReader(asm.GetManifestResourceStream(resource + ".txt")))
            {
                string line;
                while ((line = txt.ReadLine()) != null)
                {
                    var p = line.Split(' ');
                    if (p[0] == "font") { Ascent = int.Parse(p[1]); Descent = int.Parse(p[2]); continue; }
                    if (p.Length < 8) continue;
                    int x = int.Parse(p[1]), y = int.Parse(p[2]), w = int.Parse(p[3]), h = int.Parse(p[4]);
                    _glyphs[(char)int.Parse(p[0])] = new Glyph
                    {
                        // the PNG's top row is the texture's top: v runs bottom up
                        Uv = new Rect(x / aw, 1f - (y + h) / ah, w / aw, h / ah),
                        W = w, H = h, XOff = int.Parse(p[5]), YOff = int.Parse(p[6]), Advance = int.Parse(p[7])
                    };
                }
            }
            if (_glyphs.TryGetValue('H', out var cap)) { CapTop = cap.YOff; CapHeight = cap.H; }
            else { CapTop = 2; CapHeight = Ascent - 2; }
        }

        private Glyph Of(char c)
        {
            if (_glyphs.TryGetValue(c, out var g)) return g;
            return _glyphs.TryGetValue('?', out g) ? g : default(Glyph);
        }

        public int Width(string s)
        {
            int w = 0;
            if (s != null) foreach (var c in s) w += Of(c).Advance;
            return w;
        }

        // the longest start of s that fits in width (font pixels), ending in "..." when cut
        public string Fit(string s, int width)
        {
            if (s == null || Width(s) <= width) return s;
            int dots = Width("..."), w = 0, i = 0;
            for (; i < s.Length; i++) { int a = Of(s[i]).Advance; if (w + a + dots > width) break; w += a; }
            return s.Substring(0, i) + "...";
        }

        // lines of s no wider than width (font pixels): its own line breaks, then breaks between words (a word longer
        // than a line is cut)
        public List<string> Wrap(string s, int width)
        {
            var lines = new List<string>();
            if (s == null) return lines;
            foreach (var para in s.Replace("\r", "").Split('\n'))
            {
                var line = new System.Text.StringBuilder();
                int w = 0, space = Of(' ').Advance;
                foreach (var word in para.Split(' '))
                {
                    int ww = Width(word);
                    if (line.Length > 0 && w + space + ww > width) { lines.Add(line.ToString()); line.Length = 0; w = 0; }
                    if (line.Length > 0) { line.Append(' '); w += space; }
                    if (ww > width && line.Length == 0)
                    {
                        foreach (var ch in word)
                        {
                            int a = Of(ch).Advance;
                            if (w + a > width && line.Length > 0) { lines.Add(line.ToString()); line.Length = 0; w = 0; }
                            line.Append(ch); w += a;
                        }
                        continue;
                    }
                    line.Append(word); w += ww;
                }
                lines.Add(line.ToString());
            }
            return lines;
        }

        // from the pen at (x, y) = the line's top left, in screen pixels; k screen pixels a font pixel
        public void Draw(float x, float y, string s, Color c, int k)
        {
            if (string.IsNullOrEmpty(s)) return;
            var old = GUI.color; GUI.color = c;
            foreach (var ch in s)
            {
                var g = Of(ch);
                if (g.W > 0 && g.H > 0) GUI.DrawTextureWithTexCoords(new Rect(x + g.XOff * k, y + g.YOff * k, g.W * k, g.H * k), _atlas, g.Uv);
                x += g.Advance * k;
            }
            GUI.color = old;
        }

        // in a rectangle: the capitals centred vertically, as Windows places a label
        public void Draw(Rect r, string s, Color c, int k, TextAnchor align = TextAnchor.MiddleLeft)
        {
            if (string.IsNullOrEmpty(s)) return;
            float w = Width(s) * k;
            float x = align == TextAnchor.MiddleCenter || align == TextAnchor.UpperCenter || align == TextAnchor.LowerCenter ? r.x + Mathf.Floor((r.width - w) / 2f)
                    : align == TextAnchor.MiddleRight || align == TextAnchor.UpperRight || align == TextAnchor.LowerRight ? r.xMax - w : r.x;
            float y = r.y + Mathf.Floor((r.height - CapHeight * k) / 2f) - CapTop * k;
            Draw(Mathf.Round(x), Mathf.Round(y), s, c, k);
        }
    }
}
