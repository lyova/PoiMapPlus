# POI Map Plus

**[Download on Nexus Mods](https://www.nexusmods.com/7daystodie/mods/12271)**

A client-side modlet for **7 Days to Die V 3.3** that turns the map into a useful planning tool:
every POI you have uncovered gets a marker, and the marker tells you whether its loot room has
already been emptied.

## What it does

- **Markers for every POI you have been to.** A marker appears the first time you get close
  enough for that part of the map to uncover, and stays from then on. Nothing is revealed ahead
  of time - POIs you have never approached stay invisible.
- **The skull says whether it is worth going.** Orange means the good loot is still there, green
  means it is gone.
- **The tier is printed on the marker.** `T1`-`T2` white, `T3` orange, `T4`+ red, so a glance at
  the map is enough.
- **A chest badge says whether the loot will come back.** Green chest: you left the container
  empty, so the game will restock it. Orange chest: something is still inside, which blocks the
  respawn for as long as it stays there.
- **Respawned loot turns the marker orange again**, on the day it actually happens - without you
  having to revisit the place. Badge and note go with it, so the POI reads as untouched, which
  by then it effectively is.
- **Hover for details.** The tooltip spells the state out: `cleared, looted` or
  `cleared, chest not empty`.
- **Settings on the map itself.** A gear in the bottom right corner opens a small popup:
  markers on or off, tier label on or off, chest badge on or off, and marker size as S / M / L.
  Turning the markers off leaves the tracking running - the map just goes quiet.
- **Quest resets are handled.** Take a quest that resets a POI and its marker goes back to orange.

## How "cleared" is decided

The mod follows a ranked list of loot containers, ordered from the richest down to the most
modest. In every POI it picks the best ranked container that is actually there and watches that
one - lower ranks only matter when nothing better exists in that POI. Anything not on the list is
ignored entirely, so opening a nightstand on the way in marks nothing.

The list itself lives in `PoiMapPlus.xml` under `tracked_loot_lists`, where every entry carries a
`rank` - 1 being the richest. An entry can also carry `max_poi_tier`, which limits it to POIs up
to that tier: a weapons bag is the prize of a tier 1 house, but in a tier 5 dungeon it is noise,
so by default it only counts in tier 1. Look in the file for the current defaults; reordering
them or adding your own containers is a matter of editing it.

If a POI holds nothing from the list at all, it is marked cleared straight away - there is
nothing there worth coming back for.

Weapon bags vanish from the world once emptied. The mod records the rank at the moment such a
container is opened, so they still count. Nothing respawns in their place either, so those POIs
stay green.

## How respawned loot is spotted

The game restocks a container in `TEFeatureStorage.UpdateTick`, under conditions worth knowing:

- **Per container, not per POI, and not on a world-wide schedule.** Each one counts from its own
  `worldTimeTouched`, set when you first open it.
- **Only if it is empty.** Leave a single item inside - even your own junk - and that container
  is never restocked. This is what the chest badge is about.
- **Only while nobody is around.** Until the timer expires, a player within 16 blocks pushes it
  forward, which is why the option is described as applying to "unvisited areas".
- **Only when the chunk is loaded.** The flag actually flips as you come back into range.

So the mod does not scan anything on a timer. At the last scan of a POI it stores one timestamp
and one flag - whether the watched container was left empty - and from those the respawn date is
arithmetic. Opening the map costs two integer comparisons per marker, which is why an explored
map with hundreds of markers does not stutter.

The prediction can be a few in-game hours early if you camped in the POI after looting it, since
the game kept pushing the timer while you were there. Walking back in corrects the record either
way, and the marker is right by the time it matters.

The whole thing needs the world's **Loot Respawn Time** option to be on. With it disabled the
chest badge still works, the marker simply never goes back to orange.

## Requirements

- 7 Days to Die **V 3.3**. Built and tested against b18.
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
  `XUiC_LootWindow.OnClose`, `EntityPlayer.onNewPrefabEntered`,
  `PrefabInstance.ResetBlocksAndRebuild`) and appends one nav object class through XPath, so no
  vanilla config file is overwritten.
- Both marker sprites are referenced by name out of the game's shared `UIAtlas`, so the mod ships
  no artwork and cannot drift out of sync with a UI overhaul that restyles that atlas.
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
| `MinTier` | `1` | lowest POI difficulty tier that gets a marker; `0` shows sheds too |
| `OnlyDiscovered` | `true` | `false` reveals every POI in the world from the start |
| `MaxMarkers` | `600` | cap on simultaneous markers, nearest to the player win |
| `IconScale` | `1` | starting marker size; the popup's S/M/L writes over it |
| `IconScaleSmall/Medium/Large` | `1` / `1.4` / `2` | what S, M and L mean |
| `BadgeMaxZoom` | `1.4` | zoom past which only the skull is drawn |
| `ChestSprite` | `ui_game_symbol_treasure` | any sprite name from the game's `UIAtlas` |
| `TierMid` | `3` | tier that turns orange; below it white, above it red |
| `Verbose` | `false` | log every container opened inside a POI, for troubleshooting |
| `tracked_loot_lists` | see above | which containers count, and in what order |

Container names are LootLists from `Data/Config/loot.xml`, not block names.

The badge layout - `ChestScale`, `ChestOffsetX/Y`, `TierScale`, `TierOffsetX/Y` - is in fractions
of the current icon size, measured from the centre of the skull with X right and Y up, so it
holds at every zoom level. All of it is re-read by `poimap reload`, so it can be dialled in with
the map open.

Sizes here are UI units rather than screen pixels: `UIRoot` scales the interface to your
resolution on top of them. `BadgeMaxZoom` is a zoom level rather than a size, so the cutoff it
works out to follows whichever size preset is active - switching S/M/L does not quietly move it.
`poimap status` prints both the cutoff and the size the map last drew.

What the popup changes is remembered per save, in `PoiMapPlus.ui` next to the POI state, and it
overrides the starting values from this file.

The config is re-read on every world load, so a trip through the main menu is enough. Or use the
console:

```
poimap reload    re-read the config and refresh markers
poimap rescan    reload, forget learned loot state, rescan from scratch
poimap status    show tracked lists and POI counts
poimap here      dump containers and stored state for the POI you are standing in
poimap respawn   backdate the emptied chest here so its respawn is already due, for
                 testing the marker without waiting; "poimap respawn all" does the lot
poimap verbose   toggle the per-container logging
poimap navdump   list nav objects by class and tracking type
```

## Localization

`Config/Localization.csv` covers every language the game ships with except Russian: English, German, Spanish,
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
