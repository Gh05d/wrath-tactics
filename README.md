# Wrath Tactics

**Make Companions Smart Again.**

A Unity Mod Manager mod for **Pathfinder: Wrath of the Righteous** that brings Dragon Age Origins-style tactical AI to your party. Define priority-ordered rules per companion (and globally), and the mod evaluates them every few seconds in real-time combat — automatically casting spells, using items, activating class abilities, or picking attack targets based on the conditions you set.

---

## What it does

In real-time combat, companions follow your rules:

- **Daeran** heals himself with Cure Moderate Wounds when HP drops below 50%
- **Camellia** casts Evil Eye – AC on the highest-threat enemy with AC > 20
- **Ember** casts Phantasmal Web when Will Save of the biggest threat is low
- Global rule: anyone with HP < 30% drinks a healing potion

You set the rules once, the mod handles the rest while you focus on positioning and the fun stuff.

## Features

- **Priority-ordered rule list** per companion plus global rules
- **Compound conditions** — AND within groups, OR between groups
- **Rich condition subjects**: Self, Ally, AllyCount, a specific ally, Enemy, EnemyCount, Enemy sorted by threat / HP / AC / saves / HD / distance, Combat
- **Properties**: HP%, AC, Fortitude/Reflex/Will saves, buffs, debuffs (Evil Eye variants, curses, etc.), game conditions (Paralyzed, Stunned, ...), creature type, spell slots, combat rounds, range, flanking, who is targeting whom — full list in the [Glossary](#glossary)
- **Action types**: Cast Spell, Cast Ability, Use Item, Toggle Activatable, Attack, Heal (auto-picks best available heal across spells/scrolls/potions/wands), Throw Splash, Switch weapon set, Move to target, Do Nothing
- **Target selectors**: Self, Ally with lowest HP, Enemy with highest AC / biggest threat / specific creature type, the specific entity that matched the condition, or a ground point for summons and AoE
- **Ability variants supported** — Evil Eye – AC / Evil Eye – Attack / Channel Positive Energy – Damage Undead, etc., with full cast animations
- **BubbleBuffs compatible** — Wrath Tactics handles in-combat reactions while BubbleBuffs handles pre-combat buffing. No conflicts.
- **Per-session debug logging** to its own file (`Mods/WrathTactics/Logs/wrath-tactics-YYYY-MM-DD-HHmmss.log`) with levels (Trace/Debug/Info/Warn/Error) and categories (Engine/UI/Persistence/Compat/Game)

## Installation

1. Install [Unity Mod Manager](https://www.nexusmods.com/site/mods/21) and enable it for Pathfinder: Wrath of the Righteous
2. Download the latest `WrathTactics-X.Y.Z.zip` from the [Releases](https://github.com/Gh05d/wrath-tactics/releases) page
3. Drag the zip onto the UMM window — it installs automatically
4. Launch the game

## Usage

1. Click the **helmet-with-gear button** next to the in-game HUD buttons (bottom edge of screen), or press **Ctrl+T**
2. Select a tab:
   - **Global** — rules that apply to every party member
   - **\<Character name\>** — rules specific to one companion
   - **Presets** — save/load rule collections (savegame-independent)
   - **Packs** — bundle presets into reusable, colour-coded loadouts you can apply to any companion (see [Rule Packs](#rule-packs) below)
3. Click **+ New Rule**, configure conditions and action, arrange priority with the ↑/↓ buttons — every term is explained in the [Glossary](#glossary)
4. Start combat — the mod evaluates rules every ~3 seconds (configurable). Rules only run **in combat** unless you explicitly opt them in for out-of-combat use — see [Out-of-combat rules](#out-of-combat-rules)

### Example: a "healer bot" rule set for Daeran

| Rule | IF | THEN |
|---|---|---|
| 1. Emergency self-heal | Self.HpPercent < 30 | Heal on Self (mode: Strongest) |
| 2. Revive dead allies | AllyCount `>= 1` with IsDead | Cast Spell → Breath of Life on condition target |
| 3. Mass heal | AllyCount `>= 3` with HpPercent < 60 | Cast Spell → Mass Cure Light Wounds on Self |
| 4. Keep Bless up | Self missing buff "Bless" | Cast Spell → Bless on Self |

### Rule Packs

A pack is a named, colour-coded bundle of presets — a reusable "loadout" you can apply to any
companion or pet. Packs live on their own **Packs** tab, after Presets.

- **Create one** on the **Packs** tab: hit **+ New Pack**, name it, pick a colour with the
  swatch, then open **Rules** and add existing presets to it — or build it later from a rule
  list, see below.
- **Apply a pack** to a companion or pet with **+ Apply Pack**, above their rule list. Any number
  of packs can be active on the same character at once — each pack's rules are tinted in its
  colour and shown as a chip above the list.
- **Save List as Pack** opens a dialog: a name field (pre-filled, editable) and a checkbox next
  to every rule currently on that companion's (or the Global) list, all checked by default and
  scrollable if the list is long. Uncheck anything you don't want in the pack, then confirm —
  only the checked rules are bundled, in their current list order, and any standalone ones among
  them are promoted to presets automatically. Cancel closes the dialog without changing anything;
  confirming with nothing checked shows a hint instead of saving. The button only works on the
  whole list, not a filtered view — it's disabled while a filter is active, to make that
  unambiguous.
- Rules inserted by a pack stay linked to their presets: edit the preset on the Presets tab and
  every character running that pack picks up the change. Re-applying a pack restores rules you
  deleted without duplicating the ones still there.
- **A preset never enters the same rule list twice, no matter which pack asks for it.** If two
  packs both include the same preset, applying both only ever produces one rule for it — the
  second pack simply shows no chip for that rule, since it's already covered. This matters when
  you delete: that shared rule "belongs" to whichever pack added it first, so deleting *that*
  pack's rules removes it too, even though another pack still lists it. If you only
  want a pack's colour tag gone without risking any rules, use "remove pack marking" (below)
  instead of deleting.
- **Click a chip to open a menu** with two choices: *"Remove pack marking"* leaves every rule
  exactly where it is, in order — only the colour and the chip disappear, and those rules become
  unowned (a later "Save List as Pack" can claim them again). *"Delete this pack's rules"*
  removes exactly the rules currently stamped with that pack, with no undo — the rule order that
  made up your setup is not restored by re-applying, only the rules themselves are. If any of
  those rules are also relied on by another pack, the menu tells you so before you commit.
- Deleting a pack's definition (on the Packs tab) does not touch already-applied rules — they
  stay on every character exactly as they are; only the chip and its colour go away.
- **Export** (on the Packs tab) copies a self-contained pack — including its presets — to the
  clipboard; the **Import** button on the Presets tab accepts both a pack bundle and the older
  preset-array format, so packs can be shared between players.

## Understanding conditions

A few mechanics that aren't obvious from the UI:

### Out-of-combat rules

By default, rules only fire **while the party is in combat** — so your setups don't go off while you explore, loot, or talk. To let a specific rule also run outside combat, add this condition to it:

```
Combat  →  Is In Combat  →  No
```

Any rule carrying that condition gets evaluated out of combat (every ~2 seconds instead of the in-combat ~3). Typical uses: keeping a long-duration buff up between fights, or re-toggling a stance that drops after combat ends. Two things to know:

- Rules **without** the condition are never evaluated outside combat — that's deliberate, not a bug.
- The condition is a real condition: while you're in combat it is *false*, so the group containing it won't match mid-fight. For a rule that should work in **both** states, put the condition in a second condition group (groups are OR'd).

### Groups: AND inside, OR between

Conditions stacked in the **same group** must *all* be true (AND). Add a **second group** for an "or else" branch (OR).

For **Enemy** and **Ally** conditions, AND is *same-unit*: every Enemy row in one group has to be true for **the same single enemy**. So `Enemy HP < 50%` AND `Enemy missing Bless` in one group means "one enemy that is both below 50% and unbuffed" — not two different enemies. To mean different enemies, split them into separate groups.

### "Lowest / Highest / Nearest" subjects sort — they don't stack

Subjects like **Enemy Lowest HP**, **Enemy Lowest AC** or **Enemy Nearest** tell a group *which order* to scan enemies in; they do **not** add an extra "must be the absolute lowest" test. Within one group, only the **first** such subject decides the order.

So a group of:

```
Enemy Lowest HP  →  WithinRange (<= Long)
Enemy Lowest AC  →  WithinRange (<= Long)
```

resolves to **the lowest-HP enemy that is within Long range**. The "Lowest AC" row just repeats the range check — it does *not* also require that enemy to be the lowest-AC one. There is no way to demand "lowest HP and lowest AC at the same time"; pick the one that matters and express the other as a plain property (e.g. *Enemy Lowest HP* with *AC < 25*). And if you only want to hit the weakest enemy in range, you don't even need a condition — just set the action's **target** directly to *Enemy Lowest HP*.

### DC − Save

Takes the **save DC of the spell this rule will cast** (your live caster DC, with all buffs and feats) and subtracts the target's matching saving throw — Fortitude, Reflex or Will, whichever that spell uses:

```
DC − Save = margin
```

A **positive** margin means the enemy is *likely to fail* the save; negative means they'll probably make it. Use it to fire save-or-die / save-or-suck spells only at worthwhile targets, e.g. *cast Phantasmal Killer if Enemy DC − Save ≥ 0*. It only produces a value for spells that actually force a saving throw — spells with no save (Magic Missile) and non-save effects like Demoralize can't be measured this way.

### Hit Margin (AB − AC)

Your **ally's attack bonus** minus the **enemy's AC**:

```
AB − AC = margin
```

A **positive** margin means you hit easily — it's how many points of slack you have before penalties start costing you hits. A **negative** margin is how far short you are. Both numbers are the *current* values with every active buff, stance and debuff folded in, so you never have to track exact numbers yourself. The `(Ally)` field picks whose attack bonus to use: leave it empty for the party's best, or pin a specific companion. Great for stance decisions, e.g. *toggle Power Attack on only while Hit Margin ≥ -1*.

### Range brackets

**WithinRange** uses brackets, not raw meters: Melee (≤2 m), Cone (≤5 m), Short (≤10 m), Medium (≤20 m), Far (≤30 m), Long (≤40 m). Use **`<= Short`** for "within 10 m or closer" — `= Short` means *only* the 5–10 m band and excludes anything nearer. Note: Far (20–30 m) overlaps Long (20–40 m) — Long kept its original band so rules created before Far existed behave unchanged.

## Glossary

Every term in the rule editor, in UI order. All checks are **snapshots**: a condition says whether something is true *right now* when the rule is evaluated, never "keep doing X until it changes".

### How a rule is evaluated

- Every tick the **Global** list is checked first, then the companion's own list, each **top to bottom**. Priority is the list position; there is no separate priority number.
- A rule fires when its conditions pass, its target can be resolved and its action is possible right now. If any of those fail, the rule is skipped *for this tick* and the next rule is tried.
- Each tick a companion may issue at most **one command per action slot** (standard, move, swift, free). So a cast from a higher rule and a swift action from a lower rule can both fire in one tick; two rules that need the same slot: the upper one wins.
- While a standard-action command (cast, attack, item) is still running, lower rules wait; only rules *above* it may pre-empt it.
- **DoNothing** is the one hard stop: everything below it is ignored while its conditions hold.
- In combat the lists are re-evaluated every ~3 s (configurable), out of combat every ~2 s — but out of combat only for rules carrying `Combat → IsInCombat → No`.
- **Cooldown (rounds)**: after the rule fires, it stays quiet for that many 6-second rounds. `0` = may fire every tick.
- **Enabled**: unticked rules are skipped entirely.

### Condition subjects (who is checked)

| Subject | Meaning |
|---|---|
| **Self** | The companion this rule belongs to. |
| **Ally** | *Any one* party member or pet other than Self. Several Ally rows in the same group must all be true for the **same** ally. |
| **AllyCount** | Counts allies other than Self that satisfy the property; the count is compared with the number on the right (`>= 1`, `< 3`, …). |
| **Enemy** | *Any one* visible, living enemy currently in combat with the party. Several Enemy rows in a group must all be true for the **same** enemy. |
| **EnemyCount** | Counts those enemies; `EnemyCount = 0` is a valid gate ("no enemy in combat"). |
| **Enemy: biggest / lowest threat** | Scans enemies in that order (threat ≈ attack bonus + expected damage per hit) and takes the first one that passes the row's property. Also decides which enemy **Condition target** points at. |
| **Enemy: highest / lowest HP%, AC, Fort, Reflex, Will, HD** | Same as above, sorted by that stat. Only the *first* sorted subject in a group sets the order — a second one does not add a "must also be lowest X" test. |
| **Enemy: nearest** | Same, sorted by distance to Self. |
| **Specific ally** | One pinned companion or pet, picked by name. |
| **Combat** | The fight as a whole: `IsInCombat` (Yes/No) and `CombatRounds` (6-second rounds since combat started). |

### Properties (what is checked)

| Property | Value | Meaning |
|---|---|---|
| **HP%** | number | Current HP as a percentage of max HP. Downed units count as 0. |
| **Current HP** | number | Current hit points, flat; temporary HP not included. |
| **AC** | number | Current Armor Class with every active buff and debuff. |
| **SaveFortitude / SaveReflex / SaveWill** | number | Current saving-throw bonus. |
| **HitDice** | number | Hit dice (character level without mythic ranks). |
| **HasBuff** | buff | The unit carries exactly that buff (picked from the buff list). `!=` means "is missing it". |
| **HasCondition** | condition | The unit has that game condition (Paralyzed, Stunned, Frightened, Nauseated, Confused, Blinded, Prone, Entangled, Exhausted, Fatigued, Shaken, Sickened, Sleeping, Petrified, Slowed, Staggered, Dazed, Dazzled, Helpless, Cowering, Death's Door). |
| **Affected by** | Poison / Disease / Bleed | Any active effect of that kind, no matter which spell or monster caused it. |
| **Immune to energy** | Fire / Cold / Electricity / Acid / Sonic | The unit is fully immune to that energy type. |
| **CreatureType** | type | Aberration, Animal, Construct, Dragon, Fey, Humanoid, Magical Beast, Monstrous Humanoid, Ooze, Outsider, Plant, Swarm, Undead, Vermin, Incorporeal. Uses the same type facts the game's own spells use; "Humanoid" means "none of the other types". |
| **Alignment** | Good / Evil / Lawful / Chaotic / Neutral | Alignment axis. "Neutral" = neither Good nor Evil (LN, N, CN). |
| **HasClass** | class or group | The unit has at least one level in that class, or belongs to the group: Spellcaster (has any spellbook), Arcane, Divine, Martial (a class that is neither arcane nor divine). |
| **IsDead** | Yes/No | Truly dead — needs Raise Dead / Breath of Life. A companion who is down but will get up after the fight does **not** count. |
| **Is summon** | Yes/No | A summoned monster (Summon Monster, Animate Dead, …). Pets and companions are not summons. |
| **Is pet** | Yes/No | Animal companion, Aivu, skeletal champion, eidolon, clone or similar. |
| **Wields ranged weapon** | Yes/No | The unit's current weapon is a bow, crossbow, firearm, sling or thrown weapon. Re-checked every tick, so a weapon swap changes it. |
| **Is flanked** | Yes/No | The game's own flanking state: at least two enemies are engaging the unit in melee (and it has no "cannot be flanked" feature). This is about attackers, not about being surrounded geometrically. |
| **Enemies adjacent** | number | How many living enemies in combat stand within 2 m of the unit. |
| **Ability damage** | Yes/No | One of the six ability scores carries temporary damage (what Lesser Restoration cures). Drain is not counted. |
| **Negative levels** | Yes/No | The unit has negative levels (energy drain), temporary or permanent. |
| **SpellSlotsAtLevel** | spell level | The unit still has at least one unused spell slot of exactly that level (any spellbook). |
| **SpellSlotsAboveLevel** | spell level | At least one unused slot of that level *or higher*. |
| **Resource** | resource | The unit has at least one use left of that class resource (Ki, Lay on Hands, Arcane Reservoir, …). |
| **WithinRange** | bracket | Distance between Self and the checked unit — see [Range brackets](#range-brackets). Self → WithinRange is meaningless (always 0 m). |
| **DC − Save** | number | Save DC of the spell this rule will cast minus the target's matching save — see [DC − Save](#dc--save). Enemy subjects only. |
| **Hit margin (AB − AC)** | number | An ally's attack bonus minus the enemy's AC — see [Hit Margin](#hit-margin-ab--ac). Enemy subjects only. |
| **HD − PartyMax** | number | Enemy hit dice minus the party's highest character + mythic level. Positive = the enemy outlevels you. Enemy subjects only. |
| **Targeting me** | Yes/No | This enemy's current attack or spell is aimed at Self. Enemy subjects only. |
| **Targeting ally** | Yes/No | This enemy is aiming at a party member (optionally one pinned companion). Enemy subjects only. |
| **Targeted by ally** | Yes/No | A party member (optionally a pinned one) is attacking this enemy. Enemy subjects only. |
| **Targeted by enemy** | Yes/No | Some enemy is attacking this ally. Ally subjects only. |
| **IsInCombat** | Yes/No | Whether the party is in combat. Subject Combat only — see [Out-of-combat rules](#out-of-combat-rules). |
| **CombatRounds** | number | 6-second rounds elapsed since the current fight began. Subject Combat only. |

**Operators:** `<`, `>`, `<=`, `>=`, `=`, `!=`. Yes/No properties only use `=` and `!=`. For **WithinRange** the operator compares *brackets*, not meters: `<= Short` = within 10 m, `< Short` = within 5 m, `= Short` = the 5–10 m band only.

### Actions (what the companion does)

| Action | Meaning |
|---|---|
| **CastSpell** | Casts the chosen spell. *Sources* chooses where it may come from: spellbook (also wands in quickslots), scrolls, potions. Optional fallbacks are tried in order when the first spell is unavailable. Optional metamagic rod and Arcane Reservoir boost. |
| **CastAbility** | Uses a class or racial ability that is not a spell (Lay on Hands, Kinetic Blast, Evil Eye, …). |
| **UseItem** | Uses a specific usable item: an equipped wand/scroll/trinket first, otherwise from the shared inventory (potion, then scroll, then wand, then rods and other devices). |
| **ToggleActivatable** | Switches a toggle ability (Power Attack, Fighting Defensively, stances, Smite …) **On** or **Off**. Fires only when the toggle is not already in that state. |
| **AttackTarget** | Makes a normal weapon attack on the target. |
| **Heal** | Picks a heal automatically. *Mode* Strongest/Any = highest-ranked (spellbook spell by level > class feature > scroll > potion), Weakest = lowest-ranked. *Sources* can exclude scrolls or potions. *Energy* Auto heals living with Cure and undead with Inflict; Positive/Negative force one. Scrolls that would need a risky Use Magic Device check are only used as a last resort. |
| **ThrowSplash** | Throws a splash weapon (Alchemist's Fire, Acid Flask, Holy Water …). Any = first in inventory, Strongest = highest damage, Cheapest = lowest value. |
| **Switch weapon set** | Switches to weapon set 1–4. |
| **Move to target** | Walks toward the target until within the chosen bracket ("stop within Melee / Cone / Short …"). Re-checked every tick, so a moving target is followed. Does nothing when already within that distance — which is why **Target: Self** never moves. While the walk is running, lower rules that would need the move or standard action are held back. |
| **DoNothing** | Issues no command and stops every rule below it for this tick. Use it as a blocker, e.g. "if Self HP% < 20 do nothing" above a charge rule. |

### Targets (whom the action is aimed at)

| Target | Meaning |
|---|---|
| **Self** | The companion itself. |
| **Ally: lowest HP** | Party member or pet (not Self) with the lowest HP%. |
| **Ally with condition** | First ally (not Self) with the chosen condition. |
| **Ally missing buff** | First ally (not Self) without the chosen buff. |
| **Specific ally** | One pinned companion or pet. |
| **Ally: most clustered (5 m)** | The ally with the most other living allies within 5 m — for area buffs. |
| **Enemy: nearest / lowest HP / highest HP / highest AC / lowest AC / … / highest HD / lowest HD** | Sorts the visible enemies in combat by that stat and takes the first. |
| **Enemy: highest threat** | Enemy with the highest threat score (attack bonus + expected damage). Default target for attack rules. |
| **Enemy: creature type** | First enemy of the chosen creature type. |
| **Enemy: most clustered (5 m)** | The enemy with the most other enemies within 5 m — for Fireball-style spells. |
| **Condition target** | The unit that made the conditions pass: the enemy matched by an Enemy row, else the ally matched by an Ally row, else Self when only Self rows matched. |
| **Point at self** | A ground point ~1.5 m in front of the companion — for summons and ground-targeted spells. |
| **Point at condition target** | A ground point ~1.5 m from the condition target, on the side facing the companion. Same enemy > ally > self preference as **Condition target**. |

If the target cannot be resolved (no enemy of that type, no ally missing the buff, …), the rule is skipped for this tick and the next rule is tried.

## BubbleBuffs compatibility

Wrath Tactics plays nicely with [Buff It 2 The Limit (BubbleBuffs)](https://www.nexusmods.com/pathfinderwrathoftherighteous/mods/948). The HUD button for Wrath Tactics is placed next to BubbleBuffs' quick-buttons when both mods are installed. There are no shared state conflicts — BubbleBuffs handles pre-combat buff routines, Wrath Tactics handles in-combat tactical decisions.

## Inspiration

Dragon Age: Origins had a wonderful **Tactics** system that let you program companion behavior with slot-based condition-action rules. This mod brings that concept to Pathfinder: WotR.

## Development

See [CLAUDE.md](CLAUDE.md) for dev notes. Built with:

- .NET Framework 4.8.1
- [BepInEx.AssemblyPublicizer.MSBuild](https://github.com/BepInEx/BepInEx.AssemblyPublicizer) for accessing private game fields
- [HarmonyLib](https://github.com/pardeike/Harmony) for game patches
- [Unity Mod Manager](https://www.nexusmods.com/site/mods/21) framework

### Build

```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
```

On Linux, symlink or create `GamePath.props` pointing to the game's `Wrath_Data/Managed` directory.

## License

MIT
