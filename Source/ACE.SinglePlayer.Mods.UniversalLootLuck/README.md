# All-Tier Salvage & Loot Luck

This optional OpenDereth server mod lets generated equipment from every loot tier use the complete set of material choices that ACE normally divides among tiers. Item categories remain sensible: armor still uses armor-compatible material tables, weapons use their compatible tables, and so on. The difference is that each category combines its tier 1-6 material rows before rolling. Salvaging those generated items therefore makes low- and high-tier salvage materials available without returning to a particular hunting tier.

The default `Settings.json` changes only material availability. Every luck option starts at normal ACE behavior:

```json
{
  "AllMaterialsEveryTier": true,
  "LootQualityBonus": 0.0,
  "GeneratedLootChanceMultiplier": 1.0,
  "TrophyDropRateMultiplier": 1.0,
  "RareDropRateMultiplier": 1.0
}
```

- `AllMaterialsEveryTier`: combines the ACE tier 1-6 material pools used by generated equipment. It does not make loose salvage bags appear on corpses.
- `LootQualityBonus`: adds `0.0` through `0.95` to ACE's loot-quality bias. Start small, such as `0.10` or `0.20`; high values aggressively skip lower-quality outcomes and also affect ACE systems that consume `LootQualityMod`, including cantrip and rating rolls.
- `GeneratedLootChanceMultiplier`: multiplies the chance that each normal, magic, and mundane generated-loot category appears. It does not multiply the number of items after a category succeeds. Allowed range: `0.0`-`10.0`.
- `TrophyDropRateMultiplier`: multiplies the server's existing `trophy_drop_rate` without overwriting it. ACE still caps each trophy set at 100% probability. Allowed range: `0.0`-`10.0`.
- `RareDropRateMultiplier`: multiplies the first rare-eligibility roll without changing the relative distribution of rare tiers. It combines with the server's existing `rare_drop_rate_percent` and any character luck supplied by ACE. Allowed range: `0.0`-`100.0`.

Stop the game and local server before editing settings, then restart OpenDereth. A value of `1.0` means normal ACE behavior and `0.0` disables that particular chance. Invalid or non-finite values stop the mod from loading instead of silently producing an unsafe configuration.

Turning the mod off restores stock rolls after the server restarts. Materials and quality already rolled onto saved items remain on those items; the mod does not rewrite or remove existing inventory.

This package is a preview. It has automated tests for settings bounds, profile cloning, weighted all-tier selection, Harmony targets, multiplier math, and clean removal, but it has not been thoroughly tested across every loot table in game.

Source: <https://github.com/titaniumweiner/OpenDereth/tree/main/Source/ACE.SinglePlayer.Mods.UniversalLootLuck>

This mod is distributed under the GNU Affero General Public License v3.0 included with OpenDereth.
