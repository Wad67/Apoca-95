using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace Chicago95
{
    // The Controls page of Settings: Wild Wasteland's key list (CleanHUD's KeyCatalog: the game's rebindable actions, the
    // keys the game reads directly, every key setting of every plugin and keys hardcoded in plugins) as a Windows 95 list
    // view, one category at a time. Clicking a key waits for the next key press (Backspace: none, Esc: cancel) and sets it
    // through the catalog's own setter, which saves it where it lives. Keys used twice are red.
    internal static class BindingsPage
    {
        private sealed class Slot { public KeyCode Key; public string Text; public Func<KeyCode, KeyCode[], string> Set; public bool Mouse, Shortcut; public List<string> Clash; }
        private sealed class Bind
        {
            public string Tab, Group, Label, Note, Why; public bool Mod; public int Order;
            public readonly List<Slot> Slots = new List<Slot>();
            public string Id => Group + "|" + Tab + "|" + Label + "|" + Order;
        }

        private const string Catalog = "WildWasteland.Modules.CleanHUD.KeyCatalog";
        private static Type _cat;
        private static MethodInfo _collect;
        private static FieldInfo _clashCount;
        private static PropertyInfo _scanDone;
        private static string[] _tabs;
        private static readonly Dictionary<string, FieldInfo> Fields = new Dictionary<string, FieldInfo>();

        // Wild Wasteland's catalog is there (its classes are internal: read by reflection)
        public static bool Available
        {
            get
            {
                if (_cat != null) return true;
                if (!Chainloader.PluginInfos.ContainsKey(Plugin.WildWasteland)) return false;
                var t = AccessTools.TypeByName(Catalog);
                if (t == null) return false;
                _collect = AccessTools.Method(t, "Collect"); _clashCount = AccessTools.Field(t, "ClashCount");
                _scanDone = AccessTools.Property(t, "ScanDone");
                _tabs = AccessTools.Field(t, "Tabs")?.GetValue(null) as string[];
                if (_collect == null || _tabs == null) { Plugin.Log.LogWarning("Controls: Wild Wasteland's key catalog has changed: no Controls page"); return false; }
                _cat = t;
                return true;
            }
        }

        private static object F(object o, string name)
        {
            var t = o.GetType(); string key = t.FullName + "." + name;
            if (!Fields.TryGetValue(key, out var f)) Fields[key] = f = AccessTools.Field(t, name) ?? throw new MissingFieldException(t.FullName, name);
            return f.GetValue(o);
        }

        // ---------------------------------------------------------------- the list
        private static List<Bind> _binds;
        private static int _clashes;
        private static bool _scanned;
        private static float _nextCollect;

        public static void Reset() { _binds = null; _cap = null; _msg = null; _sel = null; _scroll = 0; }

        private static void Collect()
        {
            _binds = new List<Bind>();
            try
            {
                foreach (var o in (IEnumerable)_collect.Invoke(null, null))
                {
                    var b = new Bind { Tab = (string)F(o, "Tab"), Group = (string)F(o, "Group"), Label = (string)F(o, "Label"), Note = (string)F(o, "Note"),
                        Why = (string)F(o, "Why"), Mod = (bool)F(o, "Mod"), Order = (int)F(o, "Order") };
                    foreach (var s in (IEnumerable)F(o, "Slots"))
                        b.Slots.Add(new Slot { Key = (KeyCode)F(s, "Key"), Text = (string)F(s, "Text"), Set = (Func<KeyCode, KeyCode[], string>)F(s, "Set"),
                            Mouse = (bool)F(s, "Mouse"), Shortcut = (bool)F(s, "Shortcut"), Clash = (List<string>)F(s, "Clash") });
                    _binds.Add(b);
                }
                _clashes = _clashCount != null ? (int)_clashCount.GetValue(null) : 0;
                _scanned = _scanDone == null || (bool)_scanDone.GetValue(null, null);
            }
            catch (Exception e) { Plugin.Log.LogError("Controls: reading the key list: " + e); _scanned = true; }
            _nextCollect = Time.unscaledTime + 1f;
        }

        // ---------------------------------------------------------------- names, as Windows writes them
        private static readonly Dictionary<string, string> TabNames = new Dictionary<string, string> {
            { "MOVEMENT", "Movement" }, { "ACTIONS", "Actions" }, { "WEAPONS", "Weapons" }, { "VEHICLE", "Vehicle" },
            { "ADJUST", "Adjusting parts" }, { "DEBUG", "Debug" }, { "MODS", "Mods" } };
        private static readonly HashSet<string> Acronyms = new HashSet<string> { "HUD", "UI", "FSM", "NPC", "NPCS", "POI", "FOV", "AI", "ID", "GPS", "LOD", "FPS", "HP", "RPM", "VVT", "ABS", "TCS", "XP", "UV", "GUI", "IMGUI", "OK" };
        private static string TabName(string t) { return TabNames.TryGetValue(t, out var n) ? n : Sentence(t); }

        // "HIDE / SHOW HUD" -> "Hide / show HUD"
        private static string Sentence(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var words = s.Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                var w = words[i];
                string core = w.Trim('(', ')', '[', ']', ',', '.', ':');
                if (Acronyms.Contains(core)) continue;
                w = w.ToLowerInvariant().Replace("bepinex", "BepInEx");
                words[i] = w;
            }
            var r = string.Join(" ", words);
            int f = 0; while (f < r.Length && !char.IsLetterOrDigit(r[f])) f++;
            return f < r.Length ? r.Substring(0, f) + char.ToUpperInvariant(r[f]) + r.Substring(f + 1) : r;
        }

        private static string ModName(string group)
        {
            foreach (var p in Chainloader.PluginInfos.Values)
                if (p?.Metadata?.Name != null && p.Metadata.Name.ToUpperInvariant() == group) return p.Metadata.Name;
            return Sentence(group);
        }

        // the catalog's "GAME: LABEL" / "MOD: LABEL"
        private static string Who(string who)
        {
            int c = who.IndexOf(": ", StringComparison.Ordinal);
            if (c < 0) return Sentence(who);
            string g = who.Substring(0, c), l = Sentence(who.Substring(c + 2));
            return g == "GAME" ? l : l + " (" + ModName(g) + ")";
        }

        private static string KeyName(KeyCode k)
        {
            if (k == KeyCode.None) return "None";
            if (k >= KeyCode.Alpha0 && k <= KeyCode.Alpha9) return ((int)(k - KeyCode.Alpha0)).ToString();
            if (k >= KeyCode.Keypad0 && k <= KeyCode.Keypad9) return "Num " + (int)(k - KeyCode.Keypad0);
            if (k >= KeyCode.Mouse3 && k <= KeyCode.Mouse6) return "Mouse " + (int)(k - KeyCode.Mouse0 + 1);
            if (k >= KeyCode.JoystickButton0 && k <= KeyCode.JoystickButton19) return "Pad " + (int)(k - KeyCode.JoystickButton0);
            switch (k)
            {
                case KeyCode.Mouse0: return "Left mouse";
                case KeyCode.Mouse1: return "Right mouse";
                case KeyCode.Mouse2: return "Middle mouse";
                case KeyCode.KeypadPlus: return "Num +";
                case KeyCode.KeypadMinus: return "Num -";
                case KeyCode.KeypadMultiply: return "Num *";
                case KeyCode.KeypadDivide: return "Num /";
                case KeyCode.KeypadPeriod: return "Num Del";
                case KeyCode.KeypadEnter: return "Num Enter";
                case KeyCode.KeypadEquals: return "Num =";
                case KeyCode.Return: return "Enter";
                case KeyCode.Escape: return "Esc";
                case KeyCode.Delete: return "Del";
                case KeyCode.Insert: return "Ins";
                case KeyCode.PageUp: return "Page Up";
                case KeyCode.PageDown: return "Page Down";
                case KeyCode.UpArrow: return "Up";
                case KeyCode.DownArrow: return "Down";
                case KeyCode.LeftArrow: return "Left";
                case KeyCode.RightArrow: return "Right";
                case KeyCode.LeftControl: return "Left Ctrl";
                case KeyCode.RightControl: return "Right Ctrl";
                case KeyCode.LeftShift: return "Left Shift";
                case KeyCode.RightShift: return "Right Shift";
                case KeyCode.LeftAlt: return "Left Alt";
                case KeyCode.RightAlt: return "Right Alt";
                case KeyCode.AltGr: return "Alt Gr";
                case KeyCode.LeftWindows: return "Left Windows";
                case KeyCode.RightWindows: return "Right Windows";
                case KeyCode.BackQuote: return "`";
                case KeyCode.Minus: return "-";
                case KeyCode.Equals: return "=";
                case KeyCode.Plus: return "+";
                case KeyCode.LeftBracket: return "[";
                case KeyCode.RightBracket: return "]";
                case KeyCode.Semicolon: return ";";
                case KeyCode.Quote: return "'";
                case KeyCode.Comma: return ",";
                case KeyCode.Period: return ".";
                case KeyCode.Slash: return "/";
                case KeyCode.Backslash: return "\\";
                case KeyCode.CapsLock: return "Caps Lock";
                case KeyCode.ScrollLock: return "Scroll Lock";
                case KeyCode.Numlock: return "Num Lock";
                case KeyCode.Print: return "Print Screen";
            }
            return k.ToString();
        }

        // a slot as a hot-key field shows it: "Ctrl + Shift + F5"; an axis by its name
        private static string SlotText(Slot s)
        {
            if (s.Text == null) return KeyName(s.Key);
            if (s.Key == KeyCode.None) return s.Text == "—" ? "None" : Title(s.Text);
            var parts = s.Text.Split('+');
            var mods = parts.Take(parts.Length - 1).Select(p => p == "CTRL" ? "Ctrl" : p == "SHIFT" ? "Shift" : p == "ALT" ? "Alt" : Title(p));
            return string.Join(" + ", mods.Concat(new[] { KeyName(s.Key) }).ToArray());
        }
        private static string Title(string s) { return string.Join(" ", s.Split(' ').Select(w => w.Length > 1 ? w[0] + w.Substring(1).ToLowerInvariant() : w).ToArray()); }

        // ---------------------------------------------------------------- waiting for a key
        private static Bind _cap;
        private static int _capSlot, _polled;
        private static float _capSince;
        private static bool _capMouseUp;
        private static string _msg, _sel;
        private static bool _msgBad;
        private static KeyCode[] _allKeys;
        private static readonly KeyCode[] Modifiers = { KeyCode.LeftControl, KeyCode.RightControl, KeyCode.LeftShift, KeyCode.RightShift, KeyCode.LeftAlt, KeyCode.RightAlt };

        public static bool Capturing => _cap != null;
        public static void Cancel() { _cap = null; }

        private static void StartCapture(Bind b, int si)
        {
            _sel = b.Id;
            var s = b.Slots[si];
            if (s.Set == null)
            {
                _msg = b.Why != null ? Sentence(b.Why) : b.Note == "AXIS" ? "Mouse and joystick axes are set in the game's input file."
                     : b.Mod ? "This key is hardcoded in " + ModName(b.Group) + "." : "This key is hardcoded in the game.";
                _msgBad = false;
                return;
            }
            _cap = b; _capSlot = si; _capSince = Time.unscaledTime; _capMouseUp = !Input.GetMouseButton(0); _msg = null;
        }

        // once a frame: the key pressed, if any
        private static void Poll()
        {
            if (_cap == null || _polled == Time.frameCount) return;
            _polled = Time.frameCount;
            if (!_capMouseUp && !Input.GetMouseButton(0)) _capMouseUp = true;
            if (Time.unscaledTime - _capSince < .15f) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { _cap = null; return; }
            if (Input.GetKeyDown(KeyCode.Backspace)) { EndCapture(KeyCode.None); return; }
            var s = _cap.Slots[_capSlot];
            if (_allKeys == null) _allKeys = ((KeyCode[])Enum.GetValues(typeof(KeyCode))).Where(k => k != KeyCode.None).Distinct().ToArray();
            foreach (var k in _allKeys)
            {
                bool mouse = k >= KeyCode.Mouse0 && k <= KeyCode.Mouse6;
                if (mouse && (!s.Mouse || (k == KeyCode.Mouse0 && !_capMouseUp))) continue;
                if (s.Shortcut && Modifiers.Contains(k)) continue;   // waits for the main key of a combination
                if (k >= KeyCode.JoystickButton0 && !s.Mouse) continue;
                if (Input.GetKeyDown(k)) { EndCapture(k); return; }
            }
            if (s.Shortcut) foreach (var k in Modifiers) if (Input.GetKeyUp(k)) { EndCapture(k); return; }   // a modifier alone
        }

        private static void EndCapture(KeyCode key)
        {
            var b = _cap; int si = _capSlot; _cap = null;
            var s = b.Slots[si];
            KeyCode[] mods = null;
            if (s.Shortcut && key != KeyCode.None && !Modifiers.Contains(key))
            {
                var l = new List<KeyCode>();
                if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) l.Add(KeyCode.LeftControl);
                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) l.Add(KeyCode.LeftShift);
                if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) l.Add(KeyCode.LeftAlt);
                mods = l.ToArray();
            }
            string err;
            try { err = s.Set(key, mods); }
            catch (Exception e) { err = "Could not set the key: " + e.Message; }
            string id = b.Id, label = Sentence(b.Label);
            Collect();
            if (err != null) { _msg = Sentence(err); _msgBad = true; return; }
            var ns = _binds.FirstOrDefault(x => x.Id == id)?.Slots.ElementAtOrDefault(si);
            string kt = ns != null ? SlotText(ns) : KeyName(key);
            if (ns != null && ns.Clash.Count > 0) { _msg = kt + " is also used by " + string.Join(", ", ns.Clash.Select(Who).ToArray()) + "."; _msgBad = true; }
            else { _msg = label + ": " + (key == KeyCode.None ? "none" : kt) + "."; _msgBad = false; }
            Plugin.Log.LogInfo("Controls: " + b.Group + " " + b.Label + " = " + kt);
        }

        // first in the window's OnGUI: while waiting, keys and clicks are for that alone
        public static void Absorb()
        {
            if (_cap == null) return;
            var e = Event.current;
            if (e.isKey || e.isMouse || e.type == EventType.ScrollWheel) e.Use();
        }

        // ---------------------------------------------------------------- the page
        private static int _tab;
        private static float _scroll, _contentH;

        public static void Draw(Rect page)
        {
            int k = Ui.K;
            if (_binds == null || !_scanned && Time.unscaledTime >= _nextCollect && _cap == null) Collect();   // mod code still being read
            Poll();
            var tabs = _tabs.ToList();
            _tab = Mathf.Clamp(_tab, 0, tabs.Count - 1);
            string tab = tabs[_tab];

            float y = page.y;
            Ui.Text(new Rect(page.x, y, 50 * k, 21 * k), "Category:", Color.black);
            Ui.Combo(SettingsWindow.Owner, "keys", new Rect(page.x + 56 * k, y, 150 * k, 21 * k), TabName(tab), tabs.Select(TabName).ToList(), _tab,
                i => { _tab = i; _scroll = 0; _sel = null; _msg = null; });
            y += 27 * k;

            float statusH = 2 * Ui.Line * k;
            var list = new Rect(page.x, y, page.width, page.yMax - statusH - 6 * k - y);
            Ui.Field(list);
            var inner = new Rect(list.x + 2 * k, list.y + 2 * k, list.width - 4 * k, list.height - 4 * k);
            float rowH = 18 * k, keyW = 104 * k;
            var view = new Rect(inner.x, inner.y + rowH, inner.width, inner.height - rowH);
            float cw = view.width - (_contentH > view.height ? 16 * k : 0);
            float c1 = cw - 2 * keyW, c2 = cw - keyW;
            bool mods = tab == "MODS";

            // column headers
            void Head(float x, float w, string text)
            {
                var r = new Rect(inner.x + x, inner.y, w, rowH);
                Win95.Fill(r, Win95.Face); Win95.Raised(r, k);
                Ui.Fit(new Rect(r.x + 5 * k, r.y, r.width - 8 * k, r.height), text, Color.black);
            }
            Head(0, c1, "Action");
            Head(c1, keyW, mods ? "Key" : "Key / +");
            Head(c2, inner.width - c2, mods ? "" : "Alternative / -");

            var rows = _binds.Where(b => b.Tab == tab).ToList();
            Bind selected = null;
            cw = Ui.BeginScroll(view, ref _scroll, _contentH);
            float ry = 0;
            try
            {
                string group = null; bool hard = false;
                foreach (var b in rows)
                {
                    string head = null;
                    if (mods) { if (b.Group != group) head = ModName(group = b.Group); }
                    else if (!hard && b.Order >= 10000) { hard = true; head = "Hardcoded in the game"; }
                    if (head != null)
                    {
                        if (ry > 0) ry += 4 * k;
                        Ui.Fit(new Rect(4 * k, ry, cw - 8 * k, rowH), head, Color.black, TextAnchor.MiddleLeft, true);
                        Ui.Etched(4 * k, ry + rowH - 2 * k, cw - 8 * k);
                        ry += rowH;
                    }
                    bool sel = b.Id == _sel;
                    if (sel) selected = b;

                    // the slots first: a click on one starts waiting for its key
                    for (int i = 0; i < b.Slots.Count && i < 2; i++)
                    {
                        var s = b.Slots[i];
                        var cell = new Rect((i == 0 ? c1 : c2) + 2 * k, ry + k, keyW - 4 * k, rowH - 2 * k);
                        if (GUI.Button(cell, GUIContent.none, GUIStyle.none)) StartCapture(b, i);
                        if (_cap == b && _capSlot == i)
                        {
                            Ui.Field(cell);
                            Ui.Fit(new Rect(cell.x + 4 * k, cell.y, cell.width - 6 * k, cell.height), "Press a key", Color.black);
                            continue;
                        }
                        var ink = s.Clash.Count > 0 ? Color.red : s.Set == null ? Win95.Shadow : Color.black;
                        Ui.Fit(new Rect(cell.x + 4 * k, cell.y, cell.width - 6 * k, cell.height), SlotText(s), ink);
                    }
                    var label = new Rect(4 * k, ry, c1 - 8 * k, rowH);
                    if (GUI.Button(new Rect(0, ry, c1, rowH), GUIContent.none, GUIStyle.none)) { _sel = b.Id; _msg = null; }
                    string text = Ui.Font(false).Fit(Sentence(b.Label), (int)((label.width - 4 * k) / k));
                    if (sel) Win95.Fill(new Rect(label.x - 2 * k, ry + k, Ui.Font(false).Width(text) * k + 4 * k, rowH - 2 * k), Win95.Navy);
                    Ui.Text(label, text, sel ? Color.white : Color.black);
                    ry += rowH;
                }
                if (rows.Count == 0) Ui.Text(new Rect(4 * k, 0, cw, rowH), "No keys", Win95.Shadow);
            }
            finally { Ui.EndScroll(); }
            _contentH = ry;

            // the status: what is happening, else what is chosen, else the clashes
            var status = new Rect(page.x, list.yMax + 6 * k, page.width, statusH);
            string say; Color sc = Color.black;
            if (_cap != null)
                say = "Press the new key for " + Sentence(_cap.Label) + ". Backspace: none. Esc: cancel." + (_cap.Slots[_capSlot].Shortcut ? " Hold Ctrl, Shift or Alt for a combination." : "");
            else if (_msg != null) { say = _msg; if (_msgBad) sc = Color.red; }
            else if (selected != null && selected.Slots.Any(s => s.Clash.Count > 0))
            {
                var s = selected.Slots.First(x => x.Clash.Count > 0);
                say = SlotText(s) + " is also used by " + string.Join(", ", s.Clash.Select(Who).ToArray()) + "."; sc = Color.red;
            }
            else if (selected != null && selected.Note != null) say = NoteText(selected);
            else if (_clashes > 0) { say = _clashes + (_clashes == 1 ? " key is" : " keys are") + " used twice (red). Click a key to change it."; sc = Color.red; }
            else say = "Click a key to change it." + (_scanned ? "" : " Still looking for keys hardcoded in mods...");
            Ui.Wrap(status, say, sc);
        }

        private static string NoteText(Bind b)
        {
            switch (b.Note)
            {
                case "HARDCODED": return "Hardcoded in the game: it can't be changed.";
                case "HARDCODED IN THE MOD": return "Hardcoded in " + ModName(b.Group) + ": it can't be changed.";
                case "NOT ACTIVE": return "Not active in this version of the game.";
                case "AXIS": return "An axis: set in the game's input file.";
                case "ADDED BY A MOD": return "Added to the game's controls by a mod.";
            }
            return b.Note.StartsWith("IN ", StringComparison.Ordinal) ? "Read from " + b.Note.Substring(3).ToLowerInvariant() + ": it can only be changed there." : Sentence(b.Note);
        }
    }
}
