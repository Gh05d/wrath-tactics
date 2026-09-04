# Rule Packs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let players bundle several presets into a named, colour-coded pack and apply any number of packs to any companion or pet, with every applied rule staying individually editable.

**Architecture:** A pack is a *link container* — `{Id, Name, ColorIndex, PresetIds[]}` persisted as one JSON file per pack under `{ModPath}/Packs/`. Applying a pack appends one `PresetId`-linked `TacticsRule` per member to the target rule list, stamped with `PackId` so the origin survives in the config. Nothing about evaluation changes: the engine keeps resolving rules through `PresetRegistry.Resolve`, so packs are a pure authoring/organisation layer. Because provenance lives on the rule (not on the character), a single rule list can hold rules from many packs plus hand-built rules side by side.

**Tech Stack:** C# 7.3+/.NET Framework 4.8.1, Newtonsoft.Json (game-bundled, older API), Unity uGUI + TextMeshPro, xUnit (net481, mono runner on Linux), UMM + HarmonyLib.

## Global Constraints

- Build: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/` — the `-p:SolutionDir=` suffix slash is required on Linux or all game DLL references break.
- Tests: `~/.dotnet/dotnet test WrathTactics.Tests/WrathTactics.Tests.csproj -p:SolutionDir=$(pwd)/`. The mono runner is flaky: mass-failures with run-to-run varying counts are a flake, not a regression. Loop up to 3× and trust the first all-green run; only trust failures that reproduce.
- Deploy for manual verification: `./deploy.sh` (Debug build → Steam Deck via `deck-direct`). Locale JSONs are EmbeddedResources — string changes need a rebuild + redeploy, editing them on the deck does nothing.
- **Enums and persisted numeric fields are APPEND-ONLY.** This feature adds no enum members; `TacticsPack.ColorIndex` is a plain `int` index into a palette that may only ever grow at the end.
- **Never call `ConfigManager.Save()` from inside `RuleEditorWidget`** — route through `onChanged?.Invoke()` / `PersistEdit()`. Direct saves write character rules and silently discard preset edits.
- **`PresetId`-only rules carry empty `ConditionGroups`/`Action`/`Target` by design.** Any pass over rules must exempt `!string.IsNullOrEmpty(r.PresetId)`. Pack-applied rules are exactly this shape.
- New user-facing strings need a key in **all five** locale files (`en_GB`, `de_DE`, `fr_FR`, `ru_RU`, `zh_CN`); en_GB is mandatory, the rest fall back to English if missing. zh_CN uses ASCII `: , ( )` plus fullwidth `。` — match the file's existing convention.
- Code style: K&R braces, 4-space indent, `var` when the type is apparent.
- **Do not bump the version manually.** `/release` reads the pre-bump version from `WrathTactics.csproj` and bumps itself; a manual `chore: bump version` commit makes it bump twice.
- `catch (Exception ex)` is allowed only for per-frame guards, user-surface persistence, and static blueprint init. Everything else narrows to the concrete exception type.

## Design Decisions (locked)

| Decision | Choice | Why |
|---|---|---|
| Pack contents | Preset **IDs**, not rule copies | Editing a member preset propagates to every character that has the pack applied — same contract as `+ From Preset`. |
| Storage | `{ModPath}/Packs/<packId>.json`, one file per pack | `PresetManager.LoadAll` globs `Presets/*.json` top-level; a separate directory makes it structurally impossible for a pack file to be parsed as a preset. |
| Applying | Appends linked rules, stamped `PackId` | Multiple packs per companion fall out for free; each rule keeps its own ON/OFF, position, and unlink-and-edit. |
| Re-applying | Sync, not duplicate | Adds only members not already present (same `PackId` + `PresetId`), so a user who deleted one rule can restore it without cleaning up duplicates. |
| Colour | `int ColorIndex` into a fixed 6-entry palette | No colour-picker widget to build; the palette lives in the UI layer so the model stays Unity-free and unit-testable. |
| Deleting a pack | Removes the pack file only; already-applied rules stay | Deleting an organisational label must not silently delete a character's combat setup. Dangling `PackId` degrades to "no colour", mirroring dangling `PresetId`. |
| Deleting a preset | Cascades into packs (member stripped) and into rules (existing behaviour) | Prevents a pack from carrying a member that can never resolve. |

## File Structure

**Create:**
- `WrathTactics/Models/TacticsPack.cs` — the pack model. Unity-free, JSON-serialisable.
- `WrathTactics/Persistence/PackManager.cs` — disk layer (one file per pack, write-then-rename). Testable core takes an explicit directory.
- `WrathTactics/Engine/PackRegistry.cs` — in-memory cache + CRUD forwarding + the pure apply/sync/strip logic.
- `WrathTactics/UI/PackPalette.cs` — the six pack colours and index-safe lookup.
- `WrathTactics/UI/PackPanel.cs` — the Packs section rendered on the Presets tab (CRUD, member editor, colour cycling, export).
- `WrathTactics.Tests/PackManagerTests.cs`, `WrathTactics.Tests/PackApplyTests.cs` — unit tests for the two testable cores.

**Modify:**
- `WrathTactics/Models/TacticsRule.cs` — add `PackId`.
- `WrathTactics/Engine/PresetRegistry.cs:110-123` — cascade preset deletion into packs.
- `WrathTactics/UI/RuleEditorWidget.Header.cs:9-17` — tint the header with the pack colour.
- `WrathTactics/UI/TacticsPanel.cs:451-509` — render the applied-packs row above the rule cards.
- `WrathTactics/UI/PresetPanel.cs:34-144` — host the Packs section and route pack import.
- `WrathTactics/Main.cs:35` — load packs at mod start.
- `WrathTactics/Localization/{en_GB,de_DE,fr_FR,ru_RU,zh_CN}.json` — new keys.
- `CLAUDE.md`, `claude-context/gotchas-persistence.md`, `README.md` — docs.

---

### Task 1: Pack model + disk layer

**Files:**
- Create: `WrathTactics/Models/TacticsPack.cs`
- Create: `WrathTactics/Persistence/PackManager.cs`
- Test: `WrathTactics.Tests/PackManagerTests.cs`

**Interfaces:**
- Consumes: nothing (first task).
- Produces:
  - `WrathTactics.Models.TacticsPack` with `string Id`, `string Name`, `int ColorIndex`, `List<string> PresetIds`.
  - `WrathTactics.Persistence.PackManager.LoadAll() → List<TacticsPack>`, `.Save(TacticsPack) → bool`, `.Delete(string packId) → bool`.
  - Internal testable core: `PackManager.LoadAllFrom(string dir)`, `.SaveTo(string dir, TacticsPack)`, `.DeleteFrom(string dir, string packId)`.

- [ ] **Step 1: Write the model**

Create `WrathTactics/Models/TacticsPack.cs`:

```csharp
using Newtonsoft.Json;
using System.Collections.Generic;

namespace WrathTactics.Models {
    /// <summary>
    /// A named, colour-coded bundle of preset IDs. Applying a pack appends one
    /// PresetId-linked rule per member (see PackRegistry.PlanApply), so packs hold links,
    /// not rule bodies — editing a member preset propagates to every character the pack
    /// was applied to. Persisted one file per pack under {ModPath}/Packs/.
    /// </summary>
    public class TacticsPack {
        [JsonProperty] public string Id { get; set; } = System.Guid.NewGuid().ToString();
        [JsonProperty] public string Name { get; set; } = "New Pack";
        /// <summary>Index into UI.PackPalette.Colors. Out-of-range values resolve to index 0.</summary>
        [JsonProperty] public int ColorIndex { get; set; }
        /// <summary>
        /// Preset IDs in application order. May contain IDs whose preset no longer exists
        /// (a preset deleted while the mod was not running) — every consumer resolves defensively.
        /// </summary>
        [JsonProperty] public List<string> PresetIds { get; set; } = new List<string>();
    }
}
```

- [ ] **Step 2: Write the failing tests**

Create `WrathTactics.Tests/PackManagerTests.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using WrathTactics.Models;
using WrathTactics.Persistence;
using Xunit;

namespace WrathTactics.Tests {
    public class PackManagerTests {
        static string TempDir() {
            var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            return dir;
        }

        static TacticsPack Sample(string name, params string[] presetIds) => new TacticsPack {
            Name = name,
            ColorIndex = 2,
            PresetIds = new List<string>(presetIds),
        };

        [Fact]
        public void Save_then_load_roundtrips_all_fields() {
            var dir = TempDir();
            try {
                var pack = Sample("Seelah Melee", "p1", "p2");
                Assert.True(PackManager.SaveTo(dir, pack));

                var loaded = PackManager.LoadAllFrom(dir);
                Assert.Single(loaded);
                Assert.Equal(pack.Id, loaded[0].Id);
                Assert.Equal("Seelah Melee", loaded[0].Name);
                Assert.Equal(2, loaded[0].ColorIndex);
                Assert.Equal(new[] { "p1", "p2" }, loaded[0].PresetIds);
            } finally {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void Load_sorts_by_name_case_insensitively() {
            var dir = TempDir();
            try {
                PackManager.SaveTo(dir, Sample("zebra"));
                PackManager.SaveTo(dir, Sample("Alpha"));
                var loaded = PackManager.LoadAllFrom(dir);
                Assert.Equal(new[] { "Alpha", "zebra" }, loaded.ConvertAll(p => p.Name));
            } finally {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void Load_skips_unparsable_file_and_keeps_the_rest() {
            var dir = TempDir();
            try {
                PackManager.SaveTo(dir, Sample("Good"));
                File.WriteAllText(Path.Combine(dir, "broken.json"), "{ this is not json");
                var loaded = PackManager.LoadAllFrom(dir);
                Assert.Single(loaded);
                Assert.Equal("Good", loaded[0].Name);
            } finally {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void Load_repairs_missing_id_and_null_member_list() {
            var dir = TempDir();
            try {
                File.WriteAllText(Path.Combine(dir, "legacy.json"), "{\"Name\":\"Legacy\"}");
                var loaded = PackManager.LoadAllFrom(dir);
                Assert.Single(loaded);
                Assert.False(string.IsNullOrEmpty(loaded[0].Id));
                Assert.NotNull(loaded[0].PresetIds);
                Assert.Empty(loaded[0].PresetIds);
            } finally {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void Delete_removes_the_file_and_is_idempotent() {
            var dir = TempDir();
            try {
                var pack = Sample("Doomed");
                PackManager.SaveTo(dir, pack);
                Assert.True(PackManager.DeleteFrom(dir, pack.Id));
                Assert.Empty(PackManager.LoadAllFrom(dir));
                Assert.True(PackManager.DeleteFrom(dir, pack.Id));
            } finally {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void Save_rejects_null_pack_and_empty_id() {
            var dir = TempDir();
            try {
                Assert.False(PackManager.SaveTo(dir, null));
                Assert.False(PackManager.SaveTo(dir, new TacticsPack { Id = "" }));
                Assert.Empty(PackManager.LoadAllFrom(dir));
            } finally {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void Load_from_missing_directory_returns_empty() {
            var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Assert.Empty(PackManager.LoadAllFrom(dir));
        }
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `~/.dotnet/dotnet test WrathTactics.Tests/WrathTactics.Tests.csproj -p:SolutionDir=$(pwd)/`
Expected: compile error — `PackManager` does not exist.

- [ ] **Step 4: Write the disk layer**

Create `WrathTactics/Persistence/PackManager.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using WrathTactics.Logging;
using WrathTactics.Models;

namespace WrathTactics.Persistence {
    /// <summary>
    /// Disk layer for rule packs — one JSON file per pack, write-then-rename like
    /// PresetManager. Packs live in their own directory ({ModPath}/Packs) so a pack file
    /// can never be picked up by PresetManager.LoadAll, which globs Presets/*.json.
    /// The *From variants take an explicit directory and carry no Game API dependency,
    /// which is what the unit tests drive.
    /// </summary>
    public static class PackManager {
        internal static string PackDir => Path.Combine(Main.ModPath ?? ".", "Packs");

        public static List<TacticsPack> LoadAll() => LoadAllFrom(PackDir);
        public static bool Save(TacticsPack pack) => SaveTo(PackDir, pack);
        public static bool Delete(string packId) => DeleteFrom(PackDir, packId);

        // --- Testable core (no Game API; pure file + JSON) ---

        internal static List<TacticsPack> LoadAllFrom(string dir) {
            var result = new List<TacticsPack>();
            if (!Directory.Exists(dir)) return result;

            foreach (var path in Directory.GetFiles(dir, "*.json")) {
                try {
                    var pack = JsonConvert.DeserializeObject<TacticsPack>(File.ReadAllText(path));
                    if (pack == null) continue;
                    if (string.IsNullOrEmpty(pack.Id)) pack.Id = Guid.NewGuid().ToString();
                    if (pack.PresetIds == null) pack.PresetIds = new List<string>();
                    result.Add(pack);
                } catch (JsonException ex) {
                    Log.Persistence.Warn($"Skipping unreadable pack file {path}: {ex.Message}");
                } catch (IOException ex) {
                    Log.Persistence.Warn($"Skipping unreadable pack file {path}: {ex.Message}");
                }
            }
            return result.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Write-then-rename so a crash mid-write cannot leave a partial file.
        /// Returns false on any failure (already logged) — UI callers must surface it,
        /// otherwise the user sees a phantom-saved pack that vanishes on reload.
        /// </summary>
        internal static bool SaveTo(string dir, TacticsPack pack) {
            if (pack == null || string.IsNullOrEmpty(pack.Id)) {
                Log.Persistence.Warn("PackManager.Save called with null pack or empty Id — ignored");
                return false;
            }
            try {
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, $"{pack.Id}.json");
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(pack, Formatting.Indented));
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                Log.Persistence.Info($"Saved pack '{pack.Name}' (id={pack.Id}, {pack.PresetIds.Count} member(s))");
                return true;
            } catch (Exception ex) {
                Log.Persistence.Error(ex, $"Failed to save pack '{pack.Name}'");
                return false;
            }
        }

        /// <summary>Returns true if the file was removed or never existed.</summary>
        internal static bool DeleteFrom(string dir, string packId) {
            if (string.IsNullOrEmpty(packId)) return false;
            try {
                var path = Path.Combine(dir, $"{packId}.json");
                if (File.Exists(path)) {
                    File.Delete(path);
                    Log.Persistence.Info($"Deleted pack id={packId}");
                }
                return true;
            } catch (Exception ex) {
                Log.Persistence.Error(ex, $"Failed to delete pack id={packId}");
                return false;
            }
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `~/.dotnet/dotnet test WrathTactics.Tests/WrathTactics.Tests.csproj -p:SolutionDir=$(pwd)/`
Expected: PASS (7 new tests). If the whole suite mass-fails with a mono crash dump, re-run — that is the known flake. If `Load_skips_unparsable_file_and_keeps_the_rest` fails with a null-reference from inside `Log.Persistence`, the logger is unusable without `DebugLog.Init`; in that case check `WrathTactics/Logging/DebugLog.cs:WriteLine` and add a `writer == null` guard there rather than removing the log call.

- [ ] **Step 6: Commit**

```bash
git add WrathTactics/Models/TacticsPack.cs WrathTactics/Persistence/PackManager.cs WrathTactics.Tests/PackManagerTests.cs
git commit -m "feat(packs): pack model and disk layer"
```

---

### Task 2: PackRegistry — cache, apply/sync planning, cascade

**Files:**
- Create: `WrathTactics/Engine/PackRegistry.cs`
- Modify: `WrathTactics/Models/TacticsRule.cs` (add `PackId`)
- Modify: `WrathTactics/Engine/PresetRegistry.cs:110-123` (cascade into packs)
- Modify: `WrathTactics/Main.cs:35` (load packs at startup)
- Test: `WrathTactics.Tests/PackApplyTests.cs`

**Interfaces:**
- Consumes: `TacticsPack`, `PackManager.{LoadAll,Save,Delete}` (Task 1).
- Produces:
  - `TacticsRule.PackId` (string, null on non-pack rules).
  - `PackRegistry.Reload()`, `.All() → IReadOnlyList<TacticsPack>`, `.Get(string) → TacticsPack`, `.Save(TacticsPack) → bool`, `.Delete(string) → bool`.
  - `PackRegistry.PlanApply(TacticsPack pack, List<TacticsRule> existing, Func<string,bool> presetExists) → List<TacticsRule>` — the rules to append (never mutates `existing`).
  - `PackRegistry.StripPreset(List<TacticsPack> packs, string presetId) → List<TacticsPack>` — packs that changed.
  - `PackRegistry.AppliedPackIds(List<TacticsRule> rules) → List<string>` — distinct, in first-appearance order.

- [ ] **Step 1: Add `PackId` to the rule model**

In `WrathTactics/Models/TacticsRule.cs`, after the `PresetId` property (line 15), add:

```csharp
        /// <summary>
        /// Origin marker set when the rule was inserted by applying a pack. Purely
        /// organisational: it drives the header tint and the per-pack chip actions.
        /// A rule list may mix rules from several packs and hand-built rules (PackId null).
        /// A dangling PackId (pack deleted) is harmless — the rule keeps working, untinted.
        /// </summary>
        [JsonProperty] public string PackId { get; set; }
```

- [ ] **Step 2: Write the failing tests**

Create `WrathTactics.Tests/PackApplyTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using WrathTactics.Engine;
using WrathTactics.Models;
using Xunit;

namespace WrathTactics.Tests {
    public class PackApplyTests {
        static TacticsPack Pack(string id, params string[] presetIds) => new TacticsPack {
            Id = id, Name = "Pack " + id, PresetIds = new List<string>(presetIds),
        };

        static TacticsRule Linked(string presetId, string packId) => new TacticsRule {
            PresetId = presetId, PackId = packId,
        };

        // Every preset exists unless a test says otherwise.
        static bool AllExist(string presetId) => true;

        [Fact]
        public void PlanApply_on_empty_list_returns_one_linked_rule_per_member() {
            var plan = PackRegistry.PlanApply(Pack("A", "p1", "p2"), new List<TacticsRule>(), AllExist);

            Assert.Equal(2, plan.Count);
            Assert.Equal(new[] { "p1", "p2" }, plan.Select(r => r.PresetId));
            Assert.All(plan, r => Assert.Equal("A", r.PackId));
            Assert.All(plan, r => Assert.True(r.Enabled));
            Assert.All(plan, r => Assert.False(string.IsNullOrEmpty(r.Id)));
            // Linked rules must carry an empty body — PresetRegistry.Resolve supplies it.
            Assert.All(plan, r => Assert.Empty(r.ConditionGroups));
        }

        [Fact]
        public void PlanApply_preserves_member_order() {
            var plan = PackRegistry.PlanApply(Pack("A", "p3", "p1", "p2"), new List<TacticsRule>(), AllExist);
            Assert.Equal(new[] { "p3", "p1", "p2" }, plan.Select(r => r.PresetId));
        }

        [Fact]
        public void PlanApply_skips_members_already_present_from_the_same_pack() {
            var existing = new List<TacticsRule> { Linked("p1", "A") };
            var plan = PackRegistry.PlanApply(Pack("A", "p1", "p2"), existing, AllExist);

            Assert.Single(plan);
            Assert.Equal("p2", plan[0].PresetId);
        }

        [Fact]
        public void PlanApply_still_adds_a_member_present_only_from_another_pack() {
            // Two packs sharing a preset must each own their own copy — removing pack B
            // must not strip a rule that pack A also asked for.
            var existing = new List<TacticsRule> { Linked("p1", "B") };
            var plan = PackRegistry.PlanApply(Pack("A", "p1"), existing, AllExist);

            Assert.Single(plan);
            Assert.Equal("A", plan[0].PackId);
        }

        [Fact]
        public void PlanApply_still_adds_a_member_present_as_a_hand_built_link() {
            var existing = new List<TacticsRule> { Linked("p1", null) };
            var plan = PackRegistry.PlanApply(Pack("A", "p1"), existing, AllExist);
            Assert.Single(plan);
        }

        [Fact]
        public void PlanApply_drops_members_whose_preset_is_gone() {
            var plan = PackRegistry.PlanApply(Pack("A", "p1", "ghost"), new List<TacticsRule>(),
                presetId => presetId != "ghost");

            Assert.Single(plan);
            Assert.Equal("p1", plan[0].PresetId);
        }

        [Fact]
        public void PlanApply_tolerates_null_and_empty_input() {
            Assert.Empty(PackRegistry.PlanApply(null, new List<TacticsRule>(), AllExist));
            Assert.Empty(PackRegistry.PlanApply(Pack("A"), null, AllExist));
            Assert.Empty(PackRegistry.PlanApply(Pack("A", "", null), null, AllExist));
        }

        [Fact]
        public void AppliedPackIds_returns_distinct_ids_in_first_appearance_order() {
            var rules = new List<TacticsRule> {
                Linked("p1", "B"), new TacticsRule(), Linked("p2", "A"), Linked("p3", "B"),
            };
            Assert.Equal(new[] { "B", "A" }, PackRegistry.AppliedPackIds(rules));
        }

        [Fact]
        public void AppliedPackIds_ignores_null_list_and_unpacked_rules() {
            Assert.Empty(PackRegistry.AppliedPackIds(null));
            Assert.Empty(PackRegistry.AppliedPackIds(new List<TacticsRule> { new TacticsRule() }));
        }

        [Fact]
        public void StripPreset_removes_the_member_and_reports_only_changed_packs() {
            var packs = new List<TacticsPack> { Pack("A", "p1", "p2"), Pack("B", "p3") };
            var changed = PackRegistry.StripPreset(packs, "p1");

            Assert.Single(changed);
            Assert.Equal("A", changed[0].Id);
            Assert.Equal(new[] { "p2" }, packs[0].PresetIds);
            Assert.Equal(new[] { "p3" }, packs[1].PresetIds);
        }

        [Fact]
        public void StripPreset_handles_duplicates_null_list_and_unknown_id() {
            var packs = new List<TacticsPack> { Pack("A", "p1", "p1") };
            Assert.Single(PackRegistry.StripPreset(packs, "p1"));
            Assert.Empty(packs[0].PresetIds);

            Assert.Empty(PackRegistry.StripPreset(packs, "nope"));
            Assert.Empty(PackRegistry.StripPreset(null, "p1"));
        }
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `~/.dotnet/dotnet test WrathTactics.Tests/WrathTactics.Tests.csproj -p:SolutionDir=$(pwd)/`
Expected: compile error — `PackRegistry` does not exist.

- [ ] **Step 4: Write the registry**

Create `WrathTactics/Engine/PackRegistry.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using WrathTactics.Logging;
using WrathTactics.Models;
using WrathTactics.Persistence;

namespace WrathTactics.Engine {
    /// <summary>
    /// In-memory cache of rule packs keyed by pack id, plus the pure planning logic that
    /// turns a pack into rules. Mirrors PresetRegistry: the dict is updated even when the
    /// disk write fails so the UI reflects what the user just did — callers must surface
    /// a false return.
    /// </summary>
    public static class PackRegistry {
        static Dictionary<string, TacticsPack> packs;

        public static void Reload() {
            packs = PackManager.LoadAll().ToDictionary(p => p.Id, p => p);
            Log.Persistence.Info($"PackRegistry loaded {packs.Count} packs");
        }

        static Dictionary<string, TacticsPack> GetPacks() {
            if (packs == null) Reload();
            return packs;
        }

        public static IReadOnlyList<TacticsPack> All() {
            return GetPacks().Values
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static TacticsPack Get(string packId) {
            if (string.IsNullOrEmpty(packId)) return null;
            GetPacks().TryGetValue(packId, out var pack);
            return pack;
        }

        public static bool Save(TacticsPack pack) {
            if (pack == null || string.IsNullOrEmpty(pack.Id)) return false;
            bool ok = PackManager.Save(pack);
            GetPacks()[pack.Id] = pack;
            return ok;
        }

        /// <summary>
        /// Deletes the pack definition. Rules already applied from it are deliberately left
        /// in place — deleting an organisational label must not wipe a character's setup.
        /// Their PackId dangles, which only costs them the colour tint.
        /// </summary>
        public static bool Delete(string packId) {
            if (string.IsNullOrEmpty(packId)) return false;
            bool ok = PackManager.Delete(packId);
            GetPacks().Remove(packId);
            return ok;
        }

        /// <summary>
        /// Removes a deleted preset from every pack that lists it and persists the changes.
        /// Called from PresetRegistry.Delete so a pack can never carry an unresolvable member.
        /// </summary>
        public static void RemovePresetFromPacks(string presetId) {
            var changed = StripPreset(GetPacks().Values.ToList(), presetId);
            foreach (var pack in changed) PackManager.Save(pack);
            if (changed.Count > 0)
                Log.Persistence.Info($"Stripped preset id={presetId} from {changed.Count} pack(s)");
        }

        // --- Pure logic (no disk, no Game API) ---

        /// <summary>
        /// Returns the rules to append so <paramref name="pack"/> is fully represented in
        /// <paramref name="existing"/>. Members already present *from this pack* are skipped,
        /// which makes re-applying a sync rather than a duplicate. Members present from a
        /// different pack (or as a hand-made link) are still added: each pack owns its own
        /// rules so removing one pack never strips another's.
        /// Never mutates <paramref name="existing"/>.
        /// </summary>
        public static List<TacticsRule> PlanApply(TacticsPack pack, List<TacticsRule> existing,
            Func<string, bool> presetExists) {

            var plan = new List<TacticsRule>();
            if (pack?.PresetIds == null) return plan;

            var alreadyFromThisPack = new HashSet<string>(
                (existing ?? new List<TacticsRule>())
                    .Where(r => r != null && r.PackId == pack.Id && !string.IsNullOrEmpty(r.PresetId))
                    .Select(r => r.PresetId));

            foreach (var presetId in pack.PresetIds) {
                if (string.IsNullOrEmpty(presetId)) continue;
                if (alreadyFromThisPack.Contains(presetId)) continue;
                if (presetExists != null && !presetExists(presetId)) {
                    Log.Persistence.Warn($"Pack '{pack.Name}': member preset id={presetId} no longer exists — skipped");
                    continue;
                }
                // Body stays empty by design: PresetRegistry.Resolve supplies conditions,
                // action and target at evaluation time.
                plan.Add(new TacticsRule {
                    Id = Guid.NewGuid().ToString(),
                    Enabled = true,
                    PresetId = presetId,
                    PackId = pack.Id,
                });
                alreadyFromThisPack.Add(presetId);
            }
            return plan;
        }

        /// <summary>Distinct pack ids present in a rule list, in first-appearance order.</summary>
        public static List<string> AppliedPackIds(List<TacticsRule> rules) {
            var result = new List<string>();
            if (rules == null) return result;
            var seen = new HashSet<string>();
            foreach (var rule in rules) {
                if (rule == null || string.IsNullOrEmpty(rule.PackId)) continue;
                if (seen.Add(rule.PackId)) result.Add(rule.PackId);
            }
            return result;
        }

        /// <summary>Removes a preset id from all packs; returns the packs that changed.</summary>
        public static List<TacticsPack> StripPreset(List<TacticsPack> allPacks, string presetId) {
            var changed = new List<TacticsPack>();
            if (allPacks == null || string.IsNullOrEmpty(presetId)) return changed;
            foreach (var pack in allPacks) {
                if (pack?.PresetIds == null) continue;
                if (pack.PresetIds.RemoveAll(id => id == presetId) > 0) changed.Add(pack);
            }
            return changed;
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `~/.dotnet/dotnet test WrathTactics.Tests/WrathTactics.Tests.csproj -p:SolutionDir=$(pwd)/`
Expected: PASS (11 new tests). Re-run on a mono mass-failure.

- [ ] **Step 6: Cascade preset deletion into packs**

In `WrathTactics/Engine/PresetRegistry.cs`, inside `Delete` (line 110), after `GetPresets().Remove(presetId);` add:

```csharp
            // Packs hold preset ids; a deleted preset must not linger as an unresolvable member.
            PackRegistry.RemovePresetFromPacks(presetId);
```

- [ ] **Step 7: Load packs at mod start**

In `WrathTactics/Main.cs`, directly after line 35 (`Engine.PresetRegistry.Reload();`) add:

```csharp
            Engine.PackRegistry.Reload();
```

- [ ] **Step 8: Build and commit**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: Build succeeded (NU1900 warnings are expected and harmless).

```bash
git add WrathTactics/Engine/PackRegistry.cs WrathTactics/Engine/PresetRegistry.cs \
        WrathTactics/Models/TacticsRule.cs WrathTactics/Main.cs WrathTactics.Tests/PackApplyTests.cs
git commit -m "feat(packs): pack registry, apply planning and preset-delete cascade"
```

---

### Task 3: Pack palette + coloured rule headers

**Files:**
- Create: `WrathTactics/UI/PackPalette.cs`
- Modify: `WrathTactics/UI/RuleEditorWidget.Header.cs:9-17`

**Interfaces:**
- Consumes: `TacticsRule.PackId`, `PackRegistry.Get` (Task 2).
- Produces: `PackPalette.Count`, `PackPalette.ColorAt(int index) → Color`, `PackPalette.Next(int index) → int`, `PackPalette.HeaderTint(int index) → Color`.

- [ ] **Step 1: Write the palette**

Create `WrathTactics/UI/PackPalette.cs`:

```csharp
using UnityEngine;

namespace WrathTactics.UI {
    /// <summary>
    /// Fixed colour set for packs. TacticsPack.ColorIndex is a persisted index into this
    /// array, so entries may only ever be APPENDED — reordering repaints every existing
    /// pack. Colours are chosen to stay readable on the book-page background and to be
    /// distinguishable from the default rule header (brown) and the linked header (blue-grey).
    /// </summary>
    public static class PackPalette {
        static readonly Color[] Colors = {
            new Color(0.45f, 0.25f, 0.25f, 1f),  // 0 rust
            new Color(0.25f, 0.40f, 0.25f, 1f),  // 1 moss
            new Color(0.25f, 0.32f, 0.48f, 1f),  // 2 steel blue
            new Color(0.42f, 0.34f, 0.18f, 1f),  // 3 amber
            new Color(0.38f, 0.25f, 0.45f, 1f),  // 4 plum
            new Color(0.20f, 0.38f, 0.40f, 1f),  // 5 teal
        };

        public static int Count => Colors.Length;

        /// <summary>Index-safe lookup — out-of-range (corrupt/hand-edited JSON) falls back to 0.</summary>
        public static Color ColorAt(int index) {
            if (index < 0 || index >= Colors.Length) return Colors[0];
            return Colors[index];
        }

        public static int Next(int index) {
            if (index < 0 || index >= Colors.Length - 1) return 0;
            return index + 1;
        }

        /// <summary>Muted variant for rule-card headers, so the chip stays the louder element.</summary>
        public static Color HeaderTint(int index) {
            var c = ColorAt(index);
            return new Color(c.r * 0.75f, c.g * 0.75f, c.b * 0.75f, 1f);
        }
    }
}
```

- [ ] **Step 2: Tint the rule header**

In `WrathTactics/UI/RuleEditorWidget.Header.cs`, replace lines 14-17:

```csharp
            var headerBg = isLinked
                ? new Color(0.22f, 0.3f, 0.4f, 1f)   // blue-grey for linked
                : new Color(0.25f, 0.22f, 0.18f, 1f); // default brown
            UIHelpers.AddBackground(header, headerBg);
```

with:

```csharp
            // Pack origin wins over the generic linked tint so a character running several
            // packs can tell at a glance which rules belong together. A dangling PackId
            // (pack deleted) resolves to null and falls back to the normal colours.
            var pack = Engine.PackRegistry.Get(rule.PackId);
            var headerBg = pack != null
                ? PackPalette.HeaderTint(pack.ColorIndex)
                : isLinked
                    ? new Color(0.22f, 0.3f, 0.4f, 1f)   // blue-grey for linked
                    : new Color(0.25f, 0.22f, 0.18f, 1f); // default brown
            UIHelpers.AddBackground(header, headerBg);
```

- [ ] **Step 3: Build**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add WrathTactics/UI/PackPalette.cs WrathTactics/UI/RuleEditorWidget.Header.cs
git commit -m "feat(packs): pack colour palette and tinted rule headers"
```

---

### Task 4: Packs section on the Presets tab

**Files:**
- Create: `WrathTactics/UI/PackPanel.cs`
- Modify: `WrathTactics/UI/PresetPanel.cs:98-101` (mount point)
- Modify: `WrathTactics/Localization/en_GB.json` (keys used here; the other four locales land in Task 8)

**Interfaces:**
- Consumes: `PackRegistry.{All,Get,Save,Delete}`, `PresetRegistry.All`, `PackPalette` (Tasks 2-3).
- Produces: `PackPanel.Build(Transform parent, Action onChanged, Action<string, Color> setStatus)` — renders the whole Packs section into `parent`; `onChanged` triggers the host panel's rebuild, `setStatus` surfaces save failures on the host's status line.

- [ ] **Step 1: Add the en_GB strings**

In `WrathTactics/Localization/en_GB.json`, after the `"preset.button.open_folder"` line (line 75), add:

```json
  "pack.section_title": "Packs",
  "pack.hint": "A pack bundles presets under one name and colour. Apply as many packs as you like to a companion — every rule stays individually editable.",
  "pack.empty": "No packs yet.",
  "pack.button.new": "+ New Pack",
  "pack.button.members": "Members",
  "pack.button.close_members": "Close",
  "pack.button.export": "Export",
  "pack.default_name": "New Pack",
  "pack.member_count": "{0} preset(s)",
  "pack.member_add": "+",
  "pack.member_remove": "−",
  "pack.members_title": "Members (top to bottom = insertion order)",
  "pack.members_empty": "No presets in this pack yet — add one below.",
  "pack.available_title": "Available presets",
```

- [ ] **Step 2: Write the panel**

Create `WrathTactics/UI/PackPanel.cs`:

```csharp
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WrathTactics.Engine;
using WrathTactics.Localization;
using WrathTactics.Models;

namespace WrathTactics.UI {
    /// <summary>
    /// The Packs section rendered at the top of the Presets tab. Stateless apart from which
    /// pack is expanded — the host (PresetPanel) owns the rebuild, so every mutation ends in
    /// onChanged() rather than touching the hierarchy directly.
    /// </summary>
    public static class PackPanel {
        // Which pack's member editor is open. Static so it survives the host's Rebuild,
        // matching PresetPanel.expandedIds semantics.
        static string expandedPackId;

        public static void Build(Transform parent, Action onChanged, Action<string, Color> setStatus) {
            var (titleObj, _) = UIHelpers.Create("PackTitle", parent);
            titleObj.AddComponent<LayoutElement>().preferredHeight = 26;
            UIHelpers.AddLabel(titleObj, "pack.section_title".i18n(), 18f,
                TextAlignmentOptions.MidlineLeft, Color.white);

            UIHelpers.AddHintCard(parent, "pack.hint".i18n(), 40f);

            var (newBtn, _n) = UIHelpers.Create("NewPackBtn", parent);
            newBtn.AddComponent<LayoutElement>().preferredHeight = 34;
            UIHelpers.AddBackground(newBtn, new Color(0.2f, 0.4f, 0.45f, 1f));
            UIHelpers.AddLabel(newBtn, "pack.button.new".i18n(), 15f, TextAlignmentOptions.Midline);
            newBtn.AddComponent<Button>().onClick.AddListener(() => {
                var pack = new TacticsPack { Name = "pack.default_name".i18n() };
                if (!PackRegistry.Save(pack)) {
                    setStatus(string.Format("status.save_failed".i18n(), "pack.section_title".i18n()),
                        new Color(1f, 0.5f, 0.4f));
                    return;
                }
                expandedPackId = pack.Id;
                onChanged();
            });

            var packs = PackRegistry.All();
            if (packs.Count == 0) {
                var (empty, _e) = UIHelpers.Create("PackEmpty", parent);
                empty.AddComponent<LayoutElement>().preferredHeight = 26;
                UIHelpers.AddLabel(empty, "pack.empty".i18n(), 14f,
                    TextAlignmentOptions.MidlineLeft, Color.gray);
            }

            foreach (var pack in packs) CreatePackRow(parent, pack, onChanged, setStatus);

            var (sep, _s) = UIHelpers.Create("PackSep", parent);
            sep.AddComponent<LayoutElement>().preferredHeight = 12;
        }

        static void CreatePackRow(Transform parent, TacticsPack pack, Action onChanged,
            Action<string, Color> setStatus) {

            var (row, _) = UIHelpers.Create($"Pack_{pack.Id}", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 38;
            UIHelpers.AddBackground(row, new Color(0.16f, 0.16f, 0.16f, 1f));

            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.padding = new RectOffset(8, 8, 4, 4);
            hlg.childAlignment = TextAnchor.MiddleLeft;

            // Colour swatch — click cycles to the next palette entry.
            var (swatch, _sw) = UIHelpers.Create("Swatch", row.transform);
            var swatchLE = swatch.AddComponent<LayoutElement>();
            swatchLE.preferredWidth = 34;
            swatchLE.flexibleWidth = 0;
            UIHelpers.AddBackground(swatch, PackPalette.ColorAt(pack.ColorIndex));
            UIHelpers.AddLabel(swatch, "●", 16f, TextAlignmentOptions.Midline, Color.white);
            swatch.AddComponent<Button>().onClick.AddListener(() => {
                pack.ColorIndex = PackPalette.Next(pack.ColorIndex);
                if (!PackRegistry.Save(pack))
                    setStatus(string.Format("status.save_failed".i18n(), pack.Name), new Color(1f, 0.5f, 0.4f));
                onChanged();
            });

            // Name — inline rename on end-edit.
            var nameInput = UIHelpers.CreateTMPInputField(row, "PackName", 0, 1, pack.Name, 16f);
            var nameLE = nameInput.gameObject.AddComponent<LayoutElement>();
            nameLE.flexibleWidth = 1;
            nameLE.preferredWidth = 160;
            nameInput.onEndEdit.AddListener(v => {
                var trimmed = v?.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed == pack.Name) return;
                pack.Name = trimmed;
                if (!PackRegistry.Save(pack)) {
                    setStatus(string.Format("status.save_failed".i18n(), "status.context.rename".i18n()),
                        new Color(1f, 0.5f, 0.4f));
                    return;
                }
                // Deferred by the host: rebuilding here would destroy this TMP_InputField
                // while its own onEndEdit is still on the stack.
                onChanged();
            });

            var (countObj, _c) = UIHelpers.Create("PackCount", row.transform);
            var countLE = countObj.AddComponent<LayoutElement>();
            countLE.preferredWidth = 90;
            countLE.flexibleWidth = 0;
            UIHelpers.AddLabel(countObj,
                string.Format("pack.member_count".i18n(), pack.PresetIds.Count), 13f,
                TextAlignmentOptions.Midline, Color.gray);

            bool expanded = expandedPackId == pack.Id;
            var (membersBtn, _m) = UIHelpers.Create("MembersBtn", row.transform);
            var membersLE = membersBtn.AddComponent<LayoutElement>();
            membersLE.preferredWidth = 80;
            membersLE.flexibleWidth = 0;
            UIHelpers.AddBackground(membersBtn,
                expanded ? new Color(0.4f, 0.35f, 0.2f) : new Color(0.25f, 0.3f, 0.35f));
            UIHelpers.AddLabel(membersBtn,
                (expanded ? "pack.button.close_members" : "pack.button.members").i18n(), 14f,
                TextAlignmentOptions.Midline);
            membersBtn.AddComponent<Button>().onClick.AddListener(() => {
                expandedPackId = expanded ? null : pack.Id;
                onChanged();
            });

            var (delBtn, _d) = UIHelpers.Create("PackDelBtn", row.transform);
            var delLE = delBtn.AddComponent<LayoutElement>();
            delLE.preferredWidth = 70;
            delLE.flexibleWidth = 0;
            UIHelpers.AddBackground(delBtn, new Color(0.5f, 0.15f, 0.15f));
            UIHelpers.AddLabel(delBtn, "button.delete".i18n(), 14f, TextAlignmentOptions.Midline);
            delBtn.AddComponent<Button>().onClick.AddListener(() => {
                if (!PackRegistry.Delete(pack.Id))
                    setStatus(string.Format("status.save_failed".i18n(), "status.context.delete".i18n()),
                        new Color(1f, 0.5f, 0.4f));
                if (expandedPackId == pack.Id) expandedPackId = null;
                onChanged();
            });

            if (expanded) CreateMemberEditor(parent, pack, onChanged, setStatus);
        }

        static void CreateMemberEditor(Transform parent, TacticsPack pack, Action onChanged,
            Action<string, Color> setStatus) {

            var (title, _t) = UIHelpers.Create($"Members_{pack.Id}", parent);
            title.AddComponent<LayoutElement>().preferredHeight = 24;
            UIHelpers.AddLabel(title, "pack.members_title".i18n(), 13f,
                TextAlignmentOptions.MidlineLeft, new Color(0.8f, 0.8f, 0.8f));

            if (pack.PresetIds.Count == 0) {
                var (none, _no) = UIHelpers.Create($"MembersEmpty_{pack.Id}", parent);
                none.AddComponent<LayoutElement>().preferredHeight = 24;
                UIHelpers.AddLabel(none, "pack.members_empty".i18n(), 13f,
                    TextAlignmentOptions.MidlineLeft, Color.gray);
            }

            for (int i = 0; i < pack.PresetIds.Count; i++) {
                int idx = i;  // capture for the closures
                var preset = PresetRegistry.Get(pack.PresetIds[i]);
                var (memberRow, _mr) = UIHelpers.Create($"Member_{pack.Id}_{idx}", parent);
                memberRow.AddComponent<LayoutElement>().preferredHeight = 30;
                UIHelpers.AddBackground(memberRow, new Color(0.13f, 0.13f, 0.13f, 1f));

                var mhlg = memberRow.AddComponent<HorizontalLayoutGroup>();
                mhlg.spacing = 4;
                mhlg.childForceExpandWidth = false;
                mhlg.childForceExpandHeight = true;
                mhlg.childControlWidth = true;
                mhlg.childControlHeight = true;
                mhlg.padding = new RectOffset(16, 8, 2, 2);
                mhlg.childAlignment = TextAnchor.MiddleLeft;

                var (label, _l) = UIHelpers.Create("MemberLabel", memberRow.transform);
                var labelLE = label.AddComponent<LayoutElement>();
                labelLE.flexibleWidth = 1;
                labelLE.preferredWidth = 200;
                // A member whose preset was deleted outside the mod still renders, greyed —
                // silently dropping it would hide why the pack applies fewer rules than expected.
                UIHelpers.AddLabel(label, preset?.Name ?? pack.PresetIds[idx], 14f,
                    TextAlignmentOptions.MidlineLeft,
                    preset != null ? Color.white : new Color(0.7f, 0.4f, 0.4f));

                AddMemberButton(memberRow.transform, "MemberUp", "^", new Color(0.3f, 0.3f, 0.3f), () => {
                    if (idx == 0) return;
                    var tmp = pack.PresetIds[idx - 1];
                    pack.PresetIds[idx - 1] = pack.PresetIds[idx];
                    pack.PresetIds[idx] = tmp;
                    PersistPack(pack, onChanged, setStatus);
                });
                AddMemberButton(memberRow.transform, "MemberDown", "v", new Color(0.3f, 0.3f, 0.3f), () => {
                    if (idx >= pack.PresetIds.Count - 1) return;
                    var tmp = pack.PresetIds[idx + 1];
                    pack.PresetIds[idx + 1] = pack.PresetIds[idx];
                    pack.PresetIds[idx] = tmp;
                    PersistPack(pack, onChanged, setStatus);
                });
                AddMemberButton(memberRow.transform, "MemberRemove", "pack.member_remove".i18n(),
                    new Color(0.5f, 0.15f, 0.15f), () => {
                        pack.PresetIds.RemoveAt(idx);
                        PersistPack(pack, onChanged, setStatus);
                    });
            }

            var (availTitle, _at) = UIHelpers.Create($"Available_{pack.Id}", parent);
            availTitle.AddComponent<LayoutElement>().preferredHeight = 24;
            UIHelpers.AddLabel(availTitle, "pack.available_title".i18n(), 13f,
                TextAlignmentOptions.MidlineLeft, new Color(0.8f, 0.8f, 0.8f));

            foreach (var preset in PresetRegistry.All()) {
                // Duplicates inside one pack would insert the same rule twice — hide members.
                if (pack.PresetIds.Contains(preset.Id)) continue;
                var captured = preset;
                var (availRow, _ar) = UIHelpers.Create($"Avail_{pack.Id}_{preset.Id}", parent);
                availRow.AddComponent<LayoutElement>().preferredHeight = 28;

                var ahlg = availRow.AddComponent<HorizontalLayoutGroup>();
                ahlg.spacing = 4;
                ahlg.childForceExpandWidth = false;
                ahlg.childForceExpandHeight = true;
                ahlg.childControlWidth = true;
                ahlg.childControlHeight = true;
                ahlg.padding = new RectOffset(16, 8, 2, 2);
                ahlg.childAlignment = TextAnchor.MiddleLeft;

                var (label, _l) = UIHelpers.Create("AvailLabel", availRow.transform);
                var labelLE = label.AddComponent<LayoutElement>();
                labelLE.flexibleWidth = 1;
                labelLE.preferredWidth = 200;
                UIHelpers.AddLabel(label, captured.Name, 13f,
                    TextAlignmentOptions.MidlineLeft, new Color(0.75f, 0.75f, 0.75f));

                AddMemberButton(availRow.transform, "AvailAdd", "pack.member_add".i18n(),
                    new Color(0.2f, 0.45f, 0.2f), () => {
                        pack.PresetIds.Add(captured.Id);
                        PersistPack(pack, onChanged, setStatus);
                    });
            }
        }

        static void AddMemberButton(Transform parent, string name, string label, Color bg,
            UnityEngine.Events.UnityAction onClick) {
            var (obj, _) = UIHelpers.Create(name, parent);
            var le = obj.AddComponent<LayoutElement>();
            le.preferredWidth = 34;
            le.flexibleWidth = 0;
            UIHelpers.AddBackground(obj, bg);
            UIHelpers.AddLabel(obj, label, 15f, TextAlignmentOptions.Midline);
            obj.AddComponent<Button>().onClick.AddListener(onClick);
        }

        static void PersistPack(TacticsPack pack, Action onChanged, Action<string, Color> setStatus) {
            if (!PackRegistry.Save(pack))
                setStatus(string.Format("status.save_failed".i18n(), pack.Name), new Color(1f, 0.5f, 0.4f));
            onChanged();
        }
    }
}
```

- [ ] **Step 3: Mount it on the Presets tab**

In `WrathTactics/UI/PresetPanel.cs`, replace the separator block at lines 98-101:

```csharp
            // Separator
            var (sep, _s) = UIHelpers.Create("Sep", root.transform);
            sep.AddComponent<LayoutElement>().preferredHeight = 10;
```

with:

```csharp
            // Separator
            var (sep, _s) = UIHelpers.Create("Sep", root.transform);
            sep.AddComponent<LayoutElement>().preferredHeight = 10;

            // Packs section — rebuild deferred so a rename's onEndEdit is off the stack
            // before its TMP_InputField gets destroyed (same reason as the preset rename).
            PackPanel.Build(root.transform,
                () => StartCoroutine(DeferredRebuild()),
                (text, color) => SetStatus(text, color));
```

- [ ] **Step 4: Build, deploy and verify in-game**

Run: `./deploy.sh`
Then in-game: `Ctrl+T` → Presets tab. Verify:
1. "Packs" section renders above the preset list with the hint card and `+ New Pack`.
2. `+ New Pack` creates "New Pack"; the row shows swatch, name field, "0 preset(s)", Members, Delete.
3. Clicking the swatch cycles through six colours and the colour survives closing/reopening the panel (Ctrl+T twice).
4. Renaming the pack and pressing Enter keeps the new name after the rebuild and does not freeze the panel.
5. Members → available presets list; `+` moves a preset up into the member list, `−` removes it, `^`/`v` reorder, and the "N preset(s)" count follows.
6. `{ModPath}/Packs/<id>.json` exists on the deck with the expected contents:
   `ssh deck-direct "cat '<game>/Mods/WrathTactics/Packs/'*.json"`

- [ ] **Step 5: Commit**

```bash
git add WrathTactics/UI/PackPanel.cs WrathTactics/UI/PresetPanel.cs WrathTactics/Localization/en_GB.json
git commit -m "feat(packs): pack management section on the presets tab"
```

---

### Task 5: Applying packs from a character or global tab

**Files:**
- Modify: `WrathTactics/UI/TacticsPanel.cs` (add the pack row inside `RefreshRuleList`, lines 488-506)
- Modify: `WrathTactics/Localization/en_GB.json`

**Interfaces:**
- Consumes: `PackRegistry.{All,Get,PlanApply,AppliedPackIds}`, `PresetRegistry.Get`, `PackPalette.ColorAt` (Tasks 2-3).
- Produces: no new public API — `TacticsPanel.AddPackRow(List<TacticsRule>)`, `.ShowPackPicker()`, `.ApplyPack(TacticsPack)`, `.RemovePackFromList(TacticsPack)`, `.SetPackStatus(string, Color)` are private.

Character tabs are built from `Player.PartyAndPets` (`TacticsPanel.cs:198`), so pets get pack support from the same code path as companions — no special casing, but the smoke test must cover a pet tab explicitly.

- [ ] **Step 1: Add the en_GB strings**

In `WrathTactics/Localization/en_GB.json`, next to the other `pack.*` keys, add:

```json
  "pack.row_label": "Packs:",
  "pack.button.apply": "+ Apply Pack",
  "pack.none_defined": "no packs defined",
  "status.pack_applied": "Applied '{0}': {1} rule(s) added, {2} already present",
  "status.pack_nothing_to_add": "'{0}' is already fully applied",
  "status.pack_removed": "Removed {0} rule(s) of '{1}'",
```

- [ ] **Step 2: Add the pack status fields**

In `WrathTactics/UI/TacticsPanel.cs`, next to the other filter/panel fields (after line 32, `PresetPanel currentPresetPanel;`), add:

```csharp
        // Result of the last pack action, rendered in the pack row. Mirrors
        // PresetPanel.lastIOStatus: the row is rebuilt constantly, so the text must
        // live on the panel, not on the label.
        string lastPackStatus;
        Color lastPackStatusColor = Color.gray;
```

In `SelectTab` (line 233), directly after `selectedUnitId = unitId;`, add:

```csharp
            // Don't carry one character's pack message over to the next tab.
            lastPackStatus = null;
```

- [ ] **Step 3: Render the pack row above the rule cards**

In `WrathTactics/UI/TacticsPanel.cs`, inside `RefreshRuleList`, insert a call directly before the rule loop at line 501 (`for (int i = 0; i < rules.Count; i++) {`):

```csharp
            AddPackRow(rules);
```

Then add these methods after `AddHudButtonToggleRow` (i.e. after line 536):

```csharp
        // Applied-packs strip: one chip per pack present in this list plus the apply button.
        // Lives in the rule list like the hint cards — no RuleEditorWidget, so ApplyFilter
        // ignores it. Chips are per-pack, so a companion can carry any number of packs.
        void AddPackRow(List<TacticsRule> rules) {
            var (row, _) = UIHelpers.Create("PackRow", ruleListContent);
            row.AddComponent<LayoutElement>().preferredHeight = 32f * UIHelpers.FontScale;

            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.padding = new RectOffset(4, 4, 2, 2);
            hlg.childAlignment = TextAnchor.MiddleLeft;

            var (labelObj, _l) = UIHelpers.Create("PackRowLabel", row.transform);
            var labelLE = labelObj.AddComponent<LayoutElement>();
            labelLE.preferredWidth = 70;
            labelLE.flexibleWidth = 0;
            UIHelpers.AddLabel(labelObj, "pack.row_label".i18n(), 14f,
                TextAlignmentOptions.MidlineLeft, Color.white);

            foreach (var packId in Engine.PackRegistry.AppliedPackIds(rules)) {
                var pack = Engine.PackRegistry.Get(packId);
                if (pack == null) continue;  // pack deleted — rules keep working, no chip
                var captured = pack;
                var (chip, _c) = UIHelpers.Create($"PackChip_{pack.Id}", row.transform);
                var chipLE = chip.AddComponent<LayoutElement>();
                chipLE.preferredWidth = 130;
                chipLE.flexibleWidth = 0;
                UIHelpers.AddBackground(chip, PackPalette.ColorAt(pack.ColorIndex));
                UIHelpers.AddLabel(chip, pack.Name + "  ×", 13f, TextAlignmentOptions.Midline);
                chip.AddComponent<Button>().onClick.AddListener(() => RemovePackFromList(captured));
            }

            var (applyBtn, _a) = UIHelpers.Create("ApplyPackBtn", row.transform);
            var applyLE = applyBtn.AddComponent<LayoutElement>();
            applyLE.preferredWidth = 120;
            applyLE.flexibleWidth = 0;
            UIHelpers.AddBackground(applyBtn, new Color(0.2f, 0.4f, 0.45f, 1f));
            UIHelpers.AddLabel(applyBtn, "pack.button.apply".i18n(), 14f, TextAlignmentOptions.Midline);
            applyBtn.AddComponent<Button>().onClick.AddListener(ShowPackPicker);

            // Result of the last pack action — the character tab has no status line of its
            // own, and a silent "nothing happened" is indistinguishable from a broken button.
            if (!string.IsNullOrEmpty(lastPackStatus)) {
                var (statusObj, _st) = UIHelpers.Create("PackStatus", row.transform);
                var statusLE = statusObj.AddComponent<LayoutElement>();
                statusLE.preferredWidth = 240;
                statusLE.flexibleWidth = 1;
                UIHelpers.AddLabel(statusObj, lastPackStatus, 12f,
                    TextAlignmentOptions.MidlineLeft, lastPackStatusColor);
            }

            UIHelpers.EnsureAllHoverable(row);
        }

        void SetPackStatus(string text, Color color) {
            lastPackStatus = text;
            lastPackStatusColor = color;
        }

        void ShowPackPicker() {
            if (selectedUnitId == "presets") return;

            var packs = Engine.PackRegistry.All();
            if (packs.Count == 0) {
                SetPackStatus("pack.none_defined".i18n(), new Color(1f, 0.5f, 0.4f));
                Log.UI.Info("No packs available — create one on the Presets tab first");
                RefreshRuleList();
                return;
            }

            var options = new List<string>();
            foreach (var p in packs) options.Add($"{p.Name} ({p.PresetIds.Count})");

            PopupSelector.ShowPicker(options, idx => {
                if (idx < 0 || idx >= packs.Count) return;
                ApplyPack(packs[idx]);
            });
        }

        // Re-applying is a sync: only members missing from THIS pack's rules get appended,
        // so a user who deleted one rule can restore it without producing duplicates.
        void ApplyPack(TacticsPack pack) {
            var list = selectedUnitId == null
                ? ConfigManager.Current.GlobalRules
                : GetOrCreateCharacterRules(selectedUnitId);

            var plan = Engine.PackRegistry.PlanApply(pack, list,
                presetId => Engine.PresetRegistry.Get(presetId) != null);

            int alreadyPresent = pack.PresetIds.Count - plan.Count;
            list.AddRange(plan);
            ConfigManager.Save();
            SetPackStatus(
                plan.Count == 0
                    ? string.Format("status.pack_nothing_to_add".i18n(), pack.Name)
                    : string.Format("status.pack_applied".i18n(), pack.Name, plan.Count, alreadyPresent),
                plan.Count == 0 ? Color.gray : new Color(0.6f, 0.85f, 0.6f));
            Log.UI.Info($"Applied pack '{pack.Name}': +{plan.Count} rule(s), {alreadyPresent} already present");
            RefreshRuleList();
        }

        // Removes only rules stamped with this pack. Rules the user unlinked (Unlink & Edit
        // clears PresetId but keeps PackId) are removed too — they are still this pack's slot.
        void RemovePackFromList(TacticsPack pack) {
            var list = selectedUnitId == null
                ? ConfigManager.Current.GlobalRules
                : GetOrCreateCharacterRules(selectedUnitId);

            int removed = list.RemoveAll(r => r != null && r.PackId == pack.Id);
            ConfigManager.Save();
            SetPackStatus(string.Format("status.pack_removed".i18n(), removed, pack.Name),
                new Color(0.6f, 0.85f, 0.6f));
            Log.UI.Info($"Removed {removed} rule(s) of pack '{pack.Name}'");
            RefreshRuleList();
        }
```

- [ ] **Step 4: Build and verify the compile**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: Build succeeded. If `PopupSelector.ShowPicker` reports a missing overload, check the signature at `WrathTactics/UI/UIHelpers.cs` and match the call used by `AddFromPreset` (`TacticsPanel.cs:603`).

- [ ] **Step 5: Deploy and verify in-game**

Run: `./deploy.sh`
In-game verification (needs at least two packs with 2+ presets each):
1. On a companion tab, `+ Apply Pack` lists every pack with its member count.
2. Applying appends one card per member, each tinted in the pack colour, each showing the linked-preset badge; the status text reports how many were added.
3. Applying a **second** pack to the same companion adds its rules below in its own colour; both chips are visible side by side.
4. Deleting one rule of pack A and re-applying pack A restores exactly that one rule — no duplicates of the others; re-applying again reports "already fully applied".
5. Clicking a chip's `×` removes only that pack's rules; the other pack's rules and hand-built rules stay.
6. The same flow works on the Global tab **and on a pet tab** (Animal Companion / Aivu) — pets are ordinary character tabs.
7. Switching tabs clears the status text (no message bleeding from one companion to the next).
8. Reopen the panel (Ctrl+T twice) — chips, colours and rules survive; check `UserSettings/tactics-*.json` contains `PackId` on the applied rules.

- [ ] **Step 6: Commit**

```bash
git add WrathTactics/UI/TacticsPanel.cs WrathTactics/Localization/en_GB.json
git commit -m "feat(packs): apply and remove packs per character"
```

---

### Task 6: Save an existing rule list as a pack

**Files:**
- Modify: `WrathTactics/UI/TacticsPanel.cs` (extend `AddPackRow`, add `SaveListAsPack`)
- Modify: `WrathTactics/Localization/en_GB.json`

**Interfaces:**
- Consumes: `PresetRegistry.PromoteRuleToPreset` (existing), `PackRegistry.Save` (Task 2).
- Produces: private `TacticsPanel.SaveListAsPack()`.

This is the workflow that makes packs worth having: a player who already built three rules on Seelah turns them into a reusable pack in one click, instead of promoting each rule to a preset by hand.

- [ ] **Step 1: Add the en_GB strings**

```json
  "pack.button.save_list": "Save List as Pack",
  "pack.saved_list_name": "{0} Pack",
  "status.pack_saved_from_list": "Saved {0} rule(s) as pack '{1}'",
  "status.pack_save_list_empty": "Nothing to save — this list has no rules",
```

- [ ] **Step 2: Add the button to the pack row**

In `WrathTactics/UI/TacticsPanel.cs`, in `AddPackRow`, directly after the `ApplyPackBtn` block and before `UIHelpers.EnsureAllHoverable(row);`, add:

```csharp
            var (saveBtn, _s) = UIHelpers.Create("SaveListAsPackBtn", row.transform);
            var saveLE = saveBtn.AddComponent<LayoutElement>();
            saveLE.preferredWidth = 150;
            saveLE.flexibleWidth = 0;
            UIHelpers.AddBackground(saveBtn, new Color(0.25f, 0.45f, 0.3f, 1f));
            UIHelpers.AddLabel(saveBtn, "pack.button.save_list".i18n(), 14f, TextAlignmentOptions.Midline);
            saveBtn.AddComponent<Button>().onClick.AddListener(SaveListAsPack);
```

- [ ] **Step 3: Implement the promotion**

Add after `RemovePackFromList` in `WrathTactics/UI/TacticsPanel.cs`:

```csharp
        // Turns the whole visible list into a pack: every standalone rule is promoted to a
        // preset (PromoteRuleToPreset links the original in place), already-linked rules
        // contribute their existing preset. The rules stay where they are — the pack is a
        // reusable copy of the list, not a move.
        void SaveListAsPack() {
            if (selectedUnitId == "presets") return;

            var list = selectedUnitId == null
                ? ConfigManager.Current.GlobalRules
                : GetOrCreateCharacterRules(selectedUnitId);

            if (list.Count == 0) {
                SetPackStatus("status.pack_save_list_empty".i18n(), new Color(1f, 0.5f, 0.4f));
                Log.UI.Info("Save list as pack: list is empty");
                RefreshRuleList();
                return;
            }

            var pack = new TacticsPack {
                Name = string.Format("pack.saved_list_name".i18n(),
                    selectedUnitId == null ? "tab.global".i18n() : GetCharacterName(selectedUnitId)),
            };

            int promoted = 0;
            foreach (var rule in list) {
                if (rule == null) continue;
                string presetId = rule.PresetId;
                if (string.IsNullOrEmpty(presetId)) {
                    var preset = Engine.PresetRegistry.PromoteRuleToPreset(rule);
                    if (preset == null) {
                        // Promotion failed on disk; PromoteRuleToPreset left the rule intact.
                        Log.UI.Warn($"Save list as pack: could not promote rule '{rule.Name}' — skipped");
                        continue;
                    }
                    presetId = preset.Id;
                    promoted++;
                }
                // A list may legitimately hold the same preset twice; the pack keeps one slot
                // per preset because PlanApply de-duplicates per pack anyway.
                if (!pack.PresetIds.Contains(presetId)) pack.PresetIds.Add(presetId);
                // Only claim rules that don't belong to a pack yet. Re-stamping a rule that
                // came from another pack would silently steal it: its old chip would vanish
                // and "remove pack X" would no longer find it.
                if (string.IsNullOrEmpty(rule.PackId)) rule.PackId = pack.Id;
            }

            if (pack.PresetIds.Count == 0) {
                SetPackStatus(string.Format("status.save_failed".i18n(), pack.Name), new Color(1f, 0.5f, 0.4f));
                Log.UI.Warn("Save list as pack: no rule could be promoted");
                RefreshRuleList();
                return;
            }

            if (!Engine.PackRegistry.Save(pack)) {
                SetPackStatus(string.Format("status.save_failed".i18n(), pack.Name), new Color(1f, 0.5f, 0.4f));
                Log.UI.Error($"Save list as pack: failed to persist pack '{pack.Name}'");
                // The rules were already promoted and re-linked in memory; persist that much
                // so the user doesn't lose the promotion along with the pack.
                ConfigManager.Save();
                RefreshRuleList();
                return;
            }
            ConfigManager.Save();
            SetPackStatus(
                string.Format("status.pack_saved_from_list".i18n(), pack.PresetIds.Count, pack.Name),
                new Color(0.6f, 0.85f, 0.6f));
            Log.UI.Info($"Saved {pack.PresetIds.Count} rule(s) as pack '{pack.Name}' ({promoted} newly promoted)");
            RefreshRuleList();
        }
```

- [ ] **Step 4: Build, deploy, verify**

Run: `./deploy.sh`
In-game:
1. Build three plain rules on a companion, click "Save List as Pack".
2. All three cards turn into linked cards in the new pack's colour and a chip appears; the status reports "Saved 3 rule(s) as pack …".
3. The Presets tab shows three new presets and a pack containing them in list order.
4. Apply that pack to a second companion — the same three rules appear there.
5. Edit one preset on the Presets tab; both companions' cards show the change (open a card and confirm the summary line).
6. Ownership check: on a companion that already has pack A applied, add one hand-built rule and click "Save List as Pack". The A-rules keep pack A's colour and chip, only the new rule joins the new pack, and both chips remain listed.

- [ ] **Step 5: Commit**

```bash
git add WrathTactics/UI/TacticsPanel.cs WrathTactics/Localization/en_GB.json
git commit -m "feat(packs): save a character's rule list as a pack"
```

---

### Task 7: Pack export / import via clipboard

**Files:**
- Modify: `WrathTactics/UI/PackPanel.cs` (export button per pack)
- Modify: `WrathTactics/UI/PresetPanel.cs:229-288` (`ImportFromClipboard` learns the bundle format)
- Modify: `WrathTactics/Localization/en_GB.json`

**Interfaces:**
- Consumes: `PackRegistry`, `PresetRegistry` (Tasks 2, existing).
- Produces: `PackPanel.ExportPackToClipboard(TacticsPack)`; `PresetPanel.TryImportPackBundle(string json, out string status, out Color color) → bool`.

A pack export must inline its member presets — shipping only preset IDs would produce a pack that resolves to nothing on the recipient's machine.

- [ ] **Step 1: Add the en_GB strings**

```json
  "status.pack_export_copied": "Pack '{0}' copied to clipboard ({1} preset(s))",
  "status.pack_import_success": "Imported pack '{0}' with {1} preset(s)",
  "status.pack_export_empty": "Pack '{0}' has no members to export",
```

- [ ] **Step 2: Add the export button**

In `WrathTactics/UI/PackPanel.cs`, in `CreatePackRow`, directly before the `PackDelBtn` block, add:

```csharp
            var (exportBtn, _x) = UIHelpers.Create("PackExportBtn", row.transform);
            var exportLE = exportBtn.AddComponent<LayoutElement>();
            exportLE.preferredWidth = 70;
            exportLE.flexibleWidth = 0;
            UIHelpers.AddBackground(exportBtn, new Color(0.3f, 0.3f, 0.5f, 1f));
            UIHelpers.AddLabel(exportBtn, "pack.button.export".i18n(), 14f, TextAlignmentOptions.Midline);
            exportBtn.AddComponent<Button>().onClick.AddListener(() => ExportPackToClipboard(pack, setStatus));
```

And add this method to `PackPanel`:

```csharp
        /// <summary>
        /// Copies a self-contained bundle: the pack plus a full copy of every member preset.
        /// Exporting ids alone would resolve to nothing on the recipient's machine.
        /// </summary>
        static void ExportPackToClipboard(TacticsPack pack, Action<string, Color> setStatus) {
            var presets = new List<TacticsRule>();
            foreach (var id in pack.PresetIds) {
                var preset = PresetRegistry.Get(id);
                if (preset != null) presets.Add(preset);
            }
            if (presets.Count == 0) {
                setStatus(string.Format("status.pack_export_empty".i18n(), pack.Name),
                    new Color(1f, 0.5f, 0.4f));
                return;
            }

            var bundle = new PackBundle { Pack = pack, Presets = presets };
            GUIUtility.systemCopyBuffer =
                Newtonsoft.Json.JsonConvert.SerializeObject(bundle, Newtonsoft.Json.Formatting.Indented);
            setStatus(string.Format("status.pack_export_copied".i18n(), pack.Name, presets.Count),
                new Color(0.6f, 0.85f, 0.6f));
        }

        /// <summary>Clipboard wire format for a shared pack. Presets are inlined copies.</summary>
        public class PackBundle {
            public TacticsPack Pack;
            public List<TacticsRule> Presets;
        }
```

- [ ] **Step 3: Teach the importer the bundle format**

In `WrathTactics/UI/PresetPanel.cs`, in `ImportFromClipboard`, insert directly after the empty-clipboard guard (after line 234, `}`):

```csharp
            // A pack bundle is a JSON object; the legacy preset export is a JSON array.
            // Sniff the first non-whitespace character rather than attempting both parses.
            if (text[0] == '{') {
                if (TryImportPackBundle(text, out var packStatus, out var packColor)) {
                    SetStatus(packStatus, packColor);
                    onPresetsChanged?.Invoke();
                    Rebuild();
                } else {
                    SetStatus(packStatus, packColor);
                }
                return;
            }
```

And add this method to `PresetPanel`:

```csharp
        /// <summary>
        /// Imports a pack bundle produced by PackPanel's Export. Member presets are imported
        /// as new presets (fresh ids, so an existing preset is never overwritten) and the pack
        /// is rewritten to point at those new ids.
        /// </summary>
        bool TryImportPackBundle(string text, out string status, out Color color) {
            PackPanel.PackBundle bundle;
            try {
                bundle = Newtonsoft.Json.JsonConvert.DeserializeObject<PackPanel.PackBundle>(text);
            } catch (Newtonsoft.Json.JsonException ex) {
                status = string.Format("status.clipboard_invalid_json".i18n(), ex.Message);
                color = new Color(1f, 0.5f, 0.4f);
                return false;
            }
            if (bundle?.Pack == null || bundle.Presets == null) {
                status = "status.clipboard_not_array".i18n();
                color = new Color(1f, 0.5f, 0.4f);
                return false;
            }

            // oldId -> newId, so the pack's member list can be remapped after import.
            var idMap = new Dictionary<string, string>();
            foreach (var preset in bundle.Presets) {
                if (preset == null || string.IsNullOrEmpty(preset.Id)) continue;
                var oldId = preset.Id;
                preset.Id = Guid.NewGuid().ToString();
                preset.PresetId = null;
                preset.PackId = null;
                if (PresetRegistry.Save(preset)) idMap[oldId] = preset.Id;
            }

            var pack = bundle.Pack;
            pack.Id = Guid.NewGuid().ToString();
            var remapped = new List<string>();
            foreach (var oldId in pack.PresetIds) {
                if (oldId != null && idMap.TryGetValue(oldId, out var newId)) remapped.Add(newId);
            }
            pack.PresetIds = remapped;
            PackRegistry.Save(pack);

            status = string.Format("status.pack_import_success".i18n(), pack.Name, remapped.Count);
            color = new Color(0.6f, 0.85f, 0.6f);
            return true;
        }
```

`PresetPanel.cs` already has `using System;`, `using System.Collections.Generic;`, `using UnityEngine;`, `using WrathTactics.Engine;` — no new using directives are needed.

- [ ] **Step 4: Build, deploy, verify**

Run: `./deploy.sh`
In-game:
1. Export a pack → the clipboard holds `{"Pack":{...},"Presets":[...]}` (paste into a text editor to confirm).
2. Delete the pack **and** its presets, then Import → pack and presets reappear, member list intact and in order.
3. Import the same clipboard content a second time → a second pack with freshly-named presets appears; the first pack's members are untouched.
4. Export All Presets → Import still works (array path unaffected).

- [ ] **Step 5: Commit**

```bash
git add WrathTactics/UI/PackPanel.cs WrathTactics/UI/PresetPanel.cs WrathTactics/Localization/en_GB.json
git commit -m "feat(packs): clipboard export and import of self-contained packs"
```

---

### Task 8: Localisation for the remaining four locales

**Files:**
- Modify: `WrathTactics/Localization/{de_DE,fr_FR,ru_RU,zh_CN}.json`

**Interfaces:**
- Consumes: the en_GB keys added in Tasks 4-7.
- Produces: nothing (data only).

- [ ] **Step 1: Confirm the key set**

Run: `rtk proxy grep -n '"pack\.\|"status\.pack' WrathTactics/Localization/en_GB.json`
Expected: every key referenced below. If a key is missing, it was dropped in an earlier task — add it to en_GB first.

- [ ] **Step 2: Add the German block to `de_DE.json`**

```json
  "pack.section_title": "Pakete",
  "pack.hint": "Ein Paket bündelt Presets unter einem Namen und einer Farbe. Du kannst einem Begleiter beliebig viele Pakete zuweisen — jede Regel bleibt einzeln bearbeitbar.",
  "pack.empty": "Noch keine Pakete.",
  "pack.button.new": "+ Neues Paket",
  "pack.button.members": "Inhalt",
  "pack.button.close_members": "Schließen",
  "pack.button.export": "Export",
  "pack.default_name": "Neues Paket",
  "pack.member_count": "{0} Preset(s)",
  "pack.member_add": "+",
  "pack.member_remove": "−",
  "pack.members_title": "Inhalt (von oben nach unten = Einfügereihenfolge)",
  "pack.members_empty": "Noch keine Presets in diesem Paket — unten eines hinzufügen.",
  "pack.available_title": "Verfügbare Presets",
  "pack.row_label": "Pakete:",
  "pack.button.apply": "+ Paket anwenden",
  "pack.none_defined": "keine Pakete vorhanden",
  "pack.button.save_list": "Liste als Paket speichern",
  "pack.saved_list_name": "Paket {0}",
  "status.pack_applied": "'{0}' angewendet: {1} Regel(n) hinzugefügt, {2} bereits vorhanden",
  "status.pack_nothing_to_add": "'{0}' ist bereits vollständig angewendet",
  "status.pack_removed": "{0} Regel(n) von '{1}' entfernt",
  "status.pack_saved_from_list": "{0} Regel(n) als Paket '{1}' gespeichert",
  "status.pack_save_list_empty": "Nichts zu speichern — diese Liste hat keine Regeln",
  "status.pack_export_copied": "Paket '{0}' in die Zwischenablage kopiert ({1} Preset(s))",
  "status.pack_import_success": "Paket '{0}' mit {1} Preset(s) importiert",
  "status.pack_export_empty": "Paket '{0}' hat keinen Inhalt zum Exportieren",
```

- [ ] **Step 3: Add the French block to `fr_FR.json`**

```json
  "pack.section_title": "Packs",
  "pack.hint": "Un pack regroupe des préréglages sous un nom et une couleur. Appliquez autant de packs que vous voulez à un compagnon — chaque règle reste modifiable individuellement.",
  "pack.empty": "Aucun pack pour l'instant.",
  "pack.button.new": "+ Nouveau pack",
  "pack.button.members": "Contenu",
  "pack.button.close_members": "Fermer",
  "pack.button.export": "Exporter",
  "pack.default_name": "Nouveau pack",
  "pack.member_count": "{0} préréglage(s)",
  "pack.member_add": "+",
  "pack.member_remove": "−",
  "pack.members_title": "Contenu (de haut en bas = ordre d'insertion)",
  "pack.members_empty": "Aucun préréglage dans ce pack — ajoutez-en un ci-dessous.",
  "pack.available_title": "Préréglages disponibles",
  "pack.row_label": "Packs :",
  "pack.button.apply": "+ Appliquer un pack",
  "pack.none_defined": "aucun pack défini",
  "pack.button.save_list": "Enregistrer la liste comme pack",
  "pack.saved_list_name": "Pack {0}",
  "status.pack_applied": "'{0}' appliqué : {1} règle(s) ajoutée(s), {2} déjà présente(s)",
  "status.pack_nothing_to_add": "'{0}' est déjà entièrement appliqué",
  "status.pack_removed": "{0} règle(s) de '{1}' supprimée(s)",
  "status.pack_saved_from_list": "{0} règle(s) enregistrée(s) comme pack '{1}'",
  "status.pack_save_list_empty": "Rien à enregistrer — cette liste ne contient aucune règle",
  "status.pack_export_copied": "Pack '{0}' copié dans le presse-papiers ({1} préréglage(s))",
  "status.pack_import_success": "Pack '{0}' importé avec {1} préréglage(s)",
  "status.pack_export_empty": "Le pack '{0}' n'a aucun contenu à exporter",
```

- [ ] **Step 4: Add the Russian block to `ru_RU.json`**

```json
  "pack.section_title": "Наборы",
  "pack.hint": "Набор объединяет пресеты под одним именем и цветом. Применяйте компаньону сколько угодно наборов — каждое правило остаётся отдельно редактируемым.",
  "pack.empty": "Наборов пока нет.",
  "pack.button.new": "+ Новый набор",
  "pack.button.members": "Состав",
  "pack.button.close_members": "Закрыть",
  "pack.button.export": "Экспорт",
  "pack.default_name": "Новый набор",
  "pack.member_count": "Пресетов: {0}",
  "pack.member_add": "+",
  "pack.member_remove": "−",
  "pack.members_title": "Состав (сверху вниз — порядок добавления)",
  "pack.members_empty": "В этом наборе пока нет пресетов — добавьте ниже.",
  "pack.available_title": "Доступные пресеты",
  "pack.row_label": "Наборы:",
  "pack.button.apply": "+ Применить набор",
  "pack.none_defined": "наборы не созданы",
  "pack.button.save_list": "Сохранить список как набор",
  "pack.saved_list_name": "Набор {0}",
  "status.pack_applied": "'{0}' применён: добавлено правил — {1}, уже было — {2}",
  "status.pack_nothing_to_add": "'{0}' уже применён полностью",
  "status.pack_removed": "Удалено правил набора '{1}': {0}",
  "status.pack_saved_from_list": "Правил сохранено как набор '{1}': {0}",
  "status.pack_save_list_empty": "Нечего сохранять — в списке нет правил",
  "status.pack_export_copied": "Набор '{0}' скопирован в буфер обмена (пресетов: {1})",
  "status.pack_import_success": "Набор '{0}' импортирован (пресетов: {1})",
  "status.pack_export_empty": "В наборе '{0}' нет содержимого для экспорта",
```

- [ ] **Step 5: Add the Chinese block to `zh_CN.json`**

Keep the file's convention: ASCII `: , ( )` plus fullwidth `。`

```json
  "pack.section_title": "规则包",
  "pack.hint": "规则包把多个预设放在一个名称和颜色下。可以给同伴应用任意多个规则包, 每条规则仍可单独编辑。",
  "pack.empty": "暂无规则包。",
  "pack.button.new": "+ 新建规则包",
  "pack.button.members": "内容",
  "pack.button.close_members": "关闭",
  "pack.button.export": "导出",
  "pack.default_name": "新建规则包",
  "pack.member_count": "{0} 个预设",
  "pack.member_add": "+",
  "pack.member_remove": "−",
  "pack.members_title": "内容 (由上至下为插入顺序)",
  "pack.members_empty": "此规则包中还没有预设, 请在下方添加。",
  "pack.available_title": "可用预设",
  "pack.row_label": "规则包:",
  "pack.button.apply": "+ 应用规则包",
  "pack.none_defined": "尚未创建规则包",
  "pack.button.save_list": "将列表保存为规则包",
  "pack.saved_list_name": "{0} 规则包",
  "status.pack_applied": "已应用 '{0}': 新增 {1} 条规则, {2} 条已存在",
  "status.pack_nothing_to_add": "'{0}' 已完全应用",
  "status.pack_removed": "已移除 '{1}' 的 {0} 条规则",
  "status.pack_saved_from_list": "已将 {0} 条规则保存为规则包 '{1}'",
  "status.pack_save_list_empty": "没有可保存的内容, 此列表没有规则",
  "status.pack_export_copied": "规则包 '{0}' 已复制到剪贴板 ({1} 个预设)",
  "status.pack_import_success": "已导入规则包 '{0}', 包含 {1} 个预设",
  "status.pack_export_empty": "规则包 '{0}' 没有可导出的内容",
```

- [ ] **Step 6: Verify all five files parse and cover the same keys**

```bash
for f in WrathTactics/Localization/*.json; do python3 -c "import json,sys; json.load(open('$f'))" && echo "$f OK"; done
python3 - <<'EOF'
import json
en = set(json.load(open('WrathTactics/Localization/en_GB.json')))
for loc in ['de_DE','fr_FR','ru_RU','zh_CN']:
    keys = set(json.load(open(f'WrathTactics/Localization/{loc}.json')))
    missing = sorted(k for k in en if k.startswith(('pack.','status.pack')) and k not in keys)
    print(loc, 'missing:', missing or 'none')
EOF
```
Expected: every file `OK`, `missing: none` for all four locales.

- [ ] **Step 7: Build, deploy and spot-check**

Run: `./deploy.sh`
Switch the game to German, open the Presets tab, confirm the Packs section reads German (locale JSONs are EmbeddedResources — an un-rebuilt deploy shows raw keys instead).

- [ ] **Step 8: Commit**

```bash
git add WrathTactics/Localization/
git commit -m "feat(i18n): pack strings for de/fr/ru/zh"
```

---

### Task 9: Documentation

**Files:**
- Modify: `CLAUDE.md` (architecture tree + one Top-Gotcha line)
- Modify: `claude-context/gotchas-persistence.md`
- Modify: `README.md`

**Interfaces:**
- Consumes: everything above.
- Produces: nothing (docs only).

- [ ] **Step 1: Extend the architecture tree in `CLAUDE.md`**

In the `Engine/` block of the architecture tree, after the `PresetRegistry` line, add:

```
    PackRegistry       # Rule packs: cache, apply/sync planning, preset-delete cascade
```

Replace the `Persistence/` line:

```
  Persistence/         # ConfigManager (per-save JSON), PresetManager, SafeConditionConverter
```

with:

```
  Persistence/         # ConfigManager (per-save JSON), PresetManager, PackManager,
                       # SafeConditionConverter
```

Replace the `UI/` line:

```
  UI/                  # TacticsPanel, RuleEditorWidget, ConditionRowWidget, PresetPanel,
                       # BuffPickerOverlay, SpellPickerOverlay, SpellDropdownProvider, UIHelpers
```

with:

```
  UI/                  # TacticsPanel, RuleEditorWidget, ConditionRowWidget, PresetPanel,
                       # PackPanel, PackPalette, BuffPickerOverlay, SpellPickerOverlay,
                       # SpellDropdownProvider, UIHelpers
```

- [ ] **Step 2: Add the pack rules to `claude-context/gotchas-persistence.md`**

Append to the "Presets & Seeding" section:

```markdown
- **Packs are link containers, stored apart from presets**: `{ModPath}/Packs/<id>.json`, one file per pack (`PackManager`). They must NOT live in `Presets/` — `PresetManager.LoadAll` globs `Presets/*.json` and would parse a pack as a rule.
- **Applying a pack appends `PresetId`-linked rules stamped with `PackId`** (`PackRegistry.PlanApply`). Re-applying syncs (only missing members are added) — the dedup key is `PackId + PresetId`, deliberately not `PresetId` alone, so two packs sharing a preset each own their copy and removing one pack can't strip the other's rule.
- **Deleting a pack keeps its applied rules**; deleting a *preset* cascades into both rules and pack member lists (`PresetRegistry.Delete` → `PackRegistry.RemovePresetFromPacks`). A dangling `PackId` only costs the colour tint.
- **`TacticsPack.ColorIndex` is a persisted index into `UI.PackPalette.Colors`** — append-only, like the enums. Reordering the palette repaints every existing pack.
- **Pack export inlines its member presets** (`PackPanel.PackBundle`): exporting bare ids produces a pack that resolves to nothing on the recipient's machine. The importer sniffs `{` (bundle) vs `[` (legacy preset array).
```

- [ ] **Step 3: Document the feature in `README.md`**

Add a section after the presets documentation:

```markdown
### Rule Packs

A pack is a named, colour-coded bundle of presets. Build your rules once, hit **Save List as Pack**
on the character tab, and apply the pack to any other companion or pet with **+ Apply Pack**.

- A companion can carry any number of packs at once — each pack's rules are tinted in its colour
  and listed as a chip above the rule list. Clicking a chip's `×` removes that pack's rules only.
- Rules inserted by a pack stay linked to their presets: edit the preset on the Presets tab and
  every character running that pack picks up the change.
- Re-applying a pack restores members you deleted without duplicating the ones still there.
- **Export** copies a self-contained pack (including its presets) to the clipboard; the Presets
  tab's **Import** button accepts both a pack bundle and the older preset-array format.
```

- [ ] **Step 4: Commit**

```bash
git add CLAUDE.md claude-context/gotchas-persistence.md README.md
git commit -m "docs: rule packs"
```

---

## Final Verification

- [ ] **Full test suite green**

Run: `for i in 1 2 3; do ~/.dotnet/dotnet test WrathTactics.Tests/WrathTactics.Tests.csproj -p:SolutionDir=$(pwd)/; done`
Expected: at least one all-green run; any failure must reproduce across runs to count.

- [ ] **Release build produces the zip**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -c Release -p:SolutionDir=$(pwd)/`
Expected: `bin/WrathTactics-<version>.zip` exists. NU1900 warnings are expected.

- [ ] **End-to-end smoke test on the deck**

Run: `./deploy.sh`, then in one session:
1. Build 3 rules on a melee companion → Save List as Pack → rename the pack, pick a colour.
2. Apply that pack to a second companion, then apply a second pack on top → both chips visible, both colour groups distinguishable.
3. Enter combat and confirm the pack rules actually fire (check `Mods/WrathTactics/Logs/wrath-tactics-*.log` for the rule-match lines) — pack rules must behave exactly like `+ From Preset` rules.
4. Delete one preset on the Presets tab → it disappears from the pack's member list and its rules vanish from both companions (existing cascade).
5. Reload the save and confirm chips, colours and rules persist.

- [ ] **Release**

Follow `wrath-mods/CLAUDE.md` §Release Process: run `/review`, then `/release minor` (1.26.0 → 1.27.0). Do **not** hand-bump the version first — `/release` does the bump commit itself and would otherwise bump twice. The working tree must be clean before invoking it.

## Notes for the implementer

- `PopupSelector.ShowPicker(List<string>, Action<int>)` is the existing modal picker used by `+ From Preset` (`TacticsPanel.cs:603`). It is a static helper on `WrathTactics.UI` — no instantiation needed.
- `UIHelpers.Create(name, parent)` returns `(GameObject, RectTransform)`. Inside a `HorizontalLayoutGroup` with `childControlWidth = true`, size children via `LayoutElement.preferredWidth` / `flexibleWidth`, not by setting anchors — mixing the two produces zero-width buttons.
- Rebuilding a panel from inside a `TMP_InputField.onEndEdit` callback crashes the layout; `PresetPanel.DeferredRebuild()` (a one-frame coroutine) is the established workaround and is why `PackPanel.Build` receives `onChanged` rather than calling `Rebuild` itself.
- Nothing in `Engine/TacticsEvaluator`, `ConditionEvaluator`, `ActionValidator` or `CommandExecutor` changes. If a pack rule behaves differently from the same rule added via `+ From Preset`, the bug is in `PlanApply` producing a malformed rule (non-empty body, missing `PresetId`), not in evaluation.
