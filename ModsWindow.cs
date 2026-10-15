using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using Apocasetter;

namespace Chicago95
{
    // Apocasetter's Mods window, drawn as a Windows 95 dialog (Ui) in place of its own layout: SettingsUI.Draw is
    // replaced, everything else of SettingsUI stays (opening and closing, the cursor and input blocking, Enter applying
    // edits, finishing slider drags, key rebinding, error recovery). Its state (the selected mod, the filter, the tab,
    // pending edits...) and its actions (status, applying and staging, downloads, installs, removals) are its own, used
    // through reflection; the mod list, index and updates come from its public Catalog and Updates.
    internal static class ModsWindow
    {
        private const float W = 720, H = 480, LeftW = 210, RightX = 226, Bottom = H - 62;
        private static SettingsUI _ui;
        private static float _ox, _oy, _listScroll, _paneScroll, _paneH = 100, _listH = 100;
        private static string _lastSelected;

        // ---------------------------------------------------------------- SettingsUI's own state and helpers
        private static AccessTools.FieldRef<SettingsUI, T> F<T>(string n) { return AccessTools.FieldRefAccess<SettingsUI, T>(n); }
        private static readonly AccessTools.FieldRef<SettingsUI, string> Selected = F<string>("_selected"), Filter = F<string>("_filter"), Tab = F<string>("_tab"),
            Search = F<string>("_search"), SettingSearch = F<string>("_settingSearch"), Status_ = F<string>("_status");
        private static readonly AccessTools.FieldRef<SettingsUI, Dictionary<ConfigEntryBase, string>> Pending = F<Dictionary<ConfigEntryBase, string>>("_pending"), Errors = F<Dictionary<ConfigEntryBase, string>>("_errors");
        private static readonly AccessTools.FieldRef<SettingsUI, ConfigEntryBase> Dropdown = F<ConfigEntryBase>("_dropdown"), Rebind = F<ConfigEntryBase>("_rebind"), SliderDrag = F<ConfigEntryBase>("_sliderDrag");
        private static readonly AccessTools.FieldRef<SettingsUI, HashSet<string>> ShowAll = F<HashSet<string>>("_showAll"), ShowOther = F<HashSet<string>>("_showOther");
        private static readonly AccessTools.FieldRef<SettingsUI, bool> RemoveOpen = F<bool>("_removeOpen"), RemoveCfg = F<bool>("_removeCfg");
        private static readonly AccessTools.FieldRef<SettingsUI, float> StatusUntil = F<float>("_statusUntil");
        private static readonly AccessTools.FieldRef<SettingsUI, Dictionary<string, List<string>>> KeyUsers = F<Dictionary<string, List<string>>>("_keyUsers");

        private static readonly Dictionary<string, MethodInfo> Methods = new Dictionary<string, MethodInfo>();
        private static object Call(string name, params object[] args)
        {
            if (!Methods.TryGetValue(name, out var m)) Methods[name] = m = AccessTools.Method(typeof(SettingsUI), name) ?? throw new MissingMethodException("SettingsUI." + name);
            return m.Invoke(m.IsStatic ? null : _ui, args);
        }
        private static T Call<T>(string name, params object[] args) { return (T)Call(name, args); }

        // its per-mod status (a private class): read by field name
#pragma warning disable 0649   // filled by reflection
        private sealed class Status
        {
            public string Sub, Badge, Staging; public bool Update, Ahead, Staged, Downloading, Blocked, NoSource, Deprecated;
        }
#pragma warning restore 0649
        private static readonly Dictionary<string, FieldInfo> StatusFields = new Dictionary<string, FieldInfo>();
        private static Status StatusOf(ModEntry m)
        {
            var s = Call("Status", m);
            var r = new Status();
            if (s == null) return r;
            foreach (var f in typeof(Status).GetFields())
            {
                if (!StatusFields.TryGetValue(f.Name, out var src)) StatusFields[f.Name] = src = s.GetType().GetField(f.Name);
                if (src != null) f.SetValue(r, src.GetValue(s));
            }
            return r;
        }

        private static readonly Type[] NumericTypes = { typeof(int), typeof(float), typeof(double), typeof(long), typeof(short), typeof(byte), typeof(decimal) };

        // ---------------------------------------------------------------- install
        public static void Install(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(SettingsUI), "Draw"), prefix: new HarmonyMethod(typeof(ModsWindow), nameof(Draw)));
        }

        private static Rect P(float x, float y, float w, float h) { int k = Ui.K; return new Rect(_ox + x * k, _oy + y * k, w * k, h * k); }

        // ---------------------------------------------------------------- the window
        private static readonly object PopOwner = new object();

        private static bool Draw(SettingsUI __instance)
        {
            _ui = __instance;
            Ui.Begin();
            Ui.PopupInput(PopOwner);   // the open drop-down takes the mouse before anything under it
            int k = Ui.K;
            _ox = Mathf.Round((Screen.width - W * k) / 2f); _oy = Mathf.Round((Screen.height - H * k) / 2f);

            var mods = Catalog.All();
            string s = Selected(_ui);
            if (string.IsNullOrEmpty(s) || !mods.Any(m => m.Guid == s))
            {
                var first = mods.FirstOrDefault(m => StatusOf(m).Update) ?? mods.FirstOrDefault();
                Selected(_ui) = s = first != null ? first.Guid : "";
            }
            string sel = s;
            var mod = mods.FirstOrDefault(m => m.Guid == s);
            if (sel != _lastSelected) { _lastSelected = sel; _paneScroll = 0; Ui.PopupClose(); }

            bool modal = RemoveOpen(_ui) && mod != null;
            var was = GUI.enabled;
            GUI.enabled = !modal;
            if (Ui.Window(P(0, 0, W, H), "Mods")) Call("Toggle");
            Toolbar(mods);
            ModList(mods);
            if (mod != null) Pane(mod);
            BottomBar(mod);
            GUI.enabled = was;
            if (modal) RemoveDialog(mod);
            Ui.PopupDraw(PopOwner);
            return false;
        }

        private static void Toolbar(List<ModEntry> mods)
        {
            float bw = Ui.ButtonWidth("Check for Updates") / Ui.K;
            if (Ui.Button(P(8, 28, bw, 23), "Check for Updates", !Updates.Checking)) _ui.StartCoroutine(Updates.Check(true));
            int updates = mods.Count(m => StatusOf(m).Update), plugins = mods.Count(m => m.Kind == ModKind.Loaded);
            string head = plugins + " plugins";
            if (Updates.Checking) head += ", checking for updates...";
            else if (Updates.HaveIndex) head += ", " + updates + (updates == 1 ? " update" : " updates") + " on GitHub (checked " + Ago(Updates.IndexTimeUtc) + ")";
            else if (Updates.CheckError != null) head += ", no connection to GitHub";
            else head += ", not checked for updates yet";
            Ui.Fit(P(16 + bw, 28, W - 24 - bw, 23), head, Updates.CheckError != null ? Win95.Hex("800000") : Color.black);
            var rule = P(8, 56, W - 16, 0); Ui.Etched(rule.x, rule.y, rule.width);
        }

        private static string Ago(DateTime utc)
        {
            var d = DateTime.UtcNow - utc;
            if (d.TotalMinutes < 1) return "just now";
            if (d.TotalMinutes < 60) return (int)d.TotalMinutes + " min ago";
            if (d.TotalHours < 48) return (int)d.TotalHours + " h ago";
            return (int)d.TotalDays + " days ago";
        }

        // ---------------------------------------------------------------- the mod list (left)
        private sealed class Group { public string Title; public List<ModEntry> Items; }

        private static void ModList(List<ModEntry> mods)
        {
            int k = Ui.K;
            ref string search = ref Search(_ui);
            GUI.SetNextControlName("apocasetter-search");
            search = Ui.TextField(P(8, 62, LeftW, 21), search, 40, "Find a mod");

            ref string filter = ref Filter(_ui);
            int nUpd = mods.Count(m => Call<bool>("IsUpdateListed", m)), nOther = mods.Count(m => m.Other || m.Kind == ModKind.Disabled);
            var chips = new[] { new KeyValuePair<string, string>("all", "All " + mods.Count(m => m.Kind != ModKind.Available)),
                                new KeyValuePair<string, string>("updates", "Updates " + nUpd), new KeyValuePair<string, string>("other", "Other " + nOther) };
            float cw = (LeftW - 4) / 3f;
            for (int i = 0; i < chips.Length; i++)
                if (Ui.Button(P(8 + i * (cw + 2), 88, cw, 21), chips[i].Value, true, false, filter == chips[i].Key)) { filter = chips[i].Key; _listScroll = 0; }

            var q = search.Trim().ToLowerInvariant();
            Func<ModEntry, bool> match = m => q.Length == 0 || (m.Name ?? "").ToLowerInvariant().Contains(q);
            var groups = new List<Group>();
            if (filter == "updates")
            {
                groups.Add(new Group { Title = "Updates", Items = mods.Where(m => m.Kind == ModKind.Loaded && Call<bool>("IsUpdateListed", m)).Where(match).ToList() });
                groups.Add(new Group { Title = "On GitHub", Items = mods.Where(m => m.Kind == ModKind.Available).Where(match).ToList() });
            }
            else if (filter == "other")
            {
                groups.Add(new Group { Title = "Other plugins", Items = mods.Where(m => m.Other).Where(match).ToList() });
                groups.Add(new Group { Title = "Disabled", Items = mods.Where(m => m.Kind == ModKind.Disabled).Where(match).ToList() });
            }
            else
            {
                groups.Add(new Group { Title = "Mods", Items = mods.Where(m => m.Kind == ModKind.Loaded && m.OptedIn).Where(match).ToList() });
                groups.Add(new Group { Title = "Other plugins", Items = mods.Where(m => m.Other).Where(match).ToList() });
                groups.Add(new Group { Title = "Disabled", Items = mods.Where(m => m.Kind == ModKind.Disabled).Where(match).ToList() });
                groups.Add(new Group { Title = "On GitHub", Items = mods.Where(m => m.Kind == ModKind.Available).Where(match).ToList() });
            }
            groups = groups.Where(g => g.Items.Count > 0).ToList();

            var list = P(8, 114, LeftW, Bottom - 4 - 114);
            Ui.Field(list);
            var view = new Rect(list.x + 2 * k, list.y + 2 * k, list.width - 4 * k, list.height - 4 * k);
            float w = Ui.BeginScroll(view, ref _listScroll, _listH);
            float y = 0, RH = 18 * k;
            if (groups.Count == 0) { Ui.Text(new Rect(4 * k, 2 * k, w, RH), "No mod matches.", Win95.Shadow); y = RH; }
            foreach (var g in groups)
            {
                Ui.Text(new Rect(4 * k, y, w - 8 * k, RH), g.Title, Color.black, TextAnchor.MiddleLeft, true);
                y += RH;
                foreach (var m in g.Items) { ListRow(new Rect(0, y, w, RH), m); y += RH; }
            }
            _listH = y + 2 * k;
            Ui.EndScroll();
        }

        private static void ListRow(Rect r, ModEntry m)
        {
            int k = Ui.K;
            bool sel = m.Guid == Selected(_ui);
            if (Ui.Row(r, sel) && !sel) Select(m);
            var ink = sel ? Color.white : m.Kind == ModKind.Loaded ? Color.black : Win95.Shadow;
            Icon(new Rect(r.x + 4 * k, r.y + k, 16 * k, 16 * k), m, ink);
            var st = StatusOf(m);
            float bw = 0;
            if (st.Badge != null)
            {
                string badge = Short(st.Badge);
                bw = (Ui.Font(false).Width(badge) + 6) * k;
                Ui.Text(new Rect(r.xMax - bw, r.y, bw - 3 * k, r.height), badge, sel ? Color.white : BadgeInk(st), TextAnchor.MiddleRight);
            }
            Ui.Fit(new Rect(r.x + 24 * k, r.y, r.width - 28 * k - bw, r.height), m.Name, ink);
        }

        // the list's badge in a few words
        private static string Short(string badge)
        {
            if (badge.StartsWith("↑")) return "Update";
            switch (badge)
            {
                case "BLOCKED": return "Blocked";
                case "DEPRECATED": return "Old";
                case "DOWNLOADING": return "Getting";
                case "REMOVING": return "Removing";
                case "DISABLING": return "Disabling";
                case "ON RESTART": return "Restart";
            }
            return badge.StartsWith("NEEDS") ? "Needs" : badge;
        }
        private static Color BadgeInk(Status st) { return st.Blocked || st.Staging == "remove" || st.Staging == "disable" || (st.Badge ?? "").StartsWith("NEEDS") ? Win95.Hex("800000") : Win95.Navy; }

        private static void Icon(Rect r, ModEntry m, Color ink)
        {
            var tex = m.Icon;
            if (tex != null) { var old = GUI.color; GUI.color = ink; GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit); GUI.color = old; }
            else Ui.Text(r, m.Initials, ink, TextAnchor.MiddleCenter, true);
        }

        private static void Select(ModEntry m)
        {
            Call("ApplyPending");
            Selected(_ui) = m.Guid; Dropdown(_ui) = null; SettingSearch(_ui) = "";
            if (m.Kind != ModKind.Loaded && Tab(_ui) == "settings") Tab(_ui) = "about";
        }

        // ---------------------------------------------------------------- the selected mod (right)
        private static void Pane(ModEntry m)
        {
            int k = Ui.K;
            var st = StatusOf(m);
            var im = m.Index;
            float x0 = RightX, w0 = W - 8 - RightX;

            // header: icon, name and version, what it is; its buttons on the right
            var iconBox = P(x0, 62, 34, 34);
            Ui.Field(iconBox);
            Icon(new Rect(iconBox.x + 3 * k, iconBox.y + 3 * k, 28 * k, 28 * k), m, Color.black);
            var buttons = new List<KeyValuePair<string, Action>>();
            if (m.Kind == ModKind.Loaded && !m.Self && !st.Staged) buttons.Add(new KeyValuePair<string, Action>("Remove...", () => { RemoveOpen(_ui) = true; RemoveCfg(_ui) = false; }));
            if (m.Kind == ModKind.Available && !st.Staged && !st.Downloading && im != null && im.Zip != null) buttons.Add(new KeyValuePair<string, Action>("Install " + im.Version, () => Call("Install", m)));
            if (m.Kind == ModKind.Disabled && !st.Staged) buttons.Add(new KeyValuePair<string, Action>("Enable", () => { Call("StageSimple", m, "enable", null, false); if (im != null) Call("ProvideRequirements", m, im); }));
            if (im != null && !string.IsNullOrEmpty(im.Repo)) buttons.Add(new KeyValuePair<string, Action>("GitHub", () => Application.OpenURL("https://github.com/" + im.Repo)));
            float bx = x0 + w0;
            foreach (var b in buttons)
            {
                float bw = Ui.ButtonWidth(b.Key) / k;
                bx -= bw;
                if (Ui.Button(P(bx, 62, bw, 23), b.Key, b.Key == "GitHub" || Updates.InstallerPresent)) b.Value();
                bx -= 6;
            }
            string ver = m.Kind == ModKind.Available ? "not installed" : m.Kind == ModKind.Disabled ? "v" + m.Version + ", disabled" : "v" + m.Version;
            float nameW = Ui.Font(true).Width(m.Name ?? "");
            float room = bx - (x0 + 42) - 4;
            Ui.Fit(P(x0 + 42, 62, Mathf.Min(nameW, room), 16), m.Name, Color.black, TextAnchor.MiddleLeft, true);
            if (nameW + 8 < room) Ui.Fit(P(x0 + 42 + nameW + 6, 62, room - nameW - 6, 16), ver, Win95.Navy);
            var meta = new List<string>();
            if (im != null && !string.IsNullOrEmpty(im.Repo)) meta.Add("github.com/" + im.Repo);
            else if (m.Kind == ModKind.Loaded) meta.Add("no GitHub repo known");
            if (im != null && !string.IsNullOrEmpty(im.Author)) meta.Add("by " + im.Author);
            if (m.Config != null) meta.Add(Path.GetFileName(m.Config.ConfigFilePath));
            if (m.Other) meta.Add("GUID " + m.Guid);
            Ui.Fit(P(x0 + 42, 80, room, 16), string.Join(", ", meta.ToArray()), Win95.Shadow);

            // notices
            float y = 102;
            y += Banner(x0, y, w0, m, st, im);
            if (st.Deprecated && im != null) y += Deprecated(x0, y, w0, m, im);
            y += Takeover(x0, y, w0, m, im);
            y += Requirements(x0, y, w0, m, im);

            // property sheet
            if (m.Kind != ModKind.Loaded && Tab(_ui) == "settings") Tab(_ui) = "about";
            var keys = new List<string>(); var labels = new List<string>();
            if (m.Kind == ModKind.Loaded) { keys.Add("settings"); labels.Add("Settings (" + m.Entries.Count + ")"); }
            keys.Add("about"); labels.Add("About"); keys.Add("notes"); labels.Add("What's New"); keys.Add("files"); labels.Add("Files");
            int cur = Mathf.Max(0, keys.IndexOf(Tab(_ui)));
            var tabsR = P(x0, y, w0, 20);
            if (Tab(_ui) == "settings" && m.Kind == ModKind.Loaded && m.Entries.Count > 4)
            {
                ref string ss = ref SettingSearch(_ui);
                ss = Ui.TextField(P(x0 + w0 - 150, y - 1, 150, 20), ss, 40, "Find a setting", "apocasetter-findsetting");
            }
            // the sheet under the tabs; on it, inset, the page: sunken white like the mod list, its scrollbar inside it
            var sheet = P(x0, y + 20, w0, Bottom - 4 - (y + 20));
            Ui.Sheet(sheet);
            int next = Ui.Tabs(tabsR, labels, cur);
            if (next != cur) { Tab(_ui) = keys[next]; _paneScroll = 0; }
            var page = new Rect(sheet.x + 6 * k, sheet.y + 8 * k, sheet.width - 12 * k, sheet.height - 14 * k);
            Ui.Field(page);
            var view = new Rect(page.x + 2 * k, page.y + 2 * k, page.width - 4 * k, page.height - 4 * k);
            float was = _paneScroll;
            float cw = Ui.BeginScroll(view, ref _paneScroll, _paneH, 6 * k);
            if (_paneScroll != was) Ui.PopupClose();   // its box moved
            float h;
            try
            {
                switch (Tab(_ui))
                {
                    case "about": h = About(cw, m, im); break;
                    case "notes": h = Notes(cw, im); break;
                    case "files": h = Files(cw, m); break;
                    default: h = Settings(cw, m, im); break;
                }
            }
            finally { Ui.EndScroll(); }
            _paneH = h;
        }

        // a notice: pale yellow, a bold first line, a line of detail, an action button; returns the height used + gap
        private static float NoticeBox(float x, float y, float w, string title, string sub, Color titleInk, string button, Action act, bool buttonEnabled = true)
        {
            int k = Ui.K;
            var r = P(x, y, w, 40);
            Ui.Notice(r);
            float bw = button != null ? Ui.ButtonWidth(button) / k : 0;
            Ui.Fit(P(x + 6, y + 4, w - 18 - bw, 15), title, titleInk, TextAnchor.MiddleLeft, true);
            Ui.Fit(P(x + 6, y + 20, w - 18 - bw, 15), sub ?? "", Color.black);
            if (button != null && act != null && Ui.Button(P(x + w - 8 - bw, y + 8, bw, 23), button, buttonEnabled)) act();
            return 46;
        }

        private static readonly Color Red = Win95.Hex("800000"), Green = Win95.Hex("006400");

        private static float Banner(float x, float y, float w, ModEntry m, Status st, IndexMod im)
        {
            int k = Ui.K;
            if (Updates.Downloading.TryGetValue(m.Guid, out float prog))
            {
                var r = P(x, y, w, 40);
                Ui.Notice(r);
                Ui.Fit(P(x + 6, y + 4, w - 12, 15), "Downloading " + (im != null && im.Zip != null ? im.Zip.Name : "") + "...", Color.black, TextAnchor.MiddleLeft, true);
                var bar = P(x + 6, y + 22, w - 12, 14);
                Win95.Fill(bar, Win95.Face); Ui.Sunken(bar);
                Win95.Fill(new Rect(bar.x + 2 * k, bar.y + 2 * k, (bar.width - 4 * k) * Mathf.Clamp01(prog), bar.height - 4 * k), Win95.Navy);
                return 46;
            }
            var op = Updates.OpFor(m.Guid);
            if (op != null)
            {
                var kind = MiniJson.Str(op, "op");
                bool good = kind == "update" || kind == "install" || kind == "enable";
                string title = kind == "update" ? "Downloaded: installs on the next start" : kind == "install" ? "Installs on the next start"
                             : kind == "enable" ? "Enabled on the next start" : kind == "disable" ? "Disabled on the next start" : "Removed on the next start";
                string sub = kind == "update" ? "v" + m.Version + " keeps running until you quit. Old files are backed up; settings stay."
                           : kind == "install" ? m.Name + " " + MiniJson.Str(op, "version") + " is added before BepInEx loads plugins."
                           : kind == "enable" ? "Its files go back into BepInEx before plugins load."
                           : "It runs until you quit; the files go to BepInEx\\cache\\Apocasetter\\" + (kind == "disable" ? "disabled" : "removed") + "\\.";
                return NoticeBox(x, y, w, title, sub, good ? Green : Red, "Undo", () => Updates.Unstage(m.Guid));
            }
            if (Updates.Errors.TryGetValue(m.Guid, out string err))
                return NoticeBox(x, y, w, "Download failed", err, Red, im != null && im.Zip != null ? "Try Again" : null, () => Call("Download", m, im), Updates.InstallerPresent);
            if (st.Blocked)
                return NoticeBox(x, y, w, "Blocked by the Apocasetter index", string.IsNullOrEmpty(im.BlockedReason) ? "This mod was withdrawn from the index. Removing it is recommended." : im.BlockedReason, Red, null, null);
            if (st.Update && im != null)
            {
                string title = "Update available: " + m.Version + " to " + im.Version;
                if (im.Zip != null)
                {
                    var need = Call<List<string>>("MissingRequirements", im);
                    string sub = "Released " + Call<string>("When", im.Published) + ", " + Call<string>("Size", im.Zip.Size) + ", installs on the next start, settings kept";
                    if (need.Count > 0) sub = "Released " + Call<string>("When", im.Published) + ", also installs " + string.Join(", ", need.Select(g => Call<string>("GuidName", g)).ToArray());
                    return NoticeBox(x, y, w, title, sub, Win95.Navy, "Update", () => { Call("Download", m, im); Call("ProvideRequirements", m, im); }, Updates.InstallerPresent);
                }
                return NoticeBox(x, y, w, title, "This release has no .zip attached, so it can't be installed from here.", Win95.Navy, "Release Page", () => Application.OpenURL(im.Page));
            }
            if (st.Ahead && im != null)
            {
                Ui.Fit(P(x, y, w, 16), "Your copy (" + m.Version + ") is newer than the latest GitHub release (" + im.Version + ").", Win95.Shadow);
                return 20;
            }
            if (m.Kind == ModKind.Disabled)
            {
                Ui.Fit(P(x, y, w, 16), "Disabled: its files are kept in BepInEx\\cache\\Apocasetter\\disabled\\.", Win95.Shadow);
                return 20;
            }
            return 0;
        }

        private static float Requirements(float x, float y, float w, ModEntry m, IndexMod im)
        {
            if (m.Kind != ModKind.Loaded || im == null) return 0;
            var missing = Call<List<string>>("MissingRequirements", im);
            if (missing.Count == 0) return 0;
            var names = missing.Select(g => Call<string>("GuidName", g)).ToArray();
            string sub = m.Name + " can't work without " + string.Join(", ", names) + ".";
            var provide = missing.FirstOrDefault(g => { var x2 = Catalog.Find(g); var ix = Updates.Find(g); return x2 != null && (x2.Kind == ModKind.Disabled || (x2.Kind == ModKind.Available && ix != null && ix.Zip != null)); });
            string button = null;
            if (provide != null) button = (Catalog.Find(provide).Kind == ModKind.Disabled ? "Enable " : "Install ") + Catalog.Find(provide).Name;
            else sub += " It isn't in the index; install it by hand.";
            return NoticeBox(x, y, w, "Needs " + string.Join(", ", names), sub, Red, button, () => Call("ProvideRequirement", provide), Updates.InstallerPresent);
        }

        private static float Takeover(float x, float y, float w, ModEntry m, IndexMod im)
        {
            ModEntry old = null; string sub = null;
            if (m.Kind == ModKind.Loaded && Updates.OpFor(m.Guid) == null)
            {
                var newer = Call<List<ModEntry>>("InstalledTakingOver", m);
                if (newer.Count > 0) { old = m; sub = string.Join(", ", newer.Select(n => n.Name).ToArray()) + " takes over its features; with both installed they apply twice."; }
                else
                {
                    var olds = Call<List<ModEntry>>("TakesOverInstalled", im);
                    if (olds.Count > 0) { old = olds[0]; sub = m.Name + " takes over " + old.Name + "'s features; with both installed they apply twice."; }
                }
            }
            else if (m.Kind == ModKind.Available && Updates.OpFor(m.Guid) == null)
            {
                var olds = Call<List<ModEntry>>("TakesOverInstalled", im);
                if (olds.Count == 0) return 0;
                Ui.Fit(P(x, y, w, 16), "Installing it also disables " + string.Join(", ", olds.Select(o => o.Name).ToArray()) + " at the next start.", Win95.Shadow);
                return 20;
            }
            if (old == null) return 0;
            var target = old;
            return NoticeBox(x, y, w, "Runs twice with " + (target == m ? "a newer mod" : target.Name), sub, Red, "Disable " + target.Name, () => Call("StageSimple", target, "disable", null, false), Updates.InstallerPresent);
        }

        private static float Deprecated(float x, float y, float w, ModEntry m, IndexMod im)
        {
            var repl = im.ReplacedBy.Where(g => !string.IsNullOrEmpty(g) && g != m.Guid).Take(4).ToList();
            string sub = string.IsNullOrEmpty(im.DeprecatedReason) ? "No longer developed: it keeps working, but gets no fixes." : im.DeprecatedReason;
            if (repl.Count > 0) sub += " Taken over by " + string.Join(", ", repl.Select(g => Call<string>("GuidName", g)).ToArray()) + ".";
            var installable = repl.Select(g => Catalog.Find(g)).Where(x2 => x2 != null && x2.Kind == ModKind.Available && Updates.OpFor(x2.Guid) == null
                                                                         && !Updates.Downloading.ContainsKey(x2.Guid) && x2.Index != null && x2.Index.Zip != null).ToList();
            return NoticeBox(x, y, w, "Deprecated", sub, Win95.Hex("804000"), installable.Count > 0 ? "Install " + (installable.Count > 1 ? "All" : installable[0].Name) : null,
                () => { foreach (var x2 in installable) Call("Install", x2); }, Updates.InstallerPresent);
        }

        // ---------------------------------------------------------------- tab: settings
        private static float Settings(float cw, ModEntry m, IndexMod im)
        {
            int k = Ui.K;
            float y = 0;
            if (m.Other && !ShowOther(_ui).Contains(m.Guid))
            {
                y += Ui.Wrap(new Rect(0, y, cw, 0), "This plugin doesn't list itself in Apocasetter (no \"Apocasetter = true\" in its config). Its settings can still be shown, read straight from its config file.", Color.black) + 8 * k;
                if (Ui.Button(new Rect(0, y, Ui.ButtonWidth("Show Anyway"), 23 * k), "Show Anyway")) ShowOther(_ui).Add(m.Guid);
                return y + 23 * k;
            }
            if (m.Config == null || m.Entries.Count == 0) return Ui.Wrap(new Rect(0, 0, cw, 0), "This plugin has no settings.", Color.black);
            if (KeyUsers(_ui) == null) KeyUsers(_ui) = Call<Dictionary<string, List<string>>>("BuildKeyUsers");

            var q = SettingSearch(_ui).Trim().ToLowerInvariant();
            var sections = new List<KeyValuePair<string, List<ConfigEntryBase>>>();
            foreach (var e in m.Entries)
            {
                var sec = e.Definition.Section;
                var list = sections.FirstOrDefault(kv => kv.Key == sec).Value;
                if (list == null) { list = new List<ConfigEntryBase>(); sections.Add(new KeyValuePair<string, List<ConfigEntryBase>>(sec, list)); }
                list.Add(e);
            }
            int shown = 0;
            foreach (var kv in sections)
            {
                var rows = kv.Value.Where(e => q.Length == 0 || e.Definition.Key.ToLowerInvariant().Contains(q) || Call<string>("Human", e.Definition.Key).ToLowerInvariant().Contains(q)
                                               || Call<string>("Desc", e).ToLowerInvariant().Contains(q) || kv.Key.ToLowerInvariant().Contains(q)).ToList();
                if (rows.Count == 0) continue;
                // the section: its name, an etched line to the right
                float tw = Ui.Font(true).Width(kv.Key) * k;
                Ui.Text(new Rect(0, y, tw + 2 * k, 16 * k), kv.Key, Color.black, TextAnchor.MiddleLeft, true);
                Ui.Etched(tw + 8 * k, y + 8 * k, cw - tw - 8 * k);
                y += 20 * k;
                bool collapsed = rows.Count > 40 && q.Length == 0 && !ShowAll(_ui).Contains(m.Guid + "|" + kv.Key);
                if (collapsed)
                {
                    Ui.Text(new Rect(0, y, cw - 90 * k, 23 * k), rows.Count + " entries: too many to list here; they're in the .cfg file.", Win95.Shadow);
                    if (Ui.Button(new Rect(cw - 80 * k, y, 80 * k, 23 * k), "Show All")) ShowAll(_ui).Add(m.Guid + "|" + kv.Key);
                    y += 31 * k;
                    continue;
                }
                foreach (var e in rows) { y = Entry(cw, y, m, e); shown++; }
                y += 6 * k;
            }
            if (shown == 0) y += Ui.Wrap(new Rect(0, y, cw, 0), "No setting matches.", Win95.Shadow);
            return y;
        }

        // one setting: its name and description on the left, its control on the right; returns the next y
        private static float Entry(float cw, float y, ModEntry m, ConfigEntryBase e)
        {
            int k = Ui.K;
            var t = e.SettingType;
            bool isKey = Call<bool>("IsKeyEntry", e);
            var range = new object[] { e, null, null };
            bool ranged = NumericTypes.Contains(t) && Call<bool>("Range", range);
            var pending = Pending(_ui);
            bool isPending = pending.TryGetValue(e, out string pend);
            Errors(_ui).TryGetValue(e, out string err);
            var def = e.DefaultValue;
            bool modified = def != null && !Equals(def, e.BoxedValue);
            string desc = Call<string>("Desc", e);
            float ctrlW = 200 * k, leftW = cw - ctrlW - 10 * k;
            float top = y;

            // left: the name (bold) and its key, the description, a line of facts, a warning
            string title = Call<string>("Human", e.Definition.Key) + (isPending ? " *" : "");
            float titleW = Mathf.Min(Ui.Font(true).Width(title) * k, leftW);
            Ui.Fit(new Rect(0, y, titleW, 15 * k), title, Color.black, TextAnchor.MiddleLeft, true);
            if (titleW + 12 * k < leftW) Ui.Fit(new Rect(titleW + 6 * k, y, leftW - titleW - 6 * k, 15 * k), e.Definition.Key, Win95.Shadow);
            y += 16 * k;
            if (desc.Length > 0) y += Ui.Wrap(new Rect(0, y, leftW, 0), desc, Color.black) + 2 * k;
            var facts = new List<string>();
            string defTxt = def is bool b ? (b ? "on" : "off") : def == null || Convert.ToString(def, CultureInfo.InvariantCulture) == "" ? "empty" : Convert.ToString(def, CultureInfo.InvariantCulture);
            facts.Add("Default: " + defTxt);
            if (ranged) facts.Add("Range: " + Call<string>("Range0", e) + " to " + Call<string>("Range1", e));
            if (Regex.IsMatch(desc, "restart", RegexOptions.IgnoreCase)) facts.Add("needs a restart");
            if (modified) facts.Add("changed");
            y += Ui.Wrap(new Rect(0, y, leftW, 0), string.Join(", ", facts.ToArray()), Win95.Shadow);
            string conflict = isKey ? Call<string>("Conflict", m, e) : null;
            if (conflict != null) y += Ui.Wrap(new Rect(0, y, leftW, 0), "! " + conflict, Red);
            if (!string.IsNullOrEmpty(err)) y += Ui.Wrap(new Rect(0, y, leftW, 0), "! " + err, Red);
            else if (isPending) y += Ui.Wrap(new Rect(0, y, leftW, 0), "Not saved yet: press Enter", Win95.Navy);

            // right: the control, then a Default button
            float cx = cw - ctrlW, rw = 54 * k, inner = ctrlW - rw - 4 * k;
            if (Ui.Button(new Rect(cw - rw, top, rw, 21 * k), "Default", modified)) { pending.Remove(e); Call("SetNow", e, def); }
            if (t == typeof(bool))
            {
                bool v = (bool)e.BoxedValue;
                bool nv = Ui.Check(new Rect(cx, top, inner, 21 * k), v, v ? "On" : "Off");
                if (nv != v) Call("SetNow", e, nv);
            }
            else if (isKey)
            {
                var kr = new Rect(cx, top, inner, 21 * k);
                string val = e.GetSerializedValue();
                if (string.IsNullOrEmpty(val)) val = "None";
                bool waiting = Rebind(_ui) == e;
                if (GUI.Button(kr, GUIContent.none, GUIStyle.none)) Rebind(_ui) = waiting ? null : e;
                Ui.Field(kr);
                if (conflict != null) Win95.Outline(new Rect(kr.x + 2 * k, kr.y + 2 * k, kr.width - 4 * k, kr.height - 4 * k), Red, k);
                Ui.Fit(new Rect(kr.x + 4 * k, kr.y, kr.width - 8 * k, kr.height), waiting ? "Press a key..." : val, waiting ? Win95.Navy : Color.black);
                Ui.Fit(new Rect(cx, top + 22 * k, inner, 14 * k), waiting ? "Esc cancels, Delete clears" : "Click to change", Win95.Shadow);
                y = Mathf.Max(y, top + 38 * k);
            }
            else if (t.IsEnum || Call<object[]>("AcceptableList", e) != null)
            {
                var options = Call<string[]>("Options", e);
                string curVal = e.GetSerializedValue();
                Ui.Combo(PopOwner, "entry:" + e.Definition.Section + "/" + e.Definition.Key, new Rect(cx, top, inner, 21 * k), curVal, options.ToList(),
                    Array.IndexOf(options, curVal), i => { if (options[i] != curVal) Call("SetSerialized", e, options[i]); });
            }
            else if (ranged)
            {
                float fmin = Convert.ToSingle(range[1], CultureInfo.InvariantCulture), fmax = Convert.ToSingle(range[2], CultureInfo.InvariantCulture);
                float cur = Convert.ToSingle(e.BoxedValue, CultureInfo.InvariantCulture);
                float fv = isPending ? Call<float>("ParseF", pend, cur) : cur;
                float fieldW = 52 * k;
                float nfv = Ui.Trackbar(new Rect(cx, top, inner - fieldW - 4 * k, 21 * k), fv, fmin, fmax, out bool dragging);
                if (Math.Abs(nfv - fv) > 1e-6f)
                {
                    pending[e] = (t == typeof(float) || t == typeof(double) || t == typeof(decimal)) ? nfv.ToString("0.###", CultureInfo.InvariantCulture) : Mathf.RoundToInt(nfv).ToString(CultureInfo.InvariantCulture);
                    SliderDrag(_ui) = e;   // written once when the mouse is released (SettingsUI.OnGUI)
                }
                string text = isPending ? pending[e] : e.GetSerializedValue();
                string nt = Ui.TextField(new Rect(cx + inner - fieldW, top, fieldW, 21 * k), text, 16, null, null, true);
                if (nt != text) { if (nt == e.GetSerializedValue()) pending.Remove(e); else pending[e] = nt; }
            }
            else
            {
                string text = isPending ? pend : e.GetSerializedValue();
                string nt = Ui.TextField(new Rect(cx, top, inner, 21 * k), text, 400);
                if (nt != text) { if (nt == e.GetSerializedValue()) pending.Remove(e); else pending[e] = nt; }
            }
            return Mathf.Max(y, top + 23 * k) + 10 * k;
        }

        // ---------------------------------------------------------------- tab: about
        private static float About(float cw, ModEntry m, IndexMod im)
        {
            int k = Ui.K;
            float y = Ui.Wrap(new Rect(0, 0, cw, 0), im != null && !string.IsNullOrEmpty(im.Summary) ? im.Summary : "This mod isn't in the Apocasetter index, so there's no summary to show.", Color.black) + 10 * k;
            void Row(string label, string value)
            {
                float lw = 80 * k;
                Ui.Text(new Rect(0, y, lw, Ui.Line * k), label + ":", Win95.Shadow);
                y += Mathf.Max(Ui.Wrap(new Rect(lw, y, cw - lw, 0), value, Color.black), Ui.Line * k) + 4 * k;
            }
            if (im != null && !string.IsNullOrEmpty(im.Author)) Row("Author", im.Author);
            if (m.Kind != ModKind.Available) Row("Installed", string.IsNullOrEmpty(m.Version) ? "?" : m.Version);
            if (im != null && !string.IsNullOrEmpty(im.Version)) Row("Latest", im.Version + (string.IsNullOrEmpty(im.Published) ? "" : ", released " + Call<string>("When", im.Published)));
            if (im != null && !string.IsNullOrEmpty(im.Repo)) Row("GitHub", im.Repo);
            if (im != null && im.Requires.Count > 0)
            {
                var miss = Call<List<string>>("MissingRequirements", im);
                Row("Needs", string.Join(", ", im.Requires.Select(g => Call<string>("GuidName", g) + (miss.Contains(g) ? " (missing)" : "")).ToArray()) + (m.Kind == ModKind.Available ? " (installed together)" : ""));
            }
            if (im != null && im.Optional.Count > 0) Row("Works with", string.Join(", ", im.Optional.Select(g => Call<string>("GuidName", g)).ToArray()));
            if (m.Kind == ModKind.Loaded)
            {
                var deps = m.Dependents();
                if (deps.Count > 0) Row("Used by", string.Join(", ", deps.Select(d => d.Key + (d.Value ? "" : " (optional)")).ToArray()));
            }
            if (im != null && im.Deprecated) Row("Status", "Deprecated" + (string.IsNullOrEmpty(im.DeprecatedReason) ? "" : ": " + im.DeprecatedReason));
            if (im != null && im.Tags.Count > 0) Row("Tags", string.Join(", ", im.Tags.ToArray()));
            Row("Plugin ID", m.Guid);
            if (m.Kind != ModKind.Loaded)
                y += 6 * k + Ui.Wrap(new Rect(0, y + 6 * k, cw, 0), m.Kind == ModKind.Available ? "Not installed. Its settings appear here after it is installed and the game has started once."
                                                                                                : "Disabled. Its settings appear again once it is enabled.", Color.black);
            return y;
        }

        // ---------------------------------------------------------------- tab: what's new
        private static float Notes(float cw, IndexMod im)
        {
            int k = Ui.K;
            if (im == null || string.IsNullOrEmpty(im.Notes))
                return Ui.Wrap(new Rect(0, 0, cw, 0), im == null ? "This mod isn't in the Apocasetter index, so there are no release notes to show." : "The latest release has no notes.", Color.black);
            string head = im.Name + " " + im.Version;
            Ui.Text(new Rect(0, 0, cw, 15 * k), head, Color.black, TextAnchor.MiddleLeft, true);
            float hw = Ui.Font(true).Width(head) * k;
            Ui.Fit(new Rect(hw + 8 * k, 0, cw - hw - 8 * k, 15 * k), "released " + Call<string>("When", im.Published), Win95.Shadow);
            float y = 22 * k;
            foreach (var raw in im.Notes.Replace("\r", "").Split('\n'))
            {
                var line = raw.TrimEnd();
                if (line.Trim().Length == 0) { y += 6 * k; continue; }
                bool heading = line.StartsWith("#") || (line.StartsWith("**") && line.EndsWith("**"));
                bool bullet = line.TrimStart().StartsWith("- ") || line.TrimStart().StartsWith("* ");
                var text = line.TrimStart('#', ' ');
                if (bullet) text = text.TrimStart().Substring(2);
                text = text.Replace("**", "").Replace("`", "");
                if (heading) { y += 4 * k; y += Ui.Wrap(new Rect(0, y, cw, 0), text, Color.black, true); }
                else if (bullet) { Ui.Text(new Rect(4 * k, y, 8 * k, Ui.Line * k), "•", Color.black); y += Ui.Wrap(new Rect(14 * k, y, cw - 14 * k, 0), text, Color.black); }
                else y += Ui.Wrap(new Rect(0, y, cw, 0), text, Color.black);
            }
            if (!string.IsNullOrEmpty(im.Page))
            {
                y += 10 * k;
                if (Ui.Button(new Rect(0, y, Ui.ButtonWidth("Release Page"), 23 * k), "Release Page")) Application.OpenURL(im.Page);
                y += 23 * k;
            }
            return y;
        }

        // ---------------------------------------------------------------- tab: files
        private static float Files(float cw, ModEntry m)
        {
            int k = Ui.K;
            if (m.Kind == ModKind.Available) return Ui.Wrap(new Rect(0, 0, cw, 0), "Nothing installed yet. Install puts the zip's files into BepInEx\\plugins\\ at the next start.", Color.black);
            if (m.Kind == ModKind.Disabled) return Ui.Wrap(new Rect(0, 0, cw, 0), "Its files are in " + Call<string>("Rel", m.DisabledInfo.Dir) + " until it is enabled again.", Color.black);
            float y = Ui.Wrap(new Rect(0, 0, cw, 0), "What Update and Remove touch. An update unpacks the new zip over these files and leaves anything the zip doesn't contain alone.", Color.black) + 8 * k;
            var rows = new List<string[]>();
            var folder = m.PluginFolder;
            if (folder != null)
            {
                rows.Add(new[] { Call<string>("Rel", folder) + "\\", "", "replaced on update" });
                try
                {
                    foreach (var p in Directory.GetFileSystemEntries(folder).OrderBy(x => x).Take(30))
                        rows.Add(new[] { "    " + Path.GetFileName(p) + (Directory.Exists(p) ? "\\" : ""), Directory.Exists(p) ? "folder" : Call<string>("SizeOf", p), "" });
                }
                catch { }
            }
            else foreach (var p in m.PluginPaths()) rows.Add(new[] { Call<string>("Rel", p), Call<string>("SizeOf", p), "replaced on update" });
            foreach (var p in m.ConfigPaths()) rows.Add(new[] { Call<string>("Rel", p) + (Directory.Exists(p) ? "\\" : ""), p.EndsWith(".cfg") ? "settings" : "data", "kept" });
            var list = new Rect(0, y, cw, rows.Count * 16 * k + 4 * k);
            Ui.Field(list);
            float ry = list.y + 2 * k;
            foreach (var r in rows)
            {
                Ui.Fit(new Rect(4 * k, ry, cw * .6f - 6 * k, 16 * k), r[0], Color.black);
                Ui.Fit(new Rect(cw * .6f, ry, cw * .18f, 16 * k), r[1], Win95.Shadow);
                Ui.Fit(new Rect(cw * .78f, ry, cw * .22f - 4 * k, 16 * k), r[2], r[2] == "kept" ? Green : Win95.Shadow);
                ry += 16 * k;
            }
            y = list.yMax + 10 * k;
            float x = 0;
            float w1 = Ui.ButtonWidth("Open Plugin Folder"), w2 = Ui.ButtonWidth("Open Config File"), w3 = Ui.ButtonWidth("Disable");
            if (Ui.Button(new Rect(x, y, w1, 23 * k), "Open Plugin Folder")) Call("OpenPath", folder ?? Paths.PluginPath);
            x += w1 + 6 * k;
            if (Ui.Button(new Rect(x, y, w2, 23 * k), "Open Config File", m.Config != null)) Call("OpenPath", m.Config.ConfigFilePath);
            x += w2 + 6 * k;
            if (!m.Self && Ui.Button(new Rect(x, y, w3, 23 * k), "Disable", Updates.InstallerPresent && Updates.OpFor(m.Guid) == null)) Call("StageSimple", m, "disable", null, false);
            return y + 23 * k;
        }

        // ---------------------------------------------------------------- bottom: the restart queue or the buttons, the status bar
        private static void BottomBar(ModEntry mod)
        {
            int k = Ui.K;
            float y = Bottom + 2;
            string status;
            if (Updates.Ops.Count > 0)
            {
                var items = Updates.Ops.Select(o => { var kind = MiniJson.Str(o, "op"); var n = MiniJson.Str(o, "name"); return kind == "update" ? n + " to " + MiniJson.Str(o, "version") : kind + " " + n; }).ToArray();
                float qw = Ui.ButtonWidth("Quit Game to Apply") / k, uw = Ui.ButtonWidth("Undo All") / k;
                if (Apocasetter.GameMenu.InGame) Ui.Fit(P(W - 8 - qw, y, qw, 23), "Save, then restart", Win95.Shadow, TextAnchor.MiddleRight);
                else if (Ui.Button(P(W - 8 - qw, y, qw, 23), "Quit Game to Apply")) Application.Quit();
                if (Ui.Button(P(W - 8 - qw - 6 - uw, y, uw, 23), "Undo All")) Updates.UnstageAll();
                Ui.Fit(P(8, y, W - 30 - qw - uw, 23), "Waiting for a restart: " + string.Join(", ", items), Win95.Navy, TextAnchor.MiddleLeft, true);
                status = "Changes to mods apply when the game starts again.";
            }
            else
            {
                float fw = Ui.ButtonWidth("Open Config Folder") / k, lw = Ui.ButtonWidth("Reload from File") / k, cw = Ui.ButtonWidth("Close") / k;
                float x = W - 8 - cw;
                if (Ui.Button(P(x, y, cw, 23), "Close", true, true)) Call("Toggle");
                x -= fw + 6;
                if (Ui.Button(P(x, y, fw, 23), "Open Config Folder")) Call("OpenPath", Paths.ConfigPath);
                x -= lw + 6;
                if (Ui.Button(P(x, y, lw, 23), "Reload from File", mod != null && mod.Config != null))
                { Pending(_ui).Clear(); Errors(_ui).Clear(); mod.Config.Reload(); Call("SetStatus", "Reloaded " + Path.GetFileName(mod.Config.ConfigFilePath)); }
                status = Time.unscaledTime < StatusUntil(_ui) ? Status_(_ui) : Call<string>("LastResultsText");
                if (string.IsNullOrEmpty(status))
                {
                    int n = Pending(_ui).Count;
                    status = n > 0 ? n + " unsaved change(s): press Enter" : "Changes are saved as you make them.";
                    if (!Updates.InstallerPresent) status = "Apocasetter.Installer.dll is missing from BepInEx\\patchers: updates and removals are off.";
                }
            }
            var pane = P(3, H - 3 - 20, W - 6, 20);
            Ui.Sunken(pane);
            Ui.Fit(new Rect(pane.x + 4 * k, pane.y, pane.width - 8 * k, pane.height), status, Updates.InstallerPresent ? Color.black : Red);
        }

        // ---------------------------------------------------------------- the remove dialog
        private static void RemoveDialog(ModEntry m)
        {
            int k = Ui.K;
            var plugin = m.PluginPaths();
            var cfgs = m.ConfigPaths();
            var deps = m.Dependents();
            var hardDeps = Call<List<ModEntry>>("HardDependents", m);
            const float DW = 420;
            string intro = m.Name + " " + m.Version + " keeps running until you quit. On the next start its files are moved to BepInEx\\cache\\Apocasetter\\removed\\, so you can put them back.";
            float introH = Ui.WrapHeight(intro, (DW - 20) * k) / k;
            bool depRow = deps.Count > 0 || hardDeps.Count > 0;
            float h = 29 + introH + 8 + plugin.Count * 16 + 4 + 10 + 36 + (depRow ? 40 : 0) + 23 + 12;
            float ox = _ox, oy = _oy;
            _ox = Mathf.Round(_ox + (W - DW) / 2f * k); _oy = Mathf.Round(_oy + (H - h) / 2f * k);
            if (Ui.Window(P(0, 0, DW, h), "Remove " + m.Name)) RemoveOpen(_ui) = false;
            float y = 29;
            Ui.Wrap(P(10, y, DW - 20, 0), intro, Color.black);
            y += introH + 8;
            var list = P(10, y, DW - 20, plugin.Count * 16 + 4);
            Ui.Field(list);
            for (int i = 0; i < plugin.Count; i++)
                Ui.Fit(new Rect(list.x + 4 * k, list.y + 2 * k + i * 16 * k, list.width - 8 * k, 16 * k), Call<string>("Rel", plugin[i]) + (Directory.Exists(plugin[i]) ? "\\  (whole folder)" : ""), Color.black);
            y += plugin.Count * 16 + 4 + 10;
            RemoveCfg(_ui) = Ui.Check(P(10, y, DW - 20, 16), RemoveCfg(_ui), "Also remove its settings and saved data");
            Ui.Fit(P(28, y + 17, DW - 38, 15), cfgs.Count == 0 ? "(none found)" : string.Join(", ", cfgs.Select(c => Call<string>("Rel", c)).ToArray()), Win95.Shadow);
            y += 36;
            if (depRow)
            {
                var hard = deps.Where(d => d.Value).Select(d => d.Key).Union(hardDeps.Select(hd => hd.Name)).ToArray();
                var soft = deps.Where(d => !d.Value).Select(d => d.Key).Where(n => !hard.Contains(n)).ToArray();
                string warn = hard.Length > 0 ? string.Join(", ", hard) + (hard.Length == 1 ? " needs it and will be disabled with it." : " need it and will be disabled with it.")
                                              : string.Join(", ", soft) + (soft.Length == 1 ? " has" : " have") + " extra features for it and keep working without it.";
                Ui.Notice(P(10, y, DW - 20, 34));
                Ui.Wrap(P(16, y + 4, DW - 32, 0), warn, Color.black);
                y += 40;
            }
            float rw = Ui.ButtonWidth("Remove") / k, dw = Ui.ButtonWidth("Disable Instead") / k, cw = Ui.ButtonWidth("Cancel") / k;
            float bx = DW - 10 - cw;
            bool cancel = Ui.Button(P(bx, y, cw, 23), "Cancel", true, true);
            bx -= dw + 6; bool doDisable = Ui.Button(P(bx, y, dw, 23), "Disable Instead");
            bx -= rw + 6; bool doRemove = Ui.Button(P(bx, y, rw, 23), "Remove");
            if (cancel) RemoveOpen(_ui) = false;
            if (doDisable || doRemove)
            {
                Call("StageSimple", m, doRemove ? "remove" : "disable", null, doRemove && RemoveCfg(_ui));
                foreach (var hd in hardDeps) { Call("StageSimple", hd, "disable", null, false); Call("SetStatus", hd.Name + " will be disabled too: it needs " + m.Name + "."); }
                RemoveOpen(_ui) = false;
            }
            _ox = ox; _oy = oy;
        }
    }
}
