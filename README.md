# POI Map Plus

A client-side modlet for **7 Days to Die V 3.2.0** that turns the map into a useful planning tool:
every POI you have uncovered gets a marker, and the marker tells you whether its loot room has
already been emptied.

## What it does

- **Markers for every POI you have been to.** A marker appears the first time you get close
  enough for that part of the map to uncover, and stays from then on. Nothing is revealed ahead
  of time - POIs you have never approached stay invisible.
- **Hover for details.** Point at a marker and a tooltip shows the POI name and its difficulty
  tier. No permanent clutter on the map.
- **Cleared tracking.** The marker turns green once the richest container in that POI has been
  opened. Orange means the good stuff is still there.
- **Quest resets are handled.** Take a quest that resets a POI and its marker goes back to orange.

## How "cleared" is decided

The mod watches a configurable list of containers, ordered by rank. In every POI it finds the
best ranked container actually present and follows that one - lesser containers only matter when
nothing better exists there. Defaults, richest first:

| rank | container |
|------|-----------|
| 1 | Hardened Chest T5 |
| 2 | Hardened Chest T4 |
| 3 | Leather Trunk |
| 4 | Reinforced Chest, Wood Crate |
| 5 | Weapons Bag |
| 6 | Hidden Stash |
| 7 | Ammo piles |

Containers such as weapon bags and ammo piles vanish from the world once emptied. The mod records
the rank at the moment the container is opened, so those still count.

## Requirements

- 7 Days to Die **V 3.2.0**
- Launch **without EasyAntiCheat** - the mod ships a DLL, and EAC blocks those. Start the game
  from `7DaysToDie.exe`, or pick the non-EAC option in the Steam launcher.
- Single player and multiplayer clients. The mod is purely client-side: it does not have to be
  installed on the server, and no other player needs it. Cleared state is stored in your own save.

## Installation

1. Extract the archive into `<game folder>\Mods\` so you end up with
   `<game folder>\Mods\PoiMapPlus\ModInfo.xml`.
2. Start the game without EAC.

Vortex and the Mod Launcher handle the archive as a normal modlet.

## Configuration

`PoiMapPlus.xml` next to `ModInfo.xml` holds every setting, with comments. The interesting ones:

| setting | default | meaning |
|---------|---------|---------|
| `TopContainerCount` | `1` | how many of the best containers must be looted |
| `MinTier` | `1` | lowest POI difficulty tier that gets a marker; `0` shows sheds too |
| `OnlyDiscovered` | `true` | `false` reveals every POI in the world from the start |
| `MaxMarkers` | `600` | cap on simultaneous markers, nearest to the player win |
| `tracked_loot_lists` | see above | which containers count, and in what order |

Container names are LootLists from `Data/Config/loot.xml`, not block names.

The config is re-read on every world load, so a trip through the main menu is enough. Or use the
console:

```
poimap reload    re-read the config and refresh markers
poimap rescan    reload, forget learned loot state, rescan from scratch
poimap status    show tracked lists and POI counts
poimap navdump   list nav objects by class and tracking type
```

## Localization

`Config/Localization.csv` covers every language the game ships with: English, German, Spanish,
French, Italian, Japanese, Korean, Polish, Brazilian Portuguese, Turkish, Simplified and
Traditional Chinese, plus a Ukrainian column. Adding or fixing a translation is one cell in that
file - no rebuild needed.

## Building

See the [repository README](../../README.md). Short version:

```powershell
.uild.ps1 PoiMapPlus
```

## License

MIT - see [LICENSE](LICENSE). Free to fork, modify and bundle into mod packs; credit is
appreciated.
