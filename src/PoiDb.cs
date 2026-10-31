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

        // When the loot state above was last seen (world time). The world is one for the whole
        // party, so between two players' records the fresher one is the truth.
        public ulong ScannedAt;

        // Greyed out by a click on the map, the same toggle every vanilla marker has. Personal,
        // never shared.
        public bool Dimmed;

        // Not stored: the row predates OpenedContainers, so its next scan adopts what the game
        // says is opened instead of finding nothing on the list
        public bool Adopt;

        // Who uncovered the POI first, among this player and their party, and when (world
        // time). Older saves never recorded it, which reads as "this player, long ago".
        // FinderKey is the finder's PartyShare.LocalKey: names repeat, entity ids can change
        public string FinderName;
        public string FinderKey;
        public ulong FinderAt;

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

        public PoiRecord Clone() => (PoiRecord)MemberwiseClone();

        public static string MakeKey(int _x, int _z) =>
            _x.ToString(CultureInfo.InvariantCulture) + "," + _z.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>POI state storage. Lives in the current save game folder.</summary>
    public static class PoiDb
    {
        const string cFileName = "PoiMapPlus.csv";
        const int cVersion = 5;

        // v4 rows carry 12 columns, everything before it stopped at 10. v5 appends five, so a
        // v4 build reading a v5 file still finds everything it knows where it expects it.
        const int cColumnsV4 = 12;
        const int cColumnsV5 = 17;

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

        /// <summary>
        /// Where the mod keeps its files for the current game. A client on someone else's server
        /// has no world save of its own: the game keeps its data under SavesLocal instead.
        /// </summary>
        public static bool StateDirKnown
        {
            get
            {
                var cm = ConnectionManager.Instance;
                return cm == null || cm.IsServer ||
                       !string.IsNullOrEmpty(GamePrefs.GetString(EnumGamePrefs.GameGuidClient));
            }
        }

        public static string StateDir()
        {
            var cm = ConnectionManager.Instance;
            if (cm == null || cm.IsServer) return GameIO.GetSaveGameDir();

            var dir = GameIO.GetSaveGameLocalDir();
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>The local player has uncovered this POI: credit them unless someone was earlier.</summary>
        public static void MarkDiscovered(PoiRecord _rec)
        {
            if (_rec == null || _rec.Discovered) return;

            _rec.Discovered = true;
            if (string.IsNullOrEmpty(_rec.FinderName))
            {
                _rec.FinderName = PartyShare.LocalName;
                _rec.FinderKey = PartyShare.LocalKey;
                _rec.FinderAt = PartyShare.Now;
            }
            dirty = true;
        }

        public static void Load()
        {
            // A client may have scanned something before its files could be found
            var early = new List<PoiRecord>(records.Values);

            records.Clear();
            dirty = false;
            filePath = null;

            try
            {
                var dir = StateDir();
                if (string.IsNullOrEmpty(dir))
                {
                    Log.Warning("[PoiMapPlus] save game folder unavailable, POI state will not persist");
                    return;
                }

                filePath = Path.Combine(dir, cFileName);
                OpenedContainers.Load(dir);
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
                    rec.Adopt = p.Length < cColumnsV5;
                    records[rec.Key] = rec;
                }

                Log.Out($"[PoiMapPlus] POI states loaded: {records.Count}" +
                        (upgraded > 0 ? $" ({upgraded} upgraded from an older format)" : ""));
            }
            catch (Exception e)
            {
                Log.Error("[PoiMapPlus] failed to read POI state: " + e.Message);
            }
            finally
            {
                foreach (var rec in early)
                    if (!records.ContainsKey(rec.Key))
                    {
                        records[rec.Key] = rec;
                        dirty = true;
                    }
            }
        }

        /// <summary>One record as a line of the state file. The party file uses the same rows.</summary>
        public static void AppendRow(StringBuilder _sb, PoiRecord _rec)
        {
            _sb.Append(_rec.X).Append(';').Append(_rec.Z).Append(';')
               .Append(_rec.Discovered ? '1' : '0').Append(';')
               .Append(_rec.Scanned ? '1' : '0').Append(';')
               .Append(_rec.HasTracked ? '1' : '0').Append(';')
               .Append(_rec.WatchedTouched ? '1' : '0').Append(';')
               .Append(_rec.WatchedEmpty ? '1' : '0').Append(';')
               .Append(_rec.WatchedTouchedHour).Append(';')
               .Append(_rec.AllTouched).Append(';').Append(_rec.AllTotal).Append(';')
               .Append(_rec.BestOpenedRank).Append(';').Append(_rec.BestFoundRank).Append(';')
               .Append(_rec.ScannedAt).Append(';')
               .Append(_rec.Dimmed ? '1' : '0').Append(';')
               .Append(_rec.FinderAt).Append(';')
               .Append(CleanName(_rec.FinderKey)).Append(';')
               .Append(CleanName(_rec.FinderName))
               .AppendLine();
        }

        /// <summary>A current format row back into a record, or null when it is too short.</summary>
        public static PoiRecord ParseRow(string _line)
        {
            var p = _line.Split(';');
            return p.Length >= cColumnsV5 ? ReadV4(p) : null;
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
            ScannedAt          = _p.Length >= cColumnsV5 ? ParseULong(_p[12]) : 0,
            Dimmed             = _p.Length >= cColumnsV5 && _p[13] == "1",
            FinderAt           = _p.Length >= cColumnsV5 ? ParseULong(_p[14]) : 0,
            FinderKey          = _p.Length >= cColumnsV5 && _p[15].Length > 0 ? _p[15] : null,
            FinderName         = _p.Length >= cColumnsV5 && _p[16].Length > 0 ? _p[16] : null,
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
            OpenedContainers.Save(_force);
            if (!_force && !dirty) return;
            if (string.IsNullOrEmpty(filePath)) return;

            try
            {
                var sb = new StringBuilder();
                sb.Append("# PoiMapPlus v").Append(cVersion)
                  .AppendLine(" | x;z;discovered;scanned;hasTracked;watchedTouched;watchedEmpty;" +
                              "watchedTouchedHour;allTouched;allTotal;bestOpenedRank;bestFoundRank;" +
                              "scannedAt;dimmed;finderAt;finderKey;finderName");

                foreach (var rec in records.Values) AppendRow(sb, rec);

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
            OpenedContainers.Reset();
            records.Clear();
            filePath = null;
            dirty = false;
        }

        static int ParseInt(string _s) =>
            int.TryParse(_s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

        static ulong ParseULong(string _s) =>
            ulong.TryParse(_s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

        /// <summary>A player name is free text; the file is split on ';' and on lines.</summary>
        public static string CleanName(string _name) =>
            string.IsNullOrEmpty(_name) ? string.Empty
            : _name.Replace(';', ',').Replace('\r', ' ').Replace('\n', ' ');
    }
}
