using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace PoiMapPlus
{
    /// <summary>
    /// Markers shared within a party: what this player sends, and what came from the others.
    ///
    /// A teammate's markers stay after they log off or leave the party, and are kept in this
    /// player's save so they survive a restart too.
    ///
    /// Out of a party received markers still show, but a POI this player has a record of
    /// themselves is drawn from that record alone - nothing of the others gets mixed in until
    /// they are in a party again. Teammates are told apart by the key each player's mod makes
    /// up, not by entity id.
    /// </summary>
    public static class PartyShare
    {
        const float cTickSeconds = 2f;
        const float cDeltaSeconds = 5f;
        const int cChunk = 400;
        const string cFileName = "PoiMapPlus.party.csv";

        class Peer
        {
            public string Name;
            public readonly Dictionary<string, PoiRecord> Pois = new Dictionary<string, PoiRecord>();
        }

        static readonly Dictionary<string, Peer> peers = new Dictionary<string, Peer>();

        // Signature of each record as the party last received it, so only changes go out
        static readonly Dictionary<string, int> sentSig = new Dictionary<string, int>();

        static readonly HashSet<int> knownMembers = new HashSet<int>();
        static readonly List<int> scratchIds = new List<int>();

        static float nextTick;
        static float nextDelta;

        static string filePath;
        static bool storeDirty;

        public static EntityPlayerLocal Local =>
            GameManager.Instance != null && GameManager.Instance.World != null
                ? GameManager.Instance.World.GetPrimaryPlayer()
                : null;

        public static string LocalName => Local != null ? Local.PlayerDisplayName ?? string.Empty : string.Empty;

        public static string LocalKey => UiState.PlayerKey ?? string.Empty;

        public static ulong Now =>
            GameManager.Instance != null && GameManager.Instance.World != null ? GameManager.Instance.World.worldTime : 0;

        /// <summary>The local player has at least one other person in their party.</summary>
        public static bool InParty
        {
            get
            {
                var party = Local?.Party;
                return party?.MemberList != null && party.MemberList.Count > 1;
            }
        }

        /// <summary>Whether the connection can carry the share at all: the server must run the mod too.</summary>
        public static bool Available
        {
            get
            {
                var cm = ConnectionManager.Instance;
                if (cm == null || cm.IsSinglePlayer) return false;
                if (cm.IsServer) return true;

                var mappings = NetPackageManager.PackageMappings;
                return mappings != null && Array.IndexOf(mappings, typeof(NetPackagePoiMapPlusShare)) >= 0;
            }
        }

        public static void Tick()
        {
            if (Time.time < nextTick) return;
            nextTick = Time.time + cTickSeconds;

            var me = Local;
            if (me == null || LocalKey.Length == 0) return;

            if (!Available)
            {
                knownMembers.Clear();
                sentSig.Clear();
                return;
            }

            scratchIds.Clear();
            var members = me.Party?.MemberList;
            if (members != null)
                foreach (var m in members)
                    if (m != null && m.entityId != me.entityId)
                        scratchIds.Add(m.entityId);

            if (scratchIds.Count == 0)
            {
                knownMembers.Clear();
                sentSig.Clear();
                return;
            }

            var newcomer = false;
            foreach (var id in scratchIds)
                if (!knownMembers.Contains(id)) newcomer = true;

            knownMembers.Clear();
            foreach (var id in scratchIds) knownMembers.Add(id);

            if (newcomer)
            {
                SendFull(true);
                return;
            }

            if (Time.time >= nextDelta)
            {
                nextDelta = Time.time + cDeltaSeconds;
                SendDelta();
            }
        }

        public static void Receive(int _senderId, string _senderKey, string _senderName, byte _flags,
                                   List<PoiRecord> _entries)
        {
            var me = Local;
            if (me == null || _senderId == me.entityId) return;
            if (string.IsNullOrEmpty(_senderKey) || _senderKey == LocalKey) return;

            var party = me.Party;
            if (party == null || !party.ContainsMember(_senderId)) return;

            var full = (_flags & NetPackagePoiMapPlusShare.FlagFull) != 0;
            var hasData = full || (_entries != null && _entries.Count > 0);

            if (hasData)
            {
                if (!peers.TryGetValue(_senderKey, out var peer))
                {
                    peer = new Peer();
                    peers[_senderKey] = peer;
                }

                peer.Name = _senderName;
                if (full)
                {
                    peer.Pois.Clear();
                    Log.Out($"[PoiMapPlus] party: {_senderName} ({_senderKey}) shared {_entries?.Count ?? 0} POI(s)");
                }

                if (_entries != null)
                    foreach (var rec in _entries)
                    {
                        peer.Pois[rec.Key] = rec;
                        AdoptFinder(rec);
                        AdoptState(rec);
                    }

                storeDirty = true;
            }

            if ((_flags & NetPackagePoiMapPlusShare.FlagRequest) != 0 && Available)
                SendFull(false);
        }

        /// <summary>
        /// A teammate knows more about a POI this player has a record of - looted where this
        /// player saw nothing, or a later look at the same: the record takes it over for good, so
        /// it is still there out of the party.
        /// </summary>
        static void AdoptState(PoiRecord _theirs)
        {
            var own = PoiDb.Find(_theirs.X, _theirs.Z);
            if (own == null || !own.Discovered || !Better(_theirs, own)) return;

            own.Scanned = _theirs.Scanned;
            own.HasTracked = _theirs.HasTracked;
            own.WatchedTouched = _theirs.WatchedTouched;
            own.WatchedEmpty = _theirs.WatchedEmpty;
            own.WatchedTouchedHour = _theirs.WatchedTouchedHour;
            own.AllTouched = _theirs.AllTouched;
            own.AllTotal = _theirs.AllTotal;
            own.BestOpenedRank = _theirs.BestOpenedRank;
            own.BestFoundRank = _theirs.BestFoundRank;
            own.ScannedAt = _theirs.ScannedAt;
            PoiDb.MarkDirty();
        }

        /// <summary>
        /// In a party, catch this player's record up with everything the teammates know of the
        /// POI. Covers records made after the teammates' arrived, which Receive never saw.
        /// </summary>
        public static void CatchUp(int _x, int _z)
        {
            if (peers.Count == 0 || !InParty) return;

            var key = PoiRecord.MakeKey(_x, _z);
            foreach (var peer in peers.Values)
                if (peer.Pois.TryGetValue(key, out var rec))
                {
                    AdoptFinder(rec);
                    AdoptState(rec);
                }
        }

        /// <summary>A teammate was there first: this player's own record names them from now on.</summary>
        static void AdoptFinder(PoiRecord _theirs)
        {
            if (string.IsNullOrEmpty(_theirs.FinderName) || string.IsNullOrEmpty(_theirs.FinderKey)) return;

            var own = PoiDb.Find(_theirs.X, _theirs.Z);
            if (own == null || !own.Discovered) return;
            if (_theirs.FinderAt >= own.FinderAt) return;

            own.FinderName = _theirs.FinderName;
            own.FinderKey = _theirs.FinderKey;
            own.FinderAt = _theirs.FinderAt;
            PoiDb.MarkDirty();
        }

        /// <summary>Whether any teammate has a record for this POI.</summary>
        public static bool HasShared(int _x, int _z)
        {
            if (peers.Count == 0) return false;

            var key = PoiRecord.MakeKey(_x, _z);
            foreach (var peer in peers.Values)
                if (peer.Pois.ContainsKey(key)) return true;
            return false;
        }

        /// <summary>
        /// The loot state to draw. Each record only knows about the containers its own player
        /// opened, so knowing it is looted beats not knowing, an emptied chest beats a stocked one,
        /// and the latest look decides between equals. A tie keeps this player's own. Out of a
        /// party an own record is used as it is.
        /// </summary>
        public static PoiRecord FreshestFor(int _x, int _z, PoiRecord _own)
        {
            var best = _own != null && _own.Discovered ? _own : null;
            if (peers.Count == 0 || (best != null && !InParty)) return best ?? _own;

            var key = PoiRecord.MakeKey(_x, _z);
            foreach (var peer in peers.Values)
            {
                if (!peer.Pois.TryGetValue(key, out var rec)) continue;
                if (Better(rec, best)) best = rec;
            }
            return best ?? _own;
        }

        static bool Better(PoiRecord _a, PoiRecord _b)
        {
            if (_b == null) return true;
            if (_a.ClearedRaw != _b.ClearedRaw) return _a.ClearedRaw;
            if (_a.WatchedTouched != _b.WatchedTouched) return _a.WatchedTouched;

            // An emptied chest stays empty until its loot respawns, while a later look by someone
            // standing by need not have seen inside it
            if (_a.WatchedTouched && _a.WatchedEmpty != _b.WatchedEmpty)
            {
                var emptied = _a.WatchedEmpty ? _a : _b;
                if (!emptied.LootRespawned) return _a.WatchedEmpty;
            }

            return _a.ScannedAt > _b.ScannedAt;
        }

        /// <summary>
        /// Who uncovered the POI first, across this player's own record and every teammate's.
        /// Out of a party an own record answers on its own. _mine says whether that was this
        /// player, judged by key since names can repeat.
        /// </summary>
        public static string FinderFor(int _x, int _z, PoiRecord _own, out bool _mine)
        {
            string name = null;
            string finderKey = null;
            var at = ulong.MaxValue;

            if (_own != null && _own.Discovered)
            {
                var legacy = string.IsNullOrEmpty(_own.FinderName);
                name = legacy ? LocalName : _own.FinderName;
                finderKey = legacy || string.IsNullOrEmpty(_own.FinderKey) ? LocalKey : _own.FinderKey;
                at = _own.FinderAt;
            }

            if (peers.Count > 0 && (name == null || InParty))
            {
                var key = PoiRecord.MakeKey(_x, _z);
                foreach (var peer in peers.Values)
                {
                    if (!peer.Pois.TryGetValue(key, out var rec) || string.IsNullOrEmpty(rec.FinderName)) continue;
                    if (name != null && rec.FinderAt >= at) continue;
                    name = rec.FinderName;
                    finderKey = rec.FinderKey;
                    at = rec.FinderAt;
                }
            }

            _mine = !string.IsNullOrEmpty(finderKey) ? finderKey == LocalKey : name == LocalName;
            return string.IsNullOrEmpty(name) ? null : name;
        }

        public static int PeerCount => peers.Count;

        public static int SharedPoiCount
        {
            get
            {
                var n = 0;
                foreach (var peer in peers.Values) n += peer.Pois.Count;
                return n;
            }
        }

        /// <summary>Teammates' markers kept from earlier sessions, next to this player's own state.</summary>
        public static void Load()
        {
            peers.Clear();
            storeDirty = false;
            filePath = null;

            try
            {
                var dir = PoiDb.StateDir();
                if (string.IsNullOrEmpty(dir)) return;

                filePath = Path.Combine(dir, cFileName);
                if (!File.Exists(filePath)) return;

                Peer peer = null;
                foreach (var line in File.ReadAllLines(filePath))
                {
                    if (string.IsNullOrEmpty(line) || line[0] == '#') continue;

                    // "@;key;name" opens a teammate, the POI rows that follow are theirs
                    if (line[0] == '@')
                    {
                        var p = line.Split(new[] { ';' }, 3);
                        peer = null;
                        if (p.Length == 3 && p[1].Length > 0)
                        {
                            peer = new Peer { Name = p[2] };
                            peers[p[1]] = peer;
                        }
                        continue;
                    }

                    var rec = peer != null ? PoiDb.ParseRow(line) : null;
                    if (rec == null) continue;

                    rec.Discovered = true;
                    peer.Pois[rec.Key] = rec;
                }

                Log.Out($"[PoiMapPlus] party: kept markers of {peers.Count} teammate(s) loaded, {SharedPoiCount} POI(s)");
            }
            catch (Exception e)
            {
                Log.Warning("[PoiMapPlus] failed to read kept party markers: " + e.Message);
            }
        }

        public static void Save(bool _force = false)
        {
            if (!_force && !storeDirty) return;
            if (string.IsNullOrEmpty(filePath)) return;

            try
            {
                if (peers.Count == 0)
                {
                    if (File.Exists(filePath)) File.Delete(filePath);
                    storeDirty = false;
                    return;
                }

                var sb = new StringBuilder();
                sb.AppendLine("# PoiMapPlus party markers v1 | @;playerKey;name, then that teammate's POI rows " +
                              "in the PoiMapPlus.csv format");

                foreach (var pair in peers)
                {
                    sb.Append("@;").Append(pair.Key).Append(';').AppendLine(PoiDb.CleanName(pair.Value.Name));
                    foreach (var rec in pair.Value.Pois.Values) PoiDb.AppendRow(sb, rec);
                }

                File.WriteAllText(filePath, sb.ToString());
                storeDirty = false;
            }
            catch (Exception e)
            {
                Log.Warning("[PoiMapPlus] failed to save kept party markers: " + e.Message);
            }
        }

        static void SendFull(bool _request)
        {
            sentSig.Clear();
            Log.Out("[PoiMapPlus] party: sending all discovered POIs" + (_request ? ", asking for theirs" : ""));

            var all = new List<PoiRecord>();
            foreach (var rec in PoiDb.All)
            {
                if (!rec.Discovered) continue;
                all.Add(Outgoing(rec));
                sentSig[rec.Key] = Signature(rec);
            }

            var first = true;
            var i = 0;
            do
            {
                var chunk = all.GetRange(i, Math.Min(cChunk, all.Count - i));
                byte flags = 0;
                if (first)
                {
                    flags |= NetPackagePoiMapPlusShare.FlagFull;
                    if (_request) flags |= NetPackagePoiMapPlusShare.FlagRequest;
                }
                Send(flags, chunk);
                first = false;
                i += cChunk;
            } while (i < all.Count);

            nextDelta = Time.time + cDeltaSeconds;
        }

        static void SendDelta()
        {
            List<PoiRecord> changed = null;

            foreach (var rec in PoiDb.All)
            {
                if (!rec.Discovered) continue;

                var sig = Signature(rec);
                if (sentSig.TryGetValue(rec.Key, out var prev) && prev == sig) continue;

                sentSig[rec.Key] = sig;
                (changed ?? (changed = new List<PoiRecord>())).Add(Outgoing(rec));
            }

            if (changed == null) return;

            for (var i = 0; i < changed.Count; i += cChunk)
                Send(0, changed.GetRange(i, Math.Min(cChunk, changed.Count - i)));
        }

        static void Send(byte _flags, List<PoiRecord> _entries)
        {
            var me = Local;
            var cm = ConnectionManager.Instance;
            if (me == null || cm == null) return;

            var name = LocalName;

            if (cm.IsServer)
            {
                NetPackagePoiMapPlusShare.Relay(GameManager.Instance.World, me.entityId, LocalKey, name, _flags, _entries);
                return;
            }

            cm.SendToServer(NetPackageManager.GetPackage<NetPackagePoiMapPlusShare>()
                .Setup(me.entityId, LocalKey, name, _flags, _entries), false);
        }

        /// <summary>A copy for the wire, with the finder filled in for records older than the finder column.</summary>
        static PoiRecord Outgoing(PoiRecord _rec)
        {
            var copy = _rec.Clone();
            if (string.IsNullOrEmpty(copy.FinderName))
            {
                copy.FinderName = LocalName;
                copy.FinderAt = 0;
            }

            // An own record without a key was found by this player before the key existed;
            // a teammate's finder always arrives with theirs
            if (string.IsNullOrEmpty(copy.FinderKey)) copy.FinderKey = LocalKey;
            return copy;
        }

        static int Signature(PoiRecord _r)
        {
            unchecked
            {
                var h = 17;
                h = h * 31 + (_r.Scanned ? 1 : 0);
                h = h * 31 + (_r.HasTracked ? 1 : 0);
                h = h * 31 + (_r.WatchedTouched ? 1 : 0);
                h = h * 31 + (_r.WatchedEmpty ? 1 : 0);
                h = h * 31 + _r.WatchedTouchedHour;
                h = h * 31 + _r.AllTouched;
                h = h * 31 + _r.AllTotal;
                h = h * 31 + _r.BestOpenedRank;
                h = h * 31 + _r.BestFoundRank;
                h = h * 31 + _r.FinderAt.GetHashCode();
                h = h * 31 + (_r.FinderKey ?? string.Empty).GetHashCode();
                h = h * 31 + (_r.FinderName ?? string.Empty).GetHashCode();
                return h;
            }
        }

        public static void Reset()
        {
            peers.Clear();
            filePath = null;
            storeDirty = false;
            sentSig.Clear();
            knownMembers.Clear();
            nextTick = 0f;
            nextDelta = 0f;
        }
    }
}
