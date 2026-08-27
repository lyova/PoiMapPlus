using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace PoiMapPlus
{
    /// <summary>State of a single POI: whether it is discovered and whether its best loot is gone.</summary>
    public class PoiRecord
    {
        public int X;
        public int Z;
        public bool Discovered;
        public int TopTouched;      // opened containers among the top N richest ones
        public int TopTotal;        // how many rich containers were actually found (<= TopContainerCount)
        public int AllTouched;      // opened containers overall, shown in the tooltip
        public int AllTotal;        // all lootable containers inside the POI
        public bool Scanned;        // POI was scanned at least once from up close
        public int BestOpenedRank;  // best rank ever opened here, 0 when nothing was
        public int BestFoundRank;   // best rank still standing in the POI, 0 when none

        // Weapon bags have destroy_on_close, so they vanish once emptied. BestOpenedRank
        // survives that: looting something at least as rich as whatever is still standing
        // counts as clearing the POI.
        public bool Cleared
        {
            get
            {
                if (!Scanned) return false;

                // Nothing worth tracking in this POI, so there is nothing left to come back for
                if (TopTotal <= 0) return true;

                if (TopTouched >= TopTotal) return true;

                return BestOpenedRank > 0 && BestFoundRank > 0 && BestOpenedRank <= BestFoundRank;
            }
        }

        public string Key => MakeKey(X, Z);

        public static string MakeKey(int _x, int _z) =>
            _x.ToString(CultureInfo.InvariantCulture) + "," + _z.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>POI state storage. Lives in the current save game folder.</summary>
    public static class PoiDb
    {
        const string cFileName = "PoiMapPlus.csv";
        const int cVersion = 3;

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

                foreach (var line in File.ReadAllLines(filePath))
                {
                    if (string.IsNullOrEmpty(line) || line[0] == '#') continue;

                    var p = line.Split(';');
                    if (p.Length < 8) continue;

                    var rec = new PoiRecord
                    {
                        X          = ParseInt(p[0]),
                        Z          = ParseInt(p[1]),
                        Discovered = p[2] == "1",
                        Scanned    = p[3] == "1",
                        TopTouched = ParseInt(p[4]),
                        TopTotal   = ParseInt(p[5]),
                        AllTouched = ParseInt(p[6]),
                        AllTotal   = ParseInt(p[7]),
                    };

                    // v1 rows stop at 8 columns, v2 carried a plain "was opened" flag
                    if (p.Length > 9)
                    {
                        rec.BestOpenedRank = ParseInt(p[8]);
                        rec.BestFoundRank = ParseInt(p[9]);
                    }
                    else if (p.Length > 8 && p[8] == "1")
                    {
                        rec.BestOpenedRank = Cfg.cWorstRank;
                    }

                    records[rec.Key] = rec;
                }

                Log.Out($"[PoiMapPlus] POI states loaded: {records.Count}");
            }
            catch (Exception e)
            {
                Log.Error("[PoiMapPlus] failed to read POI state: " + e.Message);
            }
        }

        public static void Save(bool _force = false)
        {
            if (!_force && !dirty) return;
            if (string.IsNullOrEmpty(filePath)) return;

            try
            {
                var sb = new StringBuilder();
                sb.Append("# PoiMapPlus v").Append(cVersion)
                  .AppendLine(" | x;z;discovered;scanned;topTouched;topTotal;allTouched;allTotal;bestOpenedRank;bestFoundRank");

                foreach (var rec in records.Values)
                {
                    sb.Append(rec.X).Append(';').Append(rec.Z).Append(';')
                      .Append(rec.Discovered ? '1' : '0').Append(';')
                      .Append(rec.Scanned ? '1' : '0').Append(';')
                      .Append(rec.TopTouched).Append(';').Append(rec.TopTotal).Append(';')
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
