using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

namespace Chicago95
{
    // The game's settings as the game keeps them (the model from Wild Wasteland's CleanHUD menu): each one is the game's
    // own UI control when the scene has it (set in game, it applies at once) and its key in SaveSettings.es3; the display
    // (resolution, screen mode, quality) is the game's Settings.xml. Works on the title screen and in game alike.
    internal sealed class Setting
    {
        public string Tab, Label, Key, Kind, Path, Fmt, FsmName, OnState;
        public float Min, Max, Def;
        public bool TitleOnly, GameOnly;
    }

    internal static class GameSettings
    {
        public static readonly string[] Tabs = { "Graphics", "Audio", "Gameplay", "Player & Car", "Cheats" };
        public static readonly List<Setting> All = new List<Setting>();

        static GameSettings()
        {
            F("Graphics", "Field of view", "FOV", 40, 120, 75, "Graphics_Canvas/Graphics/fov/FOV", "0");
            B("Graphics", "Ambient occlusion", "ambient_occlusion", true, "Graphics_Canvas/Graphics/ambient occlusion/ambient_occlusion");
            B("Graphics", "Bloom", "bloom", true, "Graphics_Canvas/Graphics/bloom/bloom");
            B("Graphics", "Colour grading", "color_grading", true, "Graphics_Canvas/Graphics/color grading/color_grading");
            B("Graphics", "Screen space shadows", "screen_space_shadows", true, "Graphics_Canvas/Graphics/screen space shadows/screen_space_shadows");
            B("Graphics", "Sharpen", "sharpen", true, "Graphics_Canvas/Graphics/sharpen/sharpen");
            B("Graphics", "Show FPS", "toggleFPS", false, "Graphics_Canvas/Graphics/show fps/show_fps");
            F("Audio", "Master volume", "audio_volume", 0, 1, .3f, "Audio_Canvas/Audio/audio volume/audio_volume", "%");
            F("Audio", "Ambient music", "ambient_music", 0, 1, .3f, "Audio_Canvas/Audio/ambient music/ambient_music", "%");
            F("Audio", "Ambient sounds", "ambient_volume", 0, 1, .15f, "Audio_Canvas/Audio/ambient volume/ambient_volume", "%");
            F("Audio", "Boss fight music", "boss_music", 0, 1, .3f, "Audio_Canvas/Audio/boss music/boss_music", "%");
            F("Audio", "Engine sound", "enginesoundvolume", 0, 1, .3f, "Audio_Canvas/Audio/engine sound volume/enginesoundvolume", "%");
            B("Audio", "Footstep sounds", "toggleFootstepSounds", true, "Audio_Canvas/Audio/footstep sounds/footstep_sounds");
            F("Audio", "Menu music", "main_menu_music", 0, 1, .3f, "Canvas_MM/MainMenu/Music", "%").TitleOnly = true;
            B("Gameplay", "Disable fatigue", "disableFatigue", false, "Gameplay_Canvas/Gameplay/disable fatigue/disable_fatigue");
            B("Gameplay", "Disable thirst and hunger", "disableThirstHunger", false, "Gameplay_Canvas/Gameplay/disable thirst hunger/disable_thirst_hunger");
            B("Gameplay", "Disable insanity", "disableInsanity", false, "Gameplay_Canvas/Gameplay/disable insanity/disable_insanity");
            B("Gameplay", "Disable sandstorms", "disableSandstorm", false, "Gameplay_Canvas/Gameplay/disable sandstorm/disable_sandstorm");
            F("Player & Car", "Mouse sensitivity X", "mouse_sensy_x", -10, 10, 2, "PlayerCar_Canvas/PlayerCar/mouse sensy x/mouse_sensy_x", "0.0");
            F("Player & Car", "Mouse sensitivity Y", "mouse_sensy_y", -10, 10, 2, "PlayerCar_Canvas/PlayerCar/mouse sensy y/mouse_sensy_y", "0.0");
            F("Player & Car", "Item rotate speed", "itemRotateSpeed", -10, 10, 2, "PlayerCar_Canvas/PlayerCar/item rotate/item_rotate", "0.0");
            B("Player & Car", "Toggle aim down sights", "toggleADS", false, "PlayerCar_Canvas/PlayerCar/aim down sights/aim_down_sights");
            B("Player & Car", "Head bob", "toggleHeadbob", true, "PlayerCar_Canvas/PlayerCar/headbob/headbob");
            F("Player & Car", "Steering speed", "steeringspeed", 10, 300, 50, "PlayerCar_Canvas/PlayerCar/steeringspeed/steeringspeed", "0");
            B("Player & Car", "Automatic gearbox", "automatic_gearbox", true, "PlayerCar_Canvas/PlayerCar/automatic gearbox/automatic_gearbox");
            B("Player & Car", "Auto clutch", "autoclutch", true, "PlayerCar_Canvas/PlayerCar/autoclutch/autoclutch");
            B("Player & Car", "H-shifter", "hshifter", false, "PlayerCar_Canvas/PlayerCar/hshifter/hshifter");
            B("Player & Car", "Smooth input", "smoothinput", true, "PlayerCar_Canvas/PlayerCar/smoothinput/smoothinput");
            B("Player & Car", "Normalise input", "normalizeinput", false, "PlayerCar_Canvas/PlayerCar/normalizeinput/normalizeinput");
            B("Cheats", "Peaceful mode", "peacefulMode", false, "Gameplay_Canvas/Gameplay/peaceful mode/peaceful_mode");
            B("Cheats", "Map cheat (the whole map)", "mapCheat", false, "Gameplay_Canvas/Gameplay/map cheat/map_cheat");
            B("Cheats", "Perma base (hidden game option)", "permaBase", false, "Gameplay_Canvas/Gameplay/perma base/perma_base");
            var gm = D("Cheats", "God mode: fly, no clip (F11)", null, "fsm"); gm.Path = "Player"; gm.FsmName = "GODMODE"; gm.OnState = "active"; gm.GameOnly = true;
            var gh = D("Cheats", "God hands (F10)", null, "fsm"); gh.Path = "PlayerCameraHolder/PlayerCamera"; gh.FsmName = "GodHands"; gh.OnState = "on"; gh.GameOnly = true;
        }
        private static Setting D(string tab, string label, string key, string kind) { var d = new Setting { Tab = tab, Label = label, Key = key, Kind = kind }; All.Add(d); return d; }
        private static Setting B(string tab, string label, string key, bool def, string path) { var d = D(tab, label, key, "bool"); d.Def = def ? 1 : 0; d.Path = path; return d; }
        private static Setting F(string tab, string label, string key, float min, float max, float def, string path, string fmt)
        { var d = D(tab, label, key, "float"); d.Min = min; d.Max = max; d.Def = def; d.Path = path; d.Fmt = fmt; return d; }

        private static ES3Settings FileSet() { return new ES3Settings("SaveSettings.es3", ES3.Location.File); }
        private static ES3Settings CacheSet() { return new ES3Settings("SaveSettings.es3", ES3.Location.Cache); }

        private static PlayMakerFSM FsmOf(Setting d)
        {
            var t = TitleMenu.FindPath(d.Path);
            if (t != null) foreach (var f in t.GetComponents<PlayMakerFSM>()) if (f.FsmName == d.FsmName) return f;
            return null;
        }

        public static float Read(Setting d)
        {
            if (d.Kind == "fsm") { var f = FsmOf(d); return f != null && f.ActiveStateName == d.OnState ? 1 : 0; }
            var t = TitleMenu.FindPath(d.Path);
            if (t != null)
            {
                if (d.Kind == "bool") { var tg = t.GetComponent<Toggle>(); if (tg != null) return tg.isOn ? 1 : 0; }
                else { var sl = t.GetComponent<Slider>(); if (sl != null) return sl.value; }
            }
            try
            {
                var fs = FileSet();
                if (ES3.KeyExists(d.Key, fs)) return d.Kind == "bool" ? (ES3.Load<bool>(d.Key, fs) ? 1 : 0) : ES3.Load<float>(d.Key, fs);
            }
            catch (Exception e) { Plugin.Log.LogWarning("Settings: reading " + d.Key + ": " + e.Message); }
            return d.Def;
        }

        public static void Write(Setting d, float v)
        {
            if (d.Kind == "fsm")
            {
                var f = FsmOf(d);
                if (f == null || !f.isActiveAndEnabled) return;
                bool on = f.ActiveStateName == d.OnState;
                if (on != v > .5f) f.SendEvent(on ? "back" : "next");   // the same transitions its F key fires
                return;
            }
            var t = TitleMenu.FindPath(d.Path);
            if (t != null)
            {
                if (d.Kind == "bool") { var tg = t.GetComponent<Toggle>(); if (tg != null && tg.isOn != (v > .5f)) tg.isOn = v > .5f; }
                else { var sl = t.GetComponent<Slider>(); if (sl != null) sl.value = v; }
            }
            try
            {
                if (d.Kind == "bool") { ES3.Save<bool>(d.Key, v > .5f, FileSet()); var cs = CacheSet(); if (ES3.FileExists(cs)) ES3.Save<bool>(d.Key, v > .5f, cs); }
                else { ES3.Save<float>(d.Key, v, FileSet()); var cs = CacheSet(); if (ES3.FileExists(cs)) ES3.Save<float>(d.Key, v, cs); }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Settings: writing " + d.Key + ": " + e.Message); }
        }

        public static string Format(Setting d, float v)
        {
            if (d.Fmt == "%") return Mathf.RoundToInt(v * 100f).ToString(CultureInfo.InvariantCulture) + "%";
            return v.ToString(d.Fmt ?? "0.0", CultureInfo.InvariantCulture);
        }
    }

    // the display: the game's Settings.xml (screenWidth, screenHeight, screenMode, qualityLevel)
    internal sealed class DisplaySettings
    {
        public static readonly string[] ModeNames = { "Full screen", "Borderless", "Maximised", "Windowed" };
        public int W, H, Mode, Quality;
        public readonly List<Vector2Int> Res = new List<Vector2Int>();
        private static string Xml => Path.Combine(Application.persistentDataPath, "Settings.xml");

        public static DisplaySettings Load()
        {
            var d = new DisplaySettings { W = Screen.width, H = Screen.height, Mode = (int)Screen.fullScreenMode, Quality = QualitySettings.GetQualityLevel() };
            try
            {
                if (File.Exists(Xml))
                {
                    var x = File.ReadAllText(Xml);
                    d.W = Tag(x, "screenWidth", d.W); d.H = Tag(x, "screenHeight", d.H); d.Mode = Tag(x, "screenMode", d.Mode); d.Quality = Tag(x, "qualityLevel", d.Quality);
                }
            }
            catch { }
            foreach (var r in Screen.resolutions) { var v = new Vector2Int(r.width, r.height); if (!d.Res.Contains(v)) d.Res.Add(v); }
            var cur = new Vector2Int(d.W, d.H);
            if (!d.Res.Contains(cur)) d.Res.Add(cur);
            d.Res.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            d.Mode = Mathf.Clamp(d.Mode, 0, 3);
            d.Quality = Mathf.Clamp(d.Quality, 0, Mathf.Max(0, QualitySettings.names.Length - 1));
            return d;
        }
        private static int Tag(string x, string tag, int def)
        {
            var m = Regex.Match(x, "<" + tag + ">\\s*(-?\\d+)\\s*</" + tag + ">");
            return m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : def;
        }
        public void Apply()
        {
            Screen.SetResolution(W, H, (FullScreenMode)Mode);
            QualitySettings.SetQualityLevel(Quality, true);
            try
            {
                File.WriteAllText(Xml, "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<Settings xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">\r\n" +
                    "  <screenWidth>" + W + "</screenWidth>\r\n  <screenHeight>" + H + "</screenHeight>\r\n  <screenMode>" + Mode + "</screenMode>\r\n  <qualityLevel>" + Quality + "</qualityLevel>\r\n</Settings>");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Settings.xml not written: " + e.Message); }
        }
        public string ResText => W + " x " + H;
        public string QualityText { get { var n = QualitySettings.names; return Quality >= 0 && Quality < n.Length ? n[Quality] : Quality.ToString(); } }
    }
}
