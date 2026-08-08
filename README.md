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
- **Rich condition subjects**: Self, Ally, AllyCount, Enemy, EnemyCount, EnemyBiggestThreat, EnemyLowestThreat, Combat
- **Properties**: HP%, AC, Fortitude/Reflex/Will saves, buffs, debuffs (Evil Eye variants, curses, etc.), game conditions (Paralyzed, Stunned, ...), creature type, spell slots, combat rounds
- **Action types**: Cast Spell, Cast Ability, Use Item, Toggle Activatable, Attack, Heal (auto-picks best available heal across spells/scrolls/potions/wands), Do Nothing
- **Target selectors**: Self, Ally with lowest HP, Enemy with highest AC / biggest threat / specific creature type, or the specific entity that matched the condition
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
3. Click **+ New Rule**, configure conditions and action, arrange priority with the ↑/↓ buttons
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

**WithinRange** uses brackets, not raw meters: Melee (≤2 m), Cone (≤5 m), Short (≤10 m), Medium (≤20 m), Long (≤40 m). Use **`<= Short`** for "within 10 m or closer" — `= Short` means *only* the 5–10 m band and excludes anything nearer.

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
