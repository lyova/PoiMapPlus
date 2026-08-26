using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml.Linq;

namespace PoiMapPlus
{
    /// <summary>Mod settings, read from PoiMapPlus.xml next to ModInfo.xml.</summary>
    public static class Cfg
    {
        // How many of the richest containers must be opened before a POI counts as cleared.
        public static int TopContainerCount = 3;
        // Containers scoring below this are junk (trash bins, mailboxes) and never enter the ranking.
        public static float MinContainerScore = 25f;

        // Weights that make up a container's loot value.
        public static float SlotWeight = 1f;
        public static float LockedBonus = 30f;
        public static float JammedBonus = 15f;
        public static float QualityTemplateBonus = 25f;
        public static float LootStageBonusWeight = 3f;
        public static float LootStageModWeight = 100f;

        // Difficulty tier a POI needs before it gets a marker. Tier 0 covers sheds, ruined
        // booths and similar filler that rarely holds a tracked container.
        public static int MinTier = 1;

        public static bool OnlyDiscovered = true;
        public static float ScanIntervalSeconds = 3f;
        public static int MaxMarkers = 600;

        public static readonly Dictionary<string, float> LootListScores =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        public static readonly HashSet<string> LootListIgnore =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // When this table is not empty only these loot lists count towards clearing a POI,
        // and MinContainerScore no longer applies - the table itself is the filter.
        // Rank 1 is the richest container; ties are allowed.
        public static readonly Dictionary<string, int> TrackedRanks =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public const int cWorstRank = 9999;

        public static bool HasWhitelist => TrackedRanks.Count > 0;

        public static void Load(string _modPath)
        {
            var file = Path.Combine(_modPath, "PoiMapPlus.xml");
            if (!File.Exists(file))
            {
                Log.Warning("[PoiMapPlus] " + file + " not found, using defaults");
                return;
            }

            try
            {
                var root = XDocument.Load(file).Root;
                if (root == null) return;

                foreach (var e in root.Elements("property"))
                {
                    var name = (string)e.Attribute("name");
                    var value = (string)e.Attribute("value");
                    if (string.IsNullOrEmpty(name) || value == null) continue;
                    Apply(name, value);
                }

                var scores = root.Element("loot_scores");
                if (scores != null)
                {
                    foreach (var e in scores.Elements("loot_list"))
                    {
                        var name = (string)e.Attribute("name");
                        if (string.IsNullOrEmpty(name)) continue;

                        if (string.Equals((string)e.Attribute("ignore"), "true", StringComparison.OrdinalIgnoreCase))
                        {
                            LootListIgnore.Add(name);
                            continue;
                        }

                        if (TryFloat((string)e.Attribute("score"), out var score))
                            LootListScores[name] = score;
                    }
                }

                var tracked = root.Element("tracked_loot_lists");
                if (tracked != null)
                {
                    foreach (var e in tracked.Elements("loot_list"))
                    {
                        var name = (string)e.Attribute("name");
                        if (string.IsNullOrEmpty(name)) continue;

                        TrackedRanks[name] = TryInt((string)e.Attribute("rank"), out var rank) ? rank : cWorstRank;
                    }
                }

                Log.Out($"[PoiMapPlus] config loaded: topN={TopContainerCount}, " +
                        (HasWhitelist ? $"tracked lists={TrackedRanks.Count}" : $"threshold={MinContainerScore}") +
                        $", overrides={LootListScores.Count}, ignored={LootListIgnore.Count}");
            }
            catch (Exception e)
            {
                Log.Error("[PoiMapPlus] failed to read PoiMapPlus.xml: " + e.Message);
            }
        }

        static void Apply(string _name, string _value)
        {
            switch (_name)
            {
                case "TopContainerCount":     if (TryInt(_value, out var i)) TopContainerCount = Math.Max(1, i); break;
                case "MinContainerScore":     if (TryFloat(_value, out var f)) MinContainerScore = f; break;
                case "SlotWeight":            if (TryFloat(_value, out f)) SlotWeight = f; break;
                case "LockedBonus":           if (TryFloat(_value, out f)) LockedBonus = f; break;
                case "JammedBonus":           if (TryFloat(_value, out f)) JammedBonus = f; break;
                case "QualityTemplateBonus":  if (TryFloat(_value, out f)) QualityTemplateBonus = f; break;
                case "LootStageBonusWeight":  if (TryFloat(_value, out f)) LootStageBonusWeight = f; break;
                case "LootStageModWeight":    if (TryFloat(_value, out f)) LootStageModWeight = f; break;
                case "MinTier":               if (TryInt(_value, out i)) MinTier = Math.Max(0, i); break;
                case "OnlyDiscovered":        OnlyDiscovered = string.Equals(_value, "true", StringComparison.OrdinalIgnoreCase); break;
                case "ScanIntervalSeconds":   if (TryFloat(_value, out f)) ScanIntervalSeconds = Math.Max(0.5f, f); break;
                case "MaxMarkers":            if (TryInt(_value, out i)) MaxMarkers = Math.Max(1, i); break;
            }
        }

        static bool TryInt(string _s, out int _v) =>
            int.TryParse(_s, NumberStyles.Integer, CultureInfo.InvariantCulture, out _v);

        static bool TryFloat(string _s, out float _v) =>
            float.TryParse(_s, NumberStyles.Float, CultureInfo.InvariantCulture, out _v);
    }
}
