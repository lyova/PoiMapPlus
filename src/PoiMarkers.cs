using System.Collections.Generic;
using UnityEngine;

namespace PoiMapPlus
{
    /// <summary>What MarkerDecor has to draw on top of one skull.</summary>
    public class MarkerInfo
    {
        public int Tier;
        public ChestState Chest;
    }

    /// <summary>POI markers on the map: the skull says looted or not, the badges say the details.</summary>
    public static class PoiMarkers
    {
        const string cNavClass = "poimapplus_poi";
        const string cSprite = "ui_game_symbol_skull";

        static readonly Dictionary<string, NavObject> markers = new Dictionary<string, NavObject>();

        // Marker text is not written into NavObject.name on purpose: the map renders that
        // as a permanent label under the icon. We show it as a tooltip on hover instead.
        static readonly Dictionary<NavObject, string> tooltips = new Dictionary<NavObject, string>();

        // Per marker state for the decorator, which runs from the map's own update loop.
        static readonly Dictionary<NavObject, MarkerInfo> infos = new Dictionary<NavObject, MarkerInfo>();

        // Difficulty lives in the tier label; the skull colour only says looted or not.
        public static readonly Color OpenColor = new Color(1.00f, 0.55f, 0.15f);
        public static readonly Color ClearedColor = new Color(0.35f, 0.85f, 0.35f);

        static readonly List<PrefabInstance> visible = new List<PrefabInstance>();

        static readonly HashSet<NavObject> liveNavObjects = new HashSet<NavObject>();

        public static int MarkerCount => markers.Count;

        public static void Refresh()
        {
            if (!NavObjectManager.HasInstance) return;

            // Global toggle: the tracking keeps running, only the map goes quiet.
            if (!UiState.ShowMarkers)
            {
                if (markers.Count > 0) Clear();
                return;
            }

            if (!PoiRegistry.IsBuilt) PoiRegistry.Build();

            ApplyIconScale();
            RescanHere();
            NavGuard.Sweep();

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

        /// <summary>
        /// Re-scan the POI the player is standing in, right before drawing the markers.
        ///
        /// Everything else that updates a record is an event - a container opened, a loot window
        /// closed, a POI entered or left - and any of those can be missed or arrive in an order
        /// that leaves the marker a step behind: emptying a chest and opening the map without
        /// moving used to keep showing the previous chest state. This makes the map show what is
        /// actually there whenever it is opened, whatever happened before it.
        ///
        /// It costs one scan of one POI, and only while the player is inside one.
        /// </summary>
        static void RescanHere()
        {
            var world = GameManager.Instance != null ? GameManager.Instance.World : null;
            var player = world != null ? world.GetPrimaryPlayer() : null;
            if (player == null) return;

            var pi = PoiRegistry.FindAt(player.position);
            if (pi != null) PoiScanner.Scan(pi, true);
        }

        /// <summary>
        /// The nav object class comes from nav_objects.xml, but the marker size belongs with
        /// the rest of the mod's settings - so it is pushed onto the class instead, which also
        /// makes "poimap reload" enough to try a different value.
        /// </summary>
        static void ApplyIconScale()
        {
            var cls = NavObjectClass.GetNavObjectClass(cNavClass);
            if (cls == null) return;

            foreach (var settings in new[] { cls.MapSettings, cls.InactiveMapSettings })
            {
                if (settings == null) continue;
                settings.IconScale = Cfg.IconScale;
                settings.IconScaleVector = new Vector3(Cfg.IconScale, Cfg.IconScale, Cfg.IconScale);
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
                if (nav != null) Forget(nav);

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
            nav.OverrideColor = cleared ? ClearedColor : OpenColor;

            // Clicking a marker toggles this flag, which would grey the icon out.
            nav.hiddenOnCompass = false;

            // The map draws DisplayName as a label under the icon. That label is free - it
            // exists on every nav object already - so the tier rides on it, and MarkerDecor
            // moves it next to the skull and colours it by tier.
            var tier = PoiRegistry.Tier(_pi);
            nav.usingLocalizationId = false;
            nav.localizedName = null;
            nav.name = UiState.TierVisible && tier > 0 ? "T" + tier : string.Empty;

            tooltips[nav] = Loc.MarkerLabel(_pi, _rec);
            infos[nav] = new MarkerInfo
            {
                Tier = tier,
                Chest = _rec != null ? _rec.Chest : ChestState.Unknown,
            };
        }

        static void Remove(PrefabInstance _pi)
        {
            var key = PoiRecord.MakeKey(_pi.boundingBoxPosition.x, _pi.boundingBoxPosition.z);
            if (!markers.TryGetValue(key, out var nav)) return;

            if (nav != null)
            {
                Forget(nav);

                if (NavObjectManager.HasInstance)
                    NavObjectManager.Instance.UnRegisterNavObject(nav);
            }

            markers.Remove(key);
        }

        static void Forget(NavObject _nav)
        {
            tooltips.Remove(_nav);
            infos.Remove(_nav);
        }

        /// <summary>Hover text for a marker, or null when the nav object is not ours.</summary>
        public static string TooltipFor(NavObject _nav) =>
            _nav != null && tooltips.TryGetValue(_nav, out var text) ? text : null;

        /// <summary>Badge state for a marker, or null when the nav object is not ours.</summary>
        public static MarkerInfo InfoFor(NavObject _nav) =>
            _nav != null && infos.TryGetValue(_nav, out var info) ? info : null;

        public static void Clear()
        {
            if (NavObjectManager.HasInstance)
                foreach (var nav in markers.Values)
                    if (nav != null)
                        NavObjectManager.Instance.UnRegisterNavObject(nav);

            markers.Clear();
            tooltips.Clear();
            infos.Clear();
        }
    }
}
