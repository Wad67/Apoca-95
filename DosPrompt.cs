using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Chicago95
{
    // The console, as Windows 95's MS-DOS Prompt: a window with a black 80-column screen in the VGA font, opened and
    // closed with the backquote key (Esc closes it too). It shows the BepInEx log's warnings and errors as they come (or
    // all of it: "log all"), and runs what is
    // typed at the C:\APOCALYPTER> prompt as Lua through SigmaMod's console (found by name, so nothing breaks without
    // SigmaMod: then the prompt only knows its own commands). Up and Down step through what was typed, Page Up and
    // Page Down scroll, "cls" clears, "help" says this. While it is open, the game's keyboard and mouse read as nothing
    // (Unity's Input is answered here), so typing doesn't walk the player or fire; the game keeps running.
    internal sealed class DosPrompt : MonoBehaviour
    {
        private const int Cols = 80, Rows = 25, MaxLines = 600;
        private static readonly Color Grey = Win95.Hex("C0C0C0"), White = Color.white, Red = Win95.Hex("FF5555"), Dim = Win95.Hex("808080"),
                                      Yellow = Win95.Hex("FFFF55"), Cyan = Win95.Hex("55FFFF");

        internal static bool Open { get; private set; }
        // how much of the log it shows: 0 none, 1 warnings and errors (the default), 2 everything
        private static int _logLevel = 1;
        private static DosPrompt _instance;

        private struct Line { public string Text; public Color Ink; }
        private readonly List<Line> _lines = new List<Line>();
        private static readonly object Lock = new object();
        private static readonly Queue<Line> Pending = new Queue<Line>();
        private readonly List<string> _history = new List<string>();
        private int _historyAt = -1;
        private string _input = "";
        private int _scroll;   // lines up from the bottom
        private MethodInfo _run, _reset;
        private bool _bridged;

        public static void Install(GameObject host, Harmony h)
        {
            _instance = host.AddComponent<DosPrompt>();
            BepInEx.Logging.Logger.Listeners.Add(new LogTap());
            InputBlock.Install(h);
            Say("Apoca-95 MS-DOS Prompt. Type help.", Cyan);
        }

        private static void Say(string text, Color ink)
        {
            lock (Lock)
            {
                foreach (var l in (text ?? "").Replace("\r", "").Split('\n')) Pending.Enqueue(new Line { Text = l.Replace("\t", "    "), Ink = ink });
                while (Pending.Count > MaxLines) Pending.Dequeue();
            }
        }

        // SigmaMod's Lua console, when it is there
        private void Bridge()
        {
            if (_bridged) return;
            _bridged = true;
            var t = AccessTools.TypeByName("SigmaMod.LuaConsole");
            if (t == null) { Say("Lua: SigmaMod not loaded, so only the prompt's own commands work.", Dim); return; }
            _run = AccessTools.Method(t, "Run", new[] { typeof(string) });
            _reset = AccessTools.Method(t, "Reset");
            var ev = t.GetEvent("Output");
            // its echo of the command is left out: the prompt echoes it as DOS does
            if (ev != null) ev.AddEventHandler(null, (Action<string, int>)((s, kind) => { if (kind != 2) Say(s, kind == 1 ? Red : Grey); }));
        }

        private void Update()
        {
            lock (Lock) while (Pending.Count > 0) _lines.Add(Pending.Dequeue());
            if (_lines.Count > MaxLines) _lines.RemoveRange(0, _lines.Count - MaxLines);
        }

        // ---------------------------------------------------------------- keys
        private void OnGUI()
        {
            var e = Event.current;
            // the backquote opens and closes it, unless something else is being typed into
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.BackQuote && (Open || GUIUtility.keyboardControl == 0))
            {
                Open = !Open;
                if (Open) { Bridge(); _scroll = 0; }
                e.Use();
                return;
            }
            if (!Open) return;
            if (e.type == EventType.KeyDown) { Key(e); e.Use(); return; }
            if (e.type == EventType.ScrollWheel) { _scroll = Mathf.Clamp(_scroll + (e.delta.y < 0 ? 3 : -3), 0, Mathf.Max(0, _lines.Count - (Rows - 1))); e.Use(); return; }
            if (e.type == EventType.Repaint) Draw();
        }

        private void Key(Event e)
        {
            switch (e.keyCode)
            {
                case KeyCode.Escape: Open = false; return;
                case KeyCode.Return: case KeyCode.KeypadEnter: Enter(); return;
                case KeyCode.Backspace: if (_input.Length > 0) _input = _input.Substring(0, _input.Length - 1); return;
                case KeyCode.UpArrow:
                    if (_history.Count == 0) return;
                    _historyAt = _historyAt < 0 ? _history.Count - 1 : Mathf.Max(0, _historyAt - 1);
                    _input = _history[_historyAt]; return;
                case KeyCode.DownArrow:
                    if (_historyAt < 0) return;
                    _historyAt++;
                    if (_historyAt >= _history.Count) { _historyAt = -1; _input = ""; } else _input = _history[_historyAt];
                    return;
                case KeyCode.PageUp: _scroll = Mathf.Min(_scroll + Rows - 2, Mathf.Max(0, _lines.Count - (Rows - 1))); return;
                case KeyCode.PageDown: _scroll = Mathf.Max(0, _scroll - (Rows - 2)); return;
            }
            char c = e.character;
            if (c >= ' ' && c != '`' && c != 127 && _input.Length < 500) _input += c;
        }

        private void Enter()
        {
            string cmd = _input.Trim();
            _input = "";
            _historyAt = -1;
            _scroll = 0;
            if (cmd.Length == 0) { Say(Prompt, Grey); return; }
            if (_history.Count == 0 || _history[_history.Count - 1] != cmd) _history.Add(cmd);
            switch (cmd.ToLowerInvariant())
            {
                case "cls": lock (Lock) Pending.Clear(); _lines.Clear(); return;
                case "help":
                    Say(Prompt + cmd, White);
                    Say("Anything typed is Lua (SigmaMod). An expression prints its value.", Grey);
                    Say("  clr.type(\"Name\")          a C# type's statics (live code first)", Grey);
                    Say("  clr.objects(\"Name\")       every live object of a type (a list, from 1)", Grey);
                    Say("  clr.find(\"path\")          a GameObject by name or path", Grey);
                    Say("  clr.component(go, \"Name\") a component on it;  clr.new(\"Name\", ...) a new object", Grey);
                    Say("  vec(x, y, z)  print(...)  log(...)", Grey);
                    Say("  e.g.  g = clr.objects(\"DcGenerator\")[1]   g.volts = 20   g.LoadText", Grey);
                    Say("Prompt: cls, help, reset (a fresh Lua state), log all / warn / off (how much of the log shows), exit.", Grey);
                    Say("Keys: Up/Down history, PgUp/PgDn scroll, Esc or ` close.", Grey);
                    return;
                case "exit": Open = false; return;
                case "log all": case "log warn": case "log off":
                    _logLevel = cmd.EndsWith("all") ? 2 : cmd.EndsWith("warn") ? 1 : 0;
                    Say(Prompt + cmd, White);
                    Say(_logLevel == 2 ? "The log: everything." : _logLevel == 1 ? "The log: warnings and errors." : "The log: none.", Grey);
                    return;
                case "reset": Say(Prompt + cmd, White); _reset?.Invoke(null, null); return;
            }
            Say(Prompt + cmd, White);
            if (_run == null) { Say("Bad command or file name", Grey); return; }
            try { _run.Invoke(null, new object[] { cmd }); }
            catch (Exception ex) { Say((ex.InnerException ?? ex).Message, Red); }
        }

        private const string Prompt = "C:\\APOCALYPTER>";

        // ---------------------------------------------------------------- the window
        private void Draw()
        {
            Ui.Begin();
            int k = Ui.K;
            int depth = GUI.depth; GUI.depth = -1000;
            var dos = PixelFont.Dos;
            const int cw = 9;
            int lh = dos.LineHeight;
            float sw = Cols * cw + 12, sh = Rows * lh + 8;
            float W = sw + 8, H = 29 + sh + 8;
            float ox = Mathf.Round(Screen.width * .04f), oy = Mathf.Round(Screen.height * .05f);
            Rect P(float x, float y, float w, float h) { return new Rect(ox + x * k, oy + y * k, w * k, h * k); }

            Ui.Window(P(0, 0, W, H), "MS-DOS Prompt", false);
            var screen = P(4, 29, sw, sh);
            Win95.Fill(screen, Color.black);
            Ui.Sunken(screen);

            // the newest lines that fit above the prompt, wrapped at 80 columns
            var rows = new List<Line>();
            int need = Rows - 1 + _scroll;
            for (int i = _lines.Count - 1; i >= 0 && rows.Count < need; i--)
            {
                var wrapped = Wrap(_lines[i].Text);
                for (int j = wrapped.Count - 1; j >= 0 && rows.Count < need; j--) rows.Add(new Line { Text = wrapped[j], Ink = _lines[i].Ink });
            }
            rows.Reverse();
            int first = Mathf.Max(0, rows.Count - _scroll - (Rows - 1));
            float y = screen.y + 4 * k;
            for (int i = first; i < Mathf.Min(rows.Count, first + Rows - 1); i++)
            {
                dos.Draw(screen.x + 6 * k, y, rows[i].Text, rows[i].Ink, k);
                y += lh * k;
            }
            // the prompt, the end of what is being typed, and the blinking cursor
            string line = Prompt + _input;
            if (line.Length > Cols - 1) line = line.Substring(line.Length - (Cols - 1));
            float py = screen.y + 4 * k + (Rows - 1) * lh * k;
            dos.Draw(screen.x + 6 * k, py, line, Grey, k);
            if ((int)(Time.unscaledTime * 2.5f) % 2 == 0)
                Win95.Fill(new Rect(screen.x + 6 * k + line.Length * cw * k, py + (lh - 3) * k, cw * k, 2 * k), Grey);
            if (_scroll > 0) dos.Draw(screen.x + (sw - 6 - 9 * 10) * k, screen.y + 4 * k, "[scrolled]", Yellow, k);
            GUI.depth = depth;
        }

        private static List<string> Wrap(string s)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(s)) { list.Add(""); return list; }
            for (int i = 0; i < s.Length; i += Cols) list.Add(s.Substring(i, Math.Min(Cols, s.Length - i)));
            return list;
        }

        // ---------------------------------------------------------------- the log
        private sealed class LogTap : ILogListener
        {
            public void LogEvent(object sender, LogEventArgs e)
            {
                if (e?.Data == null) return;
                bool loud = (e.Level & (LogLevel.Error | LogLevel.Fatal | LogLevel.Warning)) != 0;
                if (_logLevel == 0 || _logLevel == 1 && !loud) return;
                var ink = (e.Level & (LogLevel.Error | LogLevel.Fatal)) != 0 ? Red : (e.Level & LogLevel.Warning) != 0 ? Yellow : Dim;
                Say("[" + (e.Source?.SourceName ?? "?") + "] " + e.Data, ink);
            }
            public void Dispose() { }
        }

        // ---------------------------------------------------------------- the game's input while it is open
        private static class InputBlock
        {
            public static void Install(Harmony h)
            {
                var input = typeof(Input);
                void Patch(string name, Type[] args, string prefix)
                {
                    var m = args == null ? AccessTools.PropertyGetter(input, name) : AccessTools.Method(input, name, args);
                    if (m == null) { Plugin.Log.LogWarning("console: Input." + name + " not found; not blocked"); return; }
                    try { h.Patch(m, prefix: new HarmonyMethod(typeof(InputBlock), prefix)); }
                    catch (Exception ex) { Plugin.Log.LogWarning("console: Input." + name + " not blocked: " + ex.Message); }
                }
                var key = new[] { typeof(KeyCode) }; var str = new[] { typeof(string) }; var num = new[] { typeof(int) };
                foreach (var n in new[] { "GetKey", "GetKeyDown", "GetKeyUp" }) { Patch(n, key, nameof(False)); Patch(n, str, nameof(False)); }
                foreach (var n in new[] { "GetButton", "GetButtonDown", "GetButtonUp" }) Patch(n, str, nameof(False));
                foreach (var n in new[] { "GetMouseButton", "GetMouseButtonDown", "GetMouseButtonUp" }) Patch(n, num, nameof(False));
                foreach (var n in new[] { "GetAxis", "GetAxisRaw" }) Patch(n, str, nameof(Zero));
                Patch("anyKey", null, nameof(False));
                Patch("anyKeyDown", null, nameof(False));
                Patch("mouseScrollDelta", null, nameof(NoScroll));
            }

            private static bool False(ref bool __result) { if (!Open) return true; __result = false; return false; }
            private static bool Zero(ref float __result) { if (!Open) return true; __result = 0f; return false; }
            private static bool NoScroll(ref Vector2 __result) { if (!Open) return true; __result = Vector2.zero; return false; }
        }
    }
}
