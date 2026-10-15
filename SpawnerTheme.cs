using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace Chicago95
{
    // Apocaspawner's window draws through its SpawnerSkin (a palette, a few text styles and primitives, much like Wild
    // Wasteland's Ledger), laid out 1120 x 755 and scaled to the screen. Here:
    //  - the scale is whole (the largest whole number that fits), so every line lands on whole pixels;
    //  - text is the desktop's bitmap font: its styles are marked for PixelStyles (at scale 1: the matrix scales);
    //  - the palette is Windows 95 (grey faces, white fields, black text, navy accents) and the primitives are replaced:
    //    a raised window with a navy title bar, sunken panels, raised buttons that press in, sunken white text fields,
    //    etched rules, the theme's scrollbars.
    internal static class SpawnerTheme
    {
        private const string Skin = "Apocaspawner.SpawnerSkin";
        private static Type _skin;
        private static GUIStyle _styledBody;

        public static void Install(Harmony h)
        {
            _skin = AccessTools.TypeByName(Skin) ?? throw new MissingMemberException(Skin);
            PixelStyles.Install(h);
            Set("Background", "C0C0C0"); Set("Surface", "C0C0C0"); Set("Raised", "C0C0C0"); Set("Field", "FFFFFF");
            Set("Text", "000000"); Set("Muted", "808080"); Set("Line", "808080"); Set("Accent", "000080"); Set("Selected", "FFFFFF");

            var me = typeof(SpawnerTheme);
            h.Patch(Method("Begin"), postfix: new HarmonyMethod(me, nameof(Begun)));
            h.Patch(Method("Panel"), prefix: new HarmonyMethod(me, nameof(Panel)));
            h.Patch(Method("Button"), prefix: new HarmonyMethod(me, nameof(Button)));
            h.Patch(Method("Window"), prefix: new HarmonyMethod(me, nameof(Window)));
            h.Patch(Method("Rule"), prefix: new HarmonyMethod(me, nameof(Rule)));
            h.Patch(Method("Scroll"), prefix: new HarmonyMethod(me, nameof(Scroll)));
            var ongui = AccessTools.Method(AccessTools.TypeByName("Apocaspawner.SpawnerUI"), "OnGUI");
            if (ongui != null) h.Patch(ongui, transpiler: new HarmonyMethod(me, nameof(WholeScale)));
            else Plugin.Log.LogWarning("Apocaspawner: SpawnerUI.OnGUI not found: its scale stays as it is");
        }

        private static MethodInfo Method(string name) { return AccessTools.Method(_skin, name) ?? throw new MissingMethodException(Skin + "." + name); }
        private static void Set(string field, string hex)
        {
            var f = AccessTools.Field(_skin, field);
            if (f != null) f.SetValue(null, Win95.Hex(hex));
        }
        private static GUIStyle Style(string name) { return AccessTools.Field(_skin, name)?.GetValue(null) as GUIStyle; }

        // ---------------------------------------------------------------- a whole scale
        // its OnGUI takes the scale as the smaller of two fits (Mathf.Min): that becomes the largest whole number below it
        private static readonly MethodInfo Min = AccessTools.Method(typeof(Mathf), nameof(Mathf.Min), new[] { typeof(float), typeof(float) });
        private static IEnumerable<CodeInstruction> WholeScale(IEnumerable<CodeInstruction> code)
        {
            bool done = false;
            foreach (var ins in code)
            {
                yield return ins;
                if (!done && ins.Calls(Min)) { done = true; yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SpawnerTheme), nameof(Whole))); }
            }
        }
        public static float Whole(float s) { return s >= 1f ? Mathf.Floor(s + .001f) : s; }

        // ---------------------------------------------------------------- styles
        private static void Begun()
        {
            Ui.K = 1;   // its window is laid out in its own pixels; the matrix scales
            var body = Style("Body");
            if (body == null || body == _styledBody) return;
            _styledBody = body;
            foreach (var n in new[] { "Body", "Small", "Heading", "Brand", "GameButton", "Value", "Wrapped", "Input" })
            {
                var st = Style(n);
                if (st == null) continue;
                PixelStyles.Mark(st, true);
                st.normal.textColor = st.hover.textColor = st.active.textColor = st.focused.textColor = Color.black;
            }
            foreach (var n in new[] { "Heading", "Brand", "GameButton" }) { var st = Style(n); if (st != null) st.fontStyle = FontStyle.Bold; }
            var input = Style("Input");
            if (input != null)
            {
                input.border = new RectOffset(2, 2, 2, 2); input.padding = new RectOffset(4, 4, 2, 2);
                input.normal.background = input.hover.background = input.focused.background = input.active.background = Win95.SunkenWhite;
            }
        }

        private static string Title(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Length > 1 && s.ToUpperInvariant() == s && s.ToLowerInvariant() != s ? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant()) : s;
        }

        // ---------------------------------------------------------------- primitives
        // the window, its title bar in place of the old header band (the band below it is its toolbar row)
        private static bool Window(float width, float height)
        {
            var r = new Rect(0, 0, width, height);
            Win95.Fill(r, Win95.Face); Win95.Window(r);
            var bar = new Rect(3, 3, width - 6, 18);
            Win95.Fill(bar, Win95.Navy);
            PixelFont.Bold.Draw(new Rect(bar.x + 3, bar.y, bar.width - 6, bar.height), "Apocaspawner", Color.white, 1);
            Win95.Etched(6, 63, width - 12);
            return false;
        }

        // sunken; a dark fill from the old palette becomes the face
        private static bool Panel(Rect r, Color? fill, Color? edge)
        {
            var f = fill ?? Win95.Face;
            if (f.a > .5f && f.grayscale < .25f) f = Win95.Face;
            if (f.a > 0f) Win95.Fill(r, f);
            Win95.Sunken(r);
            if (edge.HasValue && edge.Value == Win95.Navy) Win95.Outline(new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4), Win95.Navy);
            return false;
        }

        // raised grey, pressed in while held; the primary one has the default button's black outline
        private static bool Button(Rect r, string text, bool enabled, bool primary, bool gameFont, bool transparent, ref bool __result)
        {
            bool previous = GUI.enabled; GUI.enabled = previous && enabled;
            bool hit = Win95.Button(r, out bool held);
            var face = primary ? new Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2) : r;
            Win95.Fill(r, Win95.Face);
            if (primary) Win95.Outline(r, Win95.Dark);
            if (held) Win95.Pressed(face); else Win95.Raised(face);
            var label = held ? new Rect(r.x + 1, r.y + 1, r.width, r.height) : r;
            var st = Style(gameFont ? "GameButton" : "Body") ?? GUI.skin.label;
            var align = st.alignment; st.alignment = TextAnchor.MiddleCenter;
            PixelStyles.Text(label, Title(text), st, enabled ? Color.black : Win95.Shadow);
            st.alignment = align;
            GUI.enabled = previous;
            __result = hit;
            return false;
        }

        private static bool Rule(float x, float y, float width) { Win95.Etched(x, y, width); return false; }

        // the theme's scrollbar (arrows, a dithered track, a raised thumb) and the mouse wheel
        private static bool Scroll(Rect rect, Vector2 scroll, float height, Action<float> draw, ref Vector2 __result)
        {
            int k = Ui.K; Ui.K = 1;
            float y = scroll.y;
            float w = Ui.BeginScroll(rect, ref y, height);
            try { draw(w); }
            finally { Ui.EndScroll(); Ui.K = k; }
            __result = new Vector2(scroll.x, y);
            return false;
        }
    }
}
