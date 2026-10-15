using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Apocasetter;

namespace Chicago95
{
    // Apocasetter's Mods window (F6) draws through GameSkin: a palette, a few textures, text styles and drawing helpers,
    // laid out on a 1220 x 688 window times its own scale. Here:
    //  - the scale is whole (k: 1 at 1080p, 2 at 4K) so every line lands on whole pixels;
    //  - text is the desktop's bitmap font (PixelFont) everywhere it draws: its labels, buttons and text fields. Its
    //    styles carry a marker font so their measuring (GUIStyle.CalcSize / CalcHeight, which its layout uses for
    //    button widths and wrapped descriptions) answers in the same font;
    //  - the palette is Windows 95 under its old names ("White" is the text colour, "Yellow" the highlight: navy), and
    //    the dark-theme colours its code asks GameSkin.Hex for are swapped for light ones;
    //  - a raised window frame, a thin navy title bar with a close box, raised buttons that press in, sunken white
    //    fields, a navy selected row with white text, a dithered scrollbar with a raised thumb.
    internal static class SetterTheme
    {
        public static int K = 1;
        private static Font _marker;          // the font Apocasetter's styles carry: drawn and measured as PixelFont
        private static bool _inHeader, _whiteText;
        private static object _ui;
        private static AccessTools.FieldRef<object, float> _sc, _wx, _wy;
        private static AccessTools.FieldRef<object, string> _selected;
        private static System.Reflection.MethodInfo _toggle;

        // the dark-theme colours SettingsUI hard-codes, and what they become
        private static readonly Dictionary<string, string> Light = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
        {
            { "FF8A78", "800000" }, { "E0452F", "C00000" }, { "E8A33C", "804000" }, { "FFB27A", "804000" },          // warnings, red, orange text
            { "3A0D09", "FFE0E0" }, { "1E0907", "FFE0E0" }, { "121509", "E0F0E0" }, { "2A2118", "FFFFE1" },          // badge and notice backgrounds
            { "1C1610", "FFFFE1" }, { "6B5F12", "FFFFE1" }, { "7A3A1A", "FFE8D0" }, { "111111", "DFDFDF" },
            { "3D352D", "DFDFDF" }, { "3A2E1A", "DFDFDF" },
            { "17130D", "FFFFFF" }, { "050403", "FFFFFF" }, { "0A0807", "FFFFFF" }, { "0C0A08", "FFFFFF" }, { "0C0A09", "FFFFFF" },  // fields
            { "8F8A83", "808080" }, { "221C17", "808080" }, { "D9D4CB", "808080" },                                     // borders, rules, the slider track
            { "BDB3A6", "404040" }, { "E8E0D2", "FFFFFF" },                                                             // dim text; text on the selected row
        };

        public static void Install(Harmony h)
        {
            Set("Yellow", "000080"); Set("White", "000000"); Set("Desc", "000000"); Set("Sub", "404040"); Set("Dim", "808080");
            Set("Red", "800000"); Set("Green", "006400"); Set("Line", "808080"); Set("Pit", "C0C0C0");
            PixelStyles.Install(h);
            _marker = PixelStyles.Scaled;

            var skin = typeof(GameSkin); var me = typeof(SetterTheme);
            h.Patch(AccessTools.Method(skin, "Load"), postfix: new HarmonyMethod(me, nameof(Loaded)));
            h.Patch(AccessTools.Method(skin, "Build"), postfix: new HarmonyMethod(me, nameof(Built)));
            h.Patch(AccessTools.Method(skin, nameof(GameSkin.Ensure)), prefix: new HarmonyMethod(me, nameof(Ensure)));
            h.Patch(AccessTools.Method(skin, nameof(GameSkin.Hex)), postfix: new HarmonyMethod(me, nameof(Hex)));
            foreach (var name in new[] { nameof(GameSkin.SetGameFont), nameof(GameSkin.Out), nameof(GameSkin.Label), nameof(GameSkin.PlankButton), nameof(GameSkin.Frame),
                                         nameof(GameSkin.RustBack), nameof(GameSkin.PitBox), nameof(GameSkin.SlotBox), nameof(GameSkin.HazardStrip) })
                h.Patch(AccessTools.Method(skin, name) ?? throw new System.MissingMethodException("GameSkin." + name), prefix: new HarmonyMethod(me, name));

            var ui = AccessTools.TypeByName("Apocasetter.SettingsUI") ?? throw new System.MissingMemberException("Apocasetter.SettingsUI");
            _sc = AccessTools.FieldRefAccess<object, float>(AccessTools.Field(ui, "_sc"));
            _wx = AccessTools.FieldRefAccess<object, float>(AccessTools.Field(ui, "_wx"));
            _wy = AccessTools.FieldRefAccess<object, float>(AccessTools.Field(ui, "_wy"));
            _selected = AccessTools.FieldRefAccess<object, string>(AccessTools.Field(ui, "_selected"));
            _toggle = AccessTools.Method(ui, "Toggle");
            h.Patch(AccessTools.Method(ui, "DrawHeader"), prefix: new HarmonyMethod(me, nameof(HeaderBegins)), postfix: new HarmonyMethod(me, nameof(HeaderEnds)));
            h.Patch(AccessTools.Method(ui, "DrawListRow"), prefix: new HarmonyMethod(me, nameof(RowBegins)), postfix: new HarmonyMethod(me, nameof(RowEnds)));
        }

        private static void Set(string field, string hex) { AccessTools.Field(typeof(GameSkin), field).SetValue(null, Win95.Hex(hex)); }
        private static void SetPrivate(string field, object value) { AccessTools.Field(typeof(GameSkin), field).SetValue(null, value); }

        // ---------------------------------------------------------------- scale, textures, fonts, styles, colours
        // SettingsUI sets its scale, then hands it to Ensure: the scale becomes whole before the window is placed
        private static void Ensure(ref float s)
        {
            K = Mathf.Max(1, Screen.height / 2000 + 1);
            s = K;
            var ui = SettingsUI.Instance;
            if (ui != null) { _ui = ui; _sc(ui) = K; }
        }

        private static void Loaded()
        {
            GameSkin.Knob = GameSkin.Rivet = Win95.RaisedFace;   // the slider knob and the reset button: small raised buttons
            GameSkin.Slot = GameSkin.Field = GameSkin.Plank = GameSkin.PlankHover = GameSkin.PlankDown = Win95.SunkenWhite;
            GameSkin.Track = Win95.Dither; GameSkin.Thumb = Win95.Thumb;
            GameSkin.Rust = GameSkin.Chains = GameSkin.RebarH = GameSkin.RebarV = null;
            SetPrivate("_body", _marker); SetPrivate("_bold", _marker); SetPrivate("_display", _marker);
            SetPrivate("_builtFor", -1f);
        }

        private static void Built()
        {
            PixelStyles.Bold.Clear();
            foreach (var st in new[] { GameSkin.Title, GameSkin.H1, GameSkin.Btn, GameSkin.BtnSmall, GameSkin.Section, GameSkin.Tab, GameSkin.BodyBold, GameSkin.Tiny, GameSkin.ListName })
                if (st != null) { st.fontStyle = FontStyle.Bold; PixelStyles.Bold.Add(st); }
            var plank = GameSkin.PlankStyle;   // the search box: sunken white (the selected row is drawn navy, RowBegins)
            plank.border = new RectOffset(2, 2, 2, 2);
            plank.normal.background = plank.hover.background = plank.active.background = plank.onNormal.background = plank.onHover.background = Win95.SunkenWhite;
            foreach (var st in new[] { GameSkin.TextField, GameSkin.NumberField })
            {
                st.border = new RectOffset(2, 2, 2, 2);
                st.normal.background = st.hover.background = st.focused.background = st.active.background = st.onNormal.background = st.onFocused.background = Win95.SunkenWhite;
            }
            GameSkin.ScrollV.fixedWidth = 16 * K; GameSkin.ScrollV.border = new RectOffset(); GameSkin.ScrollV.normal.background = Win95.Dither;
            GameSkin.ScrollThumb.fixedWidth = 16 * K; GameSkin.ScrollThumb.border = new RectOffset(2, 2, 2, 2);
            GameSkin.ScrollThumb.normal.background = GameSkin.ScrollThumb.hover.background = GameSkin.ScrollThumb.active.background = Win95.Thumb;
        }

        private static void Hex(string h, ref Color __result) { if (h != null && Light.TryGetValue(h, out var to)) __result = Win95.Hex(to); }

        // the window keeps its own font when the game's menu font turns up
        private static bool SetGameFont(Font f) { if (f != null) GameSkin.GameFont = f; return false; }

        // ---------------------------------------------------------------- text
        private static bool Label(Rect r, string text, GUIStyle st, Color c)
        {
            if (_whiteText && st != GameSkin.Tiny) c = Color.white;   // on the selected (navy) row; badges keep theirs
            PixelStyles.Text(r, text, st, c);
            return false;
        }

        // ---------------------------------------------------------------- the header: a thin title bar with a close box
        private static Rect _bar;
        private static readonly string[] Cross = { "XX    XX", " XX  XX ", "  XXXX  ", "   XX   ", "  XXXX  ", " XX  XX ", "XX    XX" };

        private static void HeaderBegins(object __instance)
        {
            float wx = _wx(__instance), wy = _wy(__instance), w = 1220 * _sc(__instance);
            _bar = new Rect(wx + 3 * K, wy + 3 * K, w - 6 * K, 18 * K);
            Win95.TitleBar(_bar);
            var box = new Rect(_bar.xMax - 2 * K - 16 * K, _bar.y + 2 * K, 16 * K, 14 * K);
            bool hit = Win95.Button(box, out bool held);
            Win95.Fill(box, Win95.Face);
            if (held) Win95.Pressed(box, K); else Win95.Raised(box, K);
            float x0 = box.x + 4 * K + (held ? K : 0), y0 = box.y + 3 * K + (held ? K : 0);
            for (int y = 0; y < Cross.Length; y++)
                for (int x = 0; x < Cross[y].Length; x++)
                    if (Cross[y][x] == 'X') Win95.Fill(new Rect(x0 + x * K, y0 + y * K, K, K), Color.black);
            if (hit) _toggle?.Invoke(__instance, null);
            _inHeader = true;
        }
        private static void HeaderEnds() { _inHeader = false; }

        // its big title goes in the title bar (bold white, title case); other text as it is
        private static bool Out(Rect r, string text, GUIStyle st, Color c)
        {
            if (_inHeader && st == GameSkin.Title)
            {
                var m = GUI.matrix; GUI.matrix = Matrix4x4.identity;   // the old title was tilted
                string t = string.IsNullOrEmpty(text) ? text : text.Substring(0, 1) + text.Substring(1).ToLowerInvariant();
                PixelFont.Bold.Draw(new Rect(_bar.x + 3 * K, _bar.y, _bar.width - 30 * K, _bar.height), t, Color.white, K);
                GUI.matrix = m;
                return false;
            }
            return Label(r, text, st, c);
        }

        // ---------------------------------------------------------------- the selected mod: a navy row, white text
        private static void RowBegins(object __instance, Rect r, object m)
        {
            string guid = m != null ? Traverse.Create(m).Field("Guid").GetValue<string>() : null;
            bool sel = guid != null && guid == _selected(__instance);
            if (!sel) return;
            Win95.Fill(r, Win95.Navy);
            GameSkin.PlankStyle.normal.background = null;   // its own selection box: not drawn
            _whiteText = true;
        }
        private static void RowEnds()
        {
            if (!_whiteText) return;
            _whiteText = false;
            GameSkin.PlankStyle.normal.background = Win95.SunkenWhite;
        }

        // ---------------------------------------------------------------- frames, buttons, boxes
        // raised grey, pressed in while held
        private static bool PlankButton(Rect r, string text, Color c, GUIStyle font, bool enabled, ref bool __result)
        {
            var old = GUI.enabled; GUI.enabled = enabled && old;
            bool hit = Win95.Button(r, out bool held);
            Win95.Fill(r, Win95.Face);
            if (held) Win95.Pressed(r, K); else Win95.Raised(r, K);
            var tr = held ? new Rect(r.x + K, r.y + K, r.width, r.height) : r;
            var st = font ?? GameSkin.Btn;
            var align = st.alignment; st.alignment = TextAnchor.MiddleCenter;
            PixelStyles.Text(tr, text, st, c);
            st.alignment = align;
            GUI.enabled = old;
            __result = hit;
            return false;
        }

        private static bool Frame(Rect r) { Win95.Window(r, K); return false; }
        private static bool RustBack(Rect r) { Win95.Fill(r, Win95.Face); return false; }
        private static bool PitBox(Rect r) { Win95.Fill(r, GameSkin.Pit); Win95.Sunken(r, K); return false; }
        private static bool SlotBox(Rect r) { Win95.Fill(r, Color.white); Win95.Sunken(r, K); return false; }
        private static bool HazardStrip(Rect r) { Win95.Fill(r, Win95.Navy); return false; }
    }
}
