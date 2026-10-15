using System.Collections.Generic;
using UnityEngine;

namespace Chicago95
{
    // Windows 95 controls for IMGUI, in Windows pixels times a whole-number scale K (1 up to 2000 screen lines, 2 at
    // 4K): every line K screen pixels, text the desktop's bitmap font (PixelFont). Push buttons, check boxes, text
    // fields, combo boxes, trackbars, property-sheet tabs, list rows and scrolling panes with Windows' own scrollbar.
    internal static class Ui
    {
        public static int K = 1;
        public const int Line = 13;   // the font's line height (Windows pixels)

        public static void Begin() { K = Mathf.Max(1, Screen.height / 2000 + 1); }

        // ---------------------------------------------------------------- text
        public static PixelFont Font(bool bold) { return bold ? PixelFont.Bold : PixelFont.Regular; }

        public static void Text(Rect r, string s, Color c, TextAnchor a = TextAnchor.MiddleLeft, bool bold = false)
        {
            if (!GUI.enabled && c != Color.white) { Font(bold).Draw(new Rect(r.x + K, r.y + K, r.width, r.height), s, Color.white, K, a); c = Win95.Shadow; }
            Font(bold).Draw(r, s, c, K, a);
        }

        // one line cut to fit with "..."
        public static void Fit(Rect r, string s, Color c, TextAnchor a = TextAnchor.MiddleLeft, bool bold = false)
        {
            Text(r, Font(bold).Fit(s, (int)(r.width / K)), c, a, bold);
        }

        public static float WrapHeight(string s, float width, bool bold = false)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            return Font(bold).Wrap(s, Mathf.Max(1, (int)(width / K))).Count * Line * K;
        }

        // wrapped from the top of r (its height is ignored); returns the height used
        public static float Wrap(Rect r, string s, Color c, bool bold = false)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            var font = Font(bold);
            var lines = font.Wrap(s, Mathf.Max(1, (int)(r.width / K)));
            float y = r.y;
            foreach (var l in lines)
            {
                if (!GUI.enabled) { font.Draw(r.x + K, y + K, l, Color.white, K); font.Draw(r.x, y, l, Win95.Shadow, K); }
                else font.Draw(r.x, y, l, c, K);
                y += Line * K;
            }
            return y - r.y;
        }

        // ---------------------------------------------------------------- glyphs (pixel patterns)
        public static readonly string[] Cross = { "XX    XX", " XX  XX ", "  XXXX  ", "   XX   ", "  XXXX  ", " XX  XX ", "XX    XX" };
        public static readonly string[] Tick = { "      X", "     XX", "X   XXX", "XX XXX ", "XXXXX  ", " XXX   ", "  X    " };
        public static readonly string[] Down = { "XXXXXXX", " XXXXX ", "  XXX  ", "   X   " };
        public static readonly string[] Up = { "   X   ", "  XXX  ", " XXXXX ", "XXXXXXX" };

        public static void Glyph(float x, float y, string[] g, Color c)
        {
            for (int j = 0; j < g.Length; j++)
                for (int i = 0; i < g[j].Length; i++)
                    if (g[j][i] == 'X') Win95.Fill(new Rect(x + i * K, y + j * K, K, K), c);
        }
        private static void GlyphCentred(Rect r, string[] g, Color c, bool shifted)
        {
            float x = r.x + Mathf.Floor((r.width / K - g[0].Length) / 2f) * K + (shifted ? K : 0);
            float y = r.y + Mathf.Floor((r.height / K - g.Length) / 2f) * K + (shifted ? K : 0);
            if (!GUI.enabled) { Glyph(x + K, y + K, g, Color.white); c = Win95.Shadow; }
            Glyph(x, y, g, c);
        }

        // ---------------------------------------------------------------- frames
        public static void Raised(Rect r) { Win95.Raised(r, K); }
        public static void Sunken(Rect r) { Win95.Sunken(r, K); }
        public static void Etched(float x, float y, float w) { Win95.Etched(x, y, w, K); }
        public static void Field(Rect r) { Win95.Fill(r, Color.white); Win95.Sunken(r, K); }
        public static void Panel(Rect r) { Win95.Fill(r, Win95.Face); Win95.Window(r, K); }

        // a window: face, raised frame, navy title bar with its bold white title and close box (true when clicked)
        public static bool Window(Rect r, string title, bool closable = true)
        {
            Panel(r);
            var bar = new Rect(r.x + 3 * K, r.y + 3 * K, r.width - 6 * K, 18 * K);
            Win95.Fill(bar, Win95.Navy);
            Text(new Rect(bar.x + 3 * K, bar.y, bar.width - 24 * K, bar.height), title, Color.white, TextAnchor.MiddleLeft, true);
            return closable && GlyphButton(new Rect(bar.xMax - 2 * K - 16 * K, bar.y + 2 * K, 16 * K, 14 * K), Cross);
        }

        // a group box: an etched frame whose top line runs through the caption, broken under it
        public static void Group(Rect r, string title)
        {
            float top = r.y + 6 * K;
            var f = new Rect(r.x, top, r.width, r.yMax - top);
            Win95.Outline(new Rect(f.x + K, f.y + K, f.width - K, f.height - K), Color.white, K);
            Win95.Outline(new Rect(f.x, f.y, f.width - K, f.height - K), Win95.Shadow, K);
            if (string.IsNullOrEmpty(title)) return;
            float tw = (Font(false).Width(title) + 4) * K;
            Win95.Fill(new Rect(r.x + 7 * K, top, tw, 2 * K), Win95.Face);
            Text(new Rect(r.x + 9 * K, r.y, tw, Line * K), title, Color.black);
        }

        // a pale yellow notice (Windows' info colour) in a black frame
        public static void Notice(Rect r)
        {
            Win95.Fill(r, Win95.Hex("FFFFE1"));
            Win95.Outline(r, Color.black, K);
        }

        // ---------------------------------------------------------------- buttons
        public static bool Button(Rect r, string text, bool enabled = true, bool isDefault = false, bool latched = false)
        {
            var was = GUI.enabled; GUI.enabled = was && enabled;
            bool hit = Win95.Button(r, out bool held);
            Win95.Fill(r, Win95.Face);
            var face = isDefault ? new Rect(r.x + K, r.y + K, r.width - 2 * K, r.height - 2 * K) : r;
            if (isDefault) Win95.Outline(r, Win95.Dark, K);
            if (latched)
            {
                GUI.DrawTextureWithTexCoords(new Rect(face.x + 2 * K, face.y + 2 * K, face.width - 4 * K, face.height - 4 * K), Win95.Dither,
                    new Rect(0, 0, (face.width - 4 * K) / (2f * K), (face.height - 4 * K) / (2f * K)));
                Win95.Latched(face, K);
            }
            else if (held) Win95.Pressed(face, K);
            else Win95.Raised(face, K);
            var tr = held || latched ? new Rect(r.x + K, r.y + K, r.width, r.height) : r;
            Text(tr, Font(false).Fit(text, (int)(r.width / K) - 6), Color.black, TextAnchor.MiddleCenter);
            GUI.enabled = was;
            return hit;
        }

        public static bool GlyphButton(Rect r, string[] glyph)
        {
            bool hit = Win95.Button(r, out bool held);
            Win95.Fill(r, Win95.Face);
            if (held) Win95.Pressed(r, K); else Win95.Raised(r, K);
            GlyphCentred(r, glyph, Color.black, held);
            return hit;
        }

        // the width a push button needs for its caption
        public static float ButtonWidth(string text, float min = 75) { return Mathf.Max(min, Font(false).Width(text) + 16) * K; }

        // a check box (13 x 13, sunken white, black tick) and its label; returns the new value
        public static bool Check(Rect r, bool value, string label, bool enabled = true)
        {
            var was = GUI.enabled; GUI.enabled = was && enabled;
            bool hit = GUI.Button(r, GUIContent.none, GUIStyle.none);
            var box = new Rect(r.x, r.y + Mathf.Floor((r.height / K - 13) / 2f) * K, 13 * K, 13 * K);
            Win95.Fill(box, GUI.enabled ? Color.white : Win95.Face); Win95.Sunken(box, K);
            if (value) Glyph(box.x + 3 * K, box.y + 3 * K, Tick, GUI.enabled ? Color.black : Win95.Shadow);
            if (!string.IsNullOrEmpty(label)) Text(new Rect(box.xMax + 5 * K, r.y, r.width - 18 * K, r.height), label, Color.black);
            GUI.enabled = was;
            return hit ? !value : value;
        }

        // ---------------------------------------------------------------- text field
        // A sunken white field in the pixel font: click to type (keyboard focus, so control names work), Enter or Escape
        // to finish, Backspace removes; the placeholder shows grey while empty and unfocused.
        public static string TextField(Rect r, string text, int max, string placeholder = null, string name = null, bool digitsOnly = false)
        {
            text = text ?? "";
            if (name != null) GUI.SetNextControlName(name);
            int id = GUIUtility.GetControlID(FocusType.Keyboard, r);
            var e = Event.current;
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (r.Contains(e.mousePosition) && GUI.enabled) { GUIUtility.keyboardControl = id; e.Use(); }
                    break;
                case EventType.KeyDown:
                    if (GUIUtility.keyboardControl != id) break;
                    if (e.keyCode == KeyCode.Backspace) { if (text.Length > 0) { text = text.Substring(0, text.Length - 1); GUI.changed = true; } e.Use(); }
                    else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Escape) GUIUtility.keyboardControl = 0;
                    else if (e.character >= ' ' && e.character != 127)
                    {
                        if (text.Length < max && (!digitsOnly || char.IsDigit(e.character) || e.character == '.' || e.character == '-')) { text += e.character; GUI.changed = true; }
                        e.Use();
                    }
                    break;
            }
            if (e.type == EventType.Repaint)
            {
                Win95.Fill(r, GUI.enabled ? Color.white : Win95.Face); Win95.Sunken(r, K);
                var inner = new Rect(r.x + 4 * K, r.y, r.width - 8 * K, r.height);
                bool focused = GUIUtility.keyboardControl == id;
                if (text.Length == 0 && !focused && placeholder != null) Text(inner, placeholder, Win95.Shadow);
                else
                {
                    var font = Font(false);
                    string shown = text;
                    while (shown.Length > 0 && font.Width(shown) * K > inner.width - 2 * K) shown = shown.Substring(1);   // the end stays in view
                    Text(inner, shown, Color.black);
                    if (focused && ((int)(Time.unscaledTime * 2f)) % 2 == 0)
                        Win95.Fill(new Rect(inner.x + font.Width(shown) * K + K, r.y + 3 * K, K, r.height - 6 * K), Color.black);   // the caret
                }
            }
            return text;
        }

        // ---------------------------------------------------------------- drop-down list (a combo box's)
        // Floats over everything, under its box, in screen space: PopupInput takes the mouse first (call it at the start
        // of the window's OnGUI, before any control), PopupDraw draws it last (at the end). Opens on the chosen entry;
        // that entry is highlighted until the mouse is over another; a click outside closes it and does nothing else.
        private static string _popKey;
        private static object _popOwner;
        private static Rect _popRect;
        private static List<string> _popOptions;
        private static int _popChosen, _popHover = -1;
        private static float _popScroll;
        private static System.Action<int> _popPick;
        private const int PopRows = 8;
        public static bool PopupOpen(string key) { return _popKey == key; }

        // a combo box whose list is the drop-down; returns nothing: picking calls pick
        public static void Combo(object owner, string key, Rect r, string value, List<string> options, int chosen, System.Action<int> pick, bool enabled = true)
        {
            if (Combo(r, value, enabled))
            {
                if (_popKey == key) { _popKey = null; return; }
                var tl = GUIUtility.GUIToScreenPoint(new Vector2(r.x, r.yMax));
                float h = Mathf.Min(PopRows, options.Count) * 16 * K + 2 * K;
                _popRect = new Rect(tl.x, tl.y, r.width, h);
                if (_popRect.yMax > Screen.height) _popRect.y = GUIUtility.GUIToScreenPoint(new Vector2(r.x, r.y)).y - h;   // no room below: above
                _popKey = key; _popOwner = owner; _popOptions = options; _popChosen = chosen; _popPick = pick; _popHover = -1;
                _popScroll = Mathf.Clamp(chosen - PopRows / 2, 0, Mathf.Max(0, options.Count - PopRows)) * 16 * K;
            }
        }
        public static void PopupClose() { _popKey = null; }

        private static bool PopBar => _popOptions != null && _popOptions.Count > PopRows;
        private static Rect PopList => new Rect(_popRect.x + K, _popRect.y + K, _popRect.width - 2 * K - (PopBar ? 16 * K : 0), _popRect.height - 2 * K);
        private static Rect PopTrack => new Rect(_popRect.xMax - K - 16 * K, _popRect.y + K, 16 * K, _popRect.height - 2 * K);

        public static void PopupInput(object owner)
        {
            if (_popKey == null || _popOwner != owner) return;
            var e = Event.current;
            if (!e.isMouse && e.type != EventType.ScrollWheel) return;
            var m = GUIUtility.GUIToScreenPoint(e.mousePosition);
            float rowH = 16 * K, maxScroll = Mathf.Max(0, _popOptions.Count * rowH - PopList.height);
            if (!_popRect.Contains(m))
            {
                if (e.type == EventType.MouseDown || e.type == EventType.ScrollWheel) { _popKey = null; e.Use(); }   // outside: it closes
                return;
            }
            var list = PopList;
            if (e.type == EventType.ScrollWheel) { _popScroll = Mathf.Clamp(_popScroll + e.delta.y * 3 * rowH, 0, maxScroll); e.Use(); return; }
            if (list.Contains(m))
            {
                _popHover = Mathf.FloorToInt((m.y - list.y + _popScroll) / rowH);
                if (e.type == EventType.MouseUp && _popHover >= 0 && _popHover < _popOptions.Count) { var pick = _popPick; int i = _popHover; _popKey = null; pick?.Invoke(i); }
                e.Use();
                return;
            }
            // the scrollbar: arrows scroll a row, the track a page, the thumb follows the mouse
            var track = PopTrack;
            if (e.type == EventType.MouseDown || e.type == EventType.MouseDrag)
            {
                if (m.y < track.y + 16 * K) _popScroll -= rowH;
                else if (m.y > track.yMax - 16 * K) _popScroll += rowH;
                else { float t = (m.y - track.y - 16 * K) / Mathf.Max(1, track.height - 32 * K); _popScroll = t * maxScroll; }
                _popScroll = Mathf.Clamp(Mathf.Round(_popScroll / rowH) * rowH, 0, maxScroll);
            }
            e.Use();
        }

        public static void PopupDraw(object owner)
        {
            if (_popKey == null || _popOwner != owner || Event.current.type != EventType.Repaint) return;
            Win95.Fill(_popRect, Color.white); Win95.Outline(_popRect, Color.black, K);
            var list = PopList;
            float rowH = 16 * K;
            var m = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);
            int lit = list.Contains(m) ? Mathf.FloorToInt((m.y - list.y + _popScroll) / rowH) : _popHover >= 0 ? _popHover : _popChosen;
            GUI.BeginGroup(GUIUtility.ScreenToGUIRect(list));
            for (int i = 0; i < _popOptions.Count; i++)
            {
                var r = new Rect(0, i * rowH - _popScroll, list.width, rowH);
                if (r.yMax < 0 || r.y > list.height) continue;
                if (i == lit) Win95.Fill(r, Win95.Navy);
                Fit(new Rect(r.x + 3 * K, r.y, r.width - 6 * K, rowH), _popOptions[i], i == lit ? Color.white : Color.black);
            }
            GUI.EndGroup();
            if (PopBar)
            {
                var track = GUIUtility.ScreenToGUIRect(PopTrack);
                float a = 16 * K;
                var up = new Rect(track.x, track.y, track.width, a); var down = new Rect(track.x, track.yMax - a, track.width, a);
                Win95.Fill(up, Win95.Face); Win95.Raised(up, K); GlyphCentred(up, Up, Color.black, false);
                Win95.Fill(down, Win95.Face); Win95.Raised(down, K); GlyphCentred(down, Down, Color.black, false);
                var inner = new Rect(track.x, track.y + a, track.width, track.height - 2 * a);
                GUI.DrawTextureWithTexCoords(inner, Win95.Dither, new Rect(0, 0, inner.width / (2f * K), inner.height / (2f * K)));
                float content = _popOptions.Count * rowH, maxScroll = Mathf.Max(1, content - list.height);
                float th = Mathf.Max(8 * K, inner.height * list.height / content);
                var thumb = new Rect(inner.x, inner.y + (inner.height - th) * _popScroll / maxScroll, inner.width, th);
                Win95.Fill(thumb, Win95.Face); Win95.Window(thumb, K);
            }
        }

        // ---------------------------------------------------------------- combo box (its box; Combo above adds the drop-down)
        public static bool Combo(Rect r, string value, bool enabled = true)
        {
            var was = GUI.enabled; GUI.enabled = was && enabled;
            bool hit = GUI.Button(r, GUIContent.none, GUIStyle.none);
            Win95.Fill(r, GUI.enabled ? Color.white : Win95.Face); Win95.Sunken(r, K);
            var drop = new Rect(r.xMax - 2 * K - 16 * K, r.y + 2 * K, 16 * K, r.height - 4 * K);
            Win95.Fill(drop, Win95.Face); Win95.Raised(drop, K);
            GlyphCentred(drop, Down, Color.black, false);
            Fit(new Rect(r.x + 4 * K, r.y, r.width - 24 * K, r.height), value, Color.black);
            GUI.enabled = was;
            return hit;
        }

        // ---------------------------------------------------------------- trackbar
        // a sunken groove and a raised 11 x 21 thumb; dragging is true while the thumb is held
        public static float Trackbar(Rect r, float value, float min, float max, out bool dragging)
        {
            int id = GUIUtility.GetControlID(FocusType.Passive, r);
            var e = Event.current;
            float thumbW = 11 * K, span = r.width - thumbW;
            float t = max > min ? Mathf.Clamp01((value - min) / (max - min)) : 0;
            dragging = GUIUtility.hotControl == id;
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (GUI.enabled && e.button == 0 && r.Contains(e.mousePosition)) { GUIUtility.hotControl = id; dragging = true; t = Mathf.Clamp01((e.mousePosition.x - r.x - thumbW / 2) / span); e.Use(); }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id) { t = Mathf.Clamp01((e.mousePosition.x - r.x - thumbW / 2) / span); e.Use(); }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; dragging = false; e.Use(); }
                    break;
            }
            var groove = new Rect(r.x, r.center.y - 2 * K, r.width, 4 * K);
            Win95.Sunken(groove, K);
            var thumb = new Rect(Mathf.Round(r.x + t * span), r.center.y - 10 * K, thumbW, 21 * K);
            Win95.Fill(thumb, Win95.Face); Win95.Raised(thumb, K);
            return min + t * (max - min);
        }

        // ---------------------------------------------------------------- property-sheet tabs
        // tabs along the top of a raised sheet (drawn first, its top edge at r.y + 20); the chosen one stands taller and
        // joins the sheet: the sheet's top edge is opened under it
        public static int Tabs(Rect r, IList<string> labels, int selected)
        {
            int chosen = selected;
            float x = r.x + 2 * K;
            for (int i = 0; i < labels.Count; i++)
            {
                float w = (Font(false).Width(labels[i]) + 14) * K;
                bool on = i == selected;
                var t = on ? new Rect(x - 2 * K, r.y, w + 4 * K, 20 * K) : new Rect(x, r.y + 2 * K, w, 18 * K);
                if (GUI.Button(t, GUIContent.none, GUIStyle.none)) chosen = i;
                if (on) t.height += 2 * K;   // down over the sheet's top edge
                Win95.Fill(t, Win95.Face);
                Win95.Fill(new Rect(t.x, t.y + 2 * K, K, t.height - 2 * K), Color.white);              // left edge
                Win95.Fill(new Rect(t.x + K, t.y + K, K, K), Color.white);                               // the rounded corner
                Win95.Fill(new Rect(t.x + 2 * K, t.y, t.width - 4 * K, K), Color.white);               // top edge
                Win95.Fill(new Rect(t.xMax - 2 * K, t.y + K, K, K), Color.black);
                Win95.Fill(new Rect(t.xMax - K, t.y + 2 * K, K, t.height - 2 * K), Color.black);       // right edge
                Win95.Fill(new Rect(t.xMax - 2 * K, t.y + 2 * K, K, t.height - 2 * K), Win95.Shadow);
                Text(new Rect(t.x, t.y, t.width, 18 * K), labels[i], Color.black, TextAnchor.MiddleCenter);
                x += w;
            }
            return chosen;
        }
        // the sheet under the tabs (y: the tabs' bottom); the chosen tab's bottom edge is left open
        public static void Sheet(Rect r) { Panel(r); }

        // ---------------------------------------------------------------- scrolling panes
        // A clipped pane whose content (height contentH) scrolls with Windows' scrollbar (16 wide: arrows, a dithered
        // track, a raised thumb) and the mouse wheel. Returns the content width; draw at (0, 0) inside, then EndScroll.
        public static float BeginScroll(Rect view, ref float scroll, float contentH, float pad = 0)
        {
            contentH += 2 * pad;
            bool bar = contentH > view.height;
            float bw = bar ? 16 * K : 0;
            float maxScroll = Mathf.Max(0, contentH - view.height);
            var e = Event.current;
            if (e.type == EventType.ScrollWheel && view.Contains(e.mousePosition)) { scroll += e.delta.y * 3 * Line * K; e.Use(); }
            if (bar) scroll = ScrollBar(new Rect(view.xMax - bw, view.y, bw, view.height), scroll, view.height, contentH);
            scroll = Mathf.Clamp(Mathf.Round(scroll), 0, maxScroll);
            GUI.BeginGroup(new Rect(view.x, view.y, view.width - bw, view.height));
            GUI.BeginGroup(new Rect(pad, pad - scroll, view.width - bw - 2 * pad, Mathf.Max(contentH, view.height)));
            return view.width - bw - 2 * pad;
        }
        public static void EndScroll() { GUI.EndGroup(); GUI.EndGroup(); }

        private static float ScrollBar(Rect r, float scroll, float viewH, float contentH)
        {
            float a = 16 * K, step = Line * K;
            if (GlyphButton(new Rect(r.x, r.y, r.width, a), Up)) scroll -= step;
            if (GlyphButton(new Rect(r.x, r.yMax - a, r.width, a), Down)) scroll += step;
            var track = new Rect(r.x, r.y + a, r.width, r.height - 2 * a);
            GUI.DrawTextureWithTexCoords(track, Win95.Dither, new Rect(0, 0, track.width / (2f * K), track.height / (2f * K)));
            float maxScroll = contentH - viewH;
            float th = Mathf.Max(8 * K, track.height * viewH / contentH), free = track.height - th;
            var thumb = new Rect(track.x, track.y + (maxScroll > 0 ? free * scroll / maxScroll : 0), track.width, th);
            int id = GUIUtility.GetControlID(FocusType.Passive, track);
            var e = Event.current;
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (e.button != 0 || !track.Contains(e.mousePosition)) break;
                    if (thumb.Contains(e.mousePosition)) { GUIUtility.hotControl = id; _grab = e.mousePosition.y - thumb.y; }
                    else scroll += e.mousePosition.y < thumb.y ? -viewH : viewH;   // a page
                    e.Use();
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id && free > 0) { scroll = (e.mousePosition.y - _grab - track.y) / free * maxScroll; e.Use(); }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; e.Use(); }
                    break;
            }
            Win95.Fill(thumb, Win95.Face); Win95.Window(thumb, K);
            return scroll;
        }
        private static float _grab;

        // ---------------------------------------------------------------- list rows
        // a list row: navy with white text when selected; true when clicked
        public static bool Row(Rect r, bool selected)
        {
            bool hit = GUI.Button(r, GUIContent.none, GUIStyle.none);
            if (selected) Win95.Fill(r, Win95.Navy);
            return hit;
        }
    }
}
