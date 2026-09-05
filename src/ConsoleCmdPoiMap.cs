using System.Collections.Generic;

namespace PoiMapPlus
{
    /// <summary>
    /// Console command so container ranks can be re-tuned without restarting the game.
    ///   poimap reload  - re-read PoiMapPlus.xml and refresh the markers
    ///   poimap rescan  - additionally drop stored POI state so ranks are recomputed on the spot
    ///   poimap status  - print what is currently tracked
    /// </summary>
    public class ConsoleCmdPoiMap : ConsoleCmdAbstract
    {
        static string modPath;

        public static void SetModPath(string _path) => modPath = _path;

        public override string[] getCommands() => new[] { "poimap", "pmp" };

        public override string getDescription() => "PoiMapPlus: reload config, rescan POIs, show status";

        public override string getHelp() =>
            "poimap reload  - re-read PoiMapPlus.xml and refresh map markers" + NewLine +
            "poimap rescan  - reload config, forget scanned POI loot state, rescan on the spot" + NewLine +
            "poimap status  - list tracked loot lists and stored POI records" + NewLine +
            "poimap here    - dump containers and stored state for the POI you are standing in" + NewLine +
            "poimap respawn [all] - pretend the emptied chest here (or everywhere) has been " +
            "restocked, to check the marker without waiting for the timer" + NewLine +
            "poimap verbose [on|off] - log every container opened inside a POI" + NewLine +
            "poimap navdump - list nav objects by class and tracking type";

        static string NewLine => System.Environment.NewLine;

        public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
        {
            var action = _params.Count > 0 ? _params[0].ToLower() : "status";

            switch (action)
            {
                case "reload":
                    Reload();
                    Log.Out("[PoiMapPlus] config reloaded");
                    break;

                case "rescan":
                    Reload();
                    ForgetLootState();
                    Log.Out("[PoiMapPlus] config reloaded and POI loot state cleared, re-enter a POI to rescan");
                    break;

                case "verbose":
                    Cfg.Verbose = _params.Count < 2 || !string.Equals(_params[1], "off", System.StringComparison.OrdinalIgnoreCase);
                    Log.Out($"[PoiMapPlus] verbose logging {(Cfg.Verbose ? "on" : "off")}");
                    break;

                case "here":
                    Here();
                    break;

                case "respawn":
                    FakeRespawn(_params.Count > 1 &&
                                string.Equals(_params[1], "all", System.StringComparison.OrdinalIgnoreCase));
                    break;

                case "navdump":
                    NavGuard.Dump();
                    break;

                default:
                    Status();
                    break;
            }
        }

        static void Reload()
        {
            if (string.IsNullOrEmpty(modPath))
            {
                Log.Warning("[PoiMapPlus] mod path unknown, cannot reload config");
                return;
            }

            Cfg.TrackedRanks.Clear();
            Cfg.LootListScores.Clear();
            Cfg.LootListIgnore.Clear();
            Cfg.Load(modPath);

            PoiMarkers.Clear();
            PoiMarkers.Refresh();
        }

        /// <summary>Drop what was learned about loot, keep which POIs are discovered.</summary>
        static void ForgetLootState()
        {
            foreach (var rec in PoiDb.All)
            {
                rec.Scanned = false;
                rec.HasTracked = false;
                rec.WatchedTouched = false;
                rec.WatchedEmpty = false;
                rec.WatchedTouchedHour = 0;
                rec.AllTouched = 0;
                rec.AllTotal = 0;
                rec.BestOpenedRank = 0;
                rec.BestFoundRank = 0;
            }

            PoiScanner.Clear();
            PoiDb.MarkDirty();
            PoiDb.Save();
            PoiMarkers.Clear();
            PoiMarkers.Refresh();
        }

        /// <summary>
        /// Backdate the watched container so its respawn is already due, for testing the marker
        /// without waiting days or winding the world clock forward with settime.
        ///
        /// Only touches POIs where the chest was actually left empty - the ones the game would
        /// restock. It changes the mod's own record, not the world, so the next scan of that POI
        /// puts the truth back.
        /// </summary>
        static void FakeRespawn(bool _all)
        {
            if (!LootRespawn.Enabled)
            {
                Log.Out("[PoiMapPlus] loot respawn is disabled for this world " +
                        "(Loot Respawn Time), so nothing would come back. Set it with: " +
                        "setgamepref LootRespawnDays 7");
                return;
            }

            var due = LootRespawn.NowHour - LootRespawn.Days * 24;
            var touched = 0;

            if (_all)
            {
                foreach (var rec in PoiDb.All)
                    if (Backdate(rec, due))
                        touched++;
            }
            else
            {
                var world = GameManager.Instance != null ? GameManager.Instance.World : null;
                var player = world != null ? world.GetPrimaryPlayer() : null;
                var pi = player != null ? PoiRegistry.FindAt(player.position) : null;

                if (pi == null)
                {
                    Log.Out("[PoiMapPlus] stand inside a POI, or use \"poimap respawn all\"");
                    return;
                }

                var rec = PoiDb.Find(pi.boundingBoxPosition.x, pi.boundingBoxPosition.z);
                if (rec == null || !Backdate(rec, due))
                {
                    Log.Out($"[PoiMapPlus] '{PoiRegistry.DisplayName(pi)}' has no emptied chest to " +
                            "bring back - loot it, leave it empty, then walk out of the POI first");
                    return;
                }
                touched = 1;
            }

            PoiDb.MarkDirty();
            PoiDb.Save();
            PoiMarkers.Clear();
            PoiMarkers.Refresh();

            Log.Out($"[PoiMapPlus] {touched} POI(s) backdated past the {LootRespawn.Days} day " +
                    "respawn - open the map: the skull turns orange again and the tooltip says " +
                    "\"loot respawned\". Walking back in re-scans and corrects it.");
        }

        static bool Backdate(PoiRecord _rec, int _dueHour)
        {
            if (!_rec.WatchedTouched || !_rec.WatchedEmpty) return false;
            _rec.WatchedTouchedHour = _dueHour;
            return true;
        }

        /// <summary>Why is this POI's marker the colour it is?</summary>
        static void Here()
        {
            var world = GameManager.Instance != null ? GameManager.Instance.World : null;
            var player = world != null ? world.GetPrimaryPlayer() : null;
            if (player == null) { Log.Out("[PoiMapPlus] no local player"); return; }

            var pi = PoiRegistry.FindAt(player.position);
            if (pi == null)
            {
                Log.Out($"[PoiMapPlus] not inside any indexed POI at {player.position}");
                return;
            }

            foreach (var line in PoiScanner.Describe(pi))
                Log.Out("[PoiMapPlus] " + line);
        }

        static void Status()
        {
            var ranks = new List<string>();
            foreach (var pair in Cfg.TrackedRanks)
                ranks.Add(pair.Value.MaxPoiTier == Cfg.cAnyTier
                    ? $"{pair.Key}=r{pair.Value.Rank}"
                    : $"{pair.Key}=r{pair.Value.Rank}/maxT{pair.Value.MaxPoiTier}");
            ranks.Sort();

            var discovered = 0;
            var cleared = 0;
            var respawned = 0;
            var stocked = 0;
            foreach (var rec in PoiDb.All)
            {
                if (rec.Discovered) discovered++;
                if (rec.Cleared) cleared++;
                if (rec.LootRespawned) respawned++;
                if (rec.Chest == ChestState.Stocked) stocked++;
            }

            Log.Out($"[PoiMapPlus] tracked={string.Join(", ", ranks.ToArray())}");
            Log.Out($"[PoiMapPlus] POIs indexed={PoiRegistry.Pois.Count}, discovered={discovered}, cleared={cleared}");
            Log.Out($"[PoiMapPlus] loot respawn: {(LootRespawn.Enabled ? LootRespawn.Days + " day(s)" : "disabled")}" +
                    $", now hour {LootRespawn.NowHour}, respawned since last visit={respawned}, chests left stocked={stocked}");
            Log.Out($"[PoiMapPlus] markers: iconScale={Cfg.IconScale}, " +
                    $"chest={(Cfg.ShowChestBadge ? Cfg.ChestSprite : "off")}, tierLabel={Cfg.ShowTierLabel}, "
                    + $"markers={(UiState.ShowMarkers ? "on" : "off")}");
            Log.Out($"[PoiMapPlus] icon size last drawn: {(MarkerDecor.LastIconSize > 0 ? MarkerDecor.LastIconSize + " units" : "unknown, open the map once")}" +
                    $", badges show from {Cfg.BadgeMinSize} up (zoom <= {Cfg.BadgeMaxZoom})");
        }
    }
}
