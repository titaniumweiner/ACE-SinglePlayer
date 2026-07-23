# Aquafir Creature Variants

This preview mod is a reviewed OpenDereth adaptation of the creature concepts in Aquafir's
[ACE.BaseMod Expansion/Creatures](https://github.com/aquafir/ACE.BaseMod/tree/41c2728fc0e4fb96d06b4e7949ca369ed5be9621/Samples/Expansion/Creatures).
Aquafir's work and this derivative are distributed under AGPL-3.0. The pinned source commit is
`41c2728fc0e4fb96d06b4e7949ca369ed5be9621` (November 26, 2024).

This is not a verbatim binary repack. The upstream sample describes itself as minimally tested,
depends on the larger ACE.BaseMod/ACE.Shared framework, and contains unfinished or unsafe paths.
OpenDereth instead attaches bounded runtime traits to an ordinary monster after ACE creates it.
No weenie, character, or world database row is rewritten.

## Default behavior

For the requested test profile, 50% of eligible ordinary monsters receive at least one weighted
random variant when they spawn. Stacking is enabled: after the first variant, the monster has a
50% chance to receive another distinct variant, checked again until the default cap of three.
This means approximately 25% of eligible creatures receive at least two variants and 12.5% receive
three. A variant can never repeat on the same creature.

All eighteen reviewed variants have positive default weights. The encounter-changing `Boss`,
`Puppeteer`, and `Horde` variants have lower weights than ordinary traits. Combined prefixes in the
monster's name show every assigned variant. Players, vendors, NPCs, pets, combat pets, and
specialized ACE creature classes are excluded. Restarting without the mod restores stock behavior
and names.

The random chance, stacking toggle/chance/cap, level range, WCID allow/exclude lists, per-trait
weights, and every mechanical value are in `Settings.json`. Use `@creaturevariants status`,
`@creaturevariants list`, or `@creaturevariants reload` as a developer.

To disable stacking, set `AllowVariantStacking` to `false`. To keep stacking but make it rarer,
lower `AdditionalVariantChance`. `MaximumVariantsPerCreature` is validated from 1 through 18.
For a calmer long-term world, reduce `AssignmentChance` from the intentionally high test value.

## Available variants

| Variant | Name prefix | Behavior in this port |
|---|---|---|
| Accurate | Accurate | Has a configurable flat chance to make a physical attack unavoidable. |
| Berserker | Raging | Below a health threshold, gains a runtime multiplier to all six attributes. |
| Comboer | Frenetic | Every configured number of consecutive hits deals bounded bonus damage; taking damage resets the combo. |
| Drainer | Atrophying or Stultifying | Each instance drains stamina or mana in addition to successful physical damage. |
| Duelist | Dueling | Parries player damage while facing the attacker inside a configurable frontal arc. |
| Evader | Evasive | Has a configurable flat chance to evade a physical attack. |
| Exploder | Exploding | Counts down near its player target, deals health-scaled fire damage capped by settings, then dies. |
| Healer | Mending | Periodically heals a limited number of nearby damaged ordinary monsters. |
| Shielded | Shielded | Negates a configured number of hits and replenishes the shield set on a timer. |
| SpellBreaker | Breaking | A successful physical hit can interrupt an active cast and deal bounded mana-based fire damage. |
| Stomper | Massive | Successful hits splash falloff damage to a limited number of nearby players. |
| Vampire | Vampiric | Heals for a configurable fraction of successful physical damage. |
| Rogue | Rakish | Successful attacks from behind can interrupt the player's current attack or cast and drain stamina. It never moves or drops equipment. |
| Horde | Swarm of N/M | Represents a finite swarm through runtime health and outgoing-damage scaling. Members disappear as the shared health pool falls. |
| Puppeteer | Conniving | Creates temporary, invulnerable, non-attacking, zero-XP illusions during combat. Copies are destroyed on timeout, owner death, or mod unload. |
| Boss | Tyrannical | Gains bounded model, attribute, vital, damage, and XP multipliers; rotates a weakness and periodically casts one configured special spell. |
| Tank | Guardian | Intercepts player damage aimed at a nearby monster and explicitly takes a configurable fraction of that damage itself. |
| Stunner | Debilitating | Periodically interrupts and freezes its nearby player target for a short bounded duration, then restores the player's prior frozen state. |

Aquafir's accurate and evader samples used a reversed probability comparison, which made their
documented 25% effects occur roughly 75% of the time. This port uses the documented probability.
The combo and explosion mechanics are bounded rather than invoking the sample's ring-spell and
1000-damage behavior.

## Make a specific WCID an example variant

`ForcedTraitsByWeenieClassId` bypasses both random assignment and stacking, assigning exactly one
requested variant. Keys may be decimal or `0x`-prefixed
hexadecimal WCIDs. For example:

```json
"ForcedTraitsByWeenieClassId": {
  "1234": "Berserker",
  "0x4D3": "Shielded"
}
```

The entries above assign WCID 1234 and hexadecimal WCID `0x4D3` (1235). Forced entries still
respect `Enabled`, the level range, the allow list, and the exclusion list. A settings reload
affects new spawns; creatures already alive keep their assigned variants until they despawn or the
server restarts.

## How the six advanced variants were completed

The upstream ideas were retained, but their incomplete or destructive mechanisms were replaced:

- `Rogue` performs a fumble-style action interruption and stamina drain; player items never leave their slots.
- `Horde` computes living members from a bounded shared health pool and scales damage without spawning corpses or recursively growing.
- `Puppeteer` uses separately tracked runtime illusions rather than generator profiles. They cannot attack, be attacked, grant XP, drop loot, or outlive their owner/mod.
- `Boss` uses configurable multipliers, rotating weaknesses, and one periodic spell instead of 1000x XP, unbounded volleys, or 50-projectile bullet hell.
- `Tank` calls damage on the actual guardian and cancels the protected hit rather than trying to replace Harmony's method instance reference.
- `Stunner` now freezes and interrupts for a configured duration, supports overlapping expiry safely, and restores the player's pre-existing frozen state.

## Upstream types still withheld

These remain unavailable rather than presenting unreviewed behavior as installable:

- `Banisher`: instantly destroys player summons and is too destructive for default random assignment.
- `SpellThief`: enchantment ownership, expiration, and save behavior need a dedicated tested transfer design.
- `Warder`: requires a broader spell-target interception audit to avoid breaking valid casts.
- `Merger`: creature deletion and stat/XP persistence need a dedicated world-data audit.
- `Avenger`, `Bard`, `Necromancer`, `Poisoner`, `Reaper`, `Runner`, `Splitter`, and `Suppresser`: prototypes are commented or contain no complete behavior.

These exclusions are a support matrix, not a claim that the ideas are unusable. They can be added
later as individually reviewed, opt-in variants with tests and conservative defaults.

## Safety and testing

This package compiles against the OpenDereth-pinned ACE.Server and validates every settings bound.
It is still a preview: the full matrix of stacked combinations, custom creatures, spells,
dungeons, other combat mods, and long play sessions has not been thoroughly tested in game. Back
up a world before testing any new combat mod. The default 50% assignment and 50% stacking chances
are intentionally conspicuous for testing, not a recommended final balance.
