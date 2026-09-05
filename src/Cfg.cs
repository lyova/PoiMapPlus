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

        // Extra logging while troubleshooting: every container opened inside a POI, with its rank.
        public static bool Verbose;

        public static bool OnlyDiscovered = true;
        public static float ScanIntervalSeconds = 3f;
        public static int MaxMarkers = 600;

        // Marker size on the map, in UI units - UIRoot scales those to the screen on top. The
        // game computes
        //   units = clamp(50 * IconScale * clamp(1/(zoomScale*2), 0.02, 20), 9, 100)
        // and zoomScale runs 0.7 .. 6.15, which puts 1 at 25 units on the default zoom and 35
        // on the closest. Left at the vanilla value: the badges hang outside the skull rather
        // than inside it, so they do not need the icon to be blown up to fit.
        public static float IconScale = 1f;

        // Marker size presets behind the S/M/L buttons in the map settings popup. M is the
        // default IconScale, so the size the mod ships with is the middle option.
        public static float IconScaleSmall = 0.8f;
        public static float IconScaleMedium = 1f;
        public static float IconScaleLarge = 1.4f;

        // Zoom level past which the badges are dropped and only the skull is left. Stated as a
        // zoom rather than an icon size on purpose: the size it works out to follows IconScale,
        // so switching S/M/L does not silently move the cutoff. 1.4 sits between zoomScale 1.3
        // and 1.5, so the badges survive one step of zooming out and go away on the next.
        public static float BadgeMaxZoom = 1.4f;

        /// <summary>The cutoff in icon units, which is what the decorator can actually measure.</summary>
        public static int BadgeMinSize
        {
            get
            {
                var fac = 1f / (Math.Max(0.01f, BadgeMaxZoom) * 2f);
                if (fac > 20f) fac = 20f;
                if (fac < 0.02f) fac = 0.02f;
                return (int)Math.Round(50f * IconScale * fac);
            }
        }

        public static bool ShowChestBadge = true;
        public static bool ShowTierLabel = true;

        // Badge geometry, as fractions of the current icon size, so it holds at every zoom.
        // Measured from the centre of the skull, X right and Y up.
        public static string ChestSprite = "ui_game_symbol_treasure";
        // The offset points at the centre of the badge, so clearing the skull needs
        // hypot(X, Y) - ChestScale/2 > 0.5. At 0.60/0.46 that leaves a small visible gap.
        public static float ChestScale = 0.42f;
        public static float ChestOffsetX = 0.60f;
        public static float ChestOffsetY = 0.46f;

        // The offset is the bottom left corner of the text - the corner it grows away from -
        // so the same hypot(X, Y) > 0.5 rule keeps it off the skull. 0.44 puts the font at 11
        // units on the default zoom, where the original 0.34 gave 9.
        public static float TierScale = 0.44f;
        public static float TierOffsetX = 0.48f;
        public static float TierOffsetY = -0.52f;

        // Tier colour bands: up to MidTier is plain, MidTier itself is the "open" orange,
        // anything above turns red.
        public static int TierMid = 3;

        public static readonly Dictionary<string, float> LootListScores =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        public static readonly HashSet<string> LootListIgnore =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>One entry of the tracked container table.</summary>
        public class TrackedContainer
        {
            public int Rank;         // 1 is the richest
            public int MaxPoiTier;   // only counts in POIs up to this tier
        }

        // When this table is not empty only these loot lists count towards clearing a POI,
        // and MinContainerScore no longer applies - the table itself is the filter.
        public static readonly Dictionary<string, TrackedContainer> TrackedRanks =
            new Dictionary<string, TrackedContainer>(StringComparer.OrdinalIgnoreCase);

        public const int cAnyTier = int.MaxValue;

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

                        TrackedRanks[name] = new TrackedContainer
                        {
                            Rank = TryInt((string)e.Attribute("rank"), out var rank) ? rank : cWorstRank,
                            MaxPoiTier = TryInt((string)e.Attribute("max_poi_tier"), out var maxTier) ? maxTier : cAnyTier,
                        };
                    }
                }

                Log.Out("[PoiMapPlus] config loaded: " +
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
                case "MinContainerScore":     if (TryFloat(_value, out var f)) MinContainerScore = f; break;
                case "SlotWeight":            if (TryFloat(_value, out f)) SlotWeight = f; break;
                case "LockedBonus":           if (TryFloat(_value, out f)) LockedBonus = f; break;
                case "JammedBonus":           if (TryFloat(_value, out f)) JammedBonus = f; break;
                case "QualityTemplateBonus":  if (TryFloat(_value, out f)) QualityTemplateBonus = f; break;
                case "LootStageBonusWeight":  if (TryFloat(_value, out f)) LootStageBonusWeight = f; break;
                case "LootStageModWeight":    if (TryFloat(_value, out f)) LootStageModWeight = f; break;
                case "MinTier":               if (TryInt(_value, out var i)) MinTier = Math.Max(0, i); break;
                case "Verbose":               Verbose = Bool(_value); break;
                case "OnlyDiscovered":        OnlyDiscovered = Bool(_value); break;
                case "ScanIntervalSeconds":   if (TryFloat(_value, out f)) ScanIntervalSeconds = Math.Max(0.5f, f); break;
                case "MaxMarkers":            if (TryInt(_value, out i)) MaxMarkers = Math.Max(1, i); break;

                case "IconScale":             if (TryFloat(_value, out f)) IconScale = Math.Max(0.1f, f); break;
                case "BadgeMaxZoom":          if (TryFloat(_value, out f)) BadgeMaxZoom = Math.Max(0.1f, f); break;
                case "IconScaleSmall":        if (TryFloat(_value, out f)) IconScaleSmall = Math.Max(0.1f, f); break;
                case "IconScaleMedium":       if (TryFloat(_value, out f)) IconScaleMedium = Math.Max(0.1f, f); break;
                case "IconScaleLarge":        if (TryFloat(_value, out f)) IconScaleLarge = Math.Max(0.1f, f); break;
                case "ShowChestBadge":        ShowChestBadge = Bool(_value); break;
                case "ShowTierLabel":         ShowTierLabel = Bool(_value); break;
                case "ChestSprite":           if (!string.IsNullOrEmpty(_value)) ChestSprite = _value; break;
                case "ChestScale":            if (TryFloat(_value, out f)) ChestScale = f; break;
                case "ChestOffsetX":          if (TryFloat(_value, out f)) ChestOffsetX = f; break;
                case "ChestOffsetY":          if (TryFloat(_value, out f)) ChestOffsetY = f; break;
                case "TierScale":             if (TryFloat(_value, out f)) TierScale = f; break;
                case "TierOffsetX":           if (TryFloat(_value, out f)) TierOffsetX = f; break;
                case "TierOffsetY":           if (TryFloat(_value, out f)) TierOffsetY = f; break;
                case "TierMid":               if (TryInt(_value, out i)) TierMid = i; break;
            }
        }

        static bool Bool(string _s) => string.Equals(_s, "true", StringComparison.OrdinalIgnoreCase);

        static bool TryInt(string _s, out int _v) =>
            int.TryParse(_s, NumberStyles.Integer, CultureInfo.InvariantCulture, out _v);

        static bool TryFloat(string _s, out float _v) =>
            float.TryParse(_s, NumberStyles.Float, CultureInfo.InvariantCulture, out _v);
    }
}
