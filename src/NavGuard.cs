using HarmonyLib;
using UnityEngine;

namespace PoiMapPlus
{
    /// <summary>
    /// NavObject.GetPosition() dereferences trackedTransform without a null check, so a single
    /// nav object whose transform got destroyed throws inside XUiC_MapArea.updateNavObjectList
    /// and kills the whole marker layer of the map. Drop such objects before the map walks the
    /// list.
    ///
    /// The sweep runs on every map frame rather than on a timer. It used to be throttled to
    /// twice a second, which left the hole it was meant to plug: a zombie dying between two
    /// sweeps still had its nav object drawn, and the map threw. Running from the Update prefix
    /// closes that - the check and the draw are then in the same frame, with nothing in between
    /// that could destroy a transform.
    ///
    /// Every member touched here is public, so the pass is a couple of field reads per nav
    /// object and cheap enough to afford at that rate.
    /// </summary>
    public static class NavGuard
    {
        public static void Sweep()
        {
            if (!NavObjectManager.HasInstance) return;

            var list = NavObjectManager.Instance.NavObjectList;
            if (list == null) return;

            for (var i = list.Count - 1; i >= 0; i--)
            {
                var nav = list[i];
                if (nav == null) continue;

                // Only transform tracked objects can have this problem; ours track a position.
                if (nav.TrackType != NavObject.TrackTypes.Transform) continue;

                // Unity's fake null: the object is gone but the reference is not literally null
                if (nav.TrackedTransform != null) continue;

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
                var dead = nav.TrackType == NavObject.TrackTypes.Transform && nav.TrackedTransform == null;
                if (dead) broken++;

                var key = $"{cls} track={nav.TrackType}{(dead ? " DEAD" : "")}";
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
