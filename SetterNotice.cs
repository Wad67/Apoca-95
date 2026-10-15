using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Apocasetter;

namespace Chicago95
{
    // Apocasetter's extras over the game's menus (SettingsUI.DrawMenuExtras), drawn as Windows 95: its "Mods" button for
    // when the menu has none (only when neither of our menus is up: they have their own), and its title-screen notice
    // ("Mods changed", "2 mod updates": what was installed or updated, what can be) as a small window beside the title
    // menu with Later and Open Mods. Its state (dismissed, the list's filter) and opening are its own.
    internal static class SetterNotice
    {
        private static readonly AccessTools.FieldRef<SettingsUI, bool> Dismissed = AccessTools.FieldRefAccess<SettingsUI, bool>("_toastDismissed");
        private static readonly AccessTools.FieldRef<SettingsUI, string> Filter = AccessTools.FieldRefAccess<SettingsUI, string>("_filter");
        private static System.Reflection.MethodInfo _status;
        private static System.Reflection.FieldInfo _update;

        public static void Install(Harmony h)
        {
            var m = AccessTools.Method(typeof(SettingsUI), "DrawMenuExtras") ?? throw new System.MissingMethodException("SettingsUI.DrawMenuExtras");
            _status = AccessTools.Method(typeof(SettingsUI), "Status") ?? throw new System.MissingMethodException("SettingsUI.Status");
            h.Patch(m, prefix: new HarmonyMethod(typeof(SetterNotice), nameof(Draw)));
        }

        private static bool Updatable(SettingsUI ui, ModEntry m)
        {
            var s = _status.Invoke(ui, new object[] { m });
            if (s == null) return false;
            if (_update == null) _update = s.GetType().GetField("Update");
            return _update != null && (bool)_update.GetValue(s);
        }

        private static bool Draw(SettingsUI __instance)
        {
            Ui.Begin();
            int k = Ui.K;
            bool ours = TitleMenu.Claimed || PauseMenu.Claimed;
            if (!ours && !GameMenu.ButtonOnScreen && !GameMenu.HasButton && (GameMenu.MenuVisible || (!GameMenu.InGame && Cursor.visible)))
            {
                float bw = Ui.ButtonWidth("Mods...");
                if (Ui.Button(new Rect(Screen.width - bw - 24 * k, 24 * k, bw, 23 * k), "Mods...")) __instance.Open();
            }
            if (Apocasetter.Plugin.UpdateNoticeEntry != null && !Apocasetter.Plugin.UpdateNoticeEntry.Value) return false;
            if (Dismissed(__instance) || GameMenu.InGame || !TitleMenu.Claimed) return false;

            var updates = Catalog.All().Where(m => Updatable(__instance, m)).ToList();
            if (updates.Count == 0 && Updates.LastResults.Count == 0) return false;
            var rows = new List<KeyValuePair<string, string>>();
            foreach (var r in Updates.LastResults)
            {
                string op = MiniJson.Str(r, "op", "");
                rows.Add(new KeyValuePair<string, string>(MiniJson.Str(r, "name", ""),
                    !MiniJson.Bool(r, "ok", false) ? "Failed" : op == "update" ? "Updated to " + MiniJson.Str(r, "version", "") : char.ToUpperInvariant(op.FirstOrDefault()) + (op.Length > 1 ? op.Substring(1) : "") + " done"));
            }
            foreach (var m in updates.Take(6)) rows.Add(new KeyValuePair<string, string>(m.Name, m.Version + " -> " + m.Index.Version));

            const float W = 300, RH = 16;
            float listH = rows.Count * RH + 4, H = 29 + listH + 10 + 23 + 10;
            float ox = Mathf.Round(Screen.width * .06f) + 210 * k, oy = Mathf.Round(Screen.height * .2f);   // beside the title menu
            Rect P(float x, float y, float w, float h) { return new Rect(ox + x * k, oy + y * k, w * k, h * k); }
            string title = updates.Count == 0 ? "Mods Changed" : updates.Count + (updates.Count == 1 ? " Mod Update" : " Mod Updates");
            if (Ui.Window(P(0, 0, W, H), title)) { Dismissed(__instance) = true; return false; }
            Ui.Field(P(8, 29, W - 16, listH));
            for (int i = 0; i < rows.Count; i++)
            {
                var r = P(12, 31 + i * RH, W - 24, RH);
                bool failed = rows[i].Value == "Failed";
                float vw = Ui.Font(false).Width(rows[i].Value) * k;
                Ui.Fit(new Rect(r.x, r.y, r.width - vw - 8 * k, r.height), rows[i].Key, Color.black);
                Ui.Text(r, rows[i].Value, failed ? Win95.Hex("C00000") : Color.black, TextAnchor.MiddleRight);
            }
            float by = 29 + listH + 10;
            if (Ui.Button(P(W - 8 - 75, by, 75, 23), "Later")) Dismissed(__instance) = true;
            if (Ui.Button(P(W - 8 - 75 - 6 - 90, by, 90, 23), "Open Mods", true, true))
            {
                Dismissed(__instance) = true;
                Filter(__instance) = updates.Count == 0 ? "all" : "updates";
                __instance.Open();
            }
            return false;
        }
    }
}
