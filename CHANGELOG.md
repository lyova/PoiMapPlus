# Changelog

## 1.0.0

First public release. Built and tested against 7 Days to Die V 3.2.0 (b9).

- Map markers for every discovered POI, orange while unlooted, green once cleared.
- Hover tooltip with POI name and difficulty tier, no permanent labels on the map.
- Cleared detection driven by a ranked list of loot containers, configurable in `PoiMapPlus.xml`.
- Containers that vanish when emptied (weapon bags, ammo piles) are tracked through the open event.
- Quest driven POI resets clear the marker again.
- Console commands: `poimap reload`, `rescan`, `status`, `navdump`.
- Localization for every language the game ships with, plus Ukrainian.
