using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace PoiMapPlus
{
    /// <summary>
    /// Containers this player has opened themselves. In multiplayer the game's own "touched" flag
    /// tells about everyone's looting, which a player out of the party that did it has no way of
    /// knowing - so there only containers listed here count as opened. In single player there is
    /// no one else, and the game's flag is used as it is.
    /// </summary>
    public static class OpenedContainers
    {
        const string cFileName = "PoiMapPlus.opened.csv";

        static readonly HashSet<Vector3i> opened = new HashSet<Vector3i>();
        static string filePath;
        static bool dirty;

        public static bool Applies
        {
            get
            {
                var cm = ConnectionManager.Instance;
                return cm != null && !cm.IsSinglePlayer;
            }
        }

        public static int Count => opened.Count;

        public static void Add(Vector3i _pos)
        {
            if (opened.Add(_pos)) dirty = true;
        }

        /// <summary>
        /// Whether a container counts as opened for this player. A container the game has reset
        /// leaves the list, so loot that came back reads as untouched until opened again.
        /// _adopt takes the game's word and remembers it: for POIs this player already knows are
        /// looted, and for records from before the list existed.
        /// </summary>
        public static bool Opened(Vector3i _pos, bool _touchedInWorld, bool _adopt)
        {
            if (!Applies) return _touchedInWorld;

            if (!_touchedInWorld)
            {
                if (opened.Remove(_pos)) dirty = true;
                return false;
            }

            if (_adopt) Add(_pos);
            return opened.Contains(_pos);
        }

        public static void Load(string _dir)
        {
            opened.Clear();
            dirty = false;
            filePath = string.IsNullOrEmpty(_dir) ? null : Path.Combine(_dir, cFileName);
            if (filePath == null || !File.Exists(filePath)) return;

            try
            {
                foreach (var line in File.ReadAllLines(filePath))
                {
                    if (string.IsNullOrEmpty(line) || line[0] == '#') continue;

                    var p = line.Split(';');
                    if (p.Length < 3) continue;

                    if (int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) &&
                        int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y) &&
                        int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var z))
                        opened.Add(new Vector3i(x, y, z));
                }
            }
            catch (Exception e)
            {
                Log.Warning("[PoiMapPlus] failed to read opened containers: " + e.Message);
            }
        }

        public static void Save(bool _force = false)
        {
            if (!_force && !dirty) return;
            if (string.IsNullOrEmpty(filePath)) return;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# PoiMapPlus containers this player opened | x;y;z");
                foreach (var pos in opened)
                    sb.Append(pos.x).Append(';').Append(pos.y).Append(';').Append(pos.z).AppendLine();

                File.WriteAllText(filePath, sb.ToString());
                dirty = false;
            }
            catch (Exception e)
            {
                Log.Warning("[PoiMapPlus] failed to save opened containers: " + e.Message);
            }
        }

        public static void Reset()
        {
            opened.Clear();
            filePath = null;
            dirty = false;
        }
    }
}
