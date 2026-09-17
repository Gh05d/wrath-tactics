# Deck smoke-test pack

Synthetic rule pack for engine changes (slot economy, action budget, move action). Empty
conditions + `CooldownRounds: 0` force the full pattern every tick; enums are numeric
(`ActionType`: 1 CastSpell, 4 AttackTarget, 9 MoveToTarget; `TargetType`: 0 Self, 4 EnemyNearest;
`RangeBracket`: 0 Melee, 2 Short). Ability GUIDs are Ember's Evil Eye (AC) and Cackle.

Push (game closed or before the next mod load — presets/packs are read at mod start):

    tar -C docs/testing/deck-smoke -cf - Presets Packs | ssh deck-direct "tar -xf - -C '<gamepath>/Mods/WrathTactics'"

Then apply the pack to the unit in the panel; re-apply after adding presets (no sync on load).
Remove the pack's rules from the unit afterwards (chip menu, option 2) — it fires every round otherwise.
