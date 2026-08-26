using System.Collections.Generic;
using UnityEngine;

namespace PoiMapPlus
{
    /// <summary>POI markers on the map: colour is the difficulty tier, icon tells cleared from untouched.</summary>
    public static class PoiMarkers
    {
        const string cNavClass = "poimapplus_poi";
        const string cSprite = "ui_game_symbol_skull";

        static readonly Dictionary<string, NavObject> markers = new Dictionary<string, NavObject>();

        // Marker text is not written into NavObject.name on purpose: the map renders that
        // as a permanent label under the icon. We show it as a tooltip on hover instead.
        static readonly Dictionary<NavObject, string> tooltips = new Dictionary<NavObject, string>();

        // Difficulty lives in the tooltip; the icon colour only says looted or not.
        static readonly Color openColor = new Color(1.00f, 0.55f, 0.15f);
        static readonly Color clearedColor = new Color(0.35f, 0.85f, 0.35f);

        static readonly List<PrefabInstance> visible = new List<PrefabInstance>();

        static readonly HashSet<NavObject> liveNavObjects = new HashSet<NavObject>();

        public static void Refresh()
        {
            if (!NavObjectManager.HasInstance) return;
            if (!PoiRegistry.IsBuilt) PoiRegistry.Build();

            NavGuard.Sweep(true);

            // The game unregisters nav objects on its own (quests, world events) and recycles them
            // through a pool. A marker we still hold a reference to may already belong to something
            // else, so trust only what is currently in the manager's list.
            liveNavObjects.Clear();
            var navList = NavObjectManager.Instance.NavObjectList;
            if (navList != null)
                foreach (var nav in navList)
                    if (nav != null)
                        liveNavObjects.Add(nav);

            var fow = GameManager.Instance != null ? GameManager.Instance.fowDatabaseForLocalPlayer : null;

            visible.Clear();

            foreach (var pi in PoiRegistry.Pois)
            {
                if (pi == null) continue;

                var rec = PoiDb.Find(pi.boundingBoxPosition.x, pi.boundingBoxPosition.z);

                if (Cfg.OnlyDiscovered && !IsDiscovered(pi, fow, rec))
                {
                    Remove(pi);
                    continue;
                }

                visible.Add(pi);
            }

            // An explored map can hold more POIs than the marker budget, so keep the ones
            // closest to the player instead of whatever order the world list happens to have.
            if (visible.Count > Cfg.MaxMarkers)
            {
                var origin = PlayerPosition();
                visible.Sort((a, b) => SqrDistance(a, origin).CompareTo(SqrDistance(b, origin)));
            }

            for (var i = 0; i < visible.Count; i++)
            {
                var pi = visible[i];

                if (i >= Cfg.MaxMarkers)
                {
                    Remove(pi);
                    continue;
                }

                Upsert(pi, PoiDb.Find(pi.boundingBoxPosition.x, pi.boundingBoxPosition.z));
            }
        }

        static Vector3 PlayerPosition()
        {
            var world = GameManager.Instance != null ? GameManager.Instance.World : null;
            var player = world != null ? world.GetPrimaryPlayer() : null;
            return player != null ? player.position : Vector3.zero;
        }

        static float SqrDistance(PrefabInstance _pi, Vector3 _origin)
        {
            var min = _pi.boundingBoxPosition;
            var size = _pi.boundingBoxSize;
            var dx = min.x + size.x * 0.5f - _origin.x;
            var dz = min.z + size.z * 0.5f - _origin.z;
            return dx * dx + dz * dz;
        }

        static bool IsDiscovered(PrefabInstance _pi, IMapChunkDatabase _fow, PoiRecord _rec)
        {
            if (_rec != null && _rec.Discovered) return true;
            if (_fow == null) return false;

            var min = _pi.boundingBoxPosition;
            var size = _pi.boundingBoxSize;
            var cx = World.toChunkXZ(min.x + size.x / 2);
            var cz = World.toChunkXZ(min.z + size.z / 2);

            if (!_fow.Contains(WorldChunkCache.MakeChunkKey(cx, cz))) return false;

            // Remember it, so the POI does not disappear if the fog of war data is rebuilt.
            var rec = _rec ?? PoiDb.GetOrCreate(min.x, min.z);
            if (!rec.Discovered)
            {
                rec.Discovered = true;
                PoiDb.MarkDirty();
            }
            return true;
        }

        static void Upsert(PrefabInstance _pi, PoiRecord _rec)
        {
            var key = PoiRecord.MakeKey(_pi.boundingBoxPosition.x, _pi.boundingBoxPosition.z);
            var cleared = _rec != null && _rec.Cleared;

            var min = _pi.boundingBoxPosition;
            var size = _pi.boundingBoxSize;
            var center = new Vector3(min.x + size.x * 0.5f, min.y + size.y * 0.5f, min.z + size.z * 0.5f);

            if (!markers.TryGetValue(key, out var nav) || nav == null || !liveNavObjects.Contains(nav))
            {
                if (nav != null) tooltips.Remove(nav);

                // hiddenOnCompass must stay false: the map paints such icons plain grey
                // and ignores OverrideColor. The class has no compass_settings anyway,
                // so POIs never reach the compass.
                nav = NavObjectManager.Instance.RegisterNavObject(cNavClass, center, cSprite, false, -1, null);
                if (nav == null) return;

                markers[key] = nav;
                liveNavObjects.Add(nav);
            }

            // Setting the property (not the field) also pins TrackType to Position, so the map
            // never tries to read a transform for our markers.
            nav.TrackedPosition = center;

            nav.OverrideSpriteName = cSprite;
            nav.UseOverrideColor = true;
            nav.OverrideColor = cleared ? clearedColor : openColor;

            // Clicking a marker toggles this flag, which would grey the icon out.
            nav.hiddenOnCompass = false;

            // An empty name keeps the map from drawing a permanent label under the icon.
            nav.usingLocalizationId = false;
            nav.localizedName = null;
            nav.name = string.Empty;

            tooltips[nav] = Loc.MarkerLabel(_pi, _rec);
        }

        static void Remove(PrefabInstance _pi)
        {
            var key = PoiRecord.MakeKey(_pi.boundingBoxPosition.x, _pi.boundingBoxPosition.z);
            if (!markers.TryGetValue(key, out var nav)) return;

            if (nav != null)
            {
                tooltips.Remove(nav);

                if (NavObjectManager.HasInstance)
                    NavObjectManager.Instance.UnRegisterNavObject(nav);
            }

            markers.Remove(key);
        }

        /// <summary>Hover text for a marker, or null when the nav object is not ours.</summary>
        public static string TooltipFor(NavObject _nav) =>
            _nav != null && tooltips.TryGetValue(_nav, out var text) ? text : null;

        public static void Clear()
        {
            if (NavObjectManager.HasInstance)
                foreach (var nav in markers.Values)
                    if (nav != null)
                        NavObjectManager.Instance.UnRegisterNavObject(nav);

            markers.Clear();
            tooltips.Clear();
        }
    }
}
