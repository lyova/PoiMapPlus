using System;
using System.Collections.Generic;

namespace PoiMapPlus
{
    /// <summary>
    /// Scans the containers inside a POI and decides whether its richest loot is gone.
    /// Only loaded chunks can be inspected, so this runs while the player is inside the POI.
    /// </summary>
    public static class PoiScanner
    {
        struct Entry
        {
            public float Score;   // used when no whitelist is configured
            public int Rank;      // used with a whitelist: 1 is the richest
            public bool Touched;
        }

        static readonly List<Entry> buffer = new List<Entry>();
        static readonly Dictionary<string, float> lastScanAt = new Dictionary<string, float>();

        public static void Scan(PrefabInstance _pi, bool _ignoreCooldown = false)
        {
            if (_pi == null) return;

            var world = GameManager.Instance != null ? GameManager.Instance.World : null;
            if (world == null || world.ChunkCache == null) return;

            var rec = PoiDb.GetOrCreate(_pi.boundingBoxPosition.x, _pi.boundingBoxPosition.z);

            if (!_ignoreCooldown)
            {
                var now = UnityEngine.Time.time;
                if (lastScanAt.TryGetValue(rec.Key, out var prev) && now - prev < Cfg.ScanIntervalSeconds) return;
                lastScanAt[rec.Key] = now;
            }

            buffer.Clear();

            var min = _pi.boundingBoxPosition;
            var size = _pi.boundingBoxSize;

            var cx0 = World.toChunkXZ(min.x);
            var cz0 = World.toChunkXZ(min.z);
            var cx1 = World.toChunkXZ(min.x + Math.Max(0, size.x - 1));
            var cz1 = World.toChunkXZ(min.z + Math.Max(0, size.z - 1));

            var poiTier = PoiRegistry.Tier(_pi);
            var complete = true;
            var allTotal = 0;
            var allTouched = 0;

            for (var cx = cx0; cx <= cx1; cx++)
            for (var cz = cz0; cz <= cz1; cz++)
            {
                var chunk = world.ChunkCache.GetChunkSync(cx, cz);
                if (chunk == null)
                {
                    complete = false;
                    continue;
                }

                var tileEntities = chunk.GetTileEntities();
                if (tileEntities == null) continue;

                foreach (var te in tileEntities.list)
                {
                    if (!(te is TileEntityComposite composite)) continue;

                    var storage = composite.GetFeature<TEFeatureStorage>();
                    if (storage == null) continue;

                    if (!PoiRegistry.Contains(_pi, te.ToWorldPos())) continue;

                    var rank = Cfg.cWorstRank;
                    var score = 0f;

                    if (Cfg.HasWhitelist)
                    {
                        if (!LootScore.TryGetRank(storage, poiTier, out rank)) continue;
                    }
                    else
                    {
                        score = LootScore.Evaluate(composite, storage);
                        if (score <= LootScore.cIgnored) continue;
                    }

                    allTotal++;
                    if (storage.bTouched) allTouched++;

                    if (Cfg.HasWhitelist || score >= Cfg.MinContainerScore)
                        buffer.Add(new Entry { Score = score, Rank = rank, Touched = storage.bTouched });
                }
            }

            if (Cfg.HasWhitelist)
                // Best rank first; among equal ranks the looted ones come first, so a POI with
                // two identical chests counts as done once either of them is empty.
                buffer.Sort((a, b) => a.Rank != b.Rank
                    ? a.Rank.CompareTo(b.Rank)
                    : b.Touched.CompareTo(a.Touched));
            else
                buffer.Sort((a, b) => b.Score.CompareTo(a.Score));

            var topTotal = Math.Min(Cfg.TopContainerCount, buffer.Count);
            var topTouched = 0;
            for (var i = 0; i < topTotal; i++)
                if (buffer[i].Touched)
                    topTouched++;

            var bestFoundRank = buffer.Count > 0 ? buffer[0].Rank : 0;

            var wasCleared = rec.Cleared;

            if (complete)
            {
                rec.Scanned = true;
                rec.BestFoundRank = bestFoundRank;
                rec.TopTotal = topTotal;
                rec.TopTouched = topTouched;
                rec.AllTotal = allTotal;
                rec.AllTouched = allTouched;
            }
            else
            {
                // A partial scan must never lower what we already know about this POI.
                rec.TopTotal = Math.Max(rec.TopTotal, topTotal);
                rec.TopTouched = Math.Max(rec.TopTouched, topTouched);
                rec.AllTotal = Math.Max(rec.AllTotal, allTotal);
                rec.AllTouched = Math.Max(rec.AllTouched, allTouched);

                if (bestFoundRank > 0 && (rec.BestFoundRank == 0 || bestFoundRank < rec.BestFoundRank))
                    rec.BestFoundRank = bestFoundRank;
            }

            rec.Discovered = true;
            PoiDb.MarkDirty();

            if (!wasCleared && rec.Cleared)
                Log.Out($"[PoiMapPlus] POI cleared: {PoiRegistry.DisplayName(_pi)} " +
                        $"({rec.TopTouched}/{rec.TopTotal} top containers, {rec.AllTouched}/{rec.AllTotal} overall)");
        }

        /// <summary>
        /// Diagnostics for the console command: lists every storage container in the POI's chunks,
        /// with its rank and looted state, so a wrong marker can be traced to actual data.
        /// </summary>
        public static List<string> Describe(PrefabInstance _pi)
        {
            var lines = new List<string>();
            if (_pi == null) { lines.Add("no POI"); return lines; }

            var world = GameManager.Instance != null ? GameManager.Instance.World : null;
            if (world == null || world.ChunkCache == null) { lines.Add("no world"); return lines; }

            var min = _pi.boundingBoxPosition;
            var size = _pi.boundingBoxSize;

            lines.Add($"POI '{PoiRegistry.DisplayName(_pi)}' tier={PoiRegistry.Tier(_pi)} " +
                      $"box={min.x},{min.y},{min.z} size={size.x},{size.y},{size.z}");

            var cx0 = World.toChunkXZ(min.x);
            var cz0 = World.toChunkXZ(min.z);
            var cx1 = World.toChunkXZ(min.x + Math.Max(0, size.x - 1));
            var cz1 = World.toChunkXZ(min.z + Math.Max(0, size.z - 1));

            var missingChunks = 0;

            for (var cx = cx0; cx <= cx1; cx++)
            for (var cz = cz0; cz <= cz1; cz++)
            {
                var chunk = world.ChunkCache.GetChunkSync(cx, cz);
                if (chunk == null) { missingChunks++; continue; }

                var tileEntities = chunk.GetTileEntities();
                if (tileEntities == null) continue;

                foreach (var te in tileEntities.list)
                {
                    if (!(te is TileEntityComposite composite)) continue;

                    var storage = composite.GetFeature<TEFeatureStorage>();
                    if (storage == null) continue;

                    var pos = te.ToWorldPos();
                    var inside = PoiRegistry.Contains(_pi, pos);
                    var rank = LootScore.TryGetRank(storage, PoiRegistry.Tier(_pi), out var r) ? r.ToString() : "-";

                    lines.Add($"  {(inside ? "in " : "OUT")} {pos.x},{pos.y},{pos.z} " +
                              $"loot='{storage.lootListName}' rank={rank} " +
                              $"touched={storage.bTouched} player={storage.bPlayerStorage}");
                }
            }

            if (missingChunks > 0) lines.Add($"  ({missingChunks} chunk(s) not loaded)");

            var rec = PoiDb.Find(min.x, min.z);
            lines.Add(rec == null
                ? "  state: none stored"
                : $"  state: scanned={rec.Scanned} top={rec.TopTouched}/{rec.TopTotal} " +
                  $"all={rec.AllTouched}/{rec.AllTotal} bestOpened={rec.BestOpenedRank} " +
                  $"bestFound={rec.BestFoundRank} cleared={rec.Cleared}");

            return lines;
        }

        /// <summary>A quest reset the POI, so every container is untouched again.</summary>
        public static void ResetPoi(PrefabInstance _pi)
        {
            if (_pi == null) return;

            var rec = PoiDb.Find(_pi.boundingBoxPosition.x, _pi.boundingBoxPosition.z);
            if (rec == null) return;

            rec.TopTouched = 0;
            rec.AllTouched = 0;
            rec.Scanned = false;
            rec.BestOpenedRank = 0;
            rec.BestFoundRank = 0;
            lastScanAt.Remove(rec.Key);
            PoiDb.MarkDirty();

            Log.Out($"[PoiMapPlus] POI reset by quest: {PoiRegistry.DisplayName(_pi)}");
        }

        public static void Clear() => lastScanAt.Clear();
    }
}
