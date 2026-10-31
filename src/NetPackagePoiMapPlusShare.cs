using System.Collections.Generic;

namespace PoiMapPlus
{
    /// <summary>
    /// POI records of one player, on their way to the rest of their party. Clients send it to the
    /// server, the server forwards it to the sender's party members.
    ///
    /// Having it on the server is what turns the party features on: a server without the mod
    /// never maps this type, so clients see it missing and keep everything to themselves.
    /// </summary>
    public class NetPackagePoiMapPlusShare : NetPackage
    {
        const byte cVersion = 2;
        const int cMaxEntries = 5000;

        public const byte FlagFull = 1;      // replaces everything previously received from this sender
        public const byte FlagRequest = 2;   // asks the receivers to answer with their own full set

        int senderId;
        string senderKey;
        string senderName;
        byte flags;
        List<PoiRecord> entries;

        public NetPackagePoiMapPlusShare Setup(int _senderId, string _senderKey, string _senderName, byte _flags,
                                               List<PoiRecord> _entries)
        {
            senderId = _senderId;
            senderKey = _senderKey ?? string.Empty;
            senderName = _senderName ?? string.Empty;
            flags = _flags;
            entries = _entries ?? new List<PoiRecord>();
            return this;
        }

        public override bool Compress => true;

        public override NetPackageDirection PackageDirection => NetPackageDirection.Both;

        public override void read(PooledBinaryReader _reader)
        {
            entries = new List<PoiRecord>();

            var version = _reader.ReadByte();
            senderId = _reader.ReadInt32();
            senderKey = _reader.ReadString();
            senderName = _reader.ReadString();
            flags = _reader.ReadByte();

            var count = _reader.ReadInt32();
            if (version != cVersion || count < 0 || count > cMaxEntries) return;

            for (var i = 0; i < count; i++)
            {
                var rec = new PoiRecord
                {
                    X = _reader.ReadInt32(),
                    Z = _reader.ReadInt32(),
                    Discovered = true,
                };

                var bits = _reader.ReadByte();
                rec.Scanned = (bits & 1) != 0;
                rec.HasTracked = (bits & 2) != 0;
                rec.WatchedTouched = (bits & 4) != 0;
                rec.WatchedEmpty = (bits & 8) != 0;

                rec.WatchedTouchedHour = _reader.ReadInt32();
                rec.AllTouched = _reader.ReadUInt16();
                rec.AllTotal = _reader.ReadUInt16();
                rec.BestOpenedRank = _reader.ReadInt32();
                rec.BestFoundRank = _reader.ReadInt32();
                rec.ScannedAt = _reader.ReadUInt64();
                rec.FinderAt = _reader.ReadUInt64();
                rec.FinderKey = _reader.ReadString();
                rec.FinderName = _reader.ReadString();

                entries.Add(rec);
            }
        }

        public override void write(PooledBinaryWriter _writer)
        {
            base.write(_writer);

            // Through the base type: the game's own overloads drag in ReadOnlySpan, which net48 lacks
            var w = (System.IO.BinaryWriter)_writer;

            w.Write(cVersion);
            w.Write(senderId);
            w.Write(senderKey ?? string.Empty);
            w.Write(senderName ?? string.Empty);
            w.Write(flags);

            var list = entries ?? new List<PoiRecord>();
            w.Write(list.Count);

            foreach (var rec in list)
            {
                w.Write(rec.X);
                w.Write(rec.Z);
                w.Write((byte)((rec.Scanned ? 1 : 0) | (rec.HasTracked ? 2 : 0) |
                                     (rec.WatchedTouched ? 4 : 0) | (rec.WatchedEmpty ? 8 : 0)));
                w.Write(rec.WatchedTouchedHour);
                w.Write((ushort)Clamp(rec.AllTouched));
                w.Write((ushort)Clamp(rec.AllTotal));
                w.Write(rec.BestOpenedRank);
                w.Write(rec.BestFoundRank);
                w.Write(rec.ScannedAt);
                w.Write(rec.FinderAt);
                w.Write(rec.FinderKey ?? string.Empty);
                w.Write(rec.FinderName ?? string.Empty);
            }
        }

        static int Clamp(int _v) => _v < 0 ? 0 : _v > ushort.MaxValue ? ushort.MaxValue : _v;

        public override void ProcessPackage(World _world, GameManager _callbacks)
        {
            if (_world == null) return;

            if (_world.IsRemote())
            {
                PartyShare.Receive(senderId, senderKey, senderName, flags, entries);
                return;
            }

            // A client may only speak for itself
            if (Sender == null || !ValidEntityIdForSender(senderId, false)) return;

            Relay(_world, senderId, senderKey, senderName, flags, entries);
        }

        /// <summary>Server side: hand the records to every other member of the sender's party.</summary>
        public static void Relay(World _world, int _senderId, string _senderKey, string _senderName, byte _flags,
                                 List<PoiRecord> _entries)
        {
            var members = (_world.GetEntity(_senderId) as EntityPlayer)?.Party?.MemberList;
            if (members == null) return;

            foreach (var member in members)
            {
                if (member == null || member.entityId == _senderId) continue;

                // The host plays on the server itself, so its share never travels
                if (member is EntityPlayerLocal)
                {
                    PartyShare.Receive(_senderId, _senderKey, _senderName, _flags, _entries);
                    continue;
                }

                ConnectionManager.Instance.SendPackage(
                    NetPackageManager.GetPackage<NetPackagePoiMapPlusShare>()
                        .Setup(_senderId, _senderKey, _senderName, _flags, _entries),
                    false, member.entityId);
            }
        }
    }
}
