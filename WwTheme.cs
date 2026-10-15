using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WildWasteland.Core.UI;

namespace Chicago95
{
    // Wild Wasteland's windows (the F7 settings, which also opens on a new game, and the patrol template menu and
    // exporter) all draw through its Ledger: one palette and a few primitives, laid out on a 1028-wide window that each
    // scales to the screen with GUI.matrix. Here:
    //  - the scale is whole (the largest whole number that fits), so every line lands on whole pixels;
    //  - text is the desktop's bitmap font: every Ledger style is marked for PixelStyles (at scale 1: the matrix scales);
    //  - the palette is Windows 95 (grey faces, white fields, black text, navy accents) and the primitives are replaced:
    //    a raised window, a navy title bar with the title in title case, sunken panels, raised buttons that press in
    //    (the chosen one of a row of choices latched down), sunken white check boxes, fields and drop-down boxes, a
    //    white drop-down list with a navy row, etched rules, and the theme's scrollbars.
    internal static class WwTheme
    {
        private static GUIStyle _styledBody;
        private static bool _choices;   // inside ToolLayout.Choices: its chosen button is latched, not a default button

        private static readonly AccessTools.FieldRef<GUIStyle> CloseText = AccessTools.StaticFieldRefAccess<GUIStyle>(AccessTools.Field(typeof(Ledger), "CloseText"));
        private static readonly AccessTools.FieldRef<GUIStyle> InputRight = AccessTools.StaticFieldRefAccess<GUIStyle>(AccessTools.Field(typeof(Ledger), "InputRight"));
        private static readonly AccessTools.FieldRef<LedgerDropdown, Rect> DropBox = AccessTools.FieldRefAccess<LedgerDropdown, Rect>("_box");
        private static readonly AccessTools.FieldRef<LedgerDropdown, Vector2> DropScroll = AccessTools.FieldRefAccess<LedgerDropdown, Vector2>("_scroll");
        private static readonly AccessTools.FieldRef<LedgerDropdown, string> DropSelected = AccessTools.FieldRefAccess<LedgerDropdown, string>("_selected");
        private static readonly AccessTools.FieldRef<LedgerDropdown, List<KeyValuePair<string, string>>> DropOptions = AccessTools.FieldRefAccess<LedgerDropdown, List<KeyValuePair<string, string>>>("_options");

        public static void Install(Harmony h)
        {
            // the palette: static readonly fields, set once before anything draws
            Set("Background", "C0C0C0"); Set("Surface", "C0C0C0"); Set("Raised", "C0C0C0"); Set("Field", "FFFFFF");
            Set("Text", "000000"); Set("Muted", "808080"); Set("Line", "808080"); Set("Accent", "000080");
            Set("Selected", "FFFFFF"); Set("Danger", "800000");
            PixelStyles.Install(h);

            var me = typeof(WwTheme);
            h.Patch(AccessTools.Method(typeof(Ledger), nameof(Ledger.Begin)), postfix: new HarmonyMethod(me, nameof(BeginPostfix)));
            Prefix(h, nameof(Ledger.Window), new[] { typeof(Rect) }, nameof(Window));
            Prefix(h, nameof(Ledger.Header), new[] { typeof(float), typeof(string) }, nameof(Header));
            Prefix(h, nameof(Ledger.Panel), null, nameof(Panel));
            Prefix(h, nameof(Ledger.Button), null, nameof(Button));
            Prefix(h, nameof(Ledger.ButtonLabel), null, nameof(ButtonLabel));
            Prefix(h, nameof(Ledger.Icon), null, nameof(Icon));
            Prefix(h, nameof(Ledger.TextButton), null, nameof(TextButton));
            Prefix(h, nameof(Ledger.Dropdown), null, nameof(Dropdown));
            Prefix(h, nameof(Ledger.Rule), null, nameof(Rule));
            Prefix(h, nameof(Ledger.Toggle), null, nameof(Toggle));
            Prefix(h, nameof(Ledger.Scroll), null, nameof(Scroll));
            h.Patch(AccessTools.Method(typeof(LedgerDropdown), nameof(LedgerDropdown.Draw)), prefix: new HarmonyMethod(me, nameof(DropdownDraw)));
            var choices = AccessTools.Method(typeof(ToolLayout), nameof(ToolLayout.Choices));
            if (choices != null) h.Patch(choices, prefix: new HarmonyMethod(me, nameof(ChoicesBegin)), finalizer: new HarmonyMethod(me, nameof(ChoicesEnd)));

            // the windows' scale, whole
            Whole(h, "WildWasteland.Core.UI.ToolWindow", "Draw");
            Whole(h, "WildWasteland.Modules.Patrols.TemplateMenu", "Gui");
            Whole(h, "WildWasteland.Modules.Patrols.TemplateExporter", "Gui");
        }

        private static void Set(string field, string hex) { AccessTools.Field(typeof(Ledger), field).SetValue(null, Win95.Hex(hex)); }
        private static void Prefix(Harmony h, string method, Type[] args, string patch)
        {
            var original = AccessTools.Method(typeof(Ledger), method, args);
            if (original == null) throw new MissingMethodException("Ledger." + method);
            h.Patch(original, prefix: new HarmonyMethod(typeof(WwTheme), patch));
        }

        // ---------------------------------------------------------------- a whole scale
        private static readonly MethodInfo Trs = AccessTools.Method(typeof(Matrix4x4), nameof(Matrix4x4.TRS));
        private static void Whole(Harmony h, string type, string method)
        {
            var m = AccessTools.Method(AccessTools.TypeByName(type), method);
            if (m == null) { Plugin.Log.LogWarning("Wild Wasteland windows: " + type + "." + method + " not found: its scale stays as it is"); return; }
            h.Patch(m, transpiler: new HarmonyMethod(typeof(WwTheme), nameof(WholeScale)));
        }
        private static IEnumerable<CodeInstruction> WholeScale(IEnumerable<CodeInstruction> code)
        {
            foreach (var ins in code)
            {
                if (ins.Calls(Trs)) { yield return new CodeInstruction(System.Reflection.Emit.OpCodes.Call, AccessTools.Method(typeof(WwTheme), nameof(WholeTrs))) { labels = ins.labels, blocks = ins.blocks }; continue; }
                yield return ins;
            }
        }
        // the window's matrix (centred at scale s): the same centre at the largest whole scale that fits
        public static Matrix4x4 WholeTrs(Vector3 t, Quaternion r, Vector3 s)
        {
            if (s.x <= 0) return Matrix4x4.TRS(t, r, s);
            float w = (Screen.width - 2 * t.x) / s.x, hgt = (Screen.height - 2 * t.y) / s.y;   // the window's own size
            float k = s.x >= 1 ? Mathf.Floor(s.x + .001f) : s.x;   // a screen too small for it at 1: as it was
            return Matrix4x4.TRS(new Vector3(Mathf.Round((Screen.width - w * k) / 2f), Mathf.Round((Screen.height - hgt * k) / 2f), 0), r, new Vector3(k, k, 1));
        }

        // ---------------------------------------------------------------- styles: after Ledger builds (or rebuilds) them
        private static void BeginPostfix()
        {
            Ui.K = 1;   // its windows are laid out in their own pixels; the matrix scales
            if (Ledger.Body == null || Ledger.Body == _styledBody) return;
            _styledBody = Ledger.Body;
            foreach (var st in new[] { Ledger.Body, Ledger.Small, Ledger.Heading, Ledger.Brand, Ledger.GameButton, Ledger.DialogTitle, Ledger.Input, InputRight(), CloseText() })
            {
                if (st == null) continue;
                PixelStyles.Mark(st, true);
                // Ledger.Label colours text through GUI.contentColor: the styles' own colour is white so it shows
                st.normal.textColor = st.hover.textColor = st.active.textColor = st.focused.textColor = Color.white;
            }
            foreach (var st in new[] { Ledger.Heading, Ledger.DialogTitle, Ledger.Brand, Ledger.GameButton }) if (st != null) st.fontStyle = FontStyle.Bold;
            if (CloseText() != null) CloseText().fontStyle = FontStyle.Normal;
            foreach (var st in new[] { Ledger.Input, InputRight() })
            {
                if (st == null) continue;
                st.border = new RectOffset(2, 2, 2, 2); st.padding = new RectOffset(4, 4, 2, 2);
                st.normal.background = st.hover.background = st.focused.background = st.active.background = Win95.SunkenWhite;
            }
        }

        // "WILD WASTELAND  ·  SETTINGS" -> "Wild Wasteland · Settings"; "CLOSE" -> "Close"
        private static string Title(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            while (s.Contains("  ")) s = s.Replace("  ", " ");
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());
        }
        private static bool Shouting(string s) { return s != null && s.Length > 1 && s.ToUpperInvariant() == s && s.ToLowerInvariant() != s; }

        // ---------------------------------------------------------------- primitives
        private static bool Window(Rect r) { Win95.Fill(r, Win95.Face); Win95.Window(r); return false; }

        // the title bar (the header's band below it stays the window's face, the toolbar row its callers put there)
        private static bool Header(float width, string title)
        {
            var bar = new Rect(3, 3, width - 6, 18);
            Win95.Fill(bar, Win95.Navy);
            PixelFont.Bold.Draw(new Rect(bar.x + 3, bar.y, bar.width - 6, bar.height), Title(title), Color.white, 1);
            Win95.Etched(6, 82, width - 12);
            return false;
        }

        // sunken; an accent border (a selected card, an open list) adds a navy outline inside the bevel. Dark fills from
        // the old palette (a hard-coded one in the patrol editor) become the panel face.
        private static bool Panel(Rect r, Color? fill, Color? border)
        {
            var f = fill ?? Ledger.Surface;
            if (f.a > .5f && f.grayscale < .25f) f = Ledger.Surface;
            Win95.Fill(r, f); Win95.Sunken(r);
            if (border.HasValue && border.Value == Ledger.Accent) Win95.Outline(new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4), Ledger.Accent);
            return false;
        }

        private static void ChoicesBegin() { _choices = true; }
        private static Exception ChoicesEnd(Exception __exception) { _choices = false; return __exception; }

        // raised grey, pressed in while held; the primary button has the default button's black outline, the chosen one
        // of a row of choices is latched down
        private static bool Button(Rect r, string text, bool enabled, bool primary, bool gameFont, Color? color, ref bool __result)
        {
            bool previous = GUI.enabled; GUI.enabled = previous && enabled;
            bool latched = primary && _choices;
            if (Shouting(text)) text = Title(text);
            bool hit = Win95.Button(r, out bool held);
            Win95.Fill(r, Win95.Face);
            var face = primary && !latched ? new Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2) : r;
            if (primary && !latched) Win95.Outline(r, Win95.Dark);
            if (latched)
            {
                GUI.DrawTextureWithTexCoords(new Rect(face.x + 2, face.y + 2, face.width - 4, face.height - 4), Win95.Dither, new Rect(0, 0, (face.width - 4) / 2f, (face.height - 4) / 2f));
                Win95.Latched(face);
            }
            else if (held) Win95.Pressed(face); else Win95.Raised(face);
            if (held || latched) r = new Rect(r.x + 1, r.y + 1, r.width, r.height);
            var ink = color ?? Ledger.Text;
            if (text.StartsWith("+  ", StringComparison.Ordinal))
            {
                string caption = text.Substring(3);
                float length = Ledger.Body.CalcSize(new GUIContent(caption)).x, left = Mathf.Round(r.center.x - (length + 12) / 2);
                Ui.Glyph(left, Mathf.Round(r.center.y - 3), Plus, GUI.enabled ? ink : Win95.Shadow);
                Ledger.Label(new Rect(left + 12, r.y, length + 1, r.height), caption, Ledger.Body, ink);
            }
            else Ledger.Label(r, text, gameFont ? CloseText() : Ledger.Body, ink, TextAnchor.MiddleCenter);
            GUI.enabled = previous;
            __result = hit;
            return false;
        }
        private static readonly string[] Plus = { "  X  ", "  X  ", "XXXXX", "  X  ", "  X  " };

        // its labels on buttons are shifted for Segoe UI's line box: the pixel font is centred as it is
        private static bool ButtonLabel(Rect r, string text, GUIStyle style, Color ink, TextAnchor alignment)
        {
            Ledger.Label(r, text, style, ink, alignment);
            return false;
        }

        // a toolbar button: raised under the mouse
        private static bool Icon(Rect r, string name, bool enabled, Color? color, ref bool __result)
        {
            bool previous = GUI.enabled; GUI.enabled = previous && enabled;
            bool hit = GUI.Button(r, GUIContent.none, GUIStyle.none);
            if (GUI.enabled && r.Contains(Event.current.mousePosition)) Win95.Raised(r);
            var tint = enabled ? color ?? Ledger.Text : new Color(Ledger.Muted.r, Ledger.Muted.g, Ledger.Muted.b, .45f);
            Ledger.IconImage(new Rect(Mathf.Round(r.center.x - 8), Mathf.Round(r.center.y - 8), 16, 16), name, tint);
            GUI.enabled = previous;
            __result = hit;
            return false;
        }

        // a small push button, or a link-like text when not boxed
        private static bool TextButton(Rect r, string text, bool boxed, bool enabled, ref bool __result)
        {
            bool previous = GUI.enabled; GUI.enabled = previous && enabled;
            bool hover = GUI.enabled && r.Contains(Event.current.mousePosition);
            bool hit = Win95.Button(r, out bool held);
            if (boxed) { Win95.Fill(r, Win95.Face); if (held) Win95.Pressed(r); else Win95.Raised(r); }
            var ink = boxed ? Ledger.Text : hover ? Ledger.Accent : Ledger.Text;
            Ledger.Label(held && boxed ? new Rect(r.x + 1, r.y + 1, r.width, r.height) : r, text, Ledger.Small, ink, TextAnchor.MiddleCenter);
            GUI.enabled = previous;
            __result = hit;
            return false;
        }

        // a combo box: sunken white with the drop button at its right end
        private static bool Dropdown(Rect r, string text, ref bool __result)
        {
            int k = Ui.K; Ui.K = 1;
            __result = Ui.Combo(new Rect(r.x, Mathf.Round(r.center.y - 10.5f), r.width, 21), text);
            Ui.K = k;
            return false;
        }

        private static bool Rule(float x, float y, float width) { Win95.Etched(x, y, width); return false; }

        // a sunken white check box with a black tick
        private static bool Toggle(Rect r, bool value, string label, ref bool __result)
        {
            int k = Ui.K; Ui.K = 1;
            __result = Ui.Check(r, value, null);
            Ui.K = k;
            Ledger.Label(new Rect(r.x + 18, r.y, r.width - 18, r.height), label);
            return false;
        }

        // the theme's scrollbar (arrows, a dithered track, a raised thumb) and the mouse wheel
        private static bool Scroll(Rect r, Vector2 scroll, float height, Action<float> draw, ref Vector2 __result)
        {
            int k = Ui.K; Ui.K = 1;
            float y = scroll.y;
            float w = Ui.BeginScroll(r, ref y, height);
            try { draw(w); }
            finally { Ui.EndScroll(); Ui.K = k; }
            __result = new Vector2(scroll.x, y);
            return false;
        }

        // the open list: white in a black frame, the row under the mouse (else the chosen one) navy with white text; its
        // rows and scrolling are its own (hit-tested and wheeled in its Before)
        private static bool DropdownDraw(LedgerDropdown __instance)
        {
            if (!__instance.IsOpen) return false;
            var box = DropBox(__instance); var options = DropOptions(__instance); string selected = DropSelected(__instance);
            float scroll = DropScroll(__instance).y;
            Win95.Fill(box, Color.white); Win95.Outline(box, Win95.Dark);
            var inner = new Rect(box.x + 4, box.y + 4, box.width - 8, box.height - 8);
            GUI.BeginGroup(inner);
            var mouse = Event.current.mousePosition;
            int lit = -1;
            for (int i = 0; i < options.Count; i++) if (new Rect(0, i * 34 - scroll, inner.width, 34).Contains(mouse)) lit = i;
            if (lit < 0) lit = options.FindIndex(o => o.Key == selected);
            for (int i = 0; i < options.Count; i++)
            {
                var row = new Rect(0, i * 34 - scroll, inner.width, 34);
                if (row.yMax < 0 || row.y > inner.height) continue;
                if (i == lit) Win95.Fill(row, Win95.Navy);
                Ledger.Label(new Rect(9, row.y, inner.width - 18, 34), options[i].Value, Ledger.Body, i == lit ? Color.white : Ledger.Text);
            }
            GUI.EndGroup();
            return false;
        }
    }
}
