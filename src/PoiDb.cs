using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace PoiMapPlus
{
    /// <summary>What the marker knows about the container that decides this POI.</summary>
    public enum ChestState
    {
        Unknown = 0,  // nothing opened here yet, no badge
        Empty = 1,    // left empty, so the game will restock it
        Stocked = 2,  // something is still inside, which blocks the respawn
    }

    /// <summary>State of a single POI: whether it is discovered and whether its best loot is gone.</summary>
    public class PoiRecord
    {
        public int X;
        public int Z;
        public bool Discovered;
        public bool Scanned;        // POI was scanned at least once from up close

        // The POI holds at least one container the mod tracks. When it does not there is
        // nothing to come back for, so such a POI counts as cleared right away.
        public bool HasTracked;

        // The watched container: the best ranked one actually present, preferring an already
        // looted one among equal ranks. It alone decides the marker, which is why the whole
        // record needs no per-container table.
        public bool WatchedTouched;
        public bool WatchedEmpty;
        public int WatchedTouchedHour;

        public int AllTouched;      // opened containers overall, shown in the tooltip
        public int AllTotal;        // all lootable containers inside the POI
        public int BestOpenedRank;  // best rank ever opened here, 0 when nothing was
        public int BestFoundRank;   // best rank still standing in the POI, 0 when none

        /// <summary>Whether the loot has been taken, ignoring respawn.</summary>
        public bool ClearedRaw
        {
            get
            {
                if (!Scanned) return false;

                // Nothing worth tracking in this POI, so there is nothing left to come back for
                if (!HasTracked) return true;

                if (WatchedTouched) return true;

                // Weapon bags have destroy_on_close, so they vanish once emptied and no scan can
                // see them any more. BestOpenedRank survives that: having looted something richer
                // than anything still standing counts as clearing the POI.
                //
                // Strictly richer, not "as rich as": after a respawn the watched container is
                // untouched again while BestOpenedRank still holds its rank, and "<=" would then
                // keep calling the POI cleared. Two identical bags behave the same way - one
                // emptied, one untouched is not a cleared POI either.
                return BestOpenedRank > 0 && BestFoundRank > 0 && BestOpenedRank < BestFoundRank;
            }
        }

        /// <summary>The watched container was left empty and its respawn timer has run out.</summary>
        public bool LootRespawned =>
            WatchedTouched && WatchedEmpty && LootRespawn.IsDue(WatchedTouchedHour);

        public bool Cleared => ClearedRaw && !LootRespawned;

        /// <summary>
        /// Which chest badge the marker carries. Once the loot is back the badge goes away
        /// entirely: what the record still remembers describes the chest as it was before the
        /// restock, and showing "left empty" over a POI that is worth robbing again would be
        /// worse than showing nothing. Such a POI reads exactly like an untouched one.
        /// </summary>
        public ChestState Chest =>
            !WatchedTouched || LootRespawned ? ChestState.Unknown
            : WatchedEmpty ? ChestState.Empty
            : ChestState.Stocked;

        public string Key => MakeKey(X, Z);

        public static string MakeKey(int _x, int _z) =>
            _x.ToString(CultureInfo.InvariantCulture) + "," + _z.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>POI state storage. Lives in the current save game folder.</summary>
    public static class PoiDb
    {
        const string cFileName = "PoiMapPlus.csv";
        const int cVersion = 4;

        // v4 rows carry 12 columns, everything before it stopped at 10
        const int cColumnsV4 = 12;

        static readonly Dictionary<string, PoiRecord> records = new Dictionary<string, PoiRecord>();
        static string filePath;
        static bool dirty;

        public static IEnumerable<PoiRecord> All => records.Values;

        public static PoiRecord GetOrCreate(int _x, int _z)
        {
            var key = PoiRecord.MakeKey(_x, _z);
            if (!records.TryGetValue(key, out var rec))
            {
                rec = new PoiRecord { X = _x, Z = _z };
                records[key] = rec;
            }
            return rec;
        }

        public static PoiRecord Find(int _x, int _z) =>
            records.TryGetValue(PoiRecord.MakeKey(_x, _z), out var rec) ? rec : null;

        public static void MarkDirty() => dirty = true;

        public static void Load()
        {
            records.Clear();
            dirty = false;
            filePath = null;

            try
            {
                var dir = GameIO.GetSaveGameDir();
                if (string.IsNullOrEmpty(dir))
                {
                    Log.Warning("[PoiMapPlus] save game folder unavailable, POI state will not persist");
                    return;
                }

                filePath = Path.Combine(dir, cFileName);
                if (!File.Exists(filePath))
                {
                    Log.Out("[PoiMapPlus] fresh save, no POI state yet");
                    return;
                }

                var upgraded = 0;

                foreach (var line in File.ReadAllLines(filePath))
                {
                    if (string.IsNullOrEmpty(line) || line[0] == '#') continue;

                    var p = line.Split(';');
                    if (p.Length < 8) continue;

                    var rec = p.Length >= cColumnsV4 ? ReadV4(p) : ReadLegacy(p, ref upgraded);
                    records[rec.Key] = rec;
                }

                Log.Out($"[PoiMapPlus] POI states loaded: {records.Count}" +
                        (upgraded > 0 ? $" ({upgraded} upgraded from an older format)" : ""));
            }
            catch (Exception e)
            {
                Log.Error("[PoiMapPlus] failed to read POI state: " + e.Message);
            }
        }

        static PoiRecord ReadV4(string[] _p) => new PoiRecord
        {
            X                  = ParseInt(_p[0]),
            Z                  = ParseInt(_p[1]),
            Discovered         = _p[2] == "1",
            Scanned            = _p[3] == "1",
            HasTracked         = _p[4] == "1",
            WatchedTouched     = _p[5] == "1",
            WatchedEmpty       = _p[6] == "1",
            WatchedTouchedHour = ParseInt(_p[7]),
            AllTouched         = ParseInt(_p[8]),
            AllTotal           = ParseInt(_p[9]),
            BestOpenedRank     = ParseInt(_p[10]),
            BestFoundRank      = ParseInt(_p[11]),
        };

        /// <summary>
        /// v1-v3 rows: x;z;discovered;scanned;topTouched;topTotal;allTouched;allTotal[;bestOpened;bestFound].
        /// Those never recorded whether a looted container was left empty, so the respawn
        /// prediction stays off for them until the POI is scanned again.
        /// </summary>
        static PoiRecord ReadLegacy(string[] _p, ref int _upgraded)
        {
            _upgraded++;

            var rec = new PoiRecord
            {
                X              = ParseInt(_p[0]),
                Z              = ParseInt(_p[1]),
                Discovered     = _p[2] == "1",
                Scanned        = _p[3] == "1",
                WatchedTouched = ParseInt(_p[4]) > 0,
                HasTracked     = ParseInt(_p[5]) > 0,
                AllTouched     = ParseInt(_p[6]),
                AllTotal       = ParseInt(_p[7]),
            };

            // v1 rows stop at 8 columns, v2 carried a plain "was opened" flag
            if (_p.Length > 9)
            {
                rec.BestOpenedRank = ParseInt(_p[8]);
                rec.BestFoundRank = ParseInt(_p[9]);
            }
            else if (_p.Length > 8 && _p[8] == "1")
            {
                rec.BestOpenedRank = Cfg.cWorstRank;
            }

            return rec;
        }

        public static void Save(bool _force = false)
        {
            if (!_force && !dirty) return;
            if (string.IsNullOrEmpty(filePath)) return;

            try
            {
                var sb = new StringBuilder();
                sb.Append("# PoiMapPlus v").Append(cVersion)
                  .AppendLine(" | x;z;discovered;scanned;hasTracked;watchedTouched;watchedEmpty;" +
                              "watchedTouchedHour;allTouched;allTotal;bestOpenedRank;bestFoundRank");

                foreach (var rec in records.Values)
                {
                    sb.Append(rec.X).Append(';').Append(rec.Z).Append(';')
                      .Append(rec.Discovered ? '1' : '0').Append(';')
                      .Append(rec.Scanned ? '1' : '0').Append(';')
                      .Append(rec.HasTracked ? '1' : '0').Append(';')
                      .Append(rec.WatchedTouched ? '1' : '0').Append(';')
                      .Append(rec.WatchedEmpty ? '1' : '0').Append(';')
                      .Append(rec.WatchedTouchedHour).Append(';')
                      .Append(rec.AllTouched).Append(';').Append(rec.AllTotal).Append(';')
                      .Append(rec.BestOpenedRank).Append(';').Append(rec.BestFoundRank)
                      .AppendLine();
                }

                File.WriteAllText(filePath, sb.ToString());
                dirty = false;
            }
            catch (Exception e)
            {
                Log.Error("[PoiMapPlus] failed to save POI state: " + e.Message);
            }
        }

        public static void Reset()
        {
            records.Clear();
            filePath = null;
            dirty = false;
        }

        static int ParseInt(string _s) =>
            int.TryParse(_s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }
}
