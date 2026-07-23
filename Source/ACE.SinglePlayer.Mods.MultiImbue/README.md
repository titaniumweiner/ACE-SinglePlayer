# Three Imbues

Three Imbues allows a compatible item to receive up to three **distinct** standard salvage imbues. For example, a melee weapon can carry Armor Rending, Critical Strike, and Crippling Blow at the same time when it passes the normal ACE recipe requirements for each application.

## What changes

- The stock one-imbue recipe restriction becomes a three-imbue limit.
- Each effect is stored in ACE's persistent `ImbuedEffect`, `ImbuedEffect2`, and `ImbuedEffect3` properties.
- Weapon and equipped-defense calculations read the combined effects from all ACE imbue slots.
- Reapplying an effect already present on the item is rejected.

The classic client has only one imbue icon-underlay field. The most recent successful imbue may therefore be the one shown on the inventory icon even though all three stored effects remain active.

The mod recognizes the standard Black Opal, Fire Opal, Sunstone, elemental-rending, and defensive-rending salvage families, including their foolproof variants when ACE reports the corresponding material type.

## What does not change

Every stock rule other than the single-imbue restriction remains in force:

- the item must be eligible for the requested material and recipe;
- the ten-tinker limit and `NumTimesTinkered` progression remain unchanged;
- workmanship, required tinkering skill, and success chance remain unchanged;
- an unsuccessful non-foolproof attempt can still destroy the target under ACE's normal rules;
- incompatible item/material combinations remain invalid.

`Settings.json` defaults to `MaximumImbues: 3`. It may be lowered to one or two, but values above three are rejected.

## Installation

Stop the OpenDereth server, import the single ZIP through **Server Mods → Import a Mod ZIP...**, enable **Three Imbues**, and restart the server. OpenDereth verifies the package's embedded SHA-256 file manifest during import.

## Removal warning

Back up the private database before enabling this preview mod. Do not permanently remove the mod while characters own multi-imbued items. Secondary imbues remain saved in the item, but stock ACE combat reads only the primary slot; disabling the mod therefore makes secondary effects inactive until the mod is enabled again.

This preview has automated coverage for material-to-effect mapping, distinct-effect counting, the three-slot cap, settings validation, Harmony patch targeting, and package structure. The full matrix of weapons, casters, missile weapons, armor, foolproof salvage, success/failure destruction, appraisal display, and other tinkering mods still requires in-game testing.
