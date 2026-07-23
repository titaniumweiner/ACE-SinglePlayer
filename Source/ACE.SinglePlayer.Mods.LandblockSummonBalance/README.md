# Landblock Summon Balance

Landblock Summon Balance applies temporary combat multipliers to **player-owned combat summons only** while they fight in specifically configured landblocks. It does not edit summon weenies, the ACE world database, character saves, client DAT files, or the base statistics stored on a summon.

The included example zone is disabled. Installing the mod therefore changes nothing until you deliberately add a landblock and enable that zone.

## Quick setup

1. Install and enable **Landblock Summon Balance** in OpenDereth.
2. Log in with a Developer-level account and stand in the area you want to balance.
3. Enter `@summonbalance where` in chat.
4. Note the four-digit landblock shown, such as `0xA9B4`.
5. Open the installed mod's `Settings.json` file.
6. Add the ID under `Landblocks`, choose the multipliers, and set `Enabled` to `true`.
7. Save the file and enter `@summonbalance reload`. A server restart also reloads it.

Example:

```json
{
  "Zones": [
    {
      "Name": "Example Raid",
      "Enabled": true,
      "Priority": 0,
      "MatchLocation": "Opponent",
      "Landblocks": ["0xA9B4", "0xA9B5"],
      "ExactCells": [],
      "PhysicalDamageMultiplier": 0.4,
      "SpellDamageMultiplier": 0.4,
      "PhysicalAttackSkillMultiplier": 0.7,
      "PhysicalDefenseSkillMultiplier": 0.8,
      "DamageTakenMultiplier": 1.25
    }
  ]
}
```

In that example, player summons inflict 40% physical and direct spell damage, use 70% physical attack skill, use 80% physical defense skill, and receive 25% more damage while fighting opponents in either tagged landblock.

## What can be modified

All multipliers use `1.0` for normal ACE behavior. `0.5` means half, `0.0` removes that contribution entirely, `1.25` means 25% more, and `2.0` means double. Values from `0.0` through `10.0` are accepted.

| Setting | What it changes |
| --- | --- |
| `PhysicalDamageMultiplier` | Final melee and physical missile damage inflicted by the summon. |
| `SpellDamageMultiplier` | Final direct spell-projectile damage inflicted by the summon. |
| `PhysicalAttackSkillMultiplier` | The summon attack skill used for melee/missile hit checks. Lower values make it miss more often. |
| `PhysicalDefenseSkillMultiplier` | The summon defense skill used for melee/missile evade checks. Lower values make it easier to hit. |
| `DamageTakenMultiplier` | Physical and direct spell damage received by the summon. Use a value above `1.0` to make summons more fragile or below `1.0` to make them tougher. |

The mod intentionally does not rewrite displayed attributes, maximum health, saved skills, or ratings. Directly changing those fields risks stacking reductions when crossing a boundary, saving altered values, or producing confusing health changes when leaving a zone. These calculation-time controls provide predictable encounter balancing and disappear cleanly when the mod is disabled.

Damage-over-time effects, environmental damage, healing, buff strength, spell-resist skill, and passive non-combat pets are not changed in version 1.0.

## Landblocks and exact cells

`Landblocks` accepts four hexadecimal digits. A landblock entry applies to every cell whose ID begins with those digits:

```json
"Landblocks": ["0xA9B4"]
```

Outdoor encounters that cross a boundary should list every involved landblock. A dungeon can generally be covered by its four-digit landblock ID as well.

`ExactCells` is an advanced override for limiting a rule to individual dungeon or outdoor cells. It accepts the complete eight-digit cell displayed by `@summonbalance where`:

```json
"ExactCells": ["0xA9B4012F", "0xA9B40130"]
```

An exact-cell match is more specific than a whole-landblock match.

## Choosing which location controls the rule

`MatchLocation` supports three values:

| Value | Behavior |
| --- | --- |
| `Opponent` | Recommended. The rule applies according to the creature the summon is fighting. This prevents parking a summon just outside the boundary and attacking inward. |
| `Summon` | The rule applies according to where the summon itself is standing. |
| `Either` | The rule applies when either the summon or its opponent is in the tagged area. |

## Multiple and overlapping zones

Create additional objects inside `Zones` to balance different encounters independently. Every zone must have a unique `Name`.

When multiple zones match:

1. An exact-cell match wins over a whole-landblock match.
2. If matches are equally specific, the zone with the highest `Priority` wins.
3. If specificity and priority are equal, the first zone in `Settings.json` wins.

Zones are not multiplied together. This prevents accidental extreme scaling from overlapping entries.

## Commands

These commands require a Developer-level account:

| Command | Purpose |
| --- | --- |
| `@summonbalance where` | Shows the current four-digit landblock, full eight-digit cell, and matching zone. |
| `@summonbalance status` | Lists enabled zones and the active settings-file path. |
| `@summonbalance reload` | Validates and reloads `Settings.json` without restarting. Invalid edits are rejected and the last valid settings remain active. |

## Removing the mod

Disable the mod and restart the server. All summon calculations immediately return to stock ACE behavior. No cleanup SQL or character repair is required because the mod never stores scaled values.

This is a preview mod. The package is built and automatically tested against the pinned OpenDereth ACE version, but every summon type, custom spell, dungeon boundary, and mod interaction has not yet received thorough in-game testing.
