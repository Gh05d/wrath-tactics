# Deck smoke-test pack

Synthetic rule pack for engine changes (slot economy, action budget, move action). Empty
conditions + `CooldownRounds: 0` force the full pattern every tick; enums are numeric
(`ActionType`: 1 CastSpell, 4 AttackTarget, 9 MoveToTarget; `TargetType`: 0 Self, 4 EnemyNearest;
`RangeBracket`: 0 Melee, 2 Short). Ability GUIDs are Ember's Evil Eye (AC) and Cackle.

Push (game closed or before the next mod load — presets/packs are read at mod start):

    tar -C docs/testing/deck-smoke -cf - Presets Packs | ssh deck-direct "tar -xf - -C '<gamepath>/Mods/WrathTactics'"

Then apply the pack to the unit in the panel; re-apply after adding presets (no sync on load).
Remove the pack's rules from the unit afterwards (chip menu, option 2) — it fires every round otherwise.

## Walk Hold Test (pack `…fac2`)

Reproduces the 1.31.0 report: walk rule above Cackle, both cooldown 0, no conditions.
Preset `…0005` walks to within Short (10 m) of a **pinned ally** (`TargetType.SpecificAlly` = 22,
`Filter` = the ally's `UniqueId`; the fixture pins Arasmes in Pascal's save,
`43b607d1-766c-4cac-8034-adfd79124293` — replace for another save, ids are in
`UserSettings/tactics-<GameId>.json`). Apply to Ember, start a fight with her well behind Arasmes.
Expected log: `MoveToTarget: Ember -> Arasmes …` once, then `waiting for walk to finish (Rule N …)`
on the Cackle line every tick until `UnitMoveTo [Move] ended: Success`, then Cackle fires.
No `Move action spent` on the walk rule.
