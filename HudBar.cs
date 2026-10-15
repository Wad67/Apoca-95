using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace Chicago95
{
    // The game's HUD as a Windows 95 taskbar along the bottom of the screen: health and armour as block progress bars on
    // the left, a taskbar button for each item and weapon slot in the middle (the drawn one latched down), and a tray on
    // the right with the needs, ammunition, grenades, nuts, the heading and the clock.
    //
    // It is read from the game's own HUD, so whatever changes that changes this: the values from its texts (health,
    // needs, armour, ammo, nuts); the slots from its two slot groups, every icon (a RawImage the slot's FSM gives the
    // item's texture) with the label under it ("1", "F1": yellow when full, outlined when drawn) and, for quick items,
    // the count on it. A mod that adds slots by adding such pairs (Apocapocket's 4 to 6) adds buttons. The game's HUD
    // is hidden (its graphics' alpha), Wild Wasteland's CleanHUD vitals, weapon and slot panels too; its compass and
    // message log stay.
    internal sealed class HudBar : MonoBehaviour
    {
        public static void Install(GameObject host) { host.AddComponent<HudBar>(); }

        private sealed class Slot { public RawImage Icon; public Text Label, Count; }

        private Transform _canvas, _survival, _loading, _weaponSlots, _quickSlots;
        private Canvas _rootCanvas;
        private Text _health, _armor, _armorMax, _mag, _reserve, _nuts, _thirst, _hunger, _fatigue, _sanity, _radiation, _day, _time;
        private Transform _sanityLabel, _radiationLabel;
        private HutongGames.PlayMaker.FsmInt _grenades;
        private HutongGames.PlayMaker.FsmFloat _clock;   // the sky's timeline, hours (DayTime's Time)
        private readonly List<Slot> _quick = new List<Slot>(), _weapons = new List<Slot>();
        private readonly List<Graphic> _hidden = new List<Graphic>();
        private readonly List<CanvasGroup> _cleanHud = new List<CanvasGroup>();
        private float _nextScan;

        // ---------------------------------------------------------------- the game's HUD, found
        private static Text T(Transform root, string path) { var t = root != null ? root.Find(path) : null; return t != null ? t.GetComponent<Text>() : null; }

        private void Scan()
        {
            var root = TitleMenu.FindPath("Canvas");
            _rootCanvas = root != null ? root.GetComponent<Canvas>() : null;
            if (_rootCanvas == null || !_rootCanvas.isRootCanvas || root.Find("WeaponSlots_UI") == null) { _canvas = null; return; }
            _canvas = root;
            _survival = root.Find("SurvivalCanvas"); _loading = root.Find("LoadingScreen");
            _weaponSlots = root.Find("WeaponSlots_UI"); _quickSlots = root.Find("QuickItemSlots_UI");
            _health = T(root, "SurvivalCanvas/HealthLable/HealthText");
            _thirst = T(root, "SurvivalCanvas/ThirstLable/ThirstText"); _hunger = T(root, "SurvivalCanvas/HungerLable/HungerText");
            _fatigue = T(root, "SurvivalCanvas/FatigueLable/FatigueText");
            _sanityLabel = root.Find("SurvivalCanvas/SanityLable"); _sanity = T(root, "SurvivalCanvas/SanityLable/SanityText");
            _radiationLabel = root.Find("SurvivalCanvas/RadiationLable"); _radiation = T(root, "SurvivalCanvas/RadiationLable/RadiationText");
            _armor = T(root, "ArmorLable/ArmorText"); _armorMax = T(root, "ArmorLable/ArmorText/ArmorMaxText");
            _mag = T(root, "AmmoLable/AmmoInWeaponText"); _reserve = T(root, "AmmoLable/AmmoInWeaponText/AmmoText");
            _nuts = T(root, "NutsLable/NutsText");
            var sheet = TitleMenu.FindPath("PlayerSheet_Canvas/PlayerSheet");
            _day = T(sheet, "day/day number"); _time = T(sheet, "time/time number");
            if (_grenades == null) _grenades = HutongGames.PlayMaker.FsmVariables.GlobalVariables.FindFsmInt("ammo_grenade");
            if (_grenades == null)
            {
                var gm = GameObject.Find("__GameManager__");
                if (gm != null) foreach (var f in gm.GetComponents<PlayMakerFSM>()) if (f.FsmName == "Ammo") { _grenades = f.FsmVariables.FindFsmInt("ammo_grenade"); break; }
            }
            if (_clock == null)
            {
                var gm = GameObject.Find("__GameManager__");
                if (gm != null) foreach (var f in gm.GetComponents<PlayMakerFSM>()) if (f.FsmName == "DayTime") { _clock = f.FsmVariables.FindFsmFloat("Time"); break; }
            }
            Slots(_quickSlots, _quick); Slots(_weaponSlots, _weapons);

            // the game's HUD, hidden (and what mods add inside it)
            _hidden.Clear();
            foreach (var path in new[] { "Design_UI", "WeaponSlots_UI", "QuickItemSlots_UI", "NutsLable", "ArmorLable", "AmmoLable", "SurvivalCanvas", "Compass" })
            {
                var t = root.Find(path);
                if (t != null) _hidden.AddRange(t.GetComponentsInChildren<Graphic>(true));
            }
            // Wild Wasteland's own replacement of these panels (its compass and log stay)
            _cleanHud.Clear();
            foreach (var path in new[] { "CleanHUD/vitals", "CleanHUD/weapon", "CleanHUD/slots" })
            {
                var t = root.Find(path);
                if (t == null) continue;
                var g = t.GetComponent<CanvasGroup>() ?? t.gameObject.AddComponent<CanvasGroup>();
                _cleanHud.Add(g);
            }
        }

        // a slot group's slots: each icon with the label under it and, for quick items, the count on it (the texts
        // nearest it across, the lower one the label), left to right
        private static void Slots(Transform group, List<Slot> into)
        {
            into.Clear();
            if (group == null) return;
            var texts = group.GetComponentsInChildren<Text>(true);
            foreach (var icon in group.GetComponentsInChildren<RawImage>(true))
            {
                var ip = ((RectTransform)icon.transform).anchoredPosition;
                var near = new List<Text>();
                foreach (var t in texts)
                    if (t.transform.parent == icon.transform.parent && Mathf.Abs(((RectTransform)t.transform).anchoredPosition.x - ip.x) < 30f) near.Add(t);
                if (near.Count == 0) continue;
                near.Sort((a, b) => ((RectTransform)a.transform).anchoredPosition.y.CompareTo(((RectTransform)b.transform).anchoredPosition.y));
                into.Add(new Slot { Icon = icon, Label = near[0], Count = near.Count > 1 ? near[near.Count - 1] : null });
            }
            into.Sort((a, b) => ((RectTransform)a.Icon.transform).anchoredPosition.x.CompareTo(((RectTransform)b.Icon.transform).anchoredPosition.x));
        }

        private bool Visible
        {
            get
            {
                return _canvas != null && _rootCanvas.enabled && _canvas.gameObject.activeInHierarchy
                       && _survival != null && _survival.gameObject.activeInHierarchy
                       && (_loading == null || !_loading.gameObject.activeInHierarchy)
                       && !PauseMenu.Claimed && !TitleMenu.Claimed && !(LoadingDialog.Instance != null && LoadingDialog.Instance.Active);
            }
        }

        private void LateUpdate()
        {
            if (Time.unscaledTime >= _nextScan || (_canvas == null && Time.unscaledTime >= _nextScan - .5f)) { _nextScan = Time.unscaledTime + 1f; Scan(); }
            if (_canvas == null) return;
            foreach (var g in _hidden) if (g != null && g.canvasRenderer.GetAlpha() != 0f) g.canvasRenderer.SetAlpha(0f);
            foreach (var g in _cleanHud) if (g != null && g.alpha != 0f) g.alpha = 0f;
        }

        // ---------------------------------------------------------------- the taskbar
        private static string Of(Text t) { return t != null ? t.text.Trim() : ""; }
        private static bool On(Component c) { return c != null && c.gameObject.activeInHierarchy; }
        private static float Number(Text t)
        {
            var s = Of(t); int i = 0;
            while (i < s.Length && !(char.IsDigit(s[i]) || s[i] == '-')) i++;
            int j = i; while (j < s.Length && (char.IsDigit(s[j]) || s[j] == '.' || s[j] == ',' || (j == i && s[j] == '-'))) j++;
            return j > i && float.TryParse(s.Substring(i, j - i).Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
        }

        private const int BarH = 28;

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || !Visible) return;
            Ui.Begin();
            int k = Ui.K;
            int depth = GUI.depth; GUI.depth = 20;   // under the windows and the tooltips
            float top = Screen.height - BarH * k;
            var bar = new Rect(0, top, Screen.width, BarH * k);
            Win95.Fill(bar, Win95.Face);
            Win95.Fill(new Rect(0, top, Screen.width, k), Win95.Light);
            Win95.Fill(new Rect(0, top + k, Screen.width, k), Color.white);
            float y = top + 4 * k, h = 22 * k, x = 2 * k;

            // health and armour
            x = Meter(x, y, h, "HP", Number(_health), 100f, Mathf.CeilToInt(Number(_health)).ToString(CultureInfo.InvariantCulture));
            float armor = Number(_armor), max = Number(_armorMax);
            x = Meter(x + 4 * k, y, h, "Armor", armor, max, Mathf.RoundToInt(armor).ToString(CultureInfo.InvariantCulture));
            x += 6 * k;

            // the tray first (its width), then the slots between
            // each tray item in a cell as wide as its widest value, so the tray keeps still
            var tray = Tray();
            var font = Ui.Font(false);
            const int Gap = 12;
            float tw = 8;
            foreach (var c in tray) tw += Mathf.Max(font.Width(c.Key), font.Width(c.Value)) + Gap;
            tw = (tw - Gap + 8) * k;
            var trayRect = new Rect(Screen.width - 2 * k - tw, y, tw, h);
            Thin(trayRect);
            float cx = trayRect.x + 8 * k;
            foreach (var c in tray)
            {
                float cw = Mathf.Max(font.Width(c.Key), font.Width(c.Value)) * k;
                Ui.Text(new Rect(cx, y, cw, h), c.Key, Color.black);
                cx += cw + Gap * k;
            }

            float room = trayRect.x - 6 * k - x;
            int n = 0; foreach (var s in _quick) if (On(s.Icon)) n++; foreach (var s in _weapons) if (On(s.Icon)) n++;
            float bw = n > 0 ? Mathf.Min(150 * k, Mathf.Floor((room - (n + 1) * 3 * k - 6 * k) / n)) : 0;
            foreach (var s in _quick) if (On(s.Icon)) { Button(x, y, bw, h, s); x += bw + 3 * k; }
            x += 6 * k;
            foreach (var s in _weapons) if (On(s.Icon)) { Button(x, y, bw, h, s); x += bw + 3 * k; }
            GUI.depth = depth;
        }

        // a sunken status pane: the label, a block progress bar, the number
        private static float Meter(float x, float y, float h, string label, float value, float max, string number)
        {
            int k = Ui.K;
            float lw = (Ui.Font(true).Width(label) + 8) * k, barW = 84 * k, nw = 26 * k, w = lw + barW + nw + 8 * k;
            var r = new Rect(x, y, w, h);
            Thin(r);
            Ui.Text(new Rect(x + 5 * k, y, lw, h), label, Color.black, TextAnchor.MiddleLeft, true);
            var b = new Rect(x + lw + 2 * k, y + 5 * k, barW, h - 10 * k);
            Win95.Fill(b, Win95.Face); Win95.Sunken(b, k);
            float frac = max > 0 ? Mathf.Clamp01(value / max) : 0f;
            float bx = b.x + 2 * k, blk = 6 * k, gap = 2 * k;
            int blocks = (int)((b.width - 4 * k + gap) / (blk + gap)), lit = Mathf.CeilToInt(frac * blocks - .001f);
            var ink = frac < .25f ? Win95.Hex("800000") : Win95.Navy;
            for (int i = 0; i < lit; i++) Win95.Fill(new Rect(bx + i * (blk + gap), b.y + 2 * k, blk, b.height - 4 * k), ink);
            Ui.Text(new Rect(b.xMax + 2 * k, y, nw, h), number, Color.black, TextAnchor.MiddleRight);
            return r.xMax;
        }

        // a taskbar button: the slot's icon and its key; latched down when it is the drawn one, greyed when empty
        private static void Button(float x, float y, float w, float h, Slot s)
        {
            int k = Ui.K;
            var r = new Rect(x, y, w, h);
            bool drawn = s.Label != null && s.Label.GetComponent<Outline>() is Outline o && o.enabled;
            bool full = s.Label != null && s.Label.color.b < .5f;   // the game turns the label yellow for a full slot
            Win95.Fill(r, Win95.Face);
            if (drawn)
            {
                GUI.DrawTextureWithTexCoords(new Rect(r.x + 2 * k, r.y + 2 * k, r.width - 4 * k, r.height - 4 * k), Win95.Dither, new Rect(0, 0, (r.width - 4 * k) / (2f * k), (r.height - 4 * k) / (2f * k)));
                Win95.Latched(r, k);
            }
            else Win95.Raised(r, k);
            float d = drawn ? k : 0;
            string key = Of(s.Label);
            float kw = (Ui.Font(true).Width(key) + 4) * k;
            Ui.Text(new Rect(r.x + 4 * k + d, r.y + d, kw, h), key, full ? Color.black : Win95.Shadow, TextAnchor.MiddleLeft, true);
            var tex = s.Icon != null ? s.Icon.texture : null;
            float ix = r.x + 4 * k + kw + d, iw = h - 6 * k;
            if (full && tex != null) GUI.DrawTexture(new Rect(ix, r.y + 3 * k + d, iw, iw), tex, ScaleMode.ScaleToFit, true);
            // the count at the right end, the item's name between it and the icon
            string count = On(s.Count) ? Of(s.Count) : "";
            float cw = count.Length > 0 ? (Ui.Font(false).Width(count) + 4) * k : 0;
            if (count.Length > 0) Ui.Text(new Rect(r.xMax - 4 * k - cw + d, r.y + d, cw, h), count, Color.black, TextAnchor.MiddleRight);
            string name = full ? ItemNames.Of(tex) : null;
            float nx = ix + iw + 4 * k;
            if (!string.IsNullOrEmpty(name) && r.xMax - 4 * k - cw - nx > 8 * k)
                Ui.Fit(new Rect(nx, r.y + d, r.xMax - 4 * k - cw - nx, h), name, Color.black);
        }

        // the tray's items, each with the widest text it can have (its cell's width): needs (and insanity, radiation
        // while the game shows them), ammunition, grenades, nuts, heading, day and clock
        private readonly List<KeyValuePair<string, string>> _tray = new List<KeyValuePair<string, string>>();
        private List<KeyValuePair<string, string>> Tray()
        {
            _tray.Clear();
            void Item(string text, string widest) { _tray.Add(new KeyValuePair<string, string>(text, widest)); }
            Item("Thirst " + Mathf.RoundToInt(Number(_thirst)), "Thirst 100");
            Item("Hunger " + Mathf.RoundToInt(Number(_hunger)), "Hunger 100");
            Item("Fatigue " + Mathf.RoundToInt(Number(_fatigue)), "Fatigue 100");
            if (On(_sanityLabel)) Item("Insanity " + Mathf.RoundToInt(Number(_sanity)), "Insanity 100");
            if (On(_radiationLabel)) Item("Radiation " + Mathf.RoundToInt(Number(_radiation)), "Radiation 100");
            Item("Ammo " + Of(_mag) + " / " + Of(_reserve), "Ammo 888 / 888");
            if (_grenades != null) Item("Grenades " + Mathf.Max(0, _grenades.Value), "Grenades 88");
            Item("Nuts " + Of(_nuts), "Nuts 88888");
            var cam = Camera.main;
            if (cam != null)
            {
                float yaw = (cam.transform.eulerAngles.y % 360f + 360f) % 360f;
                string[] dirs = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
                Item(dirs[Mathf.RoundToInt(yaw / 45f) % 8] + " " + Mathf.RoundToInt(yaw) % 360 + "°", "NW 888°");
            }
            string hour = Of(_time);
            Item("Day " + Of(_day), "Day 888");
            if (_clock != null) Item(Fuzzy(_clock.Value), LongestFuzzy());
            else if (hour.Length > 0) Item(int.TryParse(hour, out _) ? hour + ":00" : hour, "88:00");
            return _tray;
        }

        private static string _longest;
        private static string LongestFuzzy()
        {
            if (_longest != null) return _longest;
            var font = Ui.Font(false);
            _longest = "";
            for (int m = 0; m < 24 * 60; m += 5) { var f = Fuzzy(m / 60f); if (font.Width(f) > font.Width(_longest)) _longest = f; }
            return _longest;
        }

        // the time to the nearest five minutes, in words: "half past six", "quarter to three", "noon"
        private static readonly string[] Hours = { "twelve", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven" };
        private static readonly string[] Past = { "{0} o'clock", "five past {0}", "ten past {0}", "quarter past {0}", "twenty past {0}", "twenty-five past {0}",
                                                  "half past {0}", "twenty-five to {1}", "twenty to {1}", "quarter to {1}", "ten to {1}", "five to {1}" };
        private static string Fuzzy(float hours)
        {
            int minutes = Mathf.RoundToInt(((hours % 24f + 24f) % 24f) * 60f / 5f) * 5 % (24 * 60);
            int h = minutes / 60, m = minutes % 60;
            if (m == 0 && h == 0) return "midnight";
            if (m == 0 && h == 12) return "noon";
            string Name(int hh) { hh %= 24; return hh == 0 ? "midnight" : hh == 12 ? "noon" : Hours[hh % 12]; }
            return string.Format(Past[m / 5], Name(h), Name(h + 1));
        }

        // an item's name from its icon: the game's slots show an item's own imageUI texture, so the item (any loaded one
        // with that texture) gives its ItemName. Looked up again, a few times at most, when an icon turns up that isn't known.
        // Apocapocket renders icons for items without one: those are matched against the items it holds, through its own
        // IconFor.
        private static class ItemNames
        {
            private static readonly Dictionary<Texture, string> Names = new Dictionary<Texture, string>();
            private static float _next;
            private static int _scans;

            public static string Of(Texture tex)
            {
                if (tex == null) return null;
                if (Names.TryGetValue(tex, out var n)) return n;
                n = Pocket(tex);
                if (n != null) return Names[tex] = n;
                if (Time.unscaledTime >= _next && _scans < 12) { _next = Time.unscaledTime + 5f; _scans++; Scan(); if (Names.TryGetValue(tex, out n)) return n; }
                return null;
            }

            private static string NameOf(GameObject item)
            {
                foreach (var g in item.GetComponents<PlayMakerFSM>())
                    if (g.FsmName == "ItemName") { var s = g.FsmVariables.FindFsmString("ItemName"); return s != null && !string.IsNullOrEmpty(s.Value) ? s.Value : null; }
                return null;
            }

            // Apocapocket's slots (and the slot it lends out): the item whose icon, as Apocapocket gives it, is this one
            private static bool _pocketTried;
            private static System.Reflection.FieldInfo _instance, _slots, _content, _borrow, _parked;
            private static System.Reflection.MethodInfo _iconFor;
            private static string Pocket(Texture tex)
            {
                if (!_pocketTried)
                {
                    _pocketTried = true;
                    var runner = HarmonyLib.AccessTools.TypeByName("Apocapocket.Runner");
                    var icons = HarmonyLib.AccessTools.TypeByName("Apocapocket.Icons");
                    var state = HarmonyLib.AccessTools.TypeByName("Apocapocket.SlotState");
                    var borrow = HarmonyLib.AccessTools.TypeByName("Apocapocket.Borrow");
                    if (runner == null || icons == null || state == null) return null;
                    _instance = HarmonyLib.AccessTools.Field(runner, "Instance"); _slots = HarmonyLib.AccessTools.Field(runner, "Slots");
                    _content = HarmonyLib.AccessTools.Field(state, "Content"); _iconFor = HarmonyLib.AccessTools.Method(icons, "IconFor");
                    _borrow = HarmonyLib.AccessTools.Field(runner, "Borrow");
                    _parked = borrow != null ? HarmonyLib.AccessTools.Field(borrow, "Parked") : null;
                }
                if (_instance == null || _slots == null || _content == null || _iconFor == null) return null;
                try
                {
                    var states = new List<object>();
                    var run = _instance.GetValue(null);
                    if (run != null && _slots.GetValue(run) is System.Array a) foreach (var x in a) states.Add(x);
                    var lent = run != null && _borrow != null ? _borrow.GetValue(run) : null;   // a slot lent out while borrowed
                    if (lent != null && _parked != null) states.Add(_parked.GetValue(lent));
                    foreach (var st in states)
                    {
                        var item = st != null ? _content.GetValue(st) as GameObject : null;
                        if (item != null && _iconFor.Invoke(null, new object[] { item }) as Texture == tex) return NameOf(item);
                    }
                }
                catch { }
                return null;
            }

            private static void Scan()
            {
                foreach (var f in Resources.FindObjectsOfTypeAll<PlayMakerFSM>())
                {
                    if (f == null || f.FsmName != "imageUI") continue;
                    var t = f.FsmVariables.FindFsmTexture("imageUI");
                    if (t == null || t.Value == null || Names.ContainsKey(t.Value)) continue;
                    var name = NameOf(f.gameObject);
                    if (name != null) Names[t.Value] = name;
                }
            }
        }

        // the taskbar's thin sunken frame (its panes and tray)
        private static void Thin(Rect r)
        {
            int k = Ui.K;
            Win95.Fill(new Rect(r.x, r.y, r.width, k), Win95.Shadow); Win95.Fill(new Rect(r.x, r.y, k, r.height), Win95.Shadow);
            Win95.Fill(new Rect(r.x, r.yMax - k, r.width, k), Color.white); Win95.Fill(new Rect(r.xMax - k, r.y, k, r.height), Color.white);
        }
    }
}
