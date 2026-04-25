# Healing for Undead and Negative-Energy-Affine Companions

**Status:** Design accepted, awaiting implementation plan
**Target version:** 1.3.0
**Owner:** Wrath Tactics

## Problem

The `Heal` action only recognises positive-energy healing (`cure`, `heal`, `lay on hands`, `channel positive`). Players running undead or negative-energy-affine party members cannot heal them through the mod:

- **Lich Mythic Path** (MC undergoes Type-switch to `Undead` after Lich Ascension)
- **Dhampir** race (carries `NegativeEnergyAffinity` race-feature; positive heals damage instead)
- **Vampire / Undead companion paths** (mythic-driven Type-switches on companions)

Reported user pain points:
- "There's no way to configure Lich/Dhampir MCs separately."
- "Heal rules don't work for anything with negative energy affinity."
- "Need a way for healing options to differentiate between living and dead."

## Goals

1. Heal action picks the correct energy type (positive vs negative) based on target affinity
2. Bestehende Heal-Rules funktionieren ohne User-Anpassung weiter (Auto = Default)
3. Power-User können Heal-Rules explizit auf Positive oder Negative pinnen für Spezialsituationen
4. Backward-Compat: alte Save-Configs deserialisieren ohne Migrations-Code

## Non-Goals

- AoE-Mixed-Affinity-Resolution (Channel Positive in einer Mixed-Party): Heal-Action bleibt single-target-fokussiert; AoE-Channel ist via `CastSpell` mit explizitem Spell-Pick zu konfigurieren
- Per-Companion-Affinity-Override im Companion-Header: redundant zur Engine-Detection, abgelehnt im Brainstorming
- Component-basierte Heal-Detection (`ContextActionHealTarget` walken statt Keyword-Match): Korrekter, aber out-of-scope; Keyword-Match mit DE-Disambig reicht für die Ziel-Use-Cases
- Neue Default-Presets für Untoten-Setups: `Auto`-Mechanik macht `EmergencySelfHeal` automatisch Lich-/Dhampir-tauglich; weitere Defaults erst bei User-Feedback-Bedarf

## Design

### 1. Detection-Logik

Ein Target gilt als negative-energy-affin, wenn **eine** der beiden Engine-Quellen matched:

```csharp
static bool IsNegativeEnergyAffine(UnitEntityData unit) {
    var d = unit?.Descriptor;
    if (d == null) return false;

    // Source 1: CreatureType (Lich-MC post-Ascension, Vampire companions, undead summons)
    if (d.Blueprint?.Type?.name == "UndeadType") return true;

    // Source 2: NegativeEnergyAffinity feature (Dhampir race-feature; pre-Ascension Lich buffs)
    if (NegativeEnergyAffinityFeature != null && d.HasFact(NegativeEnergyAffinityFeature))
        return true;

    return false;
}
```

**Verifikations-Schritt beim Implementieren:** Die exakte Blueprint-GUID für `NegativeEnergyAffinity` ist via `ilspycmd` und Blueprint-Suche zu bestimmen. Wenn die GUID nicht ermittelt werden kann, fällt nur die Dhampir-Detection weg (CreatureType deckt Vollform-Untote weiterhin ab). Lazy-Resolution via `BlueprintTool.Get<BlueprintFeature>(guid)` einmalig im Static-Init; null-tolerant im Check.

**Side-Channel:** Wenn weder Type noch Feature matchen → Default `false` → Engine wählt Cure (sicheres Default-Living-Verhalten).

### 2. Heal-Source-Klassifikation

`IsHealingSpell(blueprint)` wird durch `ClassifyHeal(blueprint) → HealEnergyType` ersetzt:

```csharp
enum HealEnergyType { None, Positive, Negative }

static HealEnergyType ClassifyHeal(BlueprintAbility bp) {
    if (bp == null) return HealEnergyType.None;
    string n = (bp.name ?? "").ToLowerInvariant();   // Internal English (stable)
    string d = (bp.Name ?? "").ToLowerInvariant();   // Localised display name

    if (MatchesNegativeKeyword(n) || MatchesNegativeKeyword(d)) return HealEnergyType.Negative;
    if (MatchesPositiveKeyword(n) || MatchesPositiveKeyword(d)) return HealEnergyType.Positive;
    return HealEnergyType.None;
}
```

**Keyword-Tabellen:**

| Type | Englisch (matcht `bp.name`) | Deutsch (matcht `bp.Name`) |
|---|---|---|
| **Positive** | `cure`, `heal`, `lay on hands`, `channel positive` | `wunden heilen`, `heilung`, `auflegen` |
| **Negative** | `inflict`, `harm`, `channel negative` | `wunden zufügen`, `negative energie` |

**DE-Disambig:** Der bisherige `wunden`-Match (1.2.0) ist falsch-positiv für Inflict-Spells im DE-Client (`Leichte Wunden zufügen`). Pre-Feature blieb der Bug stumm, weil ein Cleric ohne Inflict-Spells keine Inflict-Quellen hatte; nach diesem Feature wird aktiv nach Inflict gesucht, also wird `wunden` durch `wunden heilen` (Positive) bzw. `wunden zufügen` (Negative) ersetzt — keine Substring-Kollision mehr.

**Bekannte Imprecision (akzeptiert):**
- `cure` matcht weiterhin `Cure Disease` / `Cure Deafness` / `Neutralize Poison`. UMD-Gate für Scrolls limitiert Mis-Casts. Component-basierte Detection wäre korrekter, ist aber out-of-scope.
- `harm` matcht potentiell substrings — keine bekannten Konflikte in Vanilla-Blueprints; beim Implementieren via Blueprint-Scan verifizieren.

### 3. Engine-Filterlogik

`FindBestHealEx` wird um Target und Pin erweitert:

```csharp
public static AbilityData FindBestHealEx(
    UnitEntityData owner,
    UnitEntityData target,             // NEW
    HealMode mode,
    HealSourceMask sources,
    HealEnergyType pin,                // NEW
    out ItemEntity inventorySource);
```

Pro Heal-Kandidat:

```
type = ClassifyHeal(candidate.Blueprint)
if (type == None) continue   // not a heal at all

if (pin == Auto) {
    targetIsUndead = IsNegativeEnergyAffine(target)
    matches = (type == Negative && targetIsUndead) || (type == Positive && !targetIsUndead)
} else if (pin == Positive) {
    matches = (type == Positive)
} else { // pin == Negative
    matches = (type == Negative)
}

if (!matches) continue
heals.Add(candidate, ...)
```

**Pin = harter Override.** Ein gepinntes `Positive` auf einen Untoten-Target castet Cure und schadet — das ist gewollt (User-Choice, keine Bevormundung). Wenn keine passende Quelle gefunden wird, returned `FindBestHealEx == null` → `CanExecute == false` → Rule fired nicht → Fall-through zur nächsten Rule.

**Auto-Mode-AoE-Verhalten:** Auto betrachtet nur das **explizite Heal-Target** (resolved via `TargetResolver`, typischerweise `AllyLowestHp`). Channel Positive/Negative wird wie Single-Target-Heal behandelt; AoE-Splash auf andere Allies mit gegensätzlicher Affinität ist nicht Sache der Heal-Action. User mit AoE-Channel-Bedarf nutzt `CastSpell` mit explizitem Spell-Pick.

### 4. Datenmodell-Erweiterung

```csharp
// Models/Enums.cs
public enum HealEnergyType {
    Auto,       // Detect via target's NegativeEnergyAffinity / CreatureType
    Positive,   // Force Cure/Heal/Channel-Positive only
    Negative    // Force Inflict/Harm/Channel-Negative only
}
```

```csharp
// Models/TacticsRule.cs (ActionDef)
public class ActionDef {
    public ActionType Type;
    public HealMode HealMode;
    public HealSourceMask HealSources;
    public HealEnergyType HealEnergy = HealEnergyType.Auto;   // NEW
    // ...existing fields...
}
```

**Index-Stability:** `Auto = 0` ist Default, sodass fehlende JSON-Felder als Auto deserialisieren. Zukünftige Werte (z.B. `Either`) werden ans Ende angehängt, nie in der Mitte eingefügt — Newtonsoft serialisiert numerische Indices, Reordering bricht alte Saves.

### 5. UI — RuleEditorWidget

Neuer Dropdown an der Heal-Action, **zwischen** HealMode und HealSources:

```
[ Heal ▼ ] Mode: [ Any ▼ ]  Energy: [ Auto-Detect ▼ ]  Sources: [ All ▼ ]
```

Begründung der Position: HealEnergy ist konzeptuell näher an HealMode ("wie wird geheilt"); HealSources bleibt orthogonal ("woher kommt die Heilung").

**Dropdown-Labels** (English-only per CLAUDE.md):
- `Auto-Detect`
- `Positive (Living)`
- `Negative (Undead)`

Helper-Map analog zu existierenden `*Label`-Patterns:

```csharp
static string HealEnergyLabel(HealEnergyType t) {
    switch (t) {
        case HealEnergyType.Auto:     return "Auto-Detect";
        case HealEnergyType.Positive: return "Positive (Living)";
        case HealEnergyType.Negative: return "Negative (Undead)";
        default: return t.ToString();
    }
}
```

**Persistierungs-Routing:** Die Widget-Änderung muss durch den `PersistEdit`-Callback der `RuleEditorWidget` laufen (CLAUDE.md gotcha — direktes `ConfigManager.Save()` schreibt im Preset-Edit-Mode an die falsche Datei). `onChanged?.Invoke()` aufrufen, **nicht** `ConfigManager.Save()` direkt.

### 6. Backward-Compat & Migration

**Keine Code-Migration nötig.**

- Alte `tactics-{GameId}.json`-Configs ohne `HealEnergy`-Field → Newtonsoft setzt `HealEnergy = 0 = Auto`
- Erste `ConfigManager.Save()` nach dem Update schreibt das Field; Roundtrip-stable
- Keine Default-Preset-Änderungen — `EmergencySelfHeal`, `PartyChannelHeal`, `ChannelVsUndead` bleiben unverändert; `EmergencySelfHeal` profitiert automatisch von `Auto`-Detection

**Keine neuen Defaults in dieser Iteration.** Bei User-Feedback kann später eine `LichEmergencySelfHeal`-Convenience-Vorlage über `.seeded-defaults` ergänzt werden — die Idempotenz-Mechanik macht Hinzufügen ohne Side-Effects auf bestehende Installationen möglich.

## Acceptance Criteria

1. Eine `EmergencySelfHeal`-Default-Rule auf einem Lich-MC (post-Ascension, Type=Undead) wählt automatisch eine Inflict-Quelle (Spell, Wand oder Inventory) und castet erfolgreich
2. Dieselbe Rule auf einem lebenden Companion wählt weiterhin Cure/Heal — keine Regression
3. Eine Heal-Rule mit `HealEnergy = Negative` ignoriert die Target-Affinität; `Positive`-Pin ebenfalls
4. Wenn Auto-Mode keine passende Quelle findet, returned `FindBestHealEx == null` und die Rule fired nicht (Fall-through)
5. Pre-existierende User-Configs ohne `HealEnergy`-Field laden korrekt mit `Auto` als Default; Roundtrip-Save schreibt das Field ohne Datenverlust
6. DE-Client mit Inflict-Spells: keine falsch-positiven Cure-Matches mehr (`wunden zufügen` matcht Negative, nicht Positive)

## Verification Plan

- **Smoke-Test auf Steam Deck:**
  - Lich-MC mit Inflict-Self-Cast-Quelle, EmergencySelfHeal-Default → erwarte Inflict-Cast
  - Standard-Cleric mit Cure-Spells, EmergencySelfHeal → erwarte Cure-Cast (keine Regression)
  - Heal-Rule mit `HealEnergy = Negative` auf lebenden Cleric → erwarte Inflict-Cast (Schaden, gewolltes Override-Verhalten)
- **Build-Verifikation:** Keine neuen Compiler-Warnings; csproj-Build läuft mit `-p:SolutionDir`-Flag durch
- **Config-Roundtrip:** Pre-update-`tactics-*.json` laden, Heal-Rule unverändert lassen, save → diff zeigt nur das neu hinzugefügte `"HealEnergy": 0`-Field

## Open Questions for Implementation

- Exakte Blueprint-GUID für `NegativeEnergyAffinity`-Race-Feature — via `ilspycmd` auf Dhampir-Race-Blueprint zu ermitteln
- Verifikation: Lich Mythic Path post-Ascension wechselt der MC tatsächlich auf `Type=Undead`? Falls nein, das Lich-Mythic-Buff-Feature als zusätzlichen Fact-Check ergänzen
- Vanilla-Blueprint-Scan auf Substring-Kollisionen für `harm` (z.B. `harmless`-Spells) — bei Konflikten Match auf Wort-Grenzen einschränken
