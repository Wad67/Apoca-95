using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Chicago95
{
    // One place on the HUD for every mod's messages, drawn as Windows tooltips (pale yellow, black frame, the bitmap font):
    //  - notices (Post): a message for a few seconds, newest at the bottom of a stack under the top of the screen; a
    //    notice with a key replaces that key's last one (a countdown, a status), one without a key merges with an equal
    //    one still showing ("... (2)");
    //  - hints (Hint): instructions that stay while their owner keeps giving them (each frame), in the top left corner.
    // Mods' own toasts, notices and hint labels are routed here (NotifyRoutes).
    internal sealed class Notify : MonoBehaviour
    {
        private sealed class Notice { public string Key, Text; public int Count = 1; public float Until; }
        private sealed class HintLine { public string Text; public float Seen; public bool Prompt; }

        private static readonly List<Notice> Notices = new List<Notice>();
        private static readonly Dictionary<string, HintLine> Hints = new Dictionary<string, HintLine>();
        private static readonly List<string> HintOrder = new List<string>();
        private const int MaxNotices = 5;
        public const char Section = '\u001e';   // between a tooltip's sections: drawn as a groove

        private static HarmonyLib.Harmony _routes;
        // the mods' messages are routed from the first frame: every plugin's assembly is loaded by then
        public static void Install(GameObject host, HarmonyLib.Harmony h) { _routes = h; host.AddComponent<Notify>(); HudTexts.Install(host); }
        private void Start() { if (_routes != null) NotifyRoutes.Install(_routes); _routes = null; }

        public static void Post(string text, float seconds = 6f, string key = null)
        {
            if (string.IsNullOrEmpty(text)) return;
            text = text.Replace("\r", "").Trim();
            if (text.Length == 0) return;
            float until = Time.unscaledTime + Mathf.Max(1f, seconds);
            Notice n = null;
            if (key != null) n = Notices.Find(x => x.Key == key);
            else
            {
                n = Notices.Find(x => x.Key == null && x.Text == text);
                if (n != null) n.Count++;
            }
            if (n == null) { n = new Notice { Key = key }; Notices.Add(n); }
            else { Notices.Remove(n); Notices.Add(n); }   // to the bottom: the newest
            n.Text = text; n.Until = until;
            if (key != null) n.Count = 1;
            while (Notices.Count > MaxNotices) Notices.RemoveAt(0);
        }

        public static void Withdraw(string key) { Notices.RemoveAll(x => x.Key == key); }

        // shown while given again within a moment (call it every frame the hint applies)
        // prompt: about what is aimed at ("Pick up (E)"), shown as a tooltip beside the crosshair; else an instruction
        public static void Hint(string owner, string text, bool prompt = false)
        {
            if (string.IsNullOrEmpty(text) || text.Trim().Length == 0) return;
            if (!Hints.TryGetValue(owner, out var h)) { Hints[owner] = h = new HintLine(); HintOrder.Add(owner); }
            h.Text = text.Replace("\r", "").Trim(); h.Seen = Time.unscaledTime; h.Prompt = prompt;
        }

        // ---------------------------------------------------------------- drawing
        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            float now = Time.unscaledTime;
            Notices.RemoveAll(x => x.Until < now);
            for (int i = HintOrder.Count - 1; i >= 0; i--)
                if (now - Hints[HintOrder[i]].Seen > .25f) { Hints.Remove(HintOrder[i]); HintOrder.RemoveAt(i); }
            if (Notices.Count == 0 && HintOrder.Count == 0) return;
            if (LoadingDialog.Instance != null && LoadingDialog.Instance.Active) return;
            bool menu = PauseMenu.Claimed || TitleMenu.Claimed;   // hints are for play; notices show over menus too
            Ui.Begin();
            int k = Ui.K;
            int depth = GUI.depth; GUI.depth = 10;   // under the windows

            // instructions: the top left corner, one box each
            float y = 16 * k;
            foreach (var owner in HintOrder)
            {
                if (Hints[owner].Prompt || menu) continue;
                var r = Box(16 * k, y, Hints[owner].Text, false);
                y = r.yMax + 6 * k;
            }
            // prompts: one tooltip below and right of the crosshair, as Windows places one under the pointer
            var prompt = string.Join(Section.ToString(), HintOrder.Where(o => Hints[o].Prompt).Select(o => Hints[o].Text).ToArray());
            if (prompt.Length > 0 && !menu) Box(Mathf.Round(Screen.width / 2f) + 12 * k, Mathf.Round(Screen.height / 2f) + 20 * k, prompt, false);

            // notices: centred, under the compass, newest at the bottom
            y = Mathf.Round(Screen.height * .11f);
            foreach (var n in Notices)
            {
                string t = n.Count > 1 ? n.Text + " (" + n.Count + ")" : n.Text;
                var r = Box(0, y, t, true);
                y = r.yMax + 4 * k;
            }
            GUI.depth = depth;
        }

        // a tooltip box at (x, y), as wide as its text up to a limit (centred on the screen when centred)
        // its sections (split at Section) one under another, a groove between them
        private static Rect Box(float x, float y, string text, bool centred)
        {
            int k = Ui.K;
            const int Max = 420, Pad = 4, Gap = 7;
            var font = Ui.Font(false);
            var sections = new List<List<string>>();
            foreach (var part in text.Split(Section)) if (part.Trim().Length > 0) sections.Add(font.Wrap(part.Trim(), Max));
            if (sections.Count == 0) return new Rect(x, y, 0, 0);
            int w = 0, lines = 0;
            foreach (var s in sections) { lines += s.Count; foreach (var l in s) w = Mathf.Max(w, font.Width(l)); }
            float bw = (w + 2 * Pad + 2) * k, bh = (lines * Ui.Line + (sections.Count - 1) * Gap + 2 * Pad - 2) * k;
            if (centred) x = Mathf.Round((Screen.width - bw) / 2f);
            var r = new Rect(x, y, bw, bh);
            Ui.Notice(r);
            float ly = y + (Pad - 1) * k;
            for (int i = 0; i < sections.Count; i++)
            {
                if (i > 0) { Win95.Etched(x + 3 * k, Mathf.Round(ly + 2 * k), bw - 6 * k, k); ly += Gap * k; }
                foreach (var l in sections[i]) { font.Draw(x + (Pad + 1) * k, ly, l, Color.black, k); ly += Ui.Line * k; }
            }
            return r;
        }
    }
}
