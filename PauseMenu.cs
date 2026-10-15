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
    // The pause menu as a Windows 95 window (IMGUI) over the paused game. The game's pause canvas (MainMenu_Canvas) stays
    // enabled (it is how the game knows it is paused) but is made invisible and click-through as a whole, its background
    // with it, so the scene shows. Its actions are the game's own (taken from CleanHUD's menu): Continue through the game
    // manager's Menu FSM, the save slots' FSMs, the buttons' FSMs for Settings, Codex, Tutorial and Credits (their own
    // canvases, which this window steps aside for), "Really Quit" Yes for quitting to the title. Wild Wasteland's own pause
    // menu stands down while this one is shown.
    internal sealed class PauseMenu : MonoBehaviour
    {
        private static PauseMenu _instance;
        // the whole time the game is paused (checked live, so CleanHUD's menu never gets a frame in)
        internal static bool Claimed => _instance != null && _instance.Paused;
        private bool Paused => _van != null && _van.enabled && _van.gameObject.activeInHierarchy;

        private static readonly string[] Screens = { "Settings_Canvas", "Graphics_Canvas", "Audio_Canvas", "Gameplay_Canvas", "PlayerCar_Canvas", "Controls_Canvas",
                                                     "KeyBindings_Canvas", "Codex_Canvas", "Tutorial_Canvas", "Credits_Canvas", "Debug_Canvas" };
        private const int Slots = 10;

        private Canvas _van;
        private CanvasGroup _hider;
        private bool _hiderAdded;
        private int _scene = -1;
        private float _nextScan, _nextSlots;
        private bool _shown;
        private string _screen = "main";   // main, save
        private string _message, _confirm; // a message box (OK) or a question (Yes / No) over the window
        private Action _confirmYes;
        private int _picked = -1, _lastClickSlot = -1;
        private float _lastClickTime;
        private readonly string[] _slotLabel = new string[Slots + 1];
        private readonly bool[] _slotUsed = new bool[Slots + 1];
        private readonly List<KeyValuePair<int, Action>> _later = new List<KeyValuePair<int, Action>>();
        private PlayMakerFSM _sound;
        private float _ox, _oy;
        private static System.Reflection.FieldInfo _blockCancel;   // CleanHUD's Menu.BlockCancelFrame: Cancel is swallowed up to that frame

        internal static void Install(GameObject host)
        {
            _instance = host.AddComponent<PauseMenu>();
            var menu = AccessTools.TypeByName("WildWasteland.Modules.CleanHUD.Menu");
            if (menu != null) _blockCancel = AccessTools.Field(menu, "BlockCancelFrame");
        }

        // ---------------------------------------------------------------- the game's objects
        private static Transform Find(string path) { return TitleMenu.FindPath(path); }
        private static bool Active(string path) { var t = Find(path); return t != null && t.gameObject.activeInHierarchy; }
        private static bool CanvasOn(string path) { var t = Find(path); var c = t != null ? t.GetComponent<Canvas>() : null; return c != null && c.enabled && c.gameObject.activeInHierarchy; }
        private static PlayMakerFSM Fsm(string path, string name)
        {
            var t = Find(path);
            if (t != null) foreach (var f in t.GetComponents<PlayMakerFSM>()) if (f.FsmName == name) return f;
            return null;
        }
        private static bool Click(string path) { return TitleMenu.ClickPath(path); }

        private void Sound()
        {
            if (_sound == null) _sound = Fsm("MainMenu_Canvas/MenuSounds", "ButtonSound");
            if (_sound != null && _sound.isActiveAndEnabled) _sound.SendEvent("Activate");
        }

        private void Hide()
        {
            if (_hider == null)
            {
                _hider = _van.GetComponent<CanvasGroup>();
                _hiderAdded = _hider == null;
                if (_hiderAdded) _hider = _van.gameObject.AddComponent<CanvasGroup>();
            }
            _hider.alpha = 0f; _hider.blocksRaycasts = false; _hider.interactable = false;
        }
        private void Restore()
        {
            if (_hider == null) return;
            if (_hiderAdded) Destroy(_hider);
            else { _hider.alpha = 1f; _hider.blocksRaycasts = true; _hider.interactable = true; }
            _hider = null;
        }

        // ---------------------------------------------------------------- each frame
        private void Update()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.handle != _scene) { Restore(); _van = null; _shown = false; _scene = scene.handle; _nextScan = 0; _later.Clear(); }
            for (int i = _later.Count - 1; i >= 0; i--)
                if (Time.frameCount >= _later[i].Key) { var a = _later[i].Value; _later.RemoveAt(i); try { a(); } catch (Exception e) { Plugin.Log.LogWarning("Pause menu: " + e.Message); } }
            if (_van == null && Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + .5f;
                var t = Find("MainMenu_Canvas");
                _van = t != null ? t.GetComponent<Canvas>() : null;
            }
            bool paused = Paused;
            bool aside = paused && (ScreenOpen() || ModsOpen());
            bool show = paused && !aside;
            // the game's pause canvas, its background with it, is never seen (its screens have their own Back buttons):
            // kept invisible from when it is found, so it can't show for a frame when the game pauses
            if (_van != null) Hide();
            if (!paused && SettingsWindow.IsOpen && SettingsWindow.InGame) SettingsWindow.Close();   // only the one it opened
            if (show && !_shown) { _screen = "main"; _message = _confirm = null; _nextSlots = 0; }
            _shown = show;
            if (!show) return;
            if (Time.unscaledTime >= _nextSlots) { _nextSlots = Time.unscaledTime + 1f; ReadSlots(); }
            bool sub = _screen != "main" || _message != null || _confirm != null || SettingsWindow.IsOpen;
            if (sub && _blockCancel != null) _blockCancel.SetValue(null, Time.frameCount + 1);   // Escape closes ours, it doesn't unpause
            if (sub && Input.GetKeyDown(KeyCode.Escape))
            {
                if (SettingsWindow.IsOpen) SettingsWindow.Escape();
                else if (_message != null || _confirm != null) { _message = null; _confirm = null; }
                else _screen = "main";
            }
        }

        private static bool ScreenOpen() { foreach (var c in Screens) if (CanvasOn(c)) return true; return false; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool ModsOpen() { return Chainloader.PluginInfos.ContainsKey(Plugin.Apocasetter) && TitleMenu.ApocasetterIsOpen(); }

        // ---------------------------------------------------------------- saves (the game's slots)
        private static string SaveFile(int n) { return Path.Combine(Application.persistentDataPath, "SaveGame" + n + ".es3"); }
        private static bool SlotUsed(int n) { var f = new FileInfo(SaveFile(n)); return f.Exists && f.Length > 1024; }
        private static readonly Regex SeedPart = new Regex(@"\s*\(Seed:[^)]*\)", RegexOptions.Compiled);

        private void ReadSlots()
        {
            for (int n = 1; n <= Slots; n++)
            {
                _slotUsed[n] = SlotUsed(n);
                var t = Find("SaveGame_Canvas/SaveGame/save slot " + n + "/save " + n + " time");
                var tx = t != null ? t.GetComponent<Text>() : null;
                string s = tx != null && !string.IsNullOrEmpty(tx.text) ? tx.text : _slotUsed[n] ? File.GetLastWriteTime(SaveFile(n)).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) : "";
                _slotLabel[n] = _slotUsed[n] ? SeedPart.Replace(s, "").Trim() : "(empty)";
            }
        }

        // ---------------------------------------------------------------- actions
        private void Resume()
        {
            var menu = Fsm("__GameManager__", "Menu");
            if (menu == null) { Click("MainMenu_Canvas/MainMenu/Continue"); return; }
            PlayMakerFSM.BroadcastEvent("Save Settings");
            menu.SendEvent("Activate");
            _later.Add(new KeyValuePair<int, Action>(Time.frameCount + 1, () => { if (menu != null && menu.ActiveStateName == "pause") menu.Fsm.SetState("play"); }));
        }

        private void OpenSave()
        {
            var incar = Fsm("Player", "InCar");
            if (incar != null && incar.ActiveStateName == "InCar") { _message = "Get out of the car to save."; return; }
            _screen = "save"; _picked = -1; ReadSlots();
        }

        private void SaveTo(int slot)
        {
            if (Click("SaveGame_Canvas/SaveGame/save slot " + slot + "/save_game_" + slot)) _screen = "main";
        }

        private void QuitToMenu()
        {
            if (!Click("MainMenu_Canvas/MainMenu/Quit_To_Menu")) return;
            // the game's "Really Quit?" box (kept invisible) becomes active a frame later: press its Yes
            _later.Add(new KeyValuePair<int, Action>(Time.frameCount + 2, () => Click("MainMenu_Canvas/MainMenu/Quit_To_Menu/Really Quit/Yes_Quit")));
        }

        private string StatusText()
        {
            if (Active("MainMenu_Canvas/MainMenu/Saving game text")) return "Saving game" + new string('.', 1 + (int)(Time.unscaledTime * 2f) % 3);
            var t = Find("MainMenu_Canvas/MainMenu/Distance/distance_value");
            var tx = t != null ? t.GetComponent<Text>() : null;
            return tx != null && !string.IsNullOrEmpty(tx.text) ? "Distance travelled: " + tx.text.Trim() + " km" : "Paused";
        }

        // ---------------------------------------------------------------- drawing
        private Rect P(float x, float y, float w, float h) { int k = Ui.K; return new Rect(_ox + x * k, _oy + y * k, w * k, h * k); }
        private void Place(float w, float h, bool centred)
        {
            int k = Ui.K;
            _ox = centred ? Mathf.Round((Screen.width - w * k) / 2f) : Mathf.Round(Screen.width * .06f);
            _oy = centred ? Mathf.Round((Screen.height - h * k) / 2f) : Mathf.Round(Screen.height * .2f);
        }

        private void OnGUI()
        {
            if (!_shown) return;
            Ui.Begin();
            int depth = GUI.depth; GUI.depth = -800;
            try
            {
                bool modal = _message != null || _confirm != null;
                GUI.enabled = !modal && !SettingsWindow.IsOpen;
                if (_screen == "main") MainWindow(); else SaveWindow();
                GUI.enabled = true;
                if (SettingsWindow.IsOpen) SettingsWindow.Draw();
                else if (modal) Box();
            }
            finally { GUI.depth = depth; GUI.enabled = true; }
        }

        private bool Button(Rect r, string text, bool enabled = true, bool isDefault = false)
        {
            bool hit = Ui.Button(r, text, enabled, isDefault);
            if (hit) Sound();
            return hit;
        }

        private void MainWindow()
        {
            const float W = 200, X = 10, BW = W - 20, BH = 23, G = 6;
            var items = new List<KeyValuePair<string, Action>>
            {
                new KeyValuePair<string, Action>("Continue", Resume),
                new KeyValuePair<string, Action>("Save Game...", OpenSave),
                null2,
                new KeyValuePair<string, Action>("Settings...", () => SettingsWindow.Show(true)),
                new KeyValuePair<string, Action>("Codex...", () => Click("MainMenu_Canvas/MainMenu/Codex")),
                new KeyValuePair<string, Action>("Tutorial...", () => Click("MainMenu_Canvas/MainMenu/Tutorial")),
                new KeyValuePair<string, Action>("Credits...", () => Click("MainMenu_Canvas/MainMenu/Credits")),
                new KeyValuePair<string, Action>("Mods...", TitleMenu.OpenModsWindow),
                null2,
                new KeyValuePair<string, Action>("Quit to Main Menu...", () => { _confirm = "Quit to the main menu? Unsaved progress is lost."; _confirmYes = QuitToMenu; }),
            };
            int buttons = 0, rules = 0; foreach (var it in items) if (it.Key == null) rules++; else buttons++;
            float H = 29 + buttons * (BH + G) + rules * 12 + 4 + 20 + 3 + 4;
            Place(W, H, false);
            if (Ui.Window(P(0, 0, W, H), "Paused")) Resume();
            float y = 29;
            bool mods = Chainloader.PluginInfos.ContainsKey(Plugin.Apocasetter);
            foreach (var it in items)
            {
                if (it.Key == null) { var r = P(X, y + 2, BW, 0); Ui.Etched(r.x, r.y, r.width); y += 12; continue; }
                if (Button(P(X, y, BW, BH), it.Key, it.Key != "Mods..." || mods)) it.Value();
                y += BH + G;
            }
            var pane = P(3, H - 3 - 20, W - 6, 20);
            Ui.Sunken(pane);
            Ui.Fit(new Rect(pane.x + 4 * Ui.K, pane.y, pane.width - 8 * Ui.K, pane.height), StatusText(), Color.black);
        }
        private static readonly KeyValuePair<string, Action> null2 = new KeyValuePair<string, Action>(null, null);

        private void SaveWindow()
        {
            int k = Ui.K;
            const float W = 300, RH = 16;
            float listH = RH * Slots + 4, H = 29 + 18 + listH + 10 + 23 + 10;
            Place(W, H, false);
            float y = 29;
            if (Ui.Window(P(0, 0, W, H), "Save Game")) _screen = "main";
            Ui.Text(P(10, y, W - 20, 13), "Choose a slot to save to:", Color.black);
            y += 18;
            var list = P(10, y, W - 20, listH);
            Ui.Field(list);
            for (int n = 1; n <= Slots; n++)
            {
                var row = P(12, y + 2 + (n - 1) * RH, W - 24, RH);
                bool sel = n == _picked;
                if (Ui.Row(row, sel))
                {
                    bool twice = _lastClickSlot == n && Time.unscaledTime - _lastClickTime < .4f;
                    _picked = n; _lastClickSlot = n; _lastClickTime = Time.unscaledTime;
                    if (twice) Save(n);
                }
                var ink = sel ? Color.white : _slotUsed[n] ? Color.black : Win95.Shadow;
                Ui.Text(new Rect(row.x + 3 * k, row.y, 16 * k, row.height), n.ToString(CultureInfo.InvariantCulture), ink);
                Ui.Fit(new Rect(row.x + 22 * k, row.y, row.width - 26 * k, row.height), _slotLabel[n], ink);
            }
            y += listH + 10;
            if (Button(P(W - 10 - 75 - 6 - 75, y, 75, 23), "Save", _picked > 0, true)) Save(_picked);
            if (Button(P(W - 10 - 75, y, 75, 23), "Cancel")) _screen = "main";
        }

        private void Save(int slot)
        {
            if (slot <= 0) return;
            if (_slotUsed[slot]) { _confirm = "Overwrite slot " + slot + "?"; _confirmYes = () => SaveTo(slot); }
            else SaveTo(slot);
        }

        // a message box: a question (Yes / No) or a message (OK)
        private void Box()
        {
            int k = Ui.K;
            string text = _confirm ?? _message;
            const float W = 260;
            float textH = Ui.WrapHeight(text, (W - 20) * k) / k;
            float H = 29 + textH + 14 + 23 + 10;
            Place(W, H, true);
            if (Ui.Window(P(0, 0, W, H), "Apocalypter")) { _confirm = null; _message = null; }
            Ui.Wrap(P(10, 29, W - 20, 0), text, Color.black);
            float y = 29 + textH + 14;
            if (_confirm != null)
            {
                if (Button(P(W / 2 - 75 - 3, y, 75, 23), "Yes", true, true)) { var yes = _confirmYes; _confirm = null; yes?.Invoke(); }
                if (Button(P(W / 2 + 3, y, 75, 23), "No")) _confirm = null;
            }
            else if (Button(P((W - 75) / 2, y, 75, 23), "OK", true, true)) _message = null;
        }
    }
}
