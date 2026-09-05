using HarmonyLib;
using UnityEngine;

namespace PoiMapPlus
{
    /// <summary>
    /// The gear in the corner of the map and the popup behind it: three toggles plus a marker
    /// size picker. The widgets come from the XML patch; everything here is the wiring, done by
    /// name so no modded XUi controller class has to be resolved.
    ///
    /// With markers off the other two rows are dimmed and stop responding - a greyed out row
    /// still says the feature exists, which a missing one does not.
    /// </summary>
    public static class MapSettings
    {
        const string cGear = "poimapplusGear";
        const string cPanel = "poimapplusPanel";

        static readonly Color RowOn = new Color(1.00f, 1.00f, 1.00f);
        static readonly Color RowOff = new Color(0.55f, 0.55f, 0.55f);
        static readonly Color RowBlocked = new Color(0.32f, 0.32f, 0.32f);

        static readonly Color SizePicked = new Color(0.35f, 0.85f, 0.35f);
        static readonly Color SizeIdle = new Color(0.24f, 0.24f, 0.24f);

        static XUiController panel;
        static XUiController rowMarkers, rowTier, rowChest;
        static XUiController tickMarkers, tickTier, tickChest;
        static XUiController textMarkers, textTier, textChest;
        static XUiController sizeS, sizeM, sizeL;
        static bool wired;

        /// <summary>Called every time the map opens: cheap once wired.</summary>
        public static void Attach(XUiC_MapArea _map)
        {
            if (_map == null) return;

            if (!wired) Wire(_map);

            // The popup starts closed on every visit to the map - it is a menu, not a state.
            Show(false);
            Refresh();
        }

        static bool warned;

        static void Wire(XUiC_MapArea _map)
        {
            var window = _map.Parent ?? _map;
            var gear = Find(window, _map, cGear);
            panel = Find(window, _map, cPanel);

            if (gear == null || panel == null)
            {
                // Deliberately not marked as wired: if the window tree is not built yet on the
                // first OnOpen, the next one gets another go. Only the log is once.
                if (!warned)
                {
                    warned = true;
                    Log.Warning("[PoiMapPlus] map settings widgets not found (gear=" +
                                (gear != null) + ", panel=" + (panel != null) + ") - is " +
                                "Config/XUi_InGame/windows.xml from this mod being loaded?");
                }
                return;
            }

            wired = true;

            gear.OnPress += (_s, _b) => Show(panel.ViewComponent == null || !panel.ViewComponent.IsVisible);

            rowMarkers = panel.GetChildById("poimapplusRowMarkers");
            rowTier = panel.GetChildById("poimapplusRowTier");
            rowChest = panel.GetChildById("poimapplusRowChest");

            tickMarkers = panel.GetChildById("tickMarkers");
            tickTier = panel.GetChildById("tickTier");
            tickChest = panel.GetChildById("tickChest");

            textMarkers = panel.GetChildById("textMarkers");
            textTier = panel.GetChildById("textTier");
            textChest = panel.GetChildById("textChest");

            sizeS = panel.GetChildById("poimapplusSizeS");
            sizeM = panel.GetChildById("poimapplusSizeM");
            sizeL = panel.GetChildById("poimapplusSizeL");

            if (rowMarkers != null) rowMarkers.OnPress += (_s, _b) => ToggleMarkers();
            if (rowTier != null) rowTier.OnPress += (_s, _b) => ToggleTier();
            if (rowChest != null) rowChest.OnPress += (_s, _b) => ToggleChest();

            if (sizeS != null) sizeS.OnPress += (_s, _b) => PickSize(Cfg.IconScaleSmall);
            if (sizeM != null) sizeM.OnPress += (_s, _b) => PickSize(Cfg.IconScaleMedium);
            if (sizeL != null) sizeL.OnPress += (_s, _b) => PickSize(Cfg.IconScaleLarge);

            Report();
        }

        /// <summary>
        /// One line saying which widgets were found and whether each will actually receive a
        /// click. A row only fires if its view has EventOnPress, which comes from style="press" -
        /// without it XUiView.OnClick returns before it ever reaches the controller, which is
        /// silent enough to look like a broken handler.
        /// </summary>
        static void Report()
        {
            var missing = new System.Text.StringBuilder();
            Check(missing, "rowMarkers", rowMarkers);
            Check(missing, "rowTier", rowTier);
            Check(missing, "rowChest", rowChest);
            Check(missing, "tickMarkers", tickMarkers);
            Check(missing, "tickTier", tickTier);
            Check(missing, "tickChest", tickChest);
            Check(missing, "textMarkers", textMarkers);
            Check(missing, "textTier", textTier);
            Check(missing, "textChest", textChest);
            Check(missing, "sizeS", sizeS);
            Check(missing, "sizeM", sizeM);
            Check(missing, "sizeL", sizeL);

            if (missing.Length > 0)
                Log.Warning("[PoiMapPlus] map settings widgets with problems:" + missing);
            else
                Log.Out("[PoiMapPlus] map settings popup wired up");
        }

        static void Check(System.Text.StringBuilder _sb, string _id, XUiController _c)
        {
            if (_c == null) { _sb.Append(' ').Append(_id).Append("=missing"); return; }
            if (_c.ViewComponent == null) { _sb.Append(' ').Append(_id).Append("=noview"); return; }

            // Only the clickable ones need the press event
            if (_id.StartsWith("row") || _id.StartsWith("size"))
                if (!_c.ViewComponent.EventOnPress)
                    _sb.Append(' ').Append(_id).Append("=notclickable");
        }

        /// <summary>The widgets hang off the map window, but which node exactly is XML's call.</summary>
        static XUiController Find(XUiController _window, XUiController _map, string _id) =>
            _window.GetChildById(_id) ?? _map.GetChildById(_id);

        static void Show(bool _open)
        {
            if (panel?.ViewComponent == null) return;
            panel.ViewComponent.IsVisible = _open;
        }

        static void ToggleMarkers()
        {
            UiState.ShowMarkers = !UiState.ShowMarkers;
            UiState.Save();
            Refresh();

            // Markers are created and destroyed in Refresh, so the global switch needs a rebuild.
            // The other two only change what the decorator draws on the next frame.
            if (UiState.ShowMarkers) PoiMarkers.Refresh();
            else PoiMarkers.Clear();
        }

        static void ToggleTier()
        {
            if (!UiState.ShowMarkers) return;
            Cfg.ShowTierLabel = !Cfg.ShowTierLabel;
            UiState.Save();
            Refresh();

            // The tier rides on NavObject.name, which is only written while upserting a marker
            PoiMarkers.Refresh();
        }

        static void ToggleChest()
        {
            if (!UiState.ShowMarkers) return;
            Cfg.ShowChestBadge = !Cfg.ShowChestBadge;
            UiState.Save();
            Refresh();
        }

        static void PickSize(float _scale)
        {
            if (!UiState.ShowMarkers) return;

            Cfg.IconScale = _scale;
            UiState.Save();
            Refresh();

            // Pushes the new scale onto the nav object class and rebuilds
            PoiMarkers.Refresh();
        }

        /// <summary>Paint everything to match the current state.</summary>
        public static void Refresh()
        {
            PaintRow(rowMarkers, tickMarkers, textMarkers, UiState.ShowMarkers, true);
            PaintRow(rowTier, tickTier, textTier, Cfg.ShowTierLabel, UiState.ShowMarkers);
            PaintRow(rowChest, tickChest, textChest, Cfg.ShowChestBadge, UiState.ShowMarkers);

            PaintSize(sizeS, Cfg.IconScaleSmall);
            PaintSize(sizeM, Cfg.IconScaleMedium);
            PaintSize(sizeL, Cfg.IconScaleLarge);
        }

        static void PaintRow(XUiController _row, XUiController _tick, XUiController _text,
                             bool _on, bool _enabled)
        {
            var color = !_enabled ? RowBlocked : (_on ? RowOn : RowOff);

            // Enabled is deliberately left alone: a disabled XUiV_Button switches to
            // DisabledSpriteName, which is empty here, so the row's background would vanish
            // instead of dimming. The blocked state is said with colour, and the handlers
            // return early anyway.

            // The tick is the state; the caption only dims along with it
            if (_tick?.ViewComponent is XUiV_Sprite tick)
            {
                tick.IsVisible = _on;
                tick.Color = color;
            }

            if (_text?.ViewComponent is XUiV_Label label) label.Color = color;
        }

        static void PaintSize(XUiController _button, float _scale)
        {
            if (!(_button?.ViewComponent is XUiV_Button view)) return;

            var picked = Mathf.Abs(Cfg.IconScale - _scale) < 0.001f;
            var color = !UiState.ShowMarkers ? SizeIdle : (picked ? SizePicked : SizeIdle);

            // ManualColors reads the other way round from what the name suggests: it means
            // "the code owns the colour", and updateCurrentSprite then skips its own
            // assignment entirely - including the one from DefaultSpriteColor. So the value
            // has to go into CurrentColor. The upside is that hover cannot overwrite it.
            view.ManualColors = true;
            view.CurrentColor = color;
        }

        /// <summary>A world is going away: these controllers belong to its UI.</summary>
        public static void Forget()
        {
            panel = null;
            warned = false;
            rowMarkers = rowTier = rowChest = null;
            tickMarkers = tickTier = tickChest = null;
            textMarkers = textTier = textChest = null;
            sizeS = sizeM = sizeL = null;
            wired = false;
        }
    }

    /// <summary>Wire the settings popup up once the map window exists, and keep it in sync.</summary>
    [HarmonyPatch(typeof(XUiC_MapArea), nameof(XUiC_MapArea.OnOpen))]
    public static class Patch_MapArea_OnOpen_Settings
    {
        static void Postfix(XUiC_MapArea __instance)
        {
            try { MapSettings.Attach(__instance); }
            catch (System.Exception e) { Log.Error("[PoiMapPlus] map settings: " + e); }
        }
    }
}
