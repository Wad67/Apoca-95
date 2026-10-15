using HarmonyLib;
using UnityEngine;

namespace Chicago95
{
    // Apocasaver's "Save name" popup (SaveNaming.OnGUI) as a Windows 95 dialog: the name in a text field (focused when
    // it opens), OK and Cancel; Enter is OK, Escape is Cancel. Its state and actions (the text, confirming, which replays
    // the game's save click, and cancelling) are its own; only the drawing is replaced.
    internal static class SaverPrompt
    {
        private const string Type = "Apocasaver.SaveNaming";
        private static AccessTools.FieldRef<bool> _open, _focusPending;
        private static AccessTools.FieldRef<string> _text;
        private static AccessTools.FieldRef<float> _openedAt;
        private static System.Reflection.MethodInfo _confirm, _cancel;
        private static int _max = 24;

        public static void Install(Harmony h)
        {
            var t = AccessTools.TypeByName(Type) ?? throw new System.MissingMemberException(Type);
            _open = AccessTools.StaticFieldRefAccess<bool>(AccessTools.Field(t, "_open"));
            _focusPending = AccessTools.StaticFieldRefAccess<bool>(AccessTools.Field(t, "_focusPending"));
            _text = AccessTools.StaticFieldRefAccess<string>(AccessTools.Field(t, "_text"));
            _openedAt = AccessTools.StaticFieldRefAccess<float>(AccessTools.Field(t, "_openedAt"));
            _confirm = AccessTools.Method(t, "Confirm") ?? throw new System.MissingMethodException(Type + ".Confirm");
            _cancel = AccessTools.Method(t, "Cancel") ?? throw new System.MissingMethodException(Type + ".Cancel");
            var max = AccessTools.Field(t, "MaxLen");
            if (max != null && max.IsLiteral) _max = (int)max.GetRawConstantValue();
            h.Patch(AccessTools.Method(t, "OnGUI") ?? throw new System.MissingMethodException(Type + ".OnGUI"), prefix: new HarmonyMethod(typeof(SaverPrompt), nameof(Draw)));
        }

        private static bool Draw()
        {
            if (!_open()) return false;
            Ui.Begin();
            int k = Ui.K;
            var e = Event.current;
            // Enter is OK, before the field sees it; the press that opened it (a save point is used with Enter) is swallowed
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                e.Use();
                if (Time.realtimeSinceStartup - _openedAt() > .3f) { _confirm.Invoke(null, null); return false; }
            }
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { e.Use(); _cancel.Invoke(null, null); return false; }

            const float W = 300, H = 29 + 16 + 23 + 12 + 23 + 10;
            float ox = Mathf.Round((Screen.width - W * k) / 2f), oy = Mathf.Round((Screen.height - H * k) / 2f);
            Rect P(float x, float y, float w, float h) { return new Rect(ox + x * k, oy + y * k, w * k, h * k); }
            int depth = GUI.depth; GUI.depth = -1000;
            if (Ui.Window(P(0, 0, W, H), "Save Game")) { _cancel.Invoke(null, null); GUI.depth = depth; return false; }
            Ui.Text(P(10, 29, W - 20, 16), "Name for this save:", Color.black);
            _text() = Ui.TextField(P(10, 47, W - 20, 23), _text(), _max, "Untitled", "apocasaver_savename");
            if (_focusPending()) { GUI.FocusControl("apocasaver_savename"); _focusPending() = false; }
            float by = 47 + 23 + 12;
            if (Ui.Button(P(W - 10 - 75, by, 75, 23), "Cancel")) { _cancel.Invoke(null, null); GUI.depth = depth; return false; }
            if (Ui.Button(P(W - 10 - 75 - 6 - 75, by, 75, 23), "OK", true, true)) _confirm.Invoke(null, null);
            GUI.depth = depth;
            return false;
        }
    }
}
