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
                rec.TopTouched = 0;
                rec.TopTotal = 0;
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
            foreach (var rec in PoiDb.All)
            {
                if (rec.Discovered) discovered++;
                if (rec.Cleared) cleared++;
            }

            Log.Out($"[PoiMapPlus] topN={Cfg.TopContainerCount}, tracked={string.Join(", ", ranks.ToArray())}");
            Log.Out($"[PoiMapPlus] POIs indexed={PoiRegistry.Pois.Count}, discovered={discovered}, cleared={cleared}");
        }
    }
}
