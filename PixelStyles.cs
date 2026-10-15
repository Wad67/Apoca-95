using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Chicago95
{
    // The desktop's bitmap font (PixelFont) for other mods' IMGUI styles. A style is marked by giving it one of two
    // marker fonts: Scaled (k font pixels per font pixel, k = Ui's: 1 below 2000 lines, 2 at 4K) or Unit (always 1: for
    // windows laid out in screen pixels). Marked styles are then measured (CalcSize, CalcHeight, CalcMinMaxWidth, which
    // GUILayout uses) and drawn (GUIStyle.Draw: the background as before, the text in PixelFont) in that font, and their
    // text fields type in it. Bold styles (FontStyle.Bold, or listed in Bold) draw in the bold strike. Styles copied
    // from a marked one (new GUIStyle(marked)) are marked too.
    internal static class PixelStyles
    {
        public static Font Scaled { get; private set; }
        public static Font Unit { get; private set; }
        public static readonly HashSet<GUIStyle> Bold = new HashSet<GUIStyle>();
        private static bool _installed;

        public static void Install(Harmony h)
        {
            if (_installed) return;
            _installed = true;
            Scaled = Font.CreateDynamicFontFromOSFont("Arial", 11); Scaled.name = "Chicago95 pixel font";
            Unit = Font.CreateDynamicFontFromOSFont("Arial", 11); Unit.name = "Chicago95 pixel font (unit)";
            var me = typeof(PixelStyles);
            h.Patch(AccessTools.Method(typeof(GUIStyle), nameof(GUIStyle.CalcSize), new[] { typeof(GUIContent) }), prefix: new HarmonyMethod(me, nameof(CalcSize)));
            h.Patch(AccessTools.Method(typeof(GUIStyle), nameof(GUIStyle.CalcHeight), new[] { typeof(GUIContent), typeof(float) }), prefix: new HarmonyMethod(me, nameof(CalcHeight)));
            h.Patch(AccessTools.Method(typeof(GUIStyle), nameof(GUIStyle.CalcMinMaxWidth)), prefix: new HarmonyMethod(me, nameof(CalcMinMaxWidth)));
            var draw = AccessTools.Method(typeof(GUIStyle), "Draw", new[] { typeof(Rect), typeof(GUIContent), typeof(int), typeof(bool), typeof(bool), typeof(bool), typeof(bool) })
                       ?? throw new System.MissingMethodException("GUIStyle.Draw (the private one)");
            h.Patch(draw, prefix: new HarmonyMethod(me, nameof(DrawBegins)), postfix: new HarmonyMethod(me, nameof(DrawEnds)));
            h.Patch(AccessTools.Method(typeof(GUI), nameof(GUI.TextField), new[] { typeof(Rect), typeof(string), typeof(int), typeof(GUIStyle) }), prefix: new HarmonyMethod(me, nameof(TextField)));
        }

        public static void Mark(GUIStyle st, bool unit = false) { if (st != null) st.font = unit ? Unit : Scaled; }
        public static int ScaleOf(GUIStyle st) { return st != null && st.font == Unit ? 1 : Mathf.Max(1, Screen.height / 2000 + 1); }
        public static bool Marked(GUIStyle st) { return st != null && st.font != null && (st.font == Scaled || st.font == Unit); }
        public static PixelFont FontOf(GUIStyle st) { return st != null && (st.fontStyle == FontStyle.Bold || st.fontStyle == FontStyle.BoldAndItalic || Bold.Contains(st)) ? PixelFont.Bold : PixelFont.Regular; }

        // ---------------------------------------------------------------- drawing text
        // a style's text in its rectangle: its alignment and padding, wrapped if it wraps; one line's capitals centred
        // when vertically centred. Disabled text grey with a white emboss, as Windows draws it.
        public static void Text(Rect r, string text, GUIStyle st, Color c)
        {
            if (string.IsNullOrEmpty(text)) return;
            int k = ScaleOf(st);
            var font = FontOf(st);
            var pad = st != null ? st.padding : new RectOffset();
            var inner = new Rect(r.x + pad.left, r.y, r.width - pad.horizontal, r.height);
            var align = st != null ? st.alignment : TextAnchor.MiddleLeft;
            if (!GUI.enabled)
            {
                Lines(font, k, new Rect(inner.x + k, inner.y + k, inner.width, inner.height), text, st, Color.white, align, pad);
                c = Win95.Shadow;
            }
            Lines(font, k, inner, text, st, c, align, pad);
        }

        private static void Lines(PixelFont font, int k, Rect r, string text, GUIStyle st, Color c, TextAnchor align, RectOffset pad)
        {
            bool wrap = st != null && st.wordWrap;
            var lines = wrap ? font.Wrap(text, Mathf.Max(1, (int)(r.width / k))) : new List<string>(text.Replace("\r", "").Split('\n'));
            int h = (int)align / 3;   // 0 upper, 1 middle, 2 lower
            if (lines.Count == 1 && h == 1) { font.Draw(r, lines[0], c, k, Horizontal(align)); return; }
            float lh = font.LineHeight * k, block = lines.Count * lh;
            float y = h == 0 ? r.y + pad.top : h == 1 ? r.y + Mathf.Floor((r.height - block) / 2f) : r.yMax - pad.bottom - block;
            foreach (var line in lines)
            {
                float w = font.Width(line) * k;
                int col = (int)align % 3;
                float x = col == 0 ? r.x : col == 1 ? r.x + Mathf.Floor((r.width - w) / 2f) : r.xMax - w;
                font.Draw(Mathf.Round(x), Mathf.Round(y), line, c, k);
                y += lh;
            }
        }
        public static TextAnchor Horizontal(TextAnchor a) { int col = (int)a % 3; return col == 0 ? TextAnchor.MiddleLeft : col == 1 ? TextAnchor.MiddleCenter : TextAnchor.MiddleRight; }

        // GUIStyle.Draw: the style draws itself without its text, then the text is drawn here in its state's colour
        private static readonly GUIContent Bare = new GUIContent();
        private static void DrawBegins(GUIStyle __instance, ref GUIContent content, out string __state)
        {
            __state = null;
            if (!Marked(__instance) || content == null || string.IsNullOrEmpty(content.text)) return;
            __state = content.text;
            Bare.text = null; Bare.image = content.image; Bare.tooltip = content.tooltip;
            content = Bare;
        }
        private static void DrawEnds(GUIStyle __instance, Rect position, bool isHover, bool isActive, bool on, bool hasKeyboardFocus, string __state)
        {
            if (__state == null || Event.current.type != EventType.Repaint) return;
            var s = on ? (isActive ? __instance.onActive : hasKeyboardFocus ? __instance.onFocused : isHover ? __instance.onHover : __instance.onNormal)
                       : (isActive ? __instance.active : hasKeyboardFocus ? __instance.focused : isHover ? __instance.hover : __instance.normal);
            var c = s.textColor * GUI.contentColor;
            var r = new Rect(position.x + __instance.contentOffset.x, position.y + __instance.contentOffset.y, position.width, position.height);
            Text(r, __state, __instance, c);
        }

        // ---------------------------------------------------------------- measuring
        private static bool CalcSize(GUIStyle __instance, GUIContent content, ref Vector2 __result)
        {
            if (!Marked(__instance)) return true;
            int k = ScaleOf(__instance);
            var font = FontOf(__instance); string text = content != null ? content.text ?? "" : "";
            var lines = text.Replace("\r", "").Split('\n');
            int w = 0; foreach (var l in lines) w = Mathf.Max(w, font.Width(l));
            float pw = w * k, ph = text.Length == 0 && content?.image != null ? 0 : lines.Length * font.LineHeight * k;
            if (content?.image != null) { pw += content.image.width; ph = Mathf.Max(ph, content.image.height); }
            __result = new Vector2(pw + __instance.padding.horizontal, ph + __instance.padding.vertical);
            if (__instance.fixedWidth > 0) __result.x = __instance.fixedWidth;
            if (__instance.fixedHeight > 0) __result.y = __instance.fixedHeight;
            return false;
        }

        private static bool CalcHeight(GUIStyle __instance, GUIContent content, float width, ref float __result)
        {
            if (!Marked(__instance)) return true;
            if (__instance.fixedHeight > 0) { __result = __instance.fixedHeight; return false; }
            int k = ScaleOf(__instance);
            var font = FontOf(__instance); string text = content != null ? content.text ?? "" : "";
            int lines = __instance.wordWrap ? font.Wrap(text, Mathf.Max(1, (int)((width - __instance.padding.horizontal) / k))).Count : text.Split('\n').Length;
            __result = lines * font.LineHeight * k + __instance.padding.vertical;
            return false;
        }

        // wrapping text: as narrow as its longest word, as wide as its longest line
        private static bool CalcMinMaxWidth(GUIStyle __instance, GUIContent content, out float minWidth, out float maxWidth)
        {
            minWidth = maxWidth = 0;
            if (!Marked(__instance)) return true;
            int k = ScaleOf(__instance);
            var font = FontOf(__instance); string text = content != null ? content.text ?? "" : "";
            int max = 0, min = 0;
            foreach (var l in text.Replace("\r", "").Split('\n'))
            {
                max = Mathf.Max(max, font.Width(l));
                foreach (var w in l.Split(' ')) min = Mathf.Max(min, font.Width(w));
            }
            maxWidth = max * k + __instance.padding.horizontal;
            minWidth = (__instance.wordWrap ? min : max) * k + __instance.padding.horizontal;
            if (__instance.fixedWidth > 0) minWidth = maxWidth = __instance.fixedWidth;
            return false;
        }

        // ---------------------------------------------------------------- text fields
        // in the pixel font: click to type (the control takes keyboard focus, so GUI.SetNextControlName and
        // GetNameOfFocusedControl keep working), Enter or Escape to finish; typing adds at the end, Backspace removes
        private static bool TextField(Rect position, string text, int maxLength, GUIStyle style, ref string __result)
        {
            if (!Marked(style)) return true;
            int k = ScaleOf(style);
            text = text ?? "";
            int id = GUIUtility.GetControlID(FocusType.Keyboard, position);
            var e = Event.current;
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (position.Contains(e.mousePosition) && GUI.enabled) { GUIUtility.keyboardControl = id; e.Use(); }
                    break;
                case EventType.KeyDown:
                    if (GUIUtility.keyboardControl != id) break;
                    if (e.keyCode == KeyCode.Backspace) { if (text.Length > 0) { text = text.Substring(0, text.Length - 1); GUI.changed = true; } e.Use(); }
                    else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Escape) { GUIUtility.keyboardControl = 0; e.Use(); }
                    else if (e.character >= ' ' && e.character != 127)
                    {
                        if (maxLength <= 0 || text.Length < maxLength) { text += e.character; GUI.changed = true; }
                        e.Use();
                    }
                    break;
                case EventType.Repaint:
                    if (style.normal.background != null) { Win95.Fill(position, Color.white); Win95.Sunken(position, k); }
                    var font = FontOf(style);
                    var inner = new Rect(position.x + style.padding.left, position.y, position.width - style.padding.horizontal, position.height);
                    font.Draw(inner, text, GUI.enabled ? Color.black : Win95.Shadow, k, Horizontal(style.alignment));
                    if (GUIUtility.keyboardControl == id && ((int)(Time.unscaledTime * 2f)) % 2 == 0)
                    {
                        int col = (int)style.alignment % 3;
                        float tw = font.Width(text) * k;
                        float cx = col == 2 ? inner.xMax + k : col == 1 ? inner.x + (inner.width + tw) / 2f + k : inner.x + tw + k;
                        Win95.Fill(new Rect(Mathf.Round(cx), position.y + 3 * k, k, position.height - 6 * k), Color.black);   // the caret
                    }
                    break;
            }
            __result = text;
            return false;
        }
    }
}
