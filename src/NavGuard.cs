using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace PoiMapPlus
{
    /// <summary>
    /// NavObject.GetPosition() dereferences trackedTransform without a null check, so a single
    /// nav object whose transform got destroyed throws inside XUiC_MapArea.updateNavObjectList
    /// and kills the whole marker layer of the map. Drop such objects before the map walks the list.
    /// </summary>
    public static class NavGuard
    {
        static readonly FieldInfo fiTrackType = AccessTools.Field(typeof(NavObject), "TrackType");
        static readonly FieldInfo fiTrackedTransform = AccessTools.Field(typeof(NavObject), "trackedTransform");

        // TrackTypes: 1 = Transform, 2 = Position, 3 = Entity
        const int cTrackTypeTransform = 1;

        static float nextSweepAt;

        public static void Sweep(bool _force = false)
        {
            if (!NavObjectManager.HasInstance) return;
            if (fiTrackType == null || fiTrackedTransform == null) return;

            if (!_force)
            {
                if (Time.time < nextSweepAt) return;
                nextSweepAt = Time.time + 0.5f;
            }

            var list = NavObjectManager.Instance.NavObjectList;
            if (list == null) return;

            for (var i = list.Count - 1; i >= 0; i--)
            {
                var nav = list[i];
                if (nav == null) continue;
                if ((int)fiTrackType.GetValue(nav) != cTrackTypeTransform) continue;

                var transform = fiTrackedTransform.GetValue(nav) as Transform;
                if (transform != null) continue;

                Log.Warning("[PoiMapPlus] dropping nav object with a dead transform: " +
                            (nav.NavObjectClass != null ? nav.NavObjectClass.NavObjectClassName : "?"));

                NavObjectManager.Instance.UnRegisterNavObject(nav);
            }
        }

        /// <summary>Diagnostics for the poimap console command.</summary>
        public static void Dump()
        {
            if (!NavObjectManager.HasInstance) { Log.Out("[PoiMapPlus] no NavObjectManager"); return; }

            var list = NavObjectManager.Instance.NavObjectList;
            var counts = new System.Collections.Generic.Dictionary<string, int>();
            var broken = 0;

            foreach (var nav in list)
            {
                if (nav == null) continue;

                var cls = nav.NavObjectClass != null ? nav.NavObjectClass.NavObjectClassName : "(no class)";
                var track = (int)fiTrackType.GetValue(nav);
                var dead = track == cTrackTypeTransform && !(fiTrackedTransform.GetValue(nav) is Transform t && t != null);
                if (dead) broken++;

                var key = $"{cls} track={track}{(dead ? " DEAD" : "")}";
                counts.TryGetValue(key, out var n);
                counts[key] = n + 1;
            }

            Log.Out($"[PoiMapPlus] nav objects: {list.Count}, with a dead transform: {broken}");
            foreach (var pair in counts)
                Log.Out($"[PoiMapPlus]   {pair.Value,4} x {pair.Key}");
        }
    }

    /// <summary>Run the sweep before the map iterates nav objects, not after it already threw.</summary>
    [HarmonyPatch(typeof(XUiC_MapArea), nameof(XUiC_MapArea.Update))]
    public static class Patch_MapArea_UpdatePre
    {
        static void Prefix()
        {
            try { NavGuard.Sweep(); }
            catch (System.Exception e) { Log.Error("[PoiMapPlus] nav sweep: " + e); }
        }
    }
}
