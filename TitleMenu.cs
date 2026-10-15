using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Chicago95
{
    // The title screen's menu as a Windows 95 window (IMGUI) in place of the game's uGUI one (and Wild Wasteland's
    // CleanHUD restyling of it). It drives the game the way the game's own buttons do (taken from CleanHUD's menu): the
    // buttons' PlayMaker FSMs get "Clicked", the seed goes into the game's seed field, the save slots are the game's
    // own (LoadGame_Canvas), Mods opens Apocasetter's window. The game's menu objects are hidden (CanvasGroup), not
    // removed, and come back when this is off or out of the title screen. Settings opens the game's own settings
    // screen for now; this window steps aside while it, a load or the Mods window is up.
    internal sealed class TitleMenu : MonoBehaviour
    {
        private static TitleMenu _instance;
        internal static bool Claimed => _instance != null && _instance._shown;

        private static readonly string[] Hidden = { "Canvas_MM/MainMenu", "Canvas_MM/Game Name", "Canvas_MM/EnterSeedText", "Canvas_MM/MusicText",
                                                    "Canvas_MM/Version_text", "Canvas_MM/row_bg", "Canvas_MM/Apocasetter_Mods" };
        private static readonly string[] SettingsCanvases = { "Settings_Canvas", "Graphics_Canvas", "Audio_Canvas", "Gameplay_Canvas", "PlayerCar_Canvas", "Controls_Canvas", "KeyBindings_Canvas" };
        private const float Vh = 720f;   // the window is laid out on a 720-line screen, scaled to the real one
        private const int Slots = 10;

        private Canvas _van;
        private int _scene = -1;
        private float _nextScan, _nextSlots;
        private bool _shown, _launching;
        private string _screen = "main";   // main, load
        private string _confirm;           // a question box over the window, or null
        private Action _confirmYes;
        private string _seed;
        private int _picked = -1, _lastClickSlot = -1;
        private float _lastClickTime;
        private readonly string[] _slotLabel = new string[Slots + 1];
        private readonly bool[] _slotUsed = new bool[Slots + 1];
        private int _latest;
        private PlayMakerFSM _sound;
        private readonly Dictionary<Transform, CanvasGroup> _groups = new Dictionary<Transform, CanvasGroup>();
        private readonly Dictionary<CanvasGroup, bool> _added = new Dictionary<CanvasGroup, bool>();
        private struct GroupState { internal float Alpha; internal bool Raycasts, Interactable; }
        private readonly Dictionary<CanvasGroup, GroupState> _original = new Dictionary<CanvasGroup, GroupState>();

        internal static void Install(GameObject host) { _instance = host.AddComponent<TitleMenu>(); }

        // Wild Wasteland's own title and pause menus stand down while ours are shown
        private static bool _standDown;
        internal static void InstallStandDown(Harmony h)
        {
            if (_standDown) return;
            _standDown = true;
            var menu = AccessTools.TypeByName("WildWasteland.Modules.CleanHUD.Menu");
            if (menu != null) h.Patch(AccessTools.Method(menu, "Tick"), prefix: new HarmonyMethod(typeof(TitleMenu), nameof(CleanHudTick)));
        }

        private static readonly ConditionalWeakTable<object, object> TornDown = new ConditionalWeakTable<object, object>();
        private static bool CleanHudTick(object __instance)
        {
            if (!Claimed && !PauseMenu.Claimed) { TornDown.Remove(__instance); return true; }
            if (!TornDown.TryGetValue(__instance, out _))
            {
                TornDown.Add(__instance, true);
                AccessTools.Method(__instance.GetType(), "Teardown", Type.EmptyTypes)?.Invoke(__instance, null);   // its objects gone, the game's restored
            }
            return false;
        }

        // ---------------------------------------------------------------- the game's objects
        internal static Transform FindPath(string path) { return Find(path); }
        internal static bool ClickPath(string path) { return Click(path); }
        internal static bool ApocasetterIsOpen() { return ApocasetterOpen(); }
        internal static void OpenModsWindow() { if (Chainloader.PluginInfos.ContainsKey(Plugin.Apocasetter)) OpenMods(); }

        private static Transform Find(string path)
        {
            int split = path.IndexOf('/');
            string root = split < 0 ? path : path.Substring(0, split);
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid()) return null;
            foreach (var go in scene.GetRootGameObjects())
                if (go.name == root) return split < 0 ? go.transform : go.transform.Find(path.Substring(split + 1));
            return null;
        }
        private static bool Active(string path) { var t = Find(path); return t != null && t.gameObject.activeInHierarchy; }
        private static bool CanvasOn(string path) { var t = Find(path); var c = t != null ? t.GetComponent<Canvas>() : null; return c != null && c.enabled && c.gameObject.activeInHierarchy; }

        private static bool Click(string path)
        {
            var t = Find(path);
            if (t != null)
                foreach (var f in t.GetComponents<PlayMakerFSM>())
                    if ((f.FsmName == "NewGame" || f.FsmName == "Continue" || f.FsmName == "resetFSM") && f.isActiveAndEnabled) { f.SendEvent("Clicked"); return true; }
            Plugin.Log.LogWarning("Title menu: game button not available: " + path);
            return false;
        }

        private void Sound()
        {
            if (_sound == null)
            {
                var t = Find("Canvas_MM/MenuSounds");
                if (t != null) foreach (var f in t.GetComponents<PlayMakerFSM>()) if (f.FsmName == "ButtonSound") _sound = f;
            }
            if (_sound != null && _sound.isActiveAndEnabled) _sound.SendEvent("Activate");
        }

        private void Hide()
        {
            foreach (var p in Hidden)
            {
                var t = Find(p);
                if (t == null) continue;
                if (!_groups.TryGetValue(t, out var cg) || cg == null)
                {
                    cg = t.GetComponent<CanvasGroup>();
                    if (cg == null) { cg = t.gameObject.AddComponent<CanvasGroup>(); _added[cg] = true; }
                    else if (!_added.ContainsKey(cg)) { _added[cg] = false; _original[cg] = new GroupState { Alpha = cg.alpha, Raycasts = cg.blocksRaycasts, Interactable = cg.interactable }; }
                    _groups[t] = cg;
                }
                cg.alpha = 0f; cg.blocksRaycasts = false; cg.interactable = false;
            }
        }
        private void Restore()
        {
            foreach (var kv in _added)
            {
                if (kv.Key == null) continue;
                if (kv.Value) Destroy(kv.Key);
                else { var s = _original[kv.Key]; kv.Key.alpha = s.Alpha; kv.Key.blocksRaycasts = s.Raycasts; kv.Key.interactable = s.Interactable; }
            }
            _added.Clear(); _original.Clear(); _groups.Clear();
        }

        // ---------------------------------------------------------------- each frame
        private void Update()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.handle != _scene) { Leave(); _scene = scene.handle; _nextScan = 0; _launching = false; }
            if (_van == null && Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + .5f;
                var t = Find("Canvas_MM");
                _van = t != null ? t.GetComponent<Canvas>() : null;
                if (_van != null) { _seed = CurrentSeed(); _nextSlots = 0; }
            }
            bool show = _van != null && _van.enabled && _van.gameObject.activeInHierarchy && !_launching
                        && !Active("Canvas_MM/LoadingText") && !SettingsOpen() && !ModsOpen();
            if (show) Hide();
            else if (_shown && _van != null && !SettingsOpen() && !ModsOpen()) Restore();   // loading: the game's own objects back
            _shown = show;
            if (!show) return;
            if (Time.unscaledTime >= _nextSlots) { _nextSlots = Time.unscaledTime + 1f; ReadSlots(); }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (SettingsWindow.IsOpen) SettingsWindow.Escape();
                else if (_confirm != null) _confirm = null;
                else if (_screen != "main") _screen = "main";
            }
        }

        private void Leave() { if (_van != null || _added.Count > 0) Restore(); _van = null; _shown = false; _screen = "main"; _confirm = null; _sound = null; }

        private static bool SettingsOpen() { foreach (var c in SettingsCanvases) if (CanvasOn(c)) return true; return false; }

        private static AccessTools.FieldRef<object, bool> _modsOpen;
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool ModsOpen()
        {
            if (!Chainloader.PluginInfos.ContainsKey(Plugin.Apocasetter)) return false;
            return ApocasetterOpen();
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool ApocasetterOpen()
        {
            var ui = Apocasetter.SettingsUI.Instance;
            if (ui == null) return false;
            if (_modsOpen == null) _modsOpen = AccessTools.FieldRefAccess<object, bool>(AccessTools.Field(typeof(Apocasetter.SettingsUI), "_open"));
            return _modsOpen(ui);
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void OpenMods() { if (Apocasetter.SettingsUI.Instance != null) Apocasetter.SettingsUI.Instance.Open(); }

        // ---------------------------------------------------------------- saves (the game's slots)
        private static string SaveFile(int n) { return Path.Combine(Application.persistentDataPath, "SaveGame" + n + ".es3"); }
        private static bool SlotUsed(int n) { var f = new FileInfo(SaveFile(n)); return f.Exists && f.Length > 1024; }
        private static readonly Regex SeedPart = new Regex(@"\s*\(Seed:[^)]*\)", RegexOptions.Compiled);

        private void ReadSlots()
        {
            _latest = 0; var best = DateTime.MinValue;
            for (int n = 1; n <= Slots; n++)
            {
                _slotUsed[n] = SlotUsed(n);
                string s = null;
                var t = Find("LoadGame_Canvas/LoadGame/load slot " + n + "/load " + n + " time");
                var tx = t != null ? t.GetComponent<Text>() : null;
                if (tx != null && !string.IsNullOrEmpty(tx.text)) s = tx.text;
                if (string.IsNullOrEmpty(s) && _slotUsed[n]) s = File.GetLastWriteTime(SaveFile(n)).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
                _slotLabel[n] = _slotUsed[n] ? SeedPart.Replace(s ?? "", "").Trim() : "(empty)";
                if (_slotUsed[n]) { var w = File.GetLastWriteTime(SaveFile(n)); if (w > best) { best = w; _latest = n; } }
            }
        }

        // ---------------------------------------------------------------- actions
        private void Launch(string button)
        {
            Sound();
            if (Click(button)) { _launching = true; Restore(); }
        }
        private void Continue() { if (_latest > 0) Launch("LoadGame_Canvas/LoadGame/load slot " + _latest + "/load_game_" + _latest); }
        private void NewGame() { ApplySeed(); Launch("Canvas_MM/MainMenu/NewGame"); }
        private void LoadSlot(int n) { if (n > 0 && _slotUsed[n]) Launch("LoadGame_Canvas/LoadGame/load slot " + n + "/load_game_" + n); }

        private void OpenSettings() { Sound(); SettingsWindow.Show(false); }

        private static string CurrentSeed()
        {
            var t = Find("Canvas_MM/MainMenu/Seed");
            var f = t != null ? t.GetComponent<InputField>() : null;
            if (f != null && !string.IsNullOrEmpty(f.text)) return f.text;
            if (t != null) foreach (var g in t.GetComponents<PlayMakerFSM>()) { var v = g.FsmVariables.FindFsmInt("seed"); if (v != null) return v.Value.ToString(CultureInfo.InvariantCulture); }
            return "12345";
        }
        private void ApplySeed()
        {
            var t = Find("Canvas_MM/MainMenu/Seed");
            if (t == null || string.IsNullOrEmpty(_seed)) return;
            var f = t.GetComponent<InputField>();
            if (f != null) { f.text = _seed; f.onEndEdit?.Invoke(_seed); }
            if (int.TryParse(_seed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                foreach (var g in t.GetComponents<PlayMakerFSM>()) { var s = g.FsmVariables.FindFsmInt("seed"); if (s != null) s.Value = v; }
        }

        private void Confirm(string question, Action yes) { Sound(); _confirm = question; _confirmYes = yes; }

        private static string Version()
        {
            var t = Find("Canvas_MM/Version_text"); var tx = t != null ? t.GetComponent<Text>() : null;
            return tx != null ? tx.text : "";
        }

        // ---------------------------------------------------------------- drawing
        // Laid out in Windows 95 pixels (18 px title bars, 23 px buttons, the desktop's 11 px bitmap font) times a
        // whole-number scale k: 1 up to 2000 lines (a 1080p desktop's own size), 2 at 4K. Every line is k screen pixels.
        private int _k;
        private float _ox, _oy;
        private bool _seedFocus;
        private static PixelFont Font => PixelFont.Regular;

        private Rect P(float x, float y, float w, float h) { return new Rect(_ox + x * _k, _oy + y * _k, w * _k, h * _k); }
        private void Text(Rect r, string s, Color c, TextAnchor align = TextAnchor.MiddleLeft, PixelFont font = null) { (font ?? Font).Draw(r, s, c, _k, align); }

        private void OnGUI()
        {
            if (!_shown) return;
            _k = Mathf.Max(1, Screen.height / 2000 + 1);
            int depth = GUI.depth; GUI.depth = -800;
            try
            {
                bool modal = _confirm != null;
                GUI.enabled = !modal && !SettingsWindow.IsOpen;
                if (_screen == "main") MainWindow();
                else LoadWindow();
                GUI.enabled = true;
                if (SettingsWindow.IsOpen) { Ui.Begin(); SettingsWindow.Draw(); }
                else if (modal) ConfirmBox();
            }
            finally { GUI.depth = depth; GUI.enabled = true; }
        }

        // a window w x h (Windows pixels) at the origin: face, raised frame, navy title bar with its bold white title and
        // close box; returns where its content starts (y)
        private float Window(float w, float h, string title, Action close)
        {
            var r = P(0, 0, w, h);
            Win95.Fill(r, Win95.Face); Win95.Window(r, _k);
            Win95.Fill(P(3, 3, w - 6, 18), Win95.Navy);
            Text(P(6, 3, w - 30, 18), title, Color.white, TextAnchor.MiddleLeft, PixelFont.Bold);
            if (close != null && CloseBox(P(w - 5 - 16, 5, 16, 14))) close();
            return 3 + 18 + 8;
        }

        // the close box: a small raised button with Windows' 8 x 7 cross
        private static readonly string[] Cross = { "XX    XX", " XX  XX ", "  XXXX  ", "   XX   ", "  XXXX  ", " XX  XX ", "XX    XX" };
        private bool CloseBox(Rect r)
        {
            bool hit = Win95.Button(r, out bool held);
            Win95.Fill(r, Win95.Face);
            if (held) Win95.Pressed(r, _k); else Win95.Raised(r, _k);
            float x0 = r.x + 4 * _k + (held ? _k : 0), y0 = r.y + 3 * _k + (held ? _k : 0);
            for (int y = 0; y < Cross.Length; y++)
                for (int x = 0; x < Cross[y].Length; x++)
                    if (Cross[y][x] == 'X') Win95.Fill(new Rect(x0 + x * _k, y0 + y * _k, _k, _k), Color.black);
            if (hit) Sound();
            return hit;
        }

        // a push button; a dialog's default one has a black frame; disabled text grey with a white emboss
        private bool Button(Rect r, string text, bool enabled = true, bool isDefault = false)
        {
            var was = GUI.enabled; GUI.enabled = was && enabled;
            bool hit = Win95.Button(r, out bool held);
            Win95.Fill(r, Win95.Face);
            var face = isDefault ? new Rect(r.x + _k, r.y + _k, r.width - 2 * _k, r.height - 2 * _k) : r;
            if (isDefault) Win95.Outline(r, Win95.Dark, _k);
            if (held) Win95.Pressed(face, _k); else Win95.Raised(face, _k);
            var tr = held ? new Rect(r.x + _k, r.y + _k, r.width, r.height) : r;
            if (GUI.enabled) Text(tr, text, Color.black, TextAnchor.MiddleCenter);
            else
            {
                Text(new Rect(tr.x + _k, tr.y + _k, tr.width, tr.height), text, Color.white, TextAnchor.MiddleCenter);
                Text(tr, text, Win95.Shadow, TextAnchor.MiddleCenter);
            }
            GUI.enabled = was;
            if (hit) Sound();
            return hit;
        }

        // the seed box: sunken white, digits only; click it to type, Enter or a click elsewhere to finish
        private void SeedField(Rect r)
        {
            var e = Event.current;
            if (e.type == EventType.MouseDown && GUI.enabled) _seedFocus = r.Contains(e.mousePosition);
            if (_seedFocus && e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Backspace) { if (!string.IsNullOrEmpty(_seed)) _seed = _seed.Substring(0, _seed.Length - 1); e.Use(); }
                else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Escape) { _seedFocus = false; e.Use(); }
                else if (char.IsDigit(e.character) && (_seed ?? "").Length < 9) { _seed = (_seed ?? "") + e.character; e.Use(); }
                else if (e.character != 0) e.Use();
            }
            Win95.Fill(r, Color.white); Win95.Sunken(r, _k);
            var tr = new Rect(r.x + 4 * _k, r.y, r.width - 8 * _k, r.height);
            Text(tr, _seed, Color.black);
            if (_seedFocus && ((int)(Time.unscaledTime * 2f)) % 2 == 0)
            {
                float cx = tr.x + Font.Width(_seed) * _k + _k;
                Win95.Fill(new Rect(cx, r.y + 4 * _k, _k, r.height - 8 * _k), Color.black);   // the caret
            }
        }

        private void MainWindow()
        {
            const float W = 200, H = 284, X = 10, BW = W - 20, BH = 23;
            _ox = Mathf.Round(Screen.width * .06f); _oy = Mathf.Round(Screen.height * .2f);
            float y = Window(W, H, "Apocalypter", () => Confirm("Quit Apocalypter?", () => Click("Canvas_MM/MainMenu/Quit")));

            if (Button(P(X, y, BW, BH), "Continue", _latest > 0)) Continue();
            y += BH + 3;
            string slot = _latest > 0 ? Font.Fit("Slot " + _latest + ": " + _slotLabel[_latest], (int)BW - 2) : "No saved game";
            Text(P(X + 1, y, BW, 13), slot, _latest > 0 ? Color.black : Win95.Shadow);
            y += 13 + 7;
            if (Button(P(X, y, BW, BH), "New Game")) NewGame();
            y += BH + 8;
            Text(P(X, y, 56, 21), "Map seed:", Color.black);
            SeedField(P(X + 58, y, BW - 58, 21));
            y += 21 + 9;
            var rule = P(X, y, BW, 0); Win95.Etched(rule.x, rule.y, rule.width, _k);
            y += 2 + 9;
            if (Button(P(X, y, BW, BH), "Load Game...")) { _screen = "load"; _picked = _latest; ReadSlots(); }
            y += BH + 6;
            if (Button(P(X, y, BW, BH), "Settings...")) OpenSettings();
            y += BH + 6;
            if (Button(P(X, y, BW, BH), "Mods...", Chainloader.PluginInfos.ContainsKey(Plugin.Apocasetter))) OpenMods();
            y += BH + 6;
            if (Button(P(X, y, BW, BH), "Quit")) Confirm("Quit Apocalypter?", () => Click("Canvas_MM/MainMenu/Quit"));

            // the status bar: the game's version in a sunken pane
            var pane = P(3, H - 3 - 20, W - 6, 20);
            Win95.Sunken(pane, _k);
            Text(new Rect(pane.x + 4 * _k, pane.y, pane.width - 8 * _k, pane.height), Font.Fit(Version(), (int)W - 16), Color.black);
        }

        private void LoadWindow()
        {
            const float W = 300, RH = 16;
            _ox = Mathf.Round(Screen.width * .06f); _oy = Mathf.Round(Screen.height * .2f);
            float listH = RH * Slots + 4, H = 29 + 18 + listH + 10 + 23 + 10;
            float y = Window(W, H, "Load Game", () => _screen = "main");
            Text(P(10, y, W - 20, 13), "Choose a saved game:", Color.black);
            y += 18;
            var list = P(10, y, W - 20, listH);
            Win95.Fill(list, Color.white); Win95.Sunken(list, _k);
            for (int n = 1; n <= Slots; n++)
            {
                var row = P(12, y + 2 + (n - 1) * RH, W - 24, RH);
                bool sel = n == _picked;
                if (sel) Win95.Fill(row, Win95.Navy);
                var ink = sel ? Color.white : _slotUsed[n] ? Color.black : Win95.Shadow;
                Text(new Rect(row.x + 3 * _k, row.y, 16 * _k, row.height), n.ToString(CultureInfo.InvariantCulture), ink);
                Text(new Rect(row.x + 22 * _k, row.y, row.width - 26 * _k, row.height), Font.Fit(_slotLabel[n], (int)W - 52), ink);
                if (_slotUsed[n] && GUI.Button(row, GUIContent.none, GUIStyle.none))
                {
                    bool twice = _lastClickSlot == n && Time.unscaledTime - _lastClickTime < .4f;
                    _picked = n; _lastClickSlot = n; _lastClickTime = Time.unscaledTime;
                    if (twice) LoadSlot(n);
                }
            }
            y += listH + 10;
            if (Button(P(W - 10 - 75 - 6 - 75, y, 75, 23), "Load", _picked > 0 && _slotUsed[_picked], true)) LoadSlot(_picked);
            if (Button(P(W - 10 - 75, y, 75, 23), "Cancel")) _screen = "main";
        }

        private void ConfirmBox()
        {
            const float W = 220, H = 29 + 26 + 23 + 10;
            _ox = Mathf.Round((Screen.width - W * _k) / 2f); _oy = Mathf.Round((Screen.height - H * _k) / 2f);
            float y = Window(W, H, "Apocalypter", () => _confirm = null);
            Text(P(10, y, W - 20, 16), _confirm, Color.black, TextAnchor.MiddleCenter);
            y += 26;
            if (Button(P(W / 2 - 75 - 3, y, 75, 23), "Yes", true, true)) { var yes = _confirmYes; _confirm = null; yes?.Invoke(); }
            if (Button(P(W / 2 + 3, y, 75, 23), "No")) _confirm = null;
        }
    }
}
