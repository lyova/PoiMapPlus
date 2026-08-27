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

The mod follows a ranked list of loot containers, ordered from the richest down to the most
modest. In every POI it picks the best ranked container that is actually there and watches that
one - lower ranks only matter when nothing better exists in that POI. Anything not on the list is
ignored entirely, so opening a nightstand on the way in marks nothing.

The list itself lives in `PoiMapPlus.xml` under `tracked_loot_lists`, where every entry carries a
`rank` - 1 being the richest. Look there for the current defaults; reordering them or adding your
own containers is a matter of editing that file.

Some containers, weapon bags and ammo piles among them, vanish from the world once emptied. The
mod records the rank at the moment such a container is opened, so they still count.

## Requirements

- 7 Days to Die **V 3.2.0**
- Launch **without EasyAntiCheat** - the mod ships a DLL, and EAC blocks those. Start the game
  from `7DaysToDie.exe`, or pick the non-EAC option in the Steam launcher.
- Single player, and multiplayer clients - see below.

## Multiplayer

The mod is purely client-side: only you install it, the server does not need it and neither do the
other players. Everything it reads - the POI list, the fog of war, the state of loot containers -
is already on the client, and the cleared state lives in your own save.

That said, **it has only been tested in single player**. It should behave the same as a client on
a dedicated server, but nobody has verified that yet. Feedback welcome. Cleared marks are yours
alone: they do not sync with what teammates have looted.

## Compatibility

- Hooks vanilla methods with Harmony postfixes (`XUiC_MapArea`, `LootManager.LootContainerOpened`,
  `EntityPlayer.onNewPrefabEntered`, `PrefabInstance.ResetBlocksAndRebuild`) and appends one nav
  object class through XPath, so no vanilla config file is overwritten.
- UI overhauls that only restyle the map window are fine. A mod replacing the map controller with
  its own class would leave the markers unhooked.
- Containers from other mods are supported: add their loot lists to `tracked_loot_lists`.
- Large overhauls such as Darkness Falls or Undead Legacy rework loot entirely, so the default
  ranking needs adjusting in `PoiMapPlus.xml`.

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

Needs the .NET SDK 8.0 or newer. Game assemblies are referenced straight from the install
directory; adjust `GameDir` in `Directory.Build.props` if yours differs.

```powershell
.\build.ps1
```

The build deploys the mod into the game's `Mods` folder and packs an archive into `dist\`.

## License

MIT - see [LICENSE](LICENSE). Free to fork, modify and bundle into mod packs; credit is
appreciated.
