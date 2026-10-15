using System.Collections.Generic;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Chicago95
{
    // The loading screen's information in one Windows 95 dialog on the left (IMGUI), whatever the game shows behind it
    // left showing. It follows the game itself: its save/load FSM (SaveLoadGame: new game, loading a save, generating
    // the terrain, playing) and its own loading texts (the title's, the load list's, the in-game screen's), as Wild
    // Wasteland's loading screen does. With Wild Wasteland, that screen keeps its splash image, its tip and LOADING
    // label are hidden and the tip shown here, and its autosave screen opens the dialog for saving (WwLoading). In it:
    //  - what is loading and a block progress bar: the game's load stages up to the terrain, then MapMagic's own
    //    generation progress over all its tiles; saving has no measure, so a run of blocks moves along the bar;
    //  - a console (More Perfect DOS VGA): this machine's processor, memory and graphics card and every plugin loaded,
    //    then what is actually happening: each BepInEx log line as it is written (warnings yellow, errors red), the
    //    game's load states and the scenes it loads;
    //  - the screen's tip, as Windows' "Did you know...".
    internal sealed class LoadingDialog : MonoBehaviour
    {
        public static LoadingDialog Instance;
        private const float W = 600, ConsoleLines = 20;
        private static readonly Color Grey = Win95.Hex("AAAAAA"), Bright = Color.white, Yellow = Win95.Hex("FFFF55"), Red = Win95.Hex("FF5555");

        private struct Line { public string Text; public Color Ink; }
        private readonly List<Line> _lines = new List<Line>();
        private bool _active, _saving;
        private float _progress, _startedAt;
        private string _stage = "", _tip = "";
        private MapMagic.Core.MapMagicObject _mapMagic;
        private float _nextMapMagic;

        public static LoadingDialog Install(GameObject host)
        {
            if (Instance != null) return Instance;
            Instance = host.AddComponent<LoadingDialog>();
            Listener.Install();
            SceneManager.sceneLoaded += (s, m) => Instance.SceneLoaded();
            return Instance;
        }

        // ---------------------------------------------------------------- the game, followed
        private Transform _nativeGame, _nativeTitle, _nativeSlots;
        private PlayMakerFSM _saveLoad;
        private string _gameState;
        private bool _completed;
        private float _nextFind;

        private void Find()
        {
            if (Time.unscaledTime < _nextFind) return;
            _nextFind = Time.unscaledTime + .5f;
            if (_nativeGame == null) _nativeGame = TitleMenu.FindPath("Canvas/LoadingScreen");
            if (_nativeTitle == null) _nativeTitle = TitleMenu.FindPath("Canvas_MM/LoadingText");
            if (_nativeSlots == null) _nativeSlots = TitleMenu.FindPath("LoadGame_Canvas/LoadGame/LoadingText");
            if (_saveLoad == null)
            {
                var go = GameObject.Find("SaveLoadGame");
                if (go != null) foreach (var f in go.GetComponents<PlayMakerFSM>()) if (f.FsmName == "SaveLoadGame") { _saveLoad = f; break; }
            }
        }
        private static bool Visible(Transform t) { return t != null && t.gameObject.activeInHierarchy; }
        private bool NativeLoading() { return Visible(_nativeGame) || Visible(_nativeTitle) || Visible(_nativeSlots); }

        private void Update()
        {
            Find();
            string now = _saveLoad != null && _saveLoad.Fsm.Initialized ? _saveLoad.ActiveStateName : null;
            if (now != _gameState)
            {
                _gameState = now;
                if (now == "Start" || now == "setSeed" || now == "LoadGame" || now == "generateTerrain" || now == "generateTerrain 2") Begin(false, null);
                if (now != null) State(now);
                if (now == "isPlay") _completed = true;
            }
            bool native = NativeLoading();
            if (!_active && native) Begin(false, null);
            if (!_active) return;
            if (!_saving && _completed && !native) { End(); return; }
            Tick(_completed);
        }

        private void SceneLoaded()
        {
            _nativeGame = _nativeTitle = _nativeSlots = null; _saveLoad = null; _gameState = null; _nextFind = 0;
            Find();
            Scene();
            if (_active && !_saving && TitleMenu.FindPath("Canvas_MM") != null && !NativeLoading()) End();   // back at the title
        }

        // ---------------------------------------------------------------- what the loading screen tells it
        public bool Active => _active;

        public bool Saving => _active && _saving;

        public void Begin(bool saving, string tip)
        {
            if (_active && _saving == saving) { if (!string.IsNullOrEmpty(tip)) _tip = tip; return; }   // already showing
            _completed = false;
            _saving = saving; _progress = 0; _startedAt = Time.unscaledTime; _stage = saving ? "Saving the game" : "Starting";
            _tip = tip ?? "";
            _lines.Clear();
            Listener.Take();   // what was logged before this load is not part of it
            Post();
            _active = true;
        }

        public void End() { _active = false; }

        public void State(string current)
        {
            if (!_active || string.IsNullOrEmpty(current)) return;
            Add("Game state: " + current, Bright);
            switch (current)
            {
                case "Start": case "setSeed": Stage("Preparing a new world", .05f); break;
                case "LoadGame": Stage("Reading the save", .05f); break;
                case "generateTerrain": case "generateTerrain 2": Stage("Generating the terrain", .2f); break;
                case "isPlay": Stage("Starting the game", .98f); break;
            }
        }

        public void Scene()
        {
            if (!_active) return;
            Add("Scene loaded: " + SceneManager.GetActiveScene().name, Bright);
            if (!_saving) Stage("Loading the world", .15f);
        }

        public void Tick(bool completed)
        {
            if (!_active) return;
            foreach (var l in Listener.Take()) _lines.Add(l);
            while (_lines.Count > 200) _lines.RemoveAt(0);
            if (completed) _progress = 1f;
            else if (!_saving)
            {
                var mm = MapMagicObject();
                if (mm != null && mm.IsGenerating())
                {
                    float terrain = Mathf.Clamp01(mm.GetProgress());
                    _progress = Mathf.Max(_progress, .2f + .75f * terrain);
                    _stage = "Generating the terrain (" + Mathf.RoundToInt(terrain * 100f) + "%)";
                }
            }
        }

        private void Stage(string stage, float progress) { _stage = stage; _progress = Mathf.Max(_progress, progress); }

        // the POST: this machine and the plugins that were loaded
        private void Post()
        {
            Add(Chainloader.PluginInfos.TryGetValue(Plugin.WildWasteland, out var w) ? "Wild Wasteland BIOS v" + w.Metadata.Version : "Apocalypter BIOS", Bright);
            Add("", Grey);
            Add("Main Processor : " + SystemInfo.processorType + " x" + SystemInfo.processorCount, Grey);
            Add("Memory Test    : " + (SystemInfo.systemMemorySize * 1024L) + "K OK", Grey);
            Add("Display        : " + SystemInfo.graphicsDeviceName + ", " + SystemInfo.graphicsMemorySize + " MB", Grey);
            Add("", Grey);
            int count = 0;
            foreach (var p in Chainloader.PluginInfos.Values)
            {
                if (p == null || p.Metadata == null) continue;
                Add("Detecting " + p.Metadata.Name + " ... " + p.Metadata.Version, Grey);
                count++;
            }
            Add(count + " plugin(s) loaded.", Grey);
            Add("", Grey);
        }

        private void Add(string text, Color ink) { _lines.Add(new Line { Text = Clean(text), Ink = ink }); }
        private static string Clean(string s) { return s == null ? "" : s.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' '); }

        private MapMagic.Core.MapMagicObject MapMagicObject()
        {
            if (_mapMagic == null && Time.unscaledTime >= _nextMapMagic)
            {
                _nextMapMagic = Time.unscaledTime + 1f;
                foreach (var m in Resources.FindObjectsOfTypeAll<MapMagic.Core.MapMagicObject>())
                    if (m != null && m.gameObject.scene.IsValid()) { _mapMagic = m; break; }
            }
            return _mapMagic;
        }

        // ---------------------------------------------------------------- the dialog
        private void OnGUI()
        {
            if (!_active || Event.current.type != EventType.Repaint) return;   // nothing in it takes input
            Ui.Begin();
            int k = Ui.K;
            int depth = GUI.depth; GUI.depth = -900;
            var dos = PixelFont.Dos;

            float consoleH = ConsoleLines * dos.LineHeight + 8;
            var tipLines = string.IsNullOrEmpty(_tip) ? 0 : Ui.Font(false).Wrap(_tip, (int)(W - 44)).Count;
            float tipH = string.IsNullOrEmpty(_tip) ? 0 : 26 + tipLines * Ui.Line + 8;
            float H = 29 + 18 + 22 + 10 + consoleH + (tipH > 0 ? 10 + tipH : 0) + 10;
            float ox = Mathf.Round(Screen.width * .03f), oy = Mathf.Round((Screen.height - H * k) / 2f);
            Rect P(float x, float y, float w, float h) { return new Rect(ox + x * k, oy + y * k, w * k, h * k); }

            Ui.Window(P(0, 0, W, H), _saving ? "Saving" : "Loading Apocalypter", false);
            float y = 29;

            // what is loading, and how far
            Ui.Fit(P(10, y, W - 70, 16), _stage, Color.black, TextAnchor.MiddleLeft, true);
            if (!_saving) Ui.Text(P(W - 60, y, 50, 16), Mathf.RoundToInt(_progress * 100f) + "%", Color.black, TextAnchor.MiddleRight);
            y += 18;
            var bar = P(10, y, W - 20, 22);
            Win95.Fill(bar, Win95.Face); Ui.Sunken(bar);
            float bx = bar.x + 3 * k, bw = 8 * k, gap = 2 * k, inner = bar.width - 6 * k;
            int blocks = (int)((inner + gap) / (bw + gap));
            if (_saving)
            {
                int at = (int)((Time.unscaledTime - _startedAt) * 12f) % (blocks + 4);
                for (int i = Mathf.Max(0, at - 4); i < Mathf.Min(blocks, at); i++) Win95.Fill(new Rect(bx + i * (bw + gap), bar.y + 3 * k, bw, bar.height - 6 * k), Win95.Navy);
            }
            else
            {
                int lit = Mathf.RoundToInt(_progress * blocks);
                for (int i = 0; i < lit; i++) Win95.Fill(new Rect(bx + i * (bw + gap), bar.y + 3 * k, bw, bar.height - 6 * k), Win95.Navy);
            }
            y += 22 + 10;

            // the console: the newest lines that fit, cut to its width
            var console = P(10, y, W - 20, consoleH);
            Win95.Fill(console, Color.black); Ui.Sunken(console);
            int cols = (int)((console.width - 12 * k) / (9 * k));
            int first = Mathf.Max(0, _lines.Count - (int)ConsoleLines);
            float ly = console.y + 4 * k;
            for (int i = first; i < _lines.Count; i++)
            {
                var l = _lines[i];
                string t = l.Text.Length > cols ? l.Text.Substring(0, Mathf.Max(0, cols - 3)) + "..." : l.Text;
                dos.Draw(console.x + 6 * k, ly, t, l.Ink, k);
                ly += dos.LineHeight * k;
            }
            y += consoleH + 10;

            // the tip, as Windows' "Did you know..."
            if (tipH > 0)
            {
                var box = P(10, y, W - 20, tipH);
                Ui.Field(box);
                Ui.Text(P(18, y + 6, W - 36, 16), "Did you know...", Color.black, TextAnchor.MiddleLeft, true);
                Ui.Wrap(P(18, y + 26, W - 44, 0), _tip, Color.black);
            }
            GUI.depth = depth;
        }

        // ---------------------------------------------------------------- the log, from BepInEx
        // A BepInEx log listener, installed once: keeps the newest lines (from any thread) until the dialog takes them.
        private sealed class Listener : ILogListener
        {
            private static Listener _installed;
            private static readonly object Lock = new object();
            private static readonly Queue<Line> Pending = new Queue<Line>();

            internal static void Install()
            {
                if (_installed != null) return;
                _installed = new Listener();
                BepInEx.Logging.Logger.Listeners.Add(_installed);
            }

            internal static List<Line> Take()
            {
                var lines = new List<Line>();
                lock (Lock) { while (Pending.Count > 0) lines.Add(Pending.Dequeue()); }
                return lines;
            }

            public void LogEvent(object sender, LogEventArgs e)
            {
                if (e == null || e.Data == null || Instance == null || !Instance._active) return;
                string source = e.Source != null ? e.Source.SourceName : "?";
                var ink = (e.Level & (LogLevel.Error | LogLevel.Fatal)) != 0 ? Red : (e.Level & LogLevel.Warning) != 0 ? Yellow : Grey;
                lock (Lock)
                {
                    Pending.Enqueue(new Line { Text = Clean("[" + source + "] " + e.Data), Ink = ink });
                    while (Pending.Count > 200) Pending.Dequeue();
                }
            }

            public void Dispose() { }
        }
    }
}
