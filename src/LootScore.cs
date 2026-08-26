using System;

namespace PoiMapPlus
{
    /// <summary>
    /// Rates how rich a container is. Higher score means better loot.
    /// Derived from runtime container data instead of a hardcoded block list,
    /// so containers added by other mods are ranked as well.
    /// </summary>
    public static class LootScore
    {
        public const float cIgnored = -1f;
        const string cBaseQualityTemplate = "qualBaseTemplate";

        /// <summary>
        /// Rank of a tracked container: 1 is the richest, higher is worse.
        /// Returns false when the container is not tracked at all.
        /// </summary>
        public static bool TryGetRank(TEFeatureStorage _storage, out int _rank)
        {
            _rank = Cfg.cWorstRank;

            if (_storage == null || _storage.bPlayerStorage) return false;
            if (!Cfg.HasWhitelist) return false;

            var lootList = _storage.lootListName;
            if (string.IsNullOrEmpty(lootList)) return false;
            if (Cfg.LootListIgnore.Contains(lootList)) return false;

            return Cfg.TrackedRanks.TryGetValue(lootList, out _rank);
        }

        public static float Evaluate(TileEntityComposite _te, TEFeatureStorage _storage)
        {
            if (_te == null || _storage == null) return cIgnored;
            if (_storage.bPlayerStorage) return cIgnored;

            var lootList = _storage.lootListName;
            if (string.IsNullOrEmpty(lootList)) return cIgnored;
            if (Cfg.LootListIgnore.Contains(lootList)) return cIgnored;
            if (Cfg.HasWhitelist && !Cfg.TrackedRanks.ContainsKey(lootList)) return cIgnored;
            if (Cfg.LootListScores.TryGetValue(lootList, out var forced)) return forced;

            var size = _storage.GetContainerSize();
            var slots = size.x * size.y;

            var container = LootContainer.GetLootContainer(lootList, false);
            float score = 0f;

            if (container != null)
            {
                if (slots <= 0) slots = container.size.x * container.size.y;

                // A non-base quality template means better item quality than usual.
                if (!string.IsNullOrEmpty(container.lootQualityTemplate) &&
                    !container.lootQualityTemplate.Equals(cBaseQualityTemplate, StringComparison.OrdinalIgnoreCase))
                    score += Cfg.QualityTemplateBonus;
            }

            score += slots * Cfg.SlotWeight;
            score += _storage.LootStageBonus * Cfg.LootStageBonusWeight;
            score += _storage.LootStageMod * Cfg.LootStageModWeight;

            if (_storage.isJammed) score += Cfg.JammedBonus;

            // Locked containers (safes, gun safes) hold the best loot in a POI almost every time.
            if (_te.GetFeature<TEFeatureLockPickable>() != null) score += Cfg.LockedBonus;

            return score;
        }
    }
}
