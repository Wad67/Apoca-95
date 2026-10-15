using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Chicago95
{
    // The game's settings as a Windows 95 property sheet (GameSettings), opened from the title and pause menus: a tab
    // each for Graphics, Audio, Gameplay, Player & Car and Cheats. Check boxes and trackbars write straight away (in game
    // through the game's own controls, so they apply at once); the display (resolution, screen mode, quality) waits for
    // Apply. Closing it in game tells the game its settings changed ("Save Settings").
    internal static class SettingsWindow
    {
        public static bool IsOpen { get; private set; }
        public static bool InGame => _inGame;   // opened from the pause menu (else the title)
        private static int _tab;
        private static float _scroll, _contentH = 100;
        private static DisplaySettings _display;
        private static bool _inGame;

        public static void Show(bool inGame) { IsOpen = true; _inGame = inGame; _tab = 0; _scroll = 0; Ui.PopupClose(); BindingsPage.Reset(); _display = DisplaySettings.Load(); }

        // the tabs: the game's settings, and the keys when Wild Wasteland's key list is there
        private static List<string> _tabs;
        private static List<string> Tabs
        {
            get
            {
                if (_tabs != null) return _tabs;
                _tabs = GameSettings.Tabs.ToList();
                if (BindingsPage.Available) _tabs.Insert(_tabs.IndexOf("Audio") + 1, "Controls");
                return _tabs;
            }
        }

        // Escape: stops waiting for a key, else closes
        public static void Escape() { if (BindingsPage.Capturing) BindingsPage.Cancel(); else Close(); }

        public static void Close()
        {
            if (!IsOpen) return;
            IsOpen = false; Ui.PopupClose(); BindingsPage.Cancel();
            if (_inGame) PlayMakerFSM.BroadcastEvent("Save Settings");
        }

        private static float _ox, _oy;
        private static Rect P(float x, float y, float w, float h) { int k = Ui.K; return new Rect(_ox + x * k, _oy + y * k, w * k, h * k); }

        public static void Draw()
        {
            if (!IsOpen) return;
            BindingsPage.Absorb();   // waiting for a key: keys and clicks are for that
            Ui.PopupInput(Owner);   // the open drop-down takes the mouse before anything under it
            int k = Ui.K;
            const float W = 440, H = 400;
            _ox = Mathf.Round((Screen.width - W * k) / 2f); _oy = Mathf.Round((Screen.height - H * k) / 2f);
            if (Ui.Window(P(0, 0, W, H), "Settings")) { Close(); return; }

            var sheet = P(8, 29 + 20, W - 16, H - 29 - 20 - 8 - 23 - 10);
            Ui.Sheet(sheet);
            int next = Ui.Tabs(P(8, 29, W - 16, 20), Tabs, _tab);
            if (next != _tab) { _tab = next; _scroll = 0; Ui.PopupClose(); BindingsPage.Cancel(); }
            var page = new Rect(sheet.x + 6 * k, sheet.y + 8 * k, sheet.width - 12 * k, sheet.height - 14 * k);
            if (Tabs[_tab] == "Controls")
            {
                BindingsPage.Draw(page);
                Closer(W, H);
                return;
            }
            Ui.Field(page);
            var view = new Rect(page.x + 2 * k, page.y + 2 * k, page.width - 4 * k, page.height - 4 * k);
            float was = _scroll;
            float cw = Ui.BeginScroll(view, ref _scroll, _contentH, 6 * k);
            if (_scroll != was) Ui.PopupClose();   // its box moved
            float y = 0;
            try
            {
                string tab = Tabs[_tab];
                if (tab == "Graphics") y = Display(cw, y);
                foreach (var d in GameSettings.All.Where(s => s.Tab == tab))
                {
                    if (d.TitleOnly && _inGame || d.GameOnly && !_inGame) continue;
                    y = Row(cw, y, d);
                }
            }
            finally { Ui.EndScroll(); }
            _contentH = y;

            Closer(W, H);
        }

        private static void Closer(float W, float H)
        {
            float bw = 75;
            if (Ui.Button(P(W - 8 - bw, H - 10 - 23, bw, 23), "Close", true, true)) Close();
            Ui.PopupDraw(Owner);
        }

        // a check box, or a name, a trackbar and the value
        private static float Row(float cw, float y, Setting d)
        {
            int k = Ui.K;
            float v = GameSettings.Read(d);
            if (d.Kind == "bool" || d.Kind == "fsm")
            {
                bool on = v > .5f;
                bool nv = Ui.Check(new Rect(0, y, cw, 18 * k), on, d.Label);
                if (nv != on) GameSettings.Write(d, nv ? 1 : 0);
                return y + 20 * k;
            }
            float lw = 140 * k, vw = 44 * k;
            Ui.Fit(new Rect(0, y, lw - 4 * k, 23 * k), d.Label, Color.black);
            float nvf = Ui.Trackbar(new Rect(lw, y, cw - lw - vw - 6 * k, 23 * k), v, d.Min, d.Max, out _);
            if (Mathf.Abs(nvf - v) > 1e-5f) GameSettings.Write(d, nvf);
            Ui.Text(new Rect(cw - vw, y, vw, 23 * k), GameSettings.Format(d, nvf), Color.black, TextAnchor.MiddleRight);
            return y + 26 * k;
        }

        // the display: three combo boxes (their lists open in place) and Apply
        private static float Display(float cw, float y)
        {
            int k = Ui.K;
            var d = _display;
            float lw = 100 * k, bw = cw - lw;
            var mode = DisplaySettings.ModeNames[d.Mode];
            y = Combo(cw, y, lw, bw, "Resolution", d.ResText, "res", d.Res.Select(r => r.x + " x " + r.y).ToList(), i => { d.W = d.Res[i].x; d.H = d.Res[i].y; });
            y = Combo(cw, y, lw, bw, "Screen mode", mode, "mode", DisplaySettings.ModeNames.ToList(), i => d.Mode = i);
            y = Combo(cw, y, lw, bw, "Quality", d.QualityText, "quality", QualitySettings.names.ToList(), i => d.Quality = i);
            bool changed = d.W != Screen.width || d.H != Screen.height || d.Mode != (int)Screen.fullScreenMode || d.Quality != QualitySettings.GetQualityLevel();
            float aw = Ui.ButtonWidth("Apply");
            if (Ui.Button(new Rect(cw - aw, y, aw, 23 * k), "Apply", changed)) d.Apply();
            y += 31 * k;
            Ui.Etched(0, y, cw);
            return y + 10 * k;
        }

        private static float Combo(float cw, float y, float lw, float bw, string label, string value, string key, List<string> options, System.Action<int> pick)
        {
            int k = Ui.K;
            Ui.Text(new Rect(0, y, lw, 21 * k), label + ":", Color.black);
            Ui.Combo(Owner, key, new Rect(lw, y, bw, 21 * k), value, options, Mathf.Max(0, options.IndexOf(value)), pick);
            return y + 25 * k;
        }
        public static readonly object Owner = new object();
    }
}
