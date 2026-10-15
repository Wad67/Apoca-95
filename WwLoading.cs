using System;
using HarmonyLib;
using UnityEngine;

namespace Chicago95
{
    // Wild Wasteland's loading screen (CleanHUD's LoadingScreen) keeps its splash image; its tip lines and LOADING label
    // are switched off and its tip goes in the dialog (LoadingDialog, which follows the game's loading by itself). Its
    // autosave screen (Apocasaver saving) opens the dialog for saving, and closes it.
    internal static class WwLoading
    {
        private const string Type = "WildWasteland.Modules.CleanHUD.LoadingScreen";
        private static LoadingDialog _dialog;
        private static AccessTools.FieldRef<object, bool> _saving;
        private static AccessTools.FieldRef<object, string> _tip;
        private static System.Reflection.FieldInfo _label, _tipLines;   // a GlyphText and an array of them
        private static System.Reflection.PropertyInfo _active;

        public static void Install(Harmony h, GameObject host)
        {
            var t = AccessTools.TypeByName(Type) ?? throw new MissingMemberException(Type);
            _saving = AccessTools.FieldRefAccess<object, bool>(AccessTools.Field(t, "_saving"));
            _tip = AccessTools.FieldRefAccess<object, string>(AccessTools.Field(t, "_currentTip"));
            _label = AccessTools.Field(t, "_label") ?? throw new MissingFieldException(Type + "._label");
            _tipLines = AccessTools.Field(t, "_tipLines") ?? throw new MissingFieldException(Type + "._tipLines");
            _active = AccessTools.Property(t, "Active") ?? throw new MissingMemberException(Type + ".Active");
            void Post(string method, string patch)
            {
                var m = AccessTools.Method(t, method) ?? throw new MissingMethodException(Type + "." + method);
                h.Patch(m, postfix: new HarmonyMethod(typeof(WwLoading), patch));
            }
            Post("Begin", nameof(Begun));
            Post("End", nameof(Ended));
            Post("Tick", nameof(Ticked));
            _dialog = LoadingDialog.Install(host);
        }

        private static bool Active(object screen) { return (bool)_active.GetValue(screen, null); }

        // its own text off: the tip goes in the dialog
        private static void Quiet(object screen)
        {
            var label = _label.GetValue(screen) as Component;
            if (label != null && label.gameObject.activeSelf) label.gameObject.SetActive(false);
            var lines = _tipLines.GetValue(screen) as Array;
            if (lines != null) foreach (var o in lines) if (o is Component c && c != null && c.gameObject.activeSelf) c.gameObject.SetActive(false);
        }

        private static void Begun(object __instance)
        {
            if (!Active(__instance)) return;
            Quiet(__instance);
            _dialog.Begin(_saving(__instance), _tip(__instance));
        }
        private static void Ended() { if (_dialog.Saving) _dialog.End(); }   // loads end when the game is playing
        private static void Ticked(object __instance)
        {
            if (!Active(__instance)) return;
            Quiet(__instance);
            _dialog.Begin(_saving(__instance), _tip(__instance));   // its tip, or a save it shows
        }
    }
}
