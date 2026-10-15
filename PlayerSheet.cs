using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Chicago95
{
    // The Tab screen (PlayerSheet_Canvas, switched on and off by __GameManager__'s PlayerSheet FSM) as a Windows 95 window:
    // equipment, the player's picture, ammunition, factions, warlords, and the day and hour in a status bar. Everything is
    // read live from the game's own sheet, whose FSMs keep its texts and pictures current (WW's picture swap included); the
    // sheet itself stays on but invisible (CanvasGroup), so whatever watches its Canvas sees it as before.
    internal sealed class PlayerSheet : MonoBehaviour
    {
        public static void Install(GameObject host) { host.AddComponent<PlayerSheet>(); }

        private sealed class Item { public Text Label; public RawImage Icon; public Text[] Value; }
        private sealed class Ammo { public Text Label; public Text[] Value; }
        private sealed class Faction { public RawImage Flag; public Text Name, Relation; }
        private sealed class Boss { public Text Name; public RawImage Portrait; }

        private Canvas _canvas;
        private CanvasGroup _hide;
        private RawImage _picture;
        private Text _ammoTitle, _factionTitle, _bossTitle, _dayLabel, _day, _timeLabel, _time;
        private readonly List<Item> _items = new List<Item>();
        private readonly List<Ammo> _ammo = new List<Ammo>();
        private readonly List<Faction> _factions = new List<Faction>();
        private readonly List<Boss> _bosses = new List<Boss>();
        private float _nextFind;

        private void Update()
        {
            if (_canvas == null && Time.unscaledTime >= _nextFind)
            {
                _nextFind = Time.unscaledTime + 1f;
                var root = TitleMenu.FindPath("PlayerSheet_Canvas");
                if (root != null) Bind(root);
            }
            if (_hide != null && _hide.alpha != 0f) { _hide.alpha = 0f; _hide.blocksRaycasts = false; _hide.interactable = false; }
        }

        private static Text T(Transform t, string path) { var c = t.Find(path); return c != null ? c.GetComponent<Text>() : null; }
        // a value made of several texts ("+", "0", "%"), left to right
        private static Text[] Parts(Transform t) { return t.GetComponentsInChildren<Text>(true).Where(x => x.transform != t).ToArray(); }
        // the game's own names for a row's numbers: "<kind> ammo" (what is carried) and "<kind> max" (what can be)
        private static bool IsMax(Text t) { return t.name.EndsWith(" max", System.StringComparison.Ordinal); }
        private static bool IsCount(Text t) { return t.name.EndsWith(" ammo", System.StringComparison.Ordinal); }

        private void Bind(Transform root)
        {
            var sheet = root.Find("PlayerSheet");
            _canvas = root.GetComponent<Canvas>();
            if (sheet == null || _canvas == null) { _canvas = null; return; }
            _items.Clear(); _ammo.Clear(); _factions.Clear(); _bosses.Clear();
            var player = sheet.Find("player");
            if (player != null)
            {
                _picture = player.Find("player_icon")?.GetComponent<RawImage>();
                foreach (Transform c in player)
                {
                    var label = c.GetComponent<Text>();
                    if (label == null) continue;
                    _items.Add(new Item { Label = label, Icon = c.GetComponentsInChildren<RawImage>(true).FirstOrDefault(), Value = Parts(c) });
                }
            }
            var ammo = sheet.Find("ammo");
            if (ammo != null)
            {
                _ammoTitle = ammo.GetComponent<Text>();
                foreach (Transform c in ammo) { var l = c.GetComponent<Text>(); if (l != null) _ammo.Add(new Ammo { Label = l, Value = Parts(c) }); }
            }
            var battery = sheet.Find("battery");
            if (battery != null && battery.GetComponent<Text>() != null) _ammo.Add(new Ammo { Label = battery.GetComponent<Text>(), Value = Parts(battery) });
            var factions = sheet.Find("factions");
            if (factions != null)
            {
                _factionTitle = factions.GetComponent<Text>();
                foreach (Transform c in factions)
                {
                    var texts = c.GetComponentsInChildren<Text>(true);
                    if (texts.Length == 0) continue;
                    _factions.Add(new Faction { Flag = c.GetComponent<RawImage>(), Name = texts[0], Relation = texts.Length > 1 ? texts[1] : null });
                }
            }
            var bosses = sheet.Find("bosses");
            if (bosses != null)
                foreach (Transform c in bosses)
                {
                    var name = c.GetComponent<Text>();
                    var portrait = c.GetComponentsInChildren<RawImage>(true).FirstOrDefault();
                    if (name == null) continue;
                    if (portrait == null) _bossTitle = name;
                    else _bosses.Add(new Boss { Name = name, Portrait = portrait });
                }
            _dayLabel = T(sheet, "day"); _day = T(sheet, "day/day number");
            _timeLabel = T(sheet, "time"); _time = T(sheet, "time/time number");
            _hide = root.GetComponent<CanvasGroup>() ?? root.gameObject.AddComponent<CanvasGroup>();
            Plugin.Log.LogInfo("Player sheet: " + _items.Count + " equipment, " + _ammo.Count + " ammunition, " + _factions.Count + " factions, " + _bosses.Count + " warlords");
        }

        // ---------------------------------------------------------------- the window
        private static string Cap(string s)
        {
            s = (s ?? "").Replace('\n', ' ').Trim(' ', ':');
            return s.Length > 0 ? char.ToUpperInvariant(s[0]) + s.Substring(1) : s;
        }
        private static bool On(Component c) { return c != null && c.gameObject.activeInHierarchy; }
        private static string Of(Text t) { return On(t) ? t.text.Trim() : ""; }
        // an item's bonus: "+" before its number, "%" (or any other sign) after
        private static string Bonus(Text[] parts)
        {
            var n = parts.FirstOrDefault(IsMax);
            if (!On(n)) return "";
            var signs = parts.Where(p => p != n && On(p)).Select(p => p.text.Trim()).ToList();
            return string.Concat(signs.Where(x => x == "+")) + n.text.Trim() + string.Concat(signs.Where(x => x != "+"));
        }

        // a relation's colour, in the dialog's: green friendly, red hostile, else black
        private static Color Ink(Color c)
        {
            if (c.g > c.r + .3f) return Win95.Hex("008000");
            if (c.r > c.g + .3f) return Win95.Hex("C00000");
            return Color.black;
        }

        private static float _ox, _oy;
        private static Rect P(float x, float y, float w, float h) { int k = Ui.K; return new Rect(_ox + x * k, _oy + y * k, w * k, h * k); }

        private static void Picture(Rect r, RawImage img)
        {
            Win95.Fill(r, Color.black); Ui.Sunken(r);
            if (img == null || img.texture == null) return;
            int k = Ui.K;
            var inner = new Rect(r.x + 2 * k, r.y + 2 * k, r.width - 4 * k, r.height - 4 * k);
            GUI.DrawTexture(inner, img.texture, ScaleMode.ScaleToFit, true);
        }

        private void OnGUI()
        {
            if (_canvas == null || !_canvas.enabled || !_canvas.gameObject.activeInHierarchy || PauseMenu.Claimed) return;
            if (LoadingDialog.Instance != null && LoadingDialog.Instance.Active) return;
            if (Event.current.type != EventType.Repaint) return;   // nothing in it takes input (the mouse is the game's)
            Ui.Begin();
            int k = Ui.K;
            const float W = 700, H = 466;
            _ox = Mathf.Round((Screen.width - W * k) / 2f); _oy = Mathf.Round((Screen.height - H * k) / 2f);
            Ui.Window(P(0, 0, W, H), "Player Sheet", false);

            // equipment
            var items = _items.Where(i => On(i.Label)).ToList();
            Ui.Group(P(8, 29, 210, 272), "Equipment");
            float y = 49;
            foreach (var i in items.Take(5))
            {
                Picture(P(18, y, 44, 44), i.Icon);
                Ui.Fit(P(70, y + 6, 140, 16), Cap(i.Label.text), Color.black, TextAnchor.MiddleLeft, true);
                var v = Bonus(i.Value);
                if (v.Length > 0) Ui.Fit(P(70, y + 22, 140, 16), v, Color.black);
                y += 50;
            }

            // the player
            Picture(P(226, 35, 130, 266), _picture);

            // ammunition
            Ui.Group(P(364, 29, 328, 272), Cap(_ammoTitle != null ? _ammoTitle.text : "Ammunition"));
            y = 47;
            for (int n = 0; n < _ammo.Count; n++)
            {
                var a = _ammo[n];
                if (!On(a.Label)) continue;
                if (n == _ammo.Count - 1 && n > 0) { var e = P(376, y + 2, 304, 0); Ui.Etched(e.x, e.y, e.width); y += 6; }   // the battery, apart
                Ui.Fit(P(376, y, 180, 18), Cap(a.Label.text), Color.black);
                Ui.Text(P(556, y, 72, 18), Of(a.Value.FirstOrDefault(IsCount)), Color.black, TextAnchor.MiddleRight);
                Ui.Text(P(632, y, 48, 18), "/ " + Of(a.Value.FirstOrDefault(IsMax)), Color.black);
                y += 21;
            }

            // factions
            Ui.Group(P(8, 307, 210, 128), Cap(_factionTitle != null ? _factionTitle.text : "Factions"));
            y = 327;
            foreach (var f in _factions.Where(x => On(x.Name)).Take(2))
            {
                Picture(P(18, y, 40, 40), f.Flag);
                Ui.Fit(P(66, y + 4, 144, 16), Cap(f.Name.text), Color.black, TextAnchor.MiddleLeft, true);
                if (On(f.Relation)) Ui.Fit(P(66, y + 20, 144, 16), Cap(f.Relation.text), Ink(f.Relation.color));
                y += 50;
            }

            // warlords: their pictures change when they are killed
            Ui.Group(P(226, 307, 466, 128), Cap(_bossTitle != null ? _bossTitle.text : "Warlords"));
            var bosses = _bosses.Where(b => On(b.Name)).ToList();
            if (bosses.Count > 0)
            {
                float cell = Mathf.Floor(450f / bosses.Count), x = 234;
                foreach (var b in bosses)
                {
                    float pw = Mathf.Min(56, cell - 6);
                    Picture(P(x + (cell - pw) / 2f, 325, pw, pw), b.Portrait);
                    var lines = Ui.Font(false).Wrap(Cap(b.Name.text), (int)cell - 4);
                    float ly = 325 + pw + 4;
                    foreach (var l in lines.Take(2)) { Ui.Text(P(x, ly, cell, Ui.Line), l, Color.black, TextAnchor.MiddleCenter); ly += Ui.Line; }
                    x += cell;
                }
            }

            // status bar: the day and the hour
            void Pane(float px, float pw, string text)
            {
                var r = P(px, H - 25, pw, 19);
                Win95.Fill(new Rect(r.x, r.y, r.width, k), Win95.Shadow); Win95.Fill(new Rect(r.x, r.y, k, r.height), Win95.Shadow);
                Win95.Fill(new Rect(r.x, r.yMax - k, r.width, k), Color.white); Win95.Fill(new Rect(r.xMax - k, r.y, k, r.height), Color.white);
                Ui.Fit(new Rect(r.x + 4 * k, r.y, r.width - 8 * k, r.height), text, Color.black);
            }
            string hour = _time != null ? _time.text.Trim() : "";
            Pane(4, 160, (_dayLabel != null ? Cap(_dayLabel.text) : "Day") + " " + (_day != null ? _day.text.Trim() : ""));
            Pane(166, 160, (_timeLabel != null ? Cap(_timeLabel.text) : "Time") + " " + (int.TryParse(hour, out _) ? hour + ":00" : hour));
            Pane(328, W - 332, "");
        }
    }
}
