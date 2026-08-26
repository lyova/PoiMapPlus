namespace PoiMapPlus
{
    /// <summary>
    /// Player facing text. Every string comes from the mod's Config/Localization.csv,
    /// the English value is kept here only as a fallback if the file fails to load.
    /// </summary>
    public static class Loc
    {
        const string cKeyLabel         = "poimapplusMarkerLabel";
        const string cKeyLabelNoTier   = "poimapplusMarkerLabelNoTier";
        const string cKeyCleared       = "poimapplusMarkerLabelCleared";
        const string cKeyClearedNoTier = "poimapplusMarkerLabelClearedNoTier";

        /// <summary>
        /// Hover text for a POI marker: name and difficulty tier, plus a cleared note
        /// once its best container has been looted. Untouched POIs get no extra text.
        /// </summary>
        public static string MarkerLabel(PrefabInstance _pi, PoiRecord _rec)
        {
            var name = PoiRegistry.DisplayName(_pi);
            var tier = PoiRegistry.Tier(_pi);
            var hasTier = tier > 0;
            var cleared = _rec != null && _rec.Cleared;

            if (cleared)
                return string.Format(
                    Get(hasTier ? cKeyCleared : cKeyClearedNoTier,
                        hasTier ? "{0}  [T{1}]  cleared" : "{0}  cleared"),
                    name, tier);

            return string.Format(
                Get(hasTier ? cKeyLabel : cKeyLabelNoTier, hasTier ? "{0}  [T{1}]" : "{0}"),
                name, tier);
        }

        static string Get(string _key, string _fallback)
        {
            var value = Localization.Get(_key, false, null);
            return string.IsNullOrEmpty(value) || value == _key ? _fallback : value;
        }
    }
}
