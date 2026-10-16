using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Chicago95
{
    // Apoca-95: the game's UI, and other mods' UI, in the Windows 95 look of the Chicago95 desktop theme. The game's
    // menus, settings, loading screen, Tab screen and HUD are drawn anew (IMGUI) from the game's own state; other mods'
    // windows are patched at runtime (Harmony), their DLLs left as they are. Each part is its own switch; a mod's patch
    // does nothing when that mod isn't loaded.
    [BepInPlugin(GUID, NAME, VERSION)]
    [BepInDependency(WildWasteland, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(Apocasetter, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(EngineSim, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(Apocasaver, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(Apocaspawner, BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string GUID = "com.burner.apocalypter.chicago95", NAME = "Apoca-95", VERSION = "1.2.0";
        public const string WildWasteland = "com.denis.apocalypter.wildwasteland", Apocasetter = "com.denis.apocalypter.apocasetter", EngineSim = "com.burner.apocalypter.enginesim", Apocasaver = "com.denis.apocalypter.apocasaver", Apocaspawner = "com.denis.apocalypter.apocaspawner";
        internal static ManualLogSource Log;
        internal static GameObject Runner;

        private void Awake()
        {
            Log = Logger;
            Config.Bind("General", "Apocasetter", true, "Shown in the Mods menu.");
            var ww = Config.Bind("Patches", "WildWasteland", true, "Wild Wasteland's windows in the Windows 95 look. Restart to apply.");
            var bios = Config.Bind("Patches", "LoadingScreen", true, "Loading and saving progress, log and tip in one Windows 95 dialog. Restart to apply.");
            var setter = Config.Bind("Patches", "Apocasetter", true, "The Mods window as a Windows 95 dialog. Restart to apply.");
            var title = Config.Bind("Patches", "TitleMenu", true, "The title screen's menu as a Windows 95 window. Restart to apply.");
            var pause = Config.Bind("Patches", "PauseMenu", true, "The pause menu as a Windows 95 window over the paused game. Restart to apply.");
            var engine = Config.Bind("Patches", "EngineSim", true, "Engine Sim's windows in the bitmap font and the theme's title bars. Restart to apply.");
            var spawner = Config.Bind("Patches", "Apocaspawner", true, "Apocaspawner's window in the Windows 95 look. Restart to apply.");
            var notify = Config.Bind("Patches", "Notifications", true, "Every mod's HUD messages and hints as Windows tooltips in one place. Restart to apply.");
            var hud = Config.Bind("Patches", "Hud", true, "The HUD as a Windows 95 taskbar. Restart to apply.");
            var sheet = Config.Bind("Patches", "PlayerSheet", true, "The Tab screen as a Windows 95 window. Restart to apply.");
            var prompt = Config.Bind("Patches", "Console", true, "An MS-DOS Prompt on the backquote key: the log, and Lua with SigmaMod. Restart to apply.");

            // the windows run on an object of their own that lasts the session: BepInEx's manager object, which carries
            // the plugin itself, can be gone soon after the game starts, and everything on it with it
            Runner = new GameObject("Apoca-95");
            DontDestroyOnLoad(Runner);
            Runner.hideFlags = HideFlags.HideAndDontSave;

            var harmony = new Harmony(GUID);
            if (Chainloader.PluginInfos.ContainsKey(WildWasteland))
            {
                if (ww.Value) Try("Wild Wasteland windows", () => WwTheme.Install(harmony));
                if (bios.Value) Try("Wild Wasteland loading screen", () => WwLoading.Install(harmony, Runner));
            }
            else Log.LogInfo("Wild Wasteland not loaded: its patches are off");
            if (bios.Value) Try("Loading dialog", () => { LoadingDialog.Install(Runner); LoadingInfo.Install(harmony); });   // also without Wild Wasteland
            if (Chainloader.PluginInfos.ContainsKey(Apocasetter))
            {
                if (setter.Value) Try("Apocasetter Mods window", () => { SetterTheme.Install(harmony); ModsWindow.Install(harmony); SetterNotice.Install(harmony); });
            }
            else Log.LogInfo("Apocasetter not loaded: its patch is off");
            if (spawner.Value && Chainloader.PluginInfos.ContainsKey(Apocaspawner)) Try("Apocaspawner window", () => SpawnerTheme.Install(harmony));
            if (Chainloader.PluginInfos.ContainsKey(Apocasaver)) Try("Apocasaver save name prompt", () => SaverPrompt.Install(harmony));
            if (engine.Value && Chainloader.PluginInfos.ContainsKey(EngineSim)) Try("Engine Sim windows", () => EngineSimTheme.Install(harmony));
            if (notify.Value) Try("Notifications", () => Notify.Install(Runner, harmony));
            if (title.Value) Try("Title menu", () => TitleMenu.Install(Runner));
            if (pause.Value) Try("Pause menu", () => PauseMenu.Install(Runner));
            if (hud.Value) Try("HUD", () => HudBar.Install(Runner));
            if (sheet.Value) Try("Player sheet", () => PlayerSheet.Install(Runner));
            if (prompt.Value) Try("Console", () => DosPrompt.Install(Runner, harmony));
            if (title.Value || pause.Value) Try("Wild Wasteland's menus standing down", () => TitleMenu.InstallStandDown(harmony));
        }

        // one patch failing (another version of that mod) leaves the rest working
        private static void Try(string what, System.Action install)
        {
            try { install(); Log.LogInfo(what + ": patched"); }
            catch (System.Exception e) { Log.LogError(what + ": not patched: " + e); }
        }
    }
}
