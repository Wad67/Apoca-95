using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace Chicago95
{
    // Every mod's own HUD messages, sent to Notify instead of drawn by the mod. Each route is patched on its own (a mod
    // missing, or another version of it, leaves the others working):
    //  notices
    //   - the game's timed messages and Apocalypter Quests' toasts, as Wild Wasteland's CleanHUD collects them
    //     (MessageHook.Push), and Quests' toasts when CleanHUD doesn't (AddToast);
    //   - Wild Wasteland's "War rig spawned" banner and the patrol template exporter's results;
    //   - Immersive Map's toasts; Apocasaver's status ("Game saved", "Autosave in 5", failures); Engine Sim's notices;
    //  hints (their GUI.Label calls redirected, so the text is built as before and only shown here)
    //   - Burner's Utilities' nail gun and rope tool, Engine Sim's tuning kit, Wild Wasteland's weapon pose and vehicle
    //     hotkey hints, Apoc-Air's cockpit readout (its dark backing dropped).
    internal static class NotifyRoutes
    {
        private static Harmony _h;

        public static void Install(Harmony h)
        {
            _h = h;
            // notices
            Route("WildWasteland.Modules.CleanHUD.MessageHook", "Push", prefix: nameof(Push));
            Route("ApocalypterQuests.Plugin", "AddToast", prefix: nameof(QuestToast), last: true);
            Route("WildWasteland.Modules.Patrols.WorldTraffic", "Gui", prefix: nameof(WarRig));
            Route("WildWasteland.Modules.Patrols.TemplateExporter", "Toast", postfix: nameof(ExporterToast));
            Route("ImmersiveMap.ImmersiveMapPlugin", "Toast", postfix: nameof(MapToast));
            Route("Apocasaver.StatusLabel", "Show", prefix: nameof(SaverShow));
            Route("Apocasaver.StatusLabel", "ShowCountdown", prefix: nameof(SaverCountdown));
            Route("Apocasaver.StatusLabel", "Hide", prefix: nameof(SaverHide));
            Route("EngineSim.EngineManager", "Notice", postfix: nameof(EngineNotice));
            // hints
            Labels("BurnersUtilities.NailGun", "OnGUI", "Nail gun");
            Labels("BurnersUtilities.RopeTool", "OnGUI", "Rope tool");
            Labels("EngineSim.TuningKit", "OnGUI", "Engine tuning toolkit");
            Labels("WildWasteland.Modules.PlayerBody.GunPose", "OnGUI", "Weapon pose");
            Labels("WildWasteland.Modules.PlayerBody.Car", "OnGUI", "Vehicle hotkeys");
            Labels("ApocAir.Cockpit", "OnGUI", "Cockpit", dropBacking: true);
        }

        private static MethodInfo Target(string type, string method)
        {
            var t = AccessTools.TypeByName(type);
            if (t == null) return null;   // that mod isn't loaded
            return AccessTools.Method(t, method) ?? throw new MissingMethodException(type + "." + method);
        }

        private static void Route(string type, string method, string prefix = null, string postfix = null, bool last = false)
        {
            try
            {
                var m = Target(type, method);
                if (m == null) return;
                HarmonyMethod Hm(string n) { if (n == null) return null; var hm = new HarmonyMethod(typeof(NotifyRoutes), n); if (last) hm.priority = Priority.Last; return hm; }
                _h.Patch(m, prefix: Hm(prefix), postfix: Hm(postfix));
            }
            catch (Exception e) { Plugin.Log.LogWarning("Notifications: " + type + "." + method + " not routed: " + e.Message); }
        }

        // ---------------------------------------------------------------- notices
        private static bool Push(string msg) { Notify.Post(msg); return false; }

        // CleanHUD (some versions) takes these first and pushes them to its log, which comes here: then nothing to do
        private static bool QuestToast(string __0, bool __runOriginal)
        {
            if (!__runOriginal) return false;
            Notify.Post(__0);
            return false;
        }

        private static FieldInfo _rigNotice;
        private static void WarRig()
        {
            if (_rigNotice == null) _rigNotice = AccessTools.Field(AccessTools.TypeByName("WildWasteland.Modules.Patrols.WarRig.WorldSpawning"), "NaturalNoticeUntil");
            if (_rigNotice == null) return;
            float until = (float)_rigNotice.GetValue(null);
            if (until <= Time.unscaledTime) return;
            Notify.Post("War rig spawned", until - Time.unscaledTime);
            _rigNotice.SetValue(null, 0f);   // shown here: not by it
        }

        private static void ExporterToast(object __instance, string s)
        {
            Notify.Post(s, 8f);
            Traverse.Create(__instance).Field("_toast").SetValue(null);
        }

        private static void MapToast(object __instance, string text)
        {
            Notify.Post(text, 3f);
            Traverse.Create(__instance).Field("toast").SetValue("");
        }

        private const string Saver = "apocasaver";
        private static bool SaverShow(string text, float seconds) { Notify.Post(text, seconds, Saver); return false; }
        private static bool SaverCountdown(int seconds) { Notify.Post("Autosave in " + seconds, 1.2f, Saver); return false; }
        private static void SaverHide() { Notify.Withdraw(Saver); }

        private static void EngineNotice(object __instance, string text)
        {
            Notify.Post(text, 8f);
            Traverse.Create(__instance).Field("_notice").SetValue(null);
        }

        // ---------------------------------------------------------------- hints
        private static readonly Dictionary<MethodBase, string> Owners = new Dictionary<MethodBase, string>();

        private static void Labels(string type, string method, string owner, bool dropBacking = false)
        {
            try
            {
                var m = Target(type, method);
                if (m == null) return;
                Owners[m] = owner;
                _h.Patch(m, transpiler: new HarmonyMethod(typeof(NotifyRoutes), dropBacking ? nameof(LabelsNoBacking) : nameof(LabelsOnly)));
            }
            catch (Exception e) { Plugin.Log.LogWarning("Notifications: " + type + "." + method + " not routed: " + e.Message); }
        }

        private static readonly MethodInfo LabelText = AccessTools.Method(typeof(GUI), nameof(GUI.Label), new[] { typeof(Rect), typeof(string), typeof(GUIStyle) });
        private static readonly MethodInfo LabelContent = AccessTools.Method(typeof(GUI), nameof(GUI.Label), new[] { typeof(Rect), typeof(GUIContent), typeof(GUIStyle) });
        private static readonly MethodInfo Texture = AccessTools.Method(typeof(GUI), nameof(GUI.DrawTexture), new[] { typeof(Rect), typeof(Texture) });

        private static IEnumerable<CodeInstruction> LabelsOnly(IEnumerable<CodeInstruction> code, MethodBase original) { return Rewrite(code, original, false); }
        private static IEnumerable<CodeInstruction> LabelsNoBacking(IEnumerable<CodeInstruction> code, MethodBase original) { return Rewrite(code, original, true); }

        // each GUI.Label(rect, text, style) becomes HintText(rect, text, style, owner)
        private static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> code, MethodBase original, bool dropBacking)
        {
            string owner = Owners.TryGetValue(original, out var o) ? o : original.DeclaringType?.Name;
            int n = 0;
            foreach (var ins in code)
            {
                if (ins.Calls(LabelText) || ins.Calls(LabelContent))
                {
                    yield return new CodeInstruction(OpCodes.Ldstr, owner);
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(NotifyRoutes), ins.Calls(LabelText) ? nameof(HintText) : nameof(HintContent))) { labels = ins.labels, blocks = ins.blocks };
                    n++;
                    continue;
                }
                if (dropBacking && ins.Calls(Texture))
                {
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(NotifyRoutes), nameof(NoTexture))) { labels = ins.labels, blocks = ins.blocks };
                    continue;
                }
                yield return ins;
            }
            if (n == 0) Plugin.Log.LogWarning("Notifications: no label found in " + original.DeclaringType?.FullName + "." + original.Name);
        }

        public static void HintText(Rect r, string text, GUIStyle style, string owner) { if (Event.current.type == EventType.Repaint) Notify.Hint(owner, text); }
        public static void HintContent(Rect r, GUIContent content, GUIStyle style, string owner) { if (Event.current.type == EventType.Repaint && content != null) Notify.Hint(owner, content.text); }
        public static void NoTexture(Rect r, Texture t) { }
    }
}
