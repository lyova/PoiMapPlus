# Changelog

Versions here are the mod's own. On Nexus this mod is numbered after the game version it was
first published for, so 1.1.0 is released there as **3.3** and 1.0.0 was **3.2**.

## 1.1.1

Released on Nexus as **3.3.1**.

- Updated for 7 Days to Die V 3.3. The mod failed to load on 3.3, and this build does not run on
  3.2 or older - stay on 1.1.0 there.
- Removed the Russian translation.

## 1.1.0

- Cleared POIs turn orange again once the game respawns their loot, predicted from stored state
  without scanning the world.
- Chest badge on the marker: green when the container was left empty and its loot will come back,
  orange when something was left inside, which blocks the respawn.
- Tier printed on the marker, coloured by difficulty.
- Settings popup behind a gear on the map: markers, tier label and chest badge on or off, plus
  three marker sizes. Stored per save.
- Tooltip says what happened to the POI, not just its name.
- Badges are dropped once the map is zoomed out far enough that they stop being readable.
- `poimap respawn` fakes a due respawn, for testing the marker.
- Fixed the map losing every icon for a frame when a tracked transform was destroyed.
- Fixed the marker lagging a step behind after emptying a chest.
- Removed `TopContainerCount`: it only ever watched one container, and raising it broke the model.
- POI state file bumped to v4, older files are upgraded in place.

## 1.0.0

First public release. Built and tested against 7 Days to Die V 3.2.0 (b9).

- Map markers for every discovered POI, orange while unlooted, green once cleared.
- Hover tooltip with POI name and difficulty tier, no permanent labels on the map.
- Cleared detection driven by a ranked list of loot containers, configurable in `PoiMapPlus.xml`.
- Containers that vanish when emptied (weapon bags, ammo piles) are tracked through the open event.
- Quest driven POI resets clear the marker again.
- Console commands: `poimap reload`, `rescan`, `status`, `navdump`.
- Localization for every language the game ships with, plus Ukrainian.
