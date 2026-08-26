using System;
using System.Collections.Generic;
using UnityEngine;

namespace PoiMapPlus
{
    /// <summary>World POI list plus a chunk based index for "which POI is this position in".</summary>
    public static class PoiRegistry
    {
        // The game's own POI list only holds prefabs carrying a quest tag, which leaves out
        // tier 0 houses and sheds. We walk every world prefab instead and drop the service ones
        // by the same tags XUiC_Location uses for its "entering area" banner.
        static readonly FastTags<TagGroup.Poi> partTag = FastTags<TagGroup.Poi>.Parse("part");
        static readonly FastTags<TagGroup.Poi> streetTileTag = FastTags<TagGroup.Poi>.Parse("streettile");
        static readonly FastTags<TagGroup.Poi> navOnlyTileTag = FastTags<TagGroup.Poi>.Parse("navonly");
        static readonly FastTags<TagGroup.Poi> hideUiTag = FastTags<TagGroup.Poi>.Parse("hideui");

        static readonly List<PrefabInstance> pois = new List<PrefabInstance>();
        static readonly List<PrefabInstance> allBuffer = new List<PrefabInstance>();
        static readonly Dictionary<long, List<PrefabInstance>> byChunk = new Dictionary<long, List<PrefabInstance>>();
        static bool built;

        public static IReadOnlyList<PrefabInstance> Pois => pois;
        public static bool IsBuilt => built;

        public static void Build()
        {
            pois.Clear();
            byChunk.Clear();
            built = false;

            var decorator = GameManager.Instance != null ? GameManager.Instance.GetDynamicPrefabDecorator() : null;
            if (decorator == null)
            {
                Log.Warning("[PoiMapPlus] DynamicPrefabDecorator unavailable, POI list is empty");
                return;
            }

            allBuffer.Clear();
            decorator.GetAllPrefabs(allBuffer);

            var skippedService = 0;
            var skippedTier = 0;

            foreach (var pi in allBuffer)
            {
                if (pi == null) continue;

                if (IsServicePrefab(pi))
                {
                    skippedService++;
                    continue;
                }

                if (Tier(pi) < Cfg.MinTier)
                {
                    skippedTier++;
                    continue;
                }

                pois.Add(pi);
            }

            foreach (var pi in pois)
            {

                var min = pi.boundingBoxPosition;
                var size = pi.boundingBoxSize;

                var cx0 = World.toChunkXZ(min.x);
                var cz0 = World.toChunkXZ(min.z);
                var cx1 = World.toChunkXZ(min.x + Math.Max(0, size.x - 1));
                var cz1 = World.toChunkXZ(min.z + Math.Max(0, size.z - 1));

                for (var cx = cx0; cx <= cx1; cx++)
                for (var cz = cz0; cz <= cz1; cz++)
                {
                    var key = WorldChunkCache.MakeChunkKey(cx, cz);
                    if (!byChunk.TryGetValue(key, out var list))
                    {
                        list = new List<PrefabInstance>(1);
                        byChunk[key] = list;
                    }
                    list.Add(pi);
                }
            }

            built = true;
            Log.Out($"[PoiMapPlus] POIs indexed: {pois.Count} " +
                    $"(skipped {skippedService} parts/tiles, {skippedTier} below tier {Cfg.MinTier})");
        }

        /// <summary>Street tiles, POI parts and nav-only helpers are not places the player visits.</summary>
        static bool IsServicePrefab(PrefabInstance _pi)
        {
            if (_pi.prefab == null) return true;

            var tags = _pi.prefab.Tags;
            return tags.Test_AnySet(partTag)
                || tags.Test_AnySet(streetTileTag)
                || tags.Test_AnySet(navOnlyTileTag)
                || tags.Test_AnySet(hideUiTag);
        }

        public static void Clear()
        {
            pois.Clear();
            allBuffer.Clear();
            byChunk.Clear();
            built = false;
        }

        /// <summary>POI whose bounding box contains the position, or null when the position is outside any POI.</summary>
        public static PrefabInstance FindAt(Vector3i _pos)
        {
            var key = WorldChunkCache.MakeChunkKey(World.toChunkXZ(_pos.x), World.toChunkXZ(_pos.z));
            if (!byChunk.TryGetValue(key, out var candidates)) return null;

            foreach (var pi in candidates)
                if (Contains(pi, _pos))
                    return pi;

            return null;
        }

        public static PrefabInstance FindAt(Vector3 _pos) =>
            FindAt(new Vector3i(Mathf.FloorToInt(_pos.x), Mathf.FloorToInt(_pos.y), Mathf.FloorToInt(_pos.z)));

        public static bool Contains(PrefabInstance _pi, Vector3i _pos)
        {
            if (_pi == null) return false;

            var min = _pi.boundingBoxPosition;
            var size = _pi.boundingBoxSize;

            return _pos.x >= min.x && _pos.x < min.x + size.x
                && _pos.z >= min.z && _pos.z < min.z + size.z
                && _pos.y >= min.y - 1 && _pos.y < min.y + size.y + 1;
        }

        /// <summary>POI name as shown to the player. The game already localizes prefab names.</summary>
        public static string DisplayName(PrefabInstance _pi)
        {
            if (_pi == null) return string.Empty;

            if (_pi.prefab != null)
            {
                var localized = _pi.prefab.LocalizedName;
                if (!string.IsNullOrEmpty(localized)) return localized;

                var prefabName = _pi.prefab.PrefabName;
                if (!string.IsNullOrEmpty(prefabName)) return prefabName;
            }

            return _pi.name ?? string.Empty;
        }

        public static int Tier(PrefabInstance _pi) =>
            _pi != null && _pi.prefab != null ? _pi.prefab.DifficultyTier : 0;
    }
}
