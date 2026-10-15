using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace Chicago95
{
    // What the loading dialog shows beyond the game's load stages: Easy Save reading the save (which file, how many keys
    // read and which one now) and MapMagic generating the world (each tile: waiting, generating with its progress, its
    // draft and main detail generated and applied). Easy Save is followed through its reader (ES3Reader: Create opens a
    // file, Goto finds each key a load asks for), MapMagic through its tiles and their events. Some of this runs on
    // worker threads: what they report is queued for the console under a lock.
    internal static class LoadingInfo
    {
        // ---------------------------------------------------------------- Easy Save
        private static readonly object Lock = new object();
        public static string File { get; private set; }
        public static long FileBytes { get; private set; }
        public static int Keys { get; private set; }
        public static string LastKey { get; private set; }
        private static readonly Queue<string> Events = new Queue<string>();

        public static void Install(Harmony h)
        {
            var create = AccessTools.Method(typeof(ES3Reader), nameof(ES3Reader.Create), new[] { typeof(ES3Settings) });
            if (create != null) h.Patch(create, postfix: new HarmonyMethod(typeof(LoadingInfo), nameof(Opened)));
            var go = AccessTools.Method(typeof(ES3Reader), "Goto", new[] { typeof(string) });
            if (go != null) h.Patch(go, prefix: new HarmonyMethod(typeof(LoadingInfo), nameof(Reading)));
            MapMagic.Terrains.TerrainTile.OnBeforeTileGenerate += (t, d, s) => Note("MapMagic: tile " + Coord(t) + " generating");
            MapMagic.Terrains.TerrainTile.OnTileApplied += (t, d, s) => Note("MapMagic: tile " + Coord(t) + " applied");
        }

        public static void Reset() { lock (Lock) { File = null; FileBytes = 0; Keys = 0; LastKey = null; Events.Clear(); } }

        private static void Opened(ES3Reader __result)
        {
            if (__result == null || __result.settings == null) return;
            string path = null;
            try { path = __result.settings.FullPath; } catch { }
            if (string.IsNullOrEmpty(path)) return;
            string name = Path.GetFileName(path);
            if (name == File) return;
            long bytes = 0;
            try { var fi = new FileInfo(path); if (fi.Exists) bytes = fi.Length; } catch { }
            lock (Lock) { File = name; FileBytes = bytes; }
            Note("Easy Save: reading " + name + (bytes > 0 ? " (" + (bytes / 1024) + " kB)" : ""));
        }

        private static void Reading(string key)
        {
            lock (Lock) { Keys++; LastKey = key; }
        }

        public static void Note(string s) { lock (Lock) { Events.Enqueue(s); while (Events.Count > 64) Events.Dequeue(); } }
        public static List<string> TakeEvents() { var l = new List<string>(); lock (Lock) while (Events.Count > 0) l.Add(Events.Dequeue()); return l; }

        // ---------------------------------------------------------------- MapMagic's tiles
        public enum TileState { Waiting, Generating, Draft, Main }
        public struct Tile { public int X, Z; public TileState State; public float Progress; }

        private static string Coord(MapMagic.Terrains.TerrainTile t) { try { return t.coord.x + "," + t.coord.z; } catch { return "?"; } }

        // each tile as it stands: generating (with its share done), its draft applied, its main detail applied, or waiting
        public static List<Tile> Tiles(MapMagic.Core.MapMagicObject mm)
        {
            var list = new List<Tile>();
            if (mm == null || mm.tiles == null) return list;
            float gen = 0, apply = 0;
            try { if (mm.graph != null) { gen = mm.graph.GetGenerateComplexity(); apply = mm.graph.GetApplyComplexity(); } } catch { }
            foreach (var t in mm.tiles.All())
            {
                if (t == null) continue;
                var tile = new Tile { X = t.coord.x, Z = t.coord.z };
                bool mainDone = t.main != null && t.main.applyReady, draftDone = t.draft != null && t.draft.applyReady;
                if (t.IsGenerating)
                {
                    tile.State = TileState.Generating;
                    try { var p = t.GetProgress(mm.graph, gen, apply); tile.Progress = p.max > 0 ? Mathf.Clamp01(p.progress / p.max) : 0; } catch { }
                }
                else tile.State = mainDone ? TileState.Main : draftDone ? TileState.Draft : TileState.Waiting;
                list.Add(tile);
            }
            return list;
        }
    }
}
