# Incident-Historie hinter den Top Gotchas (Wrath Tactics)

Belege, die aus `CLAUDE.md` ausgelagert wurden; die Regel selbst bleibt dort ein Einzeiler. Cross-mod incidents (deploy timeouts, Deck-offline releases, engine-API regressions) live in the parent repo: `wrath-mods/claude-context/incidents.md`.

- **Blueprint-Matching ist exact-only (GUID oder voller Name), nie `Contains`** (Top Gotchas): Substring matchte versteckte Item-/Aura-Facts — `WrathOfTheUndeadCountBuff` machte Golems zu Untoten. Bug-Klasse traf HasBuff (pre-1.17.4) UND CreatureType (pre-1.23.3). Details `gotchas-conditions.md`.
- **Partial-class file split for `ActionValidator`** (Code Style): the single-file `ActionValidator` grew to 902 LOC once before the one-Action-type-per-file split — don't merge back.
- **Deploy-Timeout ≠ Deck offline / Deck offline blockiert keinen Release**: both dated cases are wrath-tactics history but the rules live in the parent — see `wrath-mods/claude-context/incidents.md` §Steam Deck / Deploy.
