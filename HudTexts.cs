using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using HarmonyLib;
using HutongGames.PlayMaker;
using UnityEngine;
using UnityEngine.UI;

namespace Chicago95
{
    // The HUD's instruction texts, whoever writes them, shown by Notify and hidden where they were (their graphics' alpha,
    // every frame, as Wild Wasteland hides the HUD it replaces). Read each frame, so every writer is caught: the game's
    // FSMs, and mods writing into the game's texts (Ultimate Tools adds its keys to the use text, Apocapocket its slot
    // prompt) or into their own.
    //  - prompts, beside the crosshair: the texts the game's FSMs address through its global variables (UI_ItemName,
    //    _2, _3, _4, UI_ItemUse, UI_AttachTrailer) and by name (ItemGrab, SleepTimer, UIText: what it says while you
    //    hold a key, or sleep);
    //  - instructions, top left: the game's part adjusting panel (AdjustUI) and weapon keys (WeaponUse); Ultimate
    //    Tools' tool guide and suspension hint.
    internal sealed class HudTexts : MonoBehaviour
    {
        // the tooltip's sections, in the game's order; a pair is one line the game writes in two texts side by side (a
        // number and its unit: "87" "%", "5.2" "/ 13L", "120" "Nuts", "pcs" "12")
        private static readonly string[][] Globals = { new[] { "UI_ItemName" }, new[] { "UI_ItemName_2", "UI_ItemName_3" }, new[] { "UI_ItemName_4" }, new[] { "UI_ItemUse" }, new[] { "UI_AttachTrailer" } };
        private static readonly string[] NamedTexts = { "ItemGrab", "SleepTimer", "UIText" };
        private static readonly string[] Panels = { "AdjustUI", "WeaponUse" };
        private static readonly Regex Tags = new Regex("<[^>]+>");

        private Transform _canvas;
        private readonly List<Text[]> _prompts = new List<Text[]>();
        private int _promptCount;
        private readonly List<KeyValuePair<string, Transform>> _panels = new List<KeyValuePair<string, Transform>>();
        private float _nextFind;
        private Type _guideType, _runnerType;
        private Component _guide, _runner;

        public static void Install(GameObject host) { host.AddComponent<HudTexts>(); }

        private void Start()
        {
            _guideType = AccessTools.TypeByName("PartAdjustment.ToolGuide") ?? AccessTools.TypeByName("UltimateTools.ToolGuide");
            _runnerType = AccessTools.TypeByName("PartAdjustment.ToolRunner") ?? AccessTools.TypeByName("UltimateTools.ToolRunner");
        }

        private static Transform Descendant(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) { var r = Descendant(t.GetChild(i), name); if (r != null) return r; }
            return null;
        }

        private void Find()
        {
            _prompts.Clear(); _panels.Clear(); _promptCount = 0;
            var root = TitleMenu.FindPath("Canvas");
            var canvas = root != null ? root.GetComponent<Canvas>() : null;
            _canvas = canvas != null && canvas.isRootCanvas ? root : null;
            Text Global(string g) { var v = FsmVariables.GlobalVariables.FindFsmGameObject(g); return v != null && v.Value != null ? v.Value.GetComponent<Text>() : null; }
            foreach (var section in Globals)
            {
                var texts = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(section, Global));
                _promptCount += System.Linq.Enumerable.Count(texts, t => t != null);
                _prompts.Add(texts);
            }
            // by name, in any of the scene's root canvases (the game's toolbox guide, AdjustUI, isn't under the HUD's)
            var roots = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Where(UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects(), r => r.GetComponent<Canvas>() != null));
            Transform Named(string n) { foreach (var r in roots) { var t = Descendant(r.transform, n); if (t != null) return t; } return null; }
            foreach (var n in NamedTexts) { var t = Named(n); var tx = t != null ? t.GetComponent<Text>() : null; if (tx != null) { _prompts.Add(new[] { tx }); _promptCount++; } }
            foreach (var n in Panels) { var t = Named(n); if (t != null) _panels.Add(new KeyValuePair<string, Transform>(n, t)); }
            if (_guideType != null) _guide = FindObjectOfType(_guideType) as Component;
            if (_runnerType != null) _runner = FindObjectOfType(_runnerType) as Component;
            _complete = _canvas != null && _promptCount >= 6 + NamedTexts.Length && _panels.Count == Panels.Length
                        && (_guideType == null || _guide != null) && (_runnerType == null || _runner != null);
        }
        private bool _complete;

        // a number and its unit on one line, the number first: "87%", "5.2 / 13L", "120 Nuts", "12 pcs"
        private static string Pair(string a, string b)
        {
            a = a ?? ""; b = b ?? "";
            if (b.Length == 0) return a;
            if (a.Length == 0) return b;
            if (!Number(a) && Number(b)) { var t = a; a = b; b = t; }
            return b.StartsWith("%", System.StringComparison.Ordinal) ? a + b : a + " " + b;
        }
        private static bool Number(string s) { return float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _); }

        private static bool Shown(Text t) { return t != null && t.enabled && t.gameObject.activeInHierarchy && !string.IsNullOrEmpty(t.text); }
        private static string Clean(string s) { return Tags.Replace(s ?? "", "").Trim(); }
        private static void Hide(Graphic g) { if (g != null && g.canvasRenderer.GetAlpha() != 0f) g.canvasRenderer.SetAlpha(0f); }

        private readonly List<Graphic> _graphics = new List<Graphic>();
        private readonly System.Text.StringBuilder _sb = new System.Text.StringBuilder();

        private void LateUpdate()
        {
            // looked for again every few seconds until everything is found (and again when a scene replaces them)
            if ((!_complete || _canvas == null) && Time.unscaledTime >= _nextFind) { _nextFind = Time.unscaledTime + 3f; Find(); }
            if (_canvas == null && _promptCount == 0) return;

            // prompts: one tooltip, its sections in the game's order
            _sb.Length = 0;
            foreach (var section in _prompts)
            {
                string a = null, b = null;
                for (int i = 0; i < section.Length; i++)
                {
                    var t = section[i];
                    if (t == null) continue;
                    if (Shown(t)) { var s = Clean(t.text); if (i == 0) a = s; else b = s; }
                    Hide(t);
                }
                var line = Pair(a, b);
                if (line.Length == 0) continue;
                if (_sb.Length > 0) _sb.Append(Notify.Section);
                _sb.Append(line);
            }
            if (_sb.Length > 0) Notify.Hint("game prompt", _sb.ToString(), true);

            // panels: their texts, top to bottom
            foreach (var p in _panels)
            {
                if (p.Value == null) continue;
                _graphics.Clear(); p.Value.GetComponentsInChildren(true, _graphics);
                if (p.Value.gameObject.activeInHierarchy)
                {
                    _sb.Length = 0;
                    foreach (var g in _graphics)
                        if (g is Text t && Shown(t)) { var s = Clean(t.text); if (s.Length > 0) { if (_sb.Length > 0) _sb.Append('\n'); _sb.Append(s); } }
                    if (_sb.Length > 0) Notify.Hint(p.Key, _sb.ToString());
                }
                foreach (var g in _graphics) Hide(g);
            }

            // Ultimate Tools' own labels
            Label(_guide, null, "label", "Ultimate Tools");
            Label(_runner, "hint", "label", "Ultimate Tools suspension");
        }

        private void Label(Component owner, string holder, string field, string key)
        {
            if (owner == null) return;
            object o = owner;
            if (holder != null) o = Traverse.Create(o).Field(holder).GetValue();
            var t = o != null ? Traverse.Create(o).Field(field).GetValue() as Text : null;
            if (t == null) return;
            if (Shown(t)) Notify.Hint(key, Clean(t.text));
            Hide(t);
        }
    }
}
