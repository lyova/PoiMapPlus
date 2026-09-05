using System.Reflection;
using HarmonyLib;

namespace PoiMapPlus
{
    /// <summary>
    /// Drive the marker tooltip by hand: the map only draws permanent labels for nav objects,
    /// and its own tooltip logic covers map objects only.
    /// </summary>
    [HarmonyPatch(typeof(XUiC_MapArea), nameof(XUiC_MapArea.Update))]
    public static class Patch_MapArea_Update
    {
        static readonly FieldInfo fiHovered =
            AccessTools.Field(typeof(XUiC_MapArea), "closestMouseOverNavObject");

        // XUiC_ToolTip waits ShowDelaySec before appearing; zeroing the countdown
        // makes our tooltip pop up the moment the cursor touches the icon.
        static readonly FieldInfo fiShowDelay =
            AccessTools.Field(typeof(XUiC_ToolTip), "showDelay");

        static string shownText;

        static void Postfix(XUiC_MapArea __instance)
        {
            try
            {
                if (fiHovered == null || __instance.xui == null) return;

                var tooltip = __instance.xui.ToolTipWindow;
                if (tooltip == null) return;

                var hovered = fiHovered.GetValue(__instance) as NavObject;
                var text = hovered != null ? PoiMarkers.TooltipFor(hovered) : null;

                if (!string.IsNullOrEmpty(text))
                {
                    if (tooltip.ToolTip != text)
                    {
                        tooltip.ToolTip = text;
                        fiShowDelay?.SetValue(tooltip, 0f);
                    }
                    shownText = text;
                    return;
                }

                // Cursor left the icon: clear our own text, never someone else's.
                if (shownText != null)
                {
                    if (tooltip.ToolTip == shownText) tooltip.ToolTip = string.Empty;
                    shownText = null;
                }
            }
            catch (System.Exception e) { Log.Error("[PoiMapPlus] map Update: " + e); }
        }
    }

    /// <summary>Rebuild the markers every time the player opens the map.</summary>
    [HarmonyPatch(typeof(XUiC_MapArea), nameof(XUiC_MapArea.OnOpen))]
    public static class Patch_MapArea_OnOpen
    {
        static void Postfix()
        {
            try { PoiMarkers.Refresh(); }
            catch (System.Exception e) { Log.Error("[PoiMapPlus] OnOpen: " + e); }
        }
    }

    /// <summary>
    /// Player crossed a POI boundary: scan the one being entered, and re-scan the one being
    /// left. That last scan matters for the respawn prediction - while the player stands
    /// within 16 blocks the game keeps pushing worldTimeTouched forward, so the value read on
    /// the way out is the closest we get to the one the respawn will actually be measured from.
    /// </summary>
    [HarmonyPatch(typeof(EntityPlayer), nameof(EntityPlayer.onNewPrefabEntered))]
    public static class Patch_Player_OnNewPrefabEntered
    {
        static PrefabInstance previous;

        static void Postfix(EntityPlayer __instance, PrefabInstance _prefabInstance)
        {
            try
            {
                if (!(__instance is EntityPlayerLocal)) return;

                if (previous != null && previous != _prefabInstance)
                    PoiScanner.Scan(previous, true);

                previous = _prefabInstance;

                if (_prefabInstance == null) return;

                var rec = PoiDb.GetOrCreate(_prefabInstance.boundingBoxPosition.x, _prefabInstance.boundingBoxPosition.z);
                if (!rec.Discovered)
                {
                    rec.Discovered = true;
                    PoiDb.MarkDirty();
                }

                PoiScanner.Scan(_prefabInstance, true);
            }
            catch (System.Exception e) { Log.Error("[PoiMapPlus] onNewPrefabEntered: " + e); }
        }

        public static void Forget() => previous = null;
    }

    /// <summary>
    /// The loot window closed, so what the player left behind is now settled. Opening a
    /// container is too early for that: LootManager.LootContainerOpened runs before the loot
    /// is even generated, and the emptiness of the chest is exactly what decides whether the
    /// game will ever restock it.
    /// </summary>
    [HarmonyPatch(typeof(XUiC_LootWindow), nameof(XUiC_LootWindow.OnClose))]
    public static class Patch_LootWindow_OnClose
    {
        static readonly FieldInfo fiTe = AccessTools.Field(typeof(XUiC_LootWindow), "te");

        static void Postfix(XUiC_LootWindow __instance)
        {
            try
            {
                if (fiTe == null) return;

                var lootable = fiTe.GetValue(__instance) as ITileEntityLootable;
                if (lootable == null) return;

                TileEntity te = (lootable as TEFeatureAbs)?.Parent ?? lootable as TileEntity;
                if (te == null) return;

                var pi = PoiRegistry.FindAt(te.ToWorldPos());
                if (pi == null) return;

                PoiScanner.Scan(pi, true);
            }
            catch (System.Exception e) { Log.Error("[PoiMapPlus] loot window close: " + e); }
        }
    }

    /// <summary>A container was opened: re-evaluate the POI it stands in.</summary>
    [HarmonyPatch(typeof(LootManager), nameof(LootManager.LootContainerOpened))]
    public static class Patch_LootManager_LootContainerOpened
    {
        static void Postfix(ITileEntityLootable _tileEntity)
        {
            try
            {
                TileEntity te = (_tileEntity as TEFeatureAbs)?.Parent ?? _tileEntity as TileEntity;
                var storageFeature = _tileEntity as TEFeatureStorage;
                var lootList = storageFeature != null ? storageFeature.lootListName : "?";

                if (te == null)
                {
                    if (Cfg.Verbose) Log.Out($"[PoiMapPlus] opened '{lootList}': no parent tile entity");
                    return;
                }

                var pos = te.ToWorldPos();
                var pi = PoiRegistry.FindAt(pos);

                if (pi == null)
                {
                    if (Cfg.Verbose)
                        Log.Out($"[PoiMapPlus] opened '{lootList}' at {pos.x},{pos.y},{pos.z}: not inside an indexed POI");
                    return;
                }

                // Record the opening before scanning: containers with destroy_on_close are gone
                // from the world by the time we look for them again.
                var rank = 0;
                var tracked = storageFeature != null &&
                              LootScore.TryGetRank(storageFeature, PoiRegistry.Tier(pi), out rank);

                if (Cfg.Verbose)
                    Log.Out($"[PoiMapPlus] opened '{lootList}' in '{PoiRegistry.DisplayName(pi)}': " +
                            (tracked ? $"rank {rank}" : "not tracked"));

                if (tracked)
                {
                    var rec = PoiDb.GetOrCreate(pi.boundingBoxPosition.x, pi.boundingBoxPosition.z);
                    if (rec.BestOpenedRank == 0 || rank < rec.BestOpenedRank)
                    {
                        rec.BestOpenedRank = rank;
                        PoiDb.MarkDirty();
                    }
                }

                PoiScanner.Scan(pi, true);
            }
            catch (System.Exception e) { Log.Error("[PoiMapPlus] LootContainerOpened: " + e); }
        }
    }

    /// <summary>A quest reset the POI, so its loot is untouched again and the marker has to follow.</summary>
    [HarmonyPatch(typeof(PrefabInstance), nameof(PrefabInstance.ResetBlocksAndRebuild))]
    public static class Patch_PrefabInstance_Reset
    {
        static void Prefix(PrefabInstance __instance)
        {
            try { PoiScanner.ResetPoi(__instance); }
            catch (System.Exception e) { Log.Error("[PoiMapPlus] ResetBlocksAndRebuild: " + e); }
        }
    }
}
