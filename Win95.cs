using UnityEngine;

namespace Chicago95
{
    // Windows 95 for IMGUI: grey faces, 3D bevels two pixels deep, sunken white fields, a navy title bar fading to blue,
    // MS Sans Serif. Bevels are drawn as one-pixel rectangles; styles get small generated nine-sliced textures.
    internal static class Win95
    {
        public static readonly Color Face = Hex("C0C0C0"), Light = Hex("DFDFDF"), White = Color.white, Shadow = Hex("808080"),
            Dark = Color.black, Navy = Hex("000080"), TitleEnd = Hex("1084D0");

        private static Texture2D _px, _title, _dither, _sunken, _raised, _raisedThumb;
        private static Font _font, _mono;

        // MS Sans Serif's TrueType successor (Microsoft Sans Serif), else Tahoma: picked from the fonts the game can see
        // (asking for a missing name silently gives some other font). Bold is the same font with FontStyle.Bold.
        public static Font Font => _font != null ? _font : (_font = Os("UI", new[] { "Microsoft Sans Serif", "MS Sans Serif", "Tahoma", "Arial" }));
        public static Font Bold => Font;
        public static Font Mono => _mono != null ? _mono : (_mono = Os("console", new[] { "Consolas", "Courier New", "Lucida Console", "Arial" }));

        private static string[] _installed;
        private static Font Os(string what, string[] names)
        {
            try
            {
                if (_installed == null) _installed = Font.GetOSInstalledFontNames() ?? new string[0];
                foreach (var n in names)
                    if (System.Array.IndexOf(_installed, n) >= 0)
                    {
                        Plugin.Log.LogInfo("Win95 " + what + " font: " + n);
                        return Font.CreateDynamicFontFromOSFont(n, 16);
                    }
                Plugin.Log.LogWarning("Win95 " + what + " font: none of " + string.Join(", ", names) + " is installed; using Arial");
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("Win95 " + what + " font: " + e.Message); }
            return Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        public static Color Hex(string h) { return ColorUtility.TryParseHtmlString("#" + h, out var c) ? c : Color.magenta; }

        public static void Fill(Rect r, Color c)
        {
            if (_px == null) { _px = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave }; _px.SetPixel(0, 0, Color.white); _px.Apply(); }
            var old = GUI.color; GUI.color = c; GUI.DrawTexture(r, _px); GUI.color = old;
        }

        // two lines t pixels thick each (t: the UI's whole-number scale), outer then inner, top/left then bottom/right;
        // bottom/right drawn last so they own the corners, as Windows draws them
        private static void Edges(Rect r, Color outerTL, Color outerBR, Color innerTL, Color innerBR, float t = 1)
        {
            Fill(new Rect(r.x, r.y, r.width, t), outerTL); Fill(new Rect(r.x, r.y, t, r.height), outerTL);
            Fill(new Rect(r.x + t, r.y + t, r.width - 2 * t, t), innerTL); Fill(new Rect(r.x + t, r.y + t, t, r.height - 2 * t), innerTL);
            Fill(new Rect(r.x + t, r.yMax - 2 * t, r.width - 2 * t, t), innerBR); Fill(new Rect(r.xMax - 2 * t, r.y + t, t, r.height - 2 * t), innerBR);
            Fill(new Rect(r.x, r.yMax - t, r.width, t), outerBR); Fill(new Rect(r.xMax - t, r.y, t, r.height), outerBR);
        }
        public static void Raised(Rect r, float t = 1) { Edges(r, White, Dark, Light, Shadow, t); }
        public static void Window(Rect r, float t = 1) { Edges(r, Light, Dark, White, Shadow, t); }
        public static void Sunken(Rect r, float t = 1) { Edges(r, Shadow, White, Dark, Light, t); }
        // a push button held down: a black frame with a grey one inside it
        public static void Pressed(Rect r, float t = 1) { Outline(r, Dark, t); Outline(new Rect(r.x + t, r.y + t, r.width - 2 * t, r.height - 2 * t), Shadow, t); }
        // a toggle button latched down: the reverse of raised
        public static void Latched(Rect r, float t = 1) { Edges(r, Dark, White, Shadow, Light, t); }
        public static void Outline(Rect r, Color c, float t = 1)
        {
            Fill(new Rect(r.x, r.y, r.width, t), c); Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
            Fill(new Rect(r.x, r.y, t, r.height), c); Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
        }
        // a groove: a shadow line over a white one
        public static void Etched(float x, float y, float width, float t = 1) { Fill(new Rect(x, y, width, t), Shadow); Fill(new Rect(x, y + t, width, t), White); }

        public static Texture2D TitleGradient
        {
            get
            {
                if (_title != null) return _title;
                const int w = 64;
                _title = new Texture2D(w, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
                for (int x = 0; x < w; x++) _title.SetPixel(x, 0, Color.Lerp(Navy, TitleEnd, x / (w - 1f)));
                _title.Apply();
                return _title;
            }
        }
        // Windows 95's active title bar is solid navy (the gradient is Windows 98)
        public static void TitleBar(Rect r) { Fill(r, Navy); }

        // white and face dithered: a scrollbar track, a latched button
        public static Texture2D Dither
        {
            get
            {
                if (_dither != null) return _dither;
                _dither = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, hideFlags = HideFlags.HideAndDontSave };
                _dither.SetPixels(new[] { White, Face, Face, White }); _dither.Apply();
                return _dither;
            }
        }
        public static Texture2D SunkenWhite => _sunken != null ? _sunken : (_sunken = Bevel(8, Shadow, Dark, Light, White, White));
        public static Texture2D RaisedFace => _raised != null ? _raised : (_raised = Bevel(16, White, Light, Shadow, Dark, Face));
        public static Texture2D Thumb => _raisedThumb != null ? _raisedThumb : (_raisedThumb = Bevel(8, Light, White, Shadow, Dark, Face));

        // an n x n bevel two pixels deep, for nine-slicing
        private static Texture2D Bevel(int n, Color outerTL, Color innerTL, Color innerBR, Color outerBR, Color fill)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int top = n - 1 - y;   // texture rows run bottom up
                    Color c = fill;
                    if (x == 0 || top == 0) c = outerTL;
                    else if (x == n - 1 || top == n - 1) c = outerBR;
                    else if (x == 1 || top == 1) c = innerTL;
                    else if (x == n - 2 || top == n - 2) c = innerBR;
                    t.SetPixel(x, y, c);
                }
            t.Apply();
            return t;
        }

        // A button without a face (the caller draws it): true when clicked; held while pressed with the mouse still on it.
        public static bool Button(Rect r, out bool held)
        {
            int id = GUIUtility.GetControlID(FocusType.Passive);
            var e = Event.current;
            bool hit = false;
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (GUI.enabled && e.button == 0 && r.Contains(e.mousePosition)) { GUIUtility.hotControl = id; e.Use(); }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id) e.Use();
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; e.Use(); hit = r.Contains(e.mousePosition); }
                    break;
            }
            held = GUI.enabled && GUIUtility.hotControl == id && r.Contains(e.mousePosition);
            return hit;
        }
    }
}
