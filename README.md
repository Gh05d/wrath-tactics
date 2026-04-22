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
3. Click **+ New Rule**, configure conditions and action, arrange priority with the ↑/↓ buttons
4. Start combat — the mod evaluates rules every ~3 seconds (configurable)

### Example: a "healer bot" rule set for Daeran

| Rule | IF | THEN |
|---|---|---|
| 1. Emergency self-heal | Self.HpPercent < 30 | Heal on Self (mode: Strongest) |
| 2. Revive dead allies | AllyCount `>= 1` with IsDead | Cast Spell → Breath of Life on condition target |
| 3. Mass heal | AllyCount `>= 3` with HpPercent < 60 | Cast Spell → Mass Cure Light Wounds on Self |
| 4. Keep Bless up | Self missing buff "Bless" | Cast Spell → Bless on Self |

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
