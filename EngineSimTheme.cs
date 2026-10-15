using HarmonyLib;
using UnityEngine;

namespace Chicago95
{
    // Engine Sim's windows (the Engine Tuning Toolkit, the dyno, its notices) already draw Windows 95 frames through its
    // own skin (EngineSim.Win95.Skin); here its text becomes the desktop's bitmap font (every style of that skin, and
    // so every style copied from one, is marked for PixelStyles at scale 1: its windows are laid out in screen pixels)
    // and its title bars are the theme's: solid navy, bold white title, the close box's drawn cross.
    internal static class EngineSimTheme
    {
        public static void Install(Harmony h)
        {
            var t = AccessTools.TypeByName("EngineSim.Win95") ?? throw new System.MissingMemberException("EngineSim.Win95");
            PixelStyles.Install(h);
            h.Patch(AccessTools.PropertyGetter(t, "Skin") ?? throw new System.MissingMemberException("EngineSim.Win95.Skin"), postfix: new HarmonyMethod(typeof(EngineSimTheme), nameof(Skin)));
            h.Patch(AccessTools.Method(t, "TitleBarWithClose") ?? throw new System.MissingMethodException("EngineSim.Win95.TitleBarWithClose"), prefix: new HarmonyMethod(typeof(EngineSimTheme), nameof(TitleBar)));
        }

        private static GUISkin _marked;
        private static void Skin(GUISkin __result)
        {
            if (__result == null || __result == _marked) return;
            _marked = __result;
            foreach (var st in new[] { __result.label, __result.window, __result.box, __result.button, __result.textField, __result.toggle })
                PixelStyles.Mark(st, true);
        }

        // inside the window (its own coordinates): the bar, the title, the close box; true when the box is clicked
        private static bool TitleBar(Rect window, string title, ref bool __result)
        {
            int k = Ui.K; Ui.K = 1;
            try
            {
                var bar = new Rect(3, 3, window.width - 6, 18);
                Win95.Fill(bar, Win95.Navy);
                PixelFont.Bold.Draw(new Rect(bar.x + 3, bar.y, bar.width - 24, bar.height), PixelFont.Bold.Fit(title, (int)bar.width - 24), Color.white, 1);
                __result = Ui.GlyphButton(new Rect(bar.xMax - 2 - 16, bar.y + 2, 16, 14), Ui.Cross);
            }
            finally { Ui.K = k; }
            return false;
        }
    }
}
