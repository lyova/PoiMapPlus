namespace PoiMapPlus
{
    /// <summary>
    /// Player facing text. Every string comes from the mod's Config/Localization.csv,
    /// the English value is kept here only as a fallback if the file fails to load.
    /// </summary>
    public static class Loc
    {
        const string cKeyLabel       = "poimapplusMarkerLabel";
        const string cKeyLabelNoTier = "poimapplusMarkerLabelNoTier";

        // Suffixes, appended to the label. Split out from the label itself so a new POI
        // state costs one short phrase per language instead of a whole sentence.
        const string cNoteCleared   = "poimapplusNoteCleared";
        const string cNoteLooted    = "poimapplusNoteLooted";
        const string cNoteStocked   = "poimapplusNoteStocked";

        /// <summary>
        /// Hover text for a POI marker: name, difficulty tier and what happened to its loot.
        /// An untouched POI gets no note - only the states worth acting on are spelled out.
        /// </summary>
        public static string MarkerLabel(PrefabInstance _pi, PoiRecord _rec)
        {
            var name = PoiRegistry.DisplayName(_pi);
            var tier = PoiRegistry.Tier(_pi);
            var hasTier = tier > 0;

            var label = string.Format(
                Get(hasTier ? cKeyLabel : cKeyLabelNoTier, hasTier ? "{0}  [T{1}]" : "{0}"),
                name, tier);

            // The separator lives here rather than in the note strings: leading spaces in a
            // localization cell are the kind of thing a CSV round trip quietly eats.
            var note = Note(_rec);
            return note.Length > 0 ? label + "  " + note : label;
        }

        static string Note(PoiRecord _rec)
        {
            if (_rec == null) return string.Empty;

            // A POI whose loot has come back says nothing at all: Cleared is already false for
            // it, so it reads as untouched, which is exactly what it is again.
            if (!_rec.Cleared) return string.Empty;

            switch (_rec.Chest)
            {
                case ChestState.Empty:   return Get(cNoteLooted, "cleared, looted");
                case ChestState.Stocked: return Get(cNoteStocked, "cleared, chest not empty");
                default:                 return Get(cNoteCleared, "cleared");
            }
        }

        static string Get(string _key, string _fallback)
        {
            var value = Localization.Get(_key, false, null);
            return string.IsNullOrEmpty(value) || value == _key ? _fallback : value;
        }
    }
}
