using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace PoiMapPlus
{
    /// <summary>
    /// Draws the chest badge and the tier label on top of our skulls.
    ///
    /// The map builds one GameObject per nav object from its prefabMapSprite, with a "Sprite"
    /// (UISprite) and a "Name" (UILabel) child, and resizes the sprite every frame from the
    /// zoom level. We hang a second UISprite off the same GameObject for the chest, reuse the
    /// existing label for the tier, and read the current size off the skull instead of
    /// recomputing the zoom maths.
    ///
    /// Both vanilla sprites are referenced by name out of the shared UIAtlas, so nothing from
    /// the game's assets is copied into the mod.
    /// </summary>
    public static class MarkerDecor
    {
        const string cBadgeChild = "PoiMapPlusChest";
        const string cSpriteChild = "Sprite";
        const string cLabelChild = "Name";

        static readonly FieldInfo fiKeyToNavObject = AccessTools.Field(typeof(XUiC_MapArea), "keyToNavObject");
        static readonly FieldInfo fiKeyToNavSprite = AccessTools.Field(typeof(XUiC_MapArea), "keyToNavSprite");

        static PropertyInfo piNavObjectDict;
        static PropertyInfo piNavSpriteDict;

        static readonly Color TierLow = new Color(1.00f, 1.00f, 1.00f);
        static readonly Color TierMid = new Color(1.00f, 0.55f, 0.15f);
        static readonly Color TierHigh = new Color(1.00f, 0.25f, 0.25f);

        /// <summary>
        /// Size the map last drew our icons at, in UI units. Reported by "poimap status": it is
        /// the number BadgeMinSize is compared against, and the only way to see it without
        /// working the zoom formula out by hand.
        /// </summary>
        public static int LastIconSize { get; private set; }

        public static void Decorate(XUiC_MapArea _map)
        {
            if (_map == null || !UiState.ShowMarkers || PoiMarkers.MarkerCount == 0) return;
            if (fiKeyToNavObject == null || fiKeyToNavSprite == null) return;

            var navs = Dict<NavObject>(fiKeyToNavObject.GetValue(_map), ref piNavObjectDict);
            var gos = Dict<GameObject>(fiKeyToNavSprite.GetValue(_map), ref piNavSpriteDict);
            if (navs == null || gos == null) return;

            foreach (var pair in navs)
            {
                var info = PoiMarkers.InfoFor(pair.Value);
                if (info == null) continue;

                if (!gos.TryGetValue(pair.Key, out var go) || go == null) continue;

                Apply(_map, go, info);
            }
        }

        /// <summary>DictionarySave&lt;int, T&gt; keeps the real dictionary behind a Dict property.</summary>
        static Dictionary<int, T> Dict<T>(object _save, ref PropertyInfo _cached)
        {
            if (_save == null) return null;
            if (_cached == null) _cached = AccessTools.Property(_save.GetType(), "Dict");
            return _cached?.GetValue(_save) as Dictionary<int, T>;
        }

        static void Apply(XUiC_MapArea _map, GameObject _go, MarkerInfo _info)
        {
            var skullTransform = _go.transform.Find(cSpriteChild);
            if (skullTransform == null) return;

            var skull = skullTransform.GetComponent<UISprite>();
            if (skull == null) return;

            // The map has already set the size for this frame, so this is the real icon size.
            var size = skull.width;
            LastIconSize = size;

            var room = size >= Cfg.BadgeMinSize;

            ApplyTier(_go, skull, _info, size, room);
            ApplyChest(_map, _go, skull, _info, size, room);
        }

        static void ApplyTier(GameObject _go, UISprite _skull, MarkerInfo _info, int _size, bool _room)
        {
            var labelTransform = _go.transform.Find(cLabelChild);
            if (labelTransform == null) return;

            var label = labelTransform.GetComponent<UILabel>();
            if (label == null) return;

            var wanted = _room && UiState.TierVisible && _info.Tier > 0;

            if (!wanted)
            {
                // The map writes DisplayName into the label every frame, so blanking the text
                // here is what actually hides it - and it costs nothing when already blank.
                if (!string.IsNullOrEmpty(label.text)) label.text = string.Empty;
                return;
            }

            label.color = TierColor(_info.Tier);
            label.depth = _skull.depth + 2;

            // BottomLeft, so the offset pins the corner the text grows away from. With
            // BottomRight it grows leftwards instead and a wider tier string ("T10") would
            // creep back over the skull.
            label.pivot = UIWidget.Pivot.BottomLeft;
            label.fontSize = Mathf.Max(8, Mathf.RoundToInt(_size * Cfg.TierScale));
            label.transform.localPosition =
                new Vector3(_size * Cfg.TierOffsetX, _size * Cfg.TierOffsetY, 0f);
        }

        static void ApplyChest(XUiC_MapArea _map, GameObject _go, UISprite _skull,
                               MarkerInfo _info, int _size, bool _room)
        {
            var wanted = _room && UiState.ChestVisible && _info.Chest != ChestState.Unknown;

            var badgeTransform = _go.transform.Find(cBadgeChild);

            if (!wanted)
            {
                if (badgeTransform != null && badgeTransform.gameObject.activeSelf)
                    badgeTransform.gameObject.SetActive(false);
                return;
            }

            var badge = badgeTransform != null ? badgeTransform.GetComponent<UISprite>() : null;
            if (badge == null)
            {
                badge = Create(_go, _skull);
                if (badge == null) return;
            }

            if (!badge.gameObject.activeSelf) badge.gameObject.SetActive(true);

            var atlas = Atlas(_map, _skull);
            if (atlas != null && !ReferenceEquals(badge.atlas, atlas)) badge.atlas = atlas;
            if (badge.spriteName != Cfg.ChestSprite) badge.spriteName = Cfg.ChestSprite;

            badge.color = _info.Chest == ChestState.Empty ? PoiMarkers.ClearedColor : PoiMarkers.OpenColor;
            badge.depth = _skull.depth + 1;

            var side = Mathf.Max(4, Mathf.RoundToInt(_size * Cfg.ChestScale));
            badge.width = side;
            badge.height = side;
            badge.transform.localPosition =
                new Vector3(_size * Cfg.ChestOffsetX, _size * Cfg.ChestOffsetY, 0f);
        }

        static UISprite Create(GameObject _go, UISprite _skull)
        {
            // AddChild wires up the layer, the parent panel and the depth the way NGUI expects;
            // a bare AddComponent leaves the widget half initialised.
            var badge = NGUITools.AddChild<UISprite>(_go);
            if (badge == null) return null;

            badge.gameObject.name = cBadgeChild;
            badge.pivot = UIWidget.Pivot.Center;
            badge.type = UIBasicSprite.Type.Simple;
            badge.atlas = _skull.atlas;
            return badge;
        }

        /// <summary>
        /// Which atlas holds the badge sprite. The map resolves its own icons the same way,
        /// through the multi source atlas registered under the current atlas's name.
        /// </summary>
        static INGUIAtlas Atlas(XUiC_MapArea _map, UISprite _skull)
        {
            if (_map.xui == null) return _skull.atlas;

            var current = _skull.atlas as Object;
            var name = current != null ? current.name : null;
            if (string.IsNullOrEmpty(name)) return _skull.atlas;

            // Typed as INGUIAtlas on purpose: the UL era signature returns the concrete UIAtlas
            INGUIAtlas found = _map.xui.GetAtlasByName(name, Cfg.ChestSprite);
            return found ?? _skull.atlas;
        }

        static Color TierColor(int _tier)
        {
            if (_tier < Cfg.TierMid) return TierLow;
            return _tier == Cfg.TierMid ? TierMid : TierHigh;
        }
    }

    /// <summary>
    /// The map has just laid out every nav icon: add ours on top.
    ///
    /// Applied by hand rather than through PatchAll, because the target is a private method
    /// matched by name. A rename in some future game build would make Harmony throw during
    /// PatchAll and take every other patch of the mod down with it; here it costs the badges
    /// and nothing else.
    /// </summary>
    public static class Patch_MapArea_UpdateNavObjectList
    {
        const string cTarget = "updateNavObjectList";

        public static void Apply(Harmony _harmony)
        {
            var target = AccessTools.Method(typeof(XUiC_MapArea), cTarget);
            if (target == null)
            {
                Log.Warning($"[PoiMapPlus] XUiC_MapArea.{cTarget} not found, " +
                            "marker badges are off for this game version");
                return;
            }

            _harmony.Patch(target, postfix: new HarmonyMethod(
                AccessTools.Method(typeof(Patch_MapArea_UpdateNavObjectList), nameof(Postfix))));
        }

        public static void Postfix(XUiC_MapArea __instance)
        {
            try { MarkerDecor.Decorate(__instance); }
            catch (System.Exception e) { Log.Error("[PoiMapPlus] marker decor: " + e); }
        }
    }
}
