using System;
using System.IO;

namespace PoiMapPlus
{
    /// <summary>
    /// What the map toggles are currently set to. Kept apart from Cfg: PoiMapPlus.xml holds the
    /// defaults a player edits by hand, this holds what they clicked, and the click wins.
    ///
    /// Stored next to the POI state in the save folder, so a world keeps its own preference and
    /// a fresh save starts from whatever the config says.
    /// </summary>
    public static class UiState
    {
        const string cFileName = "PoiMapPlus.ui";

        public static bool ShowMarkers = true;

        // The "Multiplayer" group: name who found each POI, and hide POIs only teammates have been to
        public static bool ShowFinder = true;
        public static bool OnlyMine;

        // This player's identity for the party features, made up once per save. Entity ids are
        // not reliable for that - a client can come back as a new entity.
        public static string PlayerKey;

        static string filePath;

        /// <summary>Markers off means nothing is drawn at all, badges included.</summary>
        public static bool TierVisible => ShowMarkers && Cfg.ShowTierLabel;
        public static bool ChestVisible => ShowMarkers && Cfg.ShowChestBadge;
        public static bool FinderVisible => ShowMarkers && ShowFinder;

        public static void Load()
        {
            ShowMarkers = true;
            ShowFinder = true;
            OnlyMine = false;
            PlayerKey = null;
            filePath = null;

            try
            {
                var dir = PoiDb.StateDir();
                if (string.IsNullOrEmpty(dir)) return;

                filePath = Path.Combine(dir, cFileName);

                // No early return here: a fresh save still needs its PlayerKey made below
                var lines = File.Exists(filePath) ? File.ReadAllLines(filePath) : new string[0];
                foreach (var line in lines)
                {
                    var sep = line.IndexOf('=');
                    if (sep <= 0) continue;

                    var key = line.Substring(0, sep).Trim();
                    var on = string.Equals(line.Substring(sep + 1).Trim(), "1", StringComparison.Ordinal);

                    switch (key)
                    {
                        case "ShowMarkers":    ShowMarkers = on; break;
                        case "ShowTierLabel":  Cfg.ShowTierLabel = on; break;
                        case "ShowChestBadge": Cfg.ShowChestBadge = on; break;
                        case "ShowFinder":     ShowFinder = on; break;
                        case "OnlyMine":       OnlyMine = on; break;
                        case "PlayerKey":      PlayerKey = line.Substring(sep + 1).Trim(); break;

                        case "IconScale":
                            if (float.TryParse(line.Substring(sep + 1).Trim(),
                                    System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out var scale)
                                && scale > 0.05f)
                                Cfg.IconScale = scale;
                            break;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warning("[PoiMapPlus] failed to read toggle state: " + e.Message);
            }

            if (string.IsNullOrEmpty(PlayerKey))
            {
                PlayerKey = Guid.NewGuid().ToString("N");
                Save();
            }
        }

        public static void Save()
        {
            if (string.IsNullOrEmpty(filePath)) return;

            try
            {
                File.WriteAllText(filePath,
                    "# PoiMapPlus map toggles" + Environment.NewLine +
                    "ShowMarkers=" + (ShowMarkers ? '1' : '0') + Environment.NewLine +
                    "ShowTierLabel=" + (Cfg.ShowTierLabel ? '1' : '0') + Environment.NewLine +
                    "ShowChestBadge=" + (Cfg.ShowChestBadge ? '1' : '0') + Environment.NewLine +
                    "ShowFinder=" + (ShowFinder ? '1' : '0') + Environment.NewLine +
                    "OnlyMine=" + (OnlyMine ? '1' : '0') + Environment.NewLine +
                    "PlayerKey=" + PlayerKey + Environment.NewLine +
                    "IconScale=" + Cfg.IconScale.ToString(
                        System.Globalization.CultureInfo.InvariantCulture) + Environment.NewLine);
            }
            catch (Exception e)
            {
                Log.Warning("[PoiMapPlus] failed to save toggle state: " + e.Message);
            }
        }

        public static void Reset()
        {
            ShowMarkers = true;
            ShowFinder = true;
            OnlyMine = false;
            PlayerKey = null;
            filePath = null;
        }
    }
}
