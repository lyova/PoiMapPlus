namespace PoiMapPlus
{
    /// <summary>
    /// The game's own loot respawn rule, read from the side.
    ///
    /// TEFeatureStorage.UpdateTick resets a container (bTouched = false) once
    ///   - it was opened at all, and
    ///   - it is empty - a single item left inside blocks the respawn forever, and
    ///   - LootRespawnDays whole days passed since its worldTimeTouched, and
    ///   - no player stood within 16 blocks while the timer was still running: until it
    ///     expires, a nearby player pushes worldTimeTouched forward to "now".
    ///
    /// The reset itself only happens while the container's chunk is loaded and ticking, so
    /// it effectively fires when the player comes back. That makes the whole thing
    /// predictable from stored data: one container timestamp per POI is enough to tell
    /// whether its loot is back, without touching the world.
    /// </summary>
    public static class LootRespawn
    {
        /// <summary>Days from the world's Loot Respawn Time option. 0 or less means never.</summary>
        public static int Days => GamePrefs.GetInt(EnumGamePrefs.LootRespawnDays);

        public static bool Enabled => Days > 0;

        /// <summary>World time in whole hours, the unit UpdateTick compares in.</summary>
        public static int HourOf(ulong _worldTime) => GameUtils.WorldTimeToTotalHours(_worldTime);

        public static int NowHour
        {
            get
            {
                var world = GameManager.Instance != null ? GameManager.Instance.World : null;
                return world != null ? HourOf(world.GetWorldTime()) : 0;
            }
        }

        /// <summary>
        /// Has an empty container touched at this hour had its loot restored by now?
        /// Errs on the early side: a player lingering in the POI delays the real reset,
        /// and the next scan of that POI corrects the record either way.
        /// </summary>
        public static bool IsDue(int _touchedHour)
        {
            if (!Enabled || _touchedHour <= 0) return false;
            return (NowHour - _touchedHour) / 24 >= Days;
        }

        /// <summary>Whole days left before the loot is back, for the tooltip and diagnostics.</summary>
        public static int DaysLeft(int _touchedHour)
        {
            if (!Enabled || _touchedHour <= 0) return -1;
            var left = Days - (NowHour - _touchedHour) / 24;
            return left > 0 ? left : 0;
        }
    }
}
