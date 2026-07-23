# Housing Upgrade Pack

Housing Upgrade Pack is a configurable OpenDereth server mod for single-player housing. Its default configuration expands every housing storage chest and removes ACE's total and category-based limits on hooked items.

Default capacity per chest:

| House type | Item slots | Pack slots |
| --- | ---: | ---: |
| Apartment | 150 | 12 |
| Cottage | 180 | 15 |
| Villa | 220 | 20 |
| Mansion | 255 | 25 |

Configured values are minimums. A chest supplied by world content with a larger stock capacity is never reduced. ACE's capacity fields are one byte, so the maximum supported value is 255.

The included `Settings.json` contains these controls:

```json
{
  "IncreaseStorageCapacity": true,
  "Apartment": { "ItemSlots": 150, "PackSlots": 12 },
  "Cottage": { "ItemSlots": 180, "PackSlots": 15 },
  "Villa": { "ItemSlots": 220, "PackSlots": 20 },
  "Mansion": { "ItemSlots": 255, "PackSlots": 25 },
  "RemoveHookLimits": true,
  "RemoveRentAndMaintenance": false,
  "RemoveMansionAllegianceRankRequirement": false,
  "RemoveHousePurchaseTimers": false
}
```

- `IncreaseStorageCapacity` enables the per-house storage minimums.
- `RemoveHookLimits` disables both ACE's total-hook cap and its item-category hook caps. It does not create new physical hook points or allow more than one item on the same hook.
- `RemoveRentAndMaintenance` makes all housing maintenance-free and prevents non-payment eviction while enabled.
- `RemoveMansionAllegianceRankRequirement` removes the configurable allegiance-rank requirement used when buying and retaining mansions. It does not remove the separate requirement that a mansion buyer be an allegiance monarch.
- `RemoveHousePurchaseTimers` removes the 15-day account-age rule and 30-day repurchase cooldown. Purchase prices, level requirements, existing-house ownership rules, and other checks remain unchanged.

Stop the game and local server before editing settings, then restart OpenDereth. Invalid capacities stop the mod from loading instead of silently wrapping past the server's byte-sized limits.

## Removing the mod

Disabling the mod restores stock capacities and housing rules after restart. Items already stored above a chest's stock capacity are not deleted, but the chest remains over capacity and cannot accept more of that item type until enough items or packs are removed. If the mansion-rank option allowed a character to acquire or retain a mansion they no longer qualify for, restoring stock rank checks can make that mansion subject to eviction at a later maintenance check. Back up the OpenDereth save before enabling those options and reduce overfilled storage before removing the mod.

The mod does not alter ACE server configuration in the database. All rule changes are reversible runtime overrides.

This package is a preview. It has automated tests for every capacity profile, settings bounds, exact ACE property targets, option isolation, and clean Harmony removal, but it has not yet been thoroughly tested with every house layout in game.

Source: <https://github.com/titaniumweiner/OpenDereth/tree/main/Source/ACE.SinglePlayer.Mods.HousingUpgradePack>

This mod is distributed under the GNU Affero General Public License v3.0 included with OpenDereth.
