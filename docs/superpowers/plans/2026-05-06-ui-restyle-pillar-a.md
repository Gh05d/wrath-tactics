# UI Re-Style — Pillar A (v1.10.0) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restyle the Wrath Tactics panel (background, title bar, action buttons, tab headers) with native Owlcat sprites bundled as PNGs, behind a single `ThemeProvider` facade — additive only, falls back to current Unity-default look on any missing sprite.

**Architecture:** PNG assets bundled under `WrathTactics/Assets/icons/`. A static `AssetLoader` (1:1 port from BuffIt2TheLimit) loads PNGs into a sprite cache once at mod init. A static `ThemeProvider` exposes typed sprite properties + `ApplyPanel`/`ApplyButton` helpers. Existing widgets (`UIHelpers`, `TacticsPanel`, `PresetPanel`, `RuleEditorWidget`) call into `ThemeProvider` from a small set of touch points. Null-sprite paths no-op silently — the mod must never crash on a missing PNG.

**Tech Stack:** C# / .NET Framework 4.8.1, Unity UI (Image, Button, Sprite, SpriteState), HarmonyLib (already in use), no new NuGet dependencies.

**Spec:** `docs/superpowers/specs/2026-05-06-ui-restyle-and-modmenu-design.md` (commit `c1924bb`).

**Out of scope (deferred to later iterations):** ModMenu integration (Pillar B → separate plan), picker overlays, dropdown widgets, HUD entry button, ConditionRowWidget inner widgets.

**Verification model:** No automated tests — Wrath mods have no test pipeline. After each task: `dotnet build` must be green. Final task runs the full Steam Deck smoke test.

---

## File Structure

| File | Status | Responsibility |
|---|---|---|
| `WrathTactics/Assets/icons/*.png` | New (12 files) | Bundled UI sprites extracted from Wrath's asset bundles |
| `WrathTactics/Assets/icons/SOURCES.md` | New | Documents the source path / Owlcat name of every PNG; tracks substitutions |
| `WrathTactics/Engine/AssetLoader.cs` | New | PNG → Sprite loader, port from BuffIt2TheLimit |
| `WrathTactics/UI/ThemeProvider.cs` | New | Single-source facade — typed sprite properties + Apply helpers |
| `WrathTactics/Main.cs` | Modify | Init hooks: `AssetLoader.Init()`, `ThemeProvider.Init()` |
| `WrathTactics/UI/UIHelpers.cs` | Modify | `MakeButton()` and `Create()` opt into themed visuals when sprites are loaded |
| `WrathTactics/UI/TacticsPanel.cs` | Modify | Title bar, close button, tab headers use themed sprites |
| `WrathTactics/UI/PresetPanel.cs` | Modify | Action buttons inherit themed visuals via UIHelpers |
| `WrathTactics/UI/RuleEditorWidget.cs` | Modify | Action buttons inherit themed visuals via UIHelpers |
| `WrathTactics/WrathTactics.csproj` | Modify | Embed PNGs via `<None Include="Assets\icons\*.png">` |

---

## Task 1: Extract sprites and bundle them in the build

**Files:**
- Create: `WrathTactics/Assets/icons/*.png` (12 files)
- Create: `WrathTactics/Assets/icons/SOURCES.md`
- Modify: `WrathTactics/WrathTactics.csproj`

The asset extraction is a one-time manual step on the laptop. Use **AssetStudio** (https://github.com/Perfare/AssetStudio) or **AssetRipper** (https://github.com/AssetRipper/AssetRipper) to open the game's `sharedassets*.assets` and `resources.assets` and export sprites as PNG. The game install is symlinked at `WrathTactics/GameInstall/` (which points to `wrath-epic-buffing/GameInstall/`).

- [ ] **Step 1: Create icons folder and extract sprites**

```bash
mkdir -p /home/pascal/Code/wrath-mods/wrath-tactics/WrathTactics/Assets/icons
cd /home/pascal/Code/wrath-mods/wrath-tactics
```

Open AssetStudio, point at `WrathTactics/GameInstall/Wrath_Data/`. Search and export the following Owlcat sprites (or their closest visual equivalents) as PNG into `WrathTactics/Assets/icons/`. Rename each export to the target filename below:

| Target filename | Search for in AssetStudio | Notes |
|---|---|---|
| `panel_background.png` | `UI_Window_Common_Bg` or `WindowBg` | 9-slice |
| `titlebar_background.png` | `UI_Window_Title_Bg` or similar header | 9-slice |
| `close_button_normal.png` | `UI_CommonButton_Close_Normal` | non-slice |
| `close_button_hover.png` | `UI_CommonButton_Close_Hover` | non-slice |
| `close_button_pressed.png` | `UI_CommonButton_Close_Pressed` or `_Down` | non-slice |
| `action_button_normal.png` | `UI_Button_Common_Normal` | 9-slice |
| `action_button_hover.png` | `UI_Button_Common_Hover` | 9-slice |
| `action_button_pressed.png` | `UI_Button_Common_Pressed` | 9-slice |
| `tab_header_active.png` | `UI_Tab_Active` or `UI_Tab_Selected` | 9-slice |
| `tab_header_inactive.png` | `UI_Tab_Inactive` or `UI_Tab_Default` | 9-slice |
| `scrollbar_track.png` | `UI_Scrollbar_Track` or `UI_Slider_Bg` | 9-slice |
| `scrollbar_handle.png` | `UI_Scrollbar_Handle` | 9-slice |

If a name doesn't exist verbatim in Wrath 1.4, substitute the closest visually-matching sprite. **Document every substitution in `SOURCES.md`.**

Cross-reference: `~/Code/wrath-mods/wrath-epic-buffing/BuffIt2TheLimit/Assets/icons/` ships `UI_HudCharacterFrameBorder_Default.png` and similar — useful as size/9-slice reference, not directly reusable for panel chrome.

- [ ] **Step 2: Write SOURCES.md**

```bash
cat > /home/pascal/Code/wrath-mods/wrath-tactics/WrathTactics/Assets/icons/SOURCES.md <<'EOF'
# Sprite Sources

Sprites extracted from Wrath of the Righteous 1.4 via AssetStudio
(`Wrath_Data/sharedassets*.assets`, `Wrath_Data/resources.assets`).

| File | Guessed source name | Actual exported name | Substitution rationale |
|---|---|---|---|
| panel_background.png | UI_Window_Common_Bg | _replace with the AssetStudio export name_ | _empty if exact, else why this sprite was chosen_ |
| titlebar_background.png | UI_Window_Title_Bg | | |
| close_button_normal.png | UI_CommonButton_Close_Normal | | |
| close_button_hover.png | UI_CommonButton_Close_Hover | | |
| close_button_pressed.png | UI_CommonButton_Close_Pressed | | |
| action_button_normal.png | UI_Button_Common_Normal | | |
| action_button_hover.png | UI_Button_Common_Hover | | |
| action_button_pressed.png | UI_Button_Common_Pressed | | |
| tab_header_active.png | UI_Tab_Active | | |
| tab_header_inactive.png | UI_Tab_Inactive | | |
| scrollbar_track.png | UI_Scrollbar_Track | | |
| scrollbar_handle.png | UI_Scrollbar_Handle | | |

## Re-extraction procedure

1. Install AssetStudio (https://github.com/Perfare/AssetStudio).
2. File → Load folder → `WrathTactics/GameInstall/Wrath_Data/`.
3. Filter Type to `Sprite`.
4. Search by name (column header), select target sprite, right-click → Export selected assets.
5. Rename to target filename above and place under `WrathTactics/Assets/icons/`.

## Re-export when Wrath updates

If a Wrath patch breaks visual consistency (sprite hash changed),
re-extract using the same procedure. The `Sprite.border` Vector4 values
are hardcoded in `ThemeProvider.cs` — if the new sprite has different
9-slice metrics, update the border constants there.
EOF
```

Replace each "(fill in)" entry with the actual exported asset name from AssetStudio, then commit. If you couldn't find an exact match for a sprite, document the substitution and note the visual reasoning ("used X because Y has same 9-slice metrics").

- [ ] **Step 3: Embed PNGs in the csproj**

Open `WrathTactics/WrathTactics.csproj` and add a new `ItemGroup` after the `Localization\*.json` block (around line 33):

```xml
<ItemGroup>
    <None Include="Assets\icons\*.png">
        <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
    <None Include="Assets\icons\SOURCES.md">
        <CopyToOutputDirectory>Never</CopyToOutputDirectory>
    </None>
</ItemGroup>
```

The `Never` on SOURCES.md keeps the documentation in the source tree but excludes it from the shipped build.

- [ ] **Step 4: Build and verify PNGs reach the output directory**

```bash
cd /home/pascal/Code/wrath-mods/wrath-tactics
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
ls WrathTactics/bin/Debug/Assets/icons/ | sort
```

Expected output: 12 PNG filenames (no `SOURCES.md`). If the directory doesn't exist or is empty, the csproj glob is wrong — fix and rebuild.

- [ ] **Step 5: Commit**

```bash
cd /home/pascal/Code/wrath-mods/wrath-tactics
git add WrathTactics/Assets/icons WrathTactics/WrathTactics.csproj
git commit -m "feat(ui): bundle Owlcat UI sprite assets

Adds 12 PNGs extracted from Wrath_Data via AssetStudio for the upcoming
panel restyle. Sources documented in Assets/icons/SOURCES.md. PNGs are
copied to the build output via PreserveNewest; SOURCES.md is source-only."
```

---

## Task 2: Port AssetLoader from BuffIt2TheLimit

**Files:**
- Create: `WrathTactics/Engine/AssetLoader.cs`
- Modify: `WrathTactics/Main.cs:25-32` (insert init call)

The BuffIt port is essentially a copy with namespace adjustment. Source: `~/Code/wrath-mods/wrath-epic-buffing/BuffIt2TheLimit/Utilities/AssetLoader.cs`.

- [ ] **Step 1: Create AssetLoader.cs**

```csharp
// WrathTactics/Engine/AssetLoader.cs
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using WrathTactics.Logging;

namespace WrathTactics.Engine {
    public static class AssetLoader {
        static readonly Dictionary<string, Sprite> SpriteCache = new();
        static bool initialized;

        public static void Init() {
            if (initialized) return;
            initialized = true;
            Log.UI.Info($"AssetLoader.Init — mod path: {Main.ModPath}");
        }

        /// <summary>
        /// Loads a PNG from `<ModPath>/Assets/<folder>/<file>` as a Sprite.
        /// Returns null on missing file or load failure (logged at warn level).
        /// Cached by filename — subsequent calls return the same Sprite instance.
        /// </summary>
        public static Sprite LoadInternal(string folder, string file, Vector2Int size) {
            var cacheKey = $"{folder}/{file}";
            if (SpriteCache.TryGetValue(cacheKey, out var cached)) return cached;
            try {
                var path = Path.Combine(Main.ModPath, "Assets", folder, file);
                if (!File.Exists(path)) {
                    Log.UI.Warn($"Sprite '{cacheKey}' missing at {path} — falling back to Unity default.");
                    SpriteCache[cacheKey] = null;
                    return null;
                }
                var bytes = File.ReadAllBytes(path);
                var texture = new Texture2D(size.x, size.y, TextureFormat.DXT5, false);
                texture.mipMapBias = 15.0f;
                if (!texture.LoadImage(bytes)) {
                    Log.UI.Warn($"Sprite '{cacheKey}' failed Texture2D.LoadImage at {path}.");
                    SpriteCache[cacheKey] = null;
                    return null;
                }
                var sprite = Sprite.Create(texture, new Rect(0, 0, size.x, size.y), new Vector2(0.5f, 0.5f));
                SpriteCache[cacheKey] = sprite;
                return sprite;
            } catch (Exception ex) {
                Log.UI.Error(ex, $"AssetLoader.LoadInternal failed for {cacheKey}");
                SpriteCache[cacheKey] = null;
                return null;
            }
        }
    }
}
```

Differences from BuffIt's version:
- Pivot is `(0.5, 0.5)` (centered) — needed for Unity UI sprite stretching to behave predictably with 9-slice. BuffIt used `(0, 0)` for icon glyphs where it doesn't matter.
- Cache key per `folder/file` (not per file alone) — future-proofs in case we add subfolders.
- Logs through our `Log.UI` category instead of BuffIt's `Main.Log`.

- [ ] **Step 2: Wire AssetLoader.Init() into Main.Load**

Open `WrathTactics/Main.cs`. Find the block at lines 25-32 (after `harmony.PatchAll();`):

```csharp
            harmony = new Harmony(modEntry.Info.Id);
            harmony.PatchAll();

            Engine.PresetRegistry.Reload();

            UI.TacticsPanel.Install();
```

Insert `AssetLoader.Init()` between `harmony.PatchAll()` and `Engine.PresetRegistry.Reload()`:

```csharp
            harmony = new Harmony(modEntry.Info.Id);
            harmony.PatchAll();

            Engine.AssetLoader.Init();

            Engine.PresetRegistry.Reload();

            UI.TacticsPanel.Install();
```

- [ ] **Step 3: Build and verify init log**

```bash
cd /home/pascal/Code/wrath-mods/wrath-tactics
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
```

Expected: `Build succeeded. 0 Error(s)`. If errors mention `Log.UI` not found, check `WrathTactics/Logging/Log.cs` — the UI category should already exist (used elsewhere). If it doesn't, add `public static readonly Logger UI = new Logger("UI");` next to the other categories.

- [ ] **Step 4: Commit**

```bash
git add WrathTactics/Engine/AssetLoader.cs WrathTactics/Main.cs
git commit -m "feat(engine): port AssetLoader from BuffIt2TheLimit

Caches PNG sprites loaded from <ModPath>/Assets/<folder>/<file>.
Returns null silently on missing file — caller is responsible for
graceful degradation. Wired into Main.Load() init sequence."
```

---

## Task 3: Build the ThemeProvider facade

**Files:**
- Create: `WrathTactics/UI/ThemeProvider.cs`
- Modify: `WrathTactics/Main.cs` (init hook)

`ThemeProvider` is the single point of contact between widget code and the sprite cache. It owns 9-slice border constants and the apply helpers.

- [ ] **Step 1: Create ThemeProvider.cs**

```csharp
// WrathTactics/UI/ThemeProvider.cs
using UnityEngine;
using UnityEngine.UI;
using WrathTactics.Engine;
using WrathTactics.Logging;

namespace WrathTactics.UI {
    public static class ThemeProvider {
        public static Sprite PanelBackground { get; private set; }
        public static Sprite TitleBarBackground { get; private set; }
        public static Sprite CloseButtonNormal { get; private set; }
        public static Sprite CloseButtonHover { get; private set; }
        public static Sprite CloseButtonPressed { get; private set; }
        public static Sprite ActionButtonNormal { get; private set; }
        public static Sprite ActionButtonHover { get; private set; }
        public static Sprite ActionButtonPressed { get; private set; }
        public static Sprite TabHeaderActive { get; private set; }
        public static Sprite TabHeaderInactive { get; private set; }
        public static Sprite ScrollbarTrack { get; private set; }
        public static Sprite ScrollbarHandle { get; private set; }

        public static void Init() {
            PanelBackground    = Load("panel_background.png",     new Vector2Int(32, 32), new Vector4(8, 8, 8, 8));
            TitleBarBackground = Load("titlebar_background.png",  new Vector2Int(32, 40), new Vector4(8, 8, 8, 8));
            CloseButtonNormal  = Load("close_button_normal.png",  new Vector2Int(32, 32), Vector4.zero);
            CloseButtonHover   = Load("close_button_hover.png",   new Vector2Int(32, 32), Vector4.zero);
            CloseButtonPressed = Load("close_button_pressed.png", new Vector2Int(32, 32), Vector4.zero);
            ActionButtonNormal = Load("action_button_normal.png", new Vector2Int(32, 40), new Vector4(6, 6, 6, 6));
            ActionButtonHover  = Load("action_button_hover.png",  new Vector2Int(32, 40), new Vector4(6, 6, 6, 6));
            ActionButtonPressed= Load("action_button_pressed.png",new Vector2Int(32, 40), new Vector4(6, 6, 6, 6));
            TabHeaderActive    = Load("tab_header_active.png",    new Vector2Int(24, 40), new Vector4(4, 4, 4, 4));
            TabHeaderInactive  = Load("tab_header_inactive.png",  new Vector2Int(24, 40), new Vector4(4, 4, 4, 4));
            ScrollbarTrack     = Load("scrollbar_track.png",      new Vector2Int(16,  8), new Vector4(2, 2, 2, 2));
            ScrollbarHandle    = Load("scrollbar_handle.png",     new Vector2Int(16, 16), new Vector4(2, 2, 2, 2));

            int loaded = 0;
            foreach (var s in new[] { PanelBackground, TitleBarBackground,
                CloseButtonNormal, CloseButtonHover, CloseButtonPressed,
                ActionButtonNormal, ActionButtonHover, ActionButtonPressed,
                TabHeaderActive, TabHeaderInactive,
                ScrollbarTrack, ScrollbarHandle }) {
                if (s != null) loaded++;
            }
            Log.UI.Info($"ThemeProvider initialised — {loaded}/12 sprites loaded.");
        }

        static Sprite Load(string file, Vector2Int size, Vector4 border) {
            var s = AssetLoader.LoadInternal("icons", file, size);
            if (s != null && border != Vector4.zero) s.border = border;
            return s;
        }

        /// <summary>
        /// Applies the panel background sprite to obj's Image (adds Image if missing).
        /// No-op if PanelBackground is null (sprite missing from disk).
        /// </summary>
        public static void ApplyPanel(GameObject obj) {
            if (obj == null || PanelBackground == null) return;
            var img = obj.GetComponent<Image>() ?? obj.AddComponent<Image>();
            img.sprite = PanelBackground;
            img.type = Image.Type.Sliced;
            img.color = Color.white;
            img.raycastTarget = true;
        }

        /// <summary>
        /// Applies a 3-state button sprite set to obj. Adds Image+Button if missing.
        /// No-op if normal sprite is null.
        /// </summary>
        public static void ApplyButton(GameObject obj, Sprite normal, Sprite hover, Sprite pressed) {
            if (obj == null || normal == null) return;
            var img = obj.GetComponent<Image>() ?? obj.AddComponent<Image>();
            img.sprite = normal;
            img.type = Image.Type.Sliced;
            img.color = Color.white;
            img.raycastTarget = true;

            var btn = obj.GetComponent<Button>() ?? obj.AddComponent<Button>();
            btn.transition = Selectable.Transition.SpriteSwap;
            btn.spriteState = new SpriteState {
                highlightedSprite = hover,
                pressedSprite     = pressed,
                selectedSprite    = hover,
                disabledSprite    = normal,
            };
            btn.targetGraphic = img;
        }

        public static void ApplyActionButton(GameObject obj) =>
            ApplyButton(obj, ActionButtonNormal, ActionButtonHover, ActionButtonPressed);

        public static void ApplyCloseButton(GameObject obj) =>
            ApplyButton(obj, CloseButtonNormal, CloseButtonHover, CloseButtonPressed);

        /// <summary>
        /// Applies a tab-header sprite. Pass `active=true` for the selected tab.
        /// Used when the tab is non-clickable (label) or paired with ApplyButton for click behaviour.
        /// </summary>
        public static void ApplyTabHeader(GameObject obj, bool active) {
            if (obj == null) return;
            var sprite = active ? TabHeaderActive : TabHeaderInactive;
            if (sprite == null) return;
            var img = obj.GetComponent<Image>() ?? obj.AddComponent<Image>();
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
            img.color = Color.white;
        }
    }
}
```

- [ ] **Step 2: Wire ThemeProvider.Init() into Main.Load**

In `WrathTactics/Main.cs`, immediately after the `Engine.AssetLoader.Init();` line added in Task 2:

```csharp
            Engine.AssetLoader.Init();
            UI.ThemeProvider.Init();

            Engine.PresetRegistry.Reload();
```

- [ ] **Step 3: Build and verify init log**

```bash
cd /home/pascal/Code/wrath-mods/wrath-tactics
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
```

Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add WrathTactics/UI/ThemeProvider.cs WrathTactics/Main.cs
git commit -m "feat(ui): add ThemeProvider facade

Single-source sprite owner. Loads all 12 panel-chrome sprites at init,
sets 9-slice borders, exposes typed properties + ApplyPanel/ApplyButton
helpers. Null-safe — every Apply method no-ops when the sprite is missing,
so existing widgets keep their Unity-default look on a partial asset
extraction."
```

---

## Task 4: Apply panel chrome (background + title bar + close button)

**Files:**
- Modify: `WrathTactics/UI/TacticsPanel.cs:90-110` (panel root + title bar + close button)

`TacticsPanel.CreatePanel()` is the entry point that builds the root GameObject. It currently calls `UIHelpers.AddBackground(titleBar, new Color(0.2f, 0.15f, 0.1f, 1f))` (line 101) for the title bar — replace with themed sprite.

Reference: actual structure is in `WrathTactics/UI/TacticsPanel.cs:88-110`:
```csharp
var (root, rootRect) = UIHelpers.Create("WrathTacticsPanel", canvas);   // line 88
panelRoot = root;
// ... rect anchors / sizing ...
UIHelpers.AddBackground(root, new Color(0.1f, 0.1f, 0.1f, 0.95f));      // line 95

var (titleBar, titleRect) = UIHelpers.Create("TitleBar", root.transform); // line 98
// ... rect anchors ...
UIHelpers.AddBackground(titleBar, new Color(0.2f, 0.15f, 0.1f, 1f));    // line 101

var (closeBtn, closeRect) = UIHelpers.Create("CloseButton", titleBar.transform); // line 105
// ... rect anchors ...
UIHelpers.AddBackground(closeBtn, new Color(0.6f, 0.2f, 0.2f, 1f));     // line 108
```

- [ ] **Step 1: Theme the panel root background (line 95)**

Replace line 95 (`UIHelpers.AddBackground(root, new Color(0.1f, 0.1f, 0.1f, 0.95f));`) with:

```csharp
            if (ThemeProvider.PanelBackground != null) {
                ThemeProvider.ApplyPanel(root);
            } else {
                UIHelpers.AddBackground(root, new Color(0.1f, 0.1f, 0.1f, 0.95f));
            }
```

The fallback flat-color path keeps a partial asset extraction (some PNGs missing) usable.

- [ ] **Step 2: Theme the title bar (line 101)**

Replace line 101 (`UIHelpers.AddBackground(titleBar, new Color(0.2f, 0.15f, 0.1f, 1f));`) with:

```csharp
            if (ThemeProvider.TitleBarBackground != null) {
                var img = titleBar.AddComponent<UnityEngine.UI.Image>();
                img.sprite = ThemeProvider.TitleBarBackground;
                img.type = UnityEngine.UI.Image.Type.Sliced;
                img.color = Color.white;
                img.raycastTarget = true;
            } else {
                UIHelpers.AddBackground(titleBar, new Color(0.2f, 0.15f, 0.1f, 1f));
            }
```

Fully-qualified `UnityEngine.UI.Image` avoids needing a new `using` if it isn't already there. If a `using UnityEngine.UI;` is present, you can drop the qualification.

- [ ] **Step 3: Theme the close button (line 108)**

Replace line 108 (`UIHelpers.AddBackground(closeBtn, new Color(0.6f, 0.2f, 0.2f, 1f));`) with:

```csharp
            if (ThemeProvider.CloseButtonNormal != null) {
                ThemeProvider.ApplyCloseButton(closeBtn);
            } else {
                UIHelpers.AddBackground(closeBtn, new Color(0.6f, 0.2f, 0.2f, 1f));
            }
```

`ApplyCloseButton` adds both the Image (with sprite swap) and a Button component. The label "X" added on a subsequent line stays — sprite renders behind the label.

- [ ] **Step 4: Build and verify**

```bash
cd /home/pascal/Code/wrath-mods/wrath-tactics
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
```

Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 5: Deploy + visual smoke test**

```bash
cd /home/pascal/Code/wrath-mods/wrath-tactics
./deploy.sh
```

Then in-game (or via Moonlight):
1. Launch Wrath, load a save.
2. Press Ctrl+T.
3. Verify the panel background is the wood/parchment sprite (not the dark flat color from before).
4. Verify the title bar shows the bordered sprite.
5. Hover the close button — sprite swaps to hover state.
6. Click close button — panel closes (functional behaviour intact).

If any sprite renders wrong (e.g., stretched weirdly), the 9-slice `border` Vector4 in `ThemeProvider.cs:Init()` is wrong for that sprite. Inspect the source PNG dimensions in AssetStudio and adjust the border constants. Per CLAUDE.md gotcha §"Verify deploy before diagnosing", confirm the deck DLL mtime matches local before debugging.

- [ ] **Step 6: Commit**

```bash
git add WrathTactics/UI/TacticsPanel.cs
git commit -m "feat(ui): apply themed panel chrome to TacticsPanel

Panel root background, title bar background, and close button now use
ThemeProvider sprites. Falls back to legacy flat-color rendering if
the corresponding PNG didn't extract."
```

---

## Task 5: Restyle action buttons via UIHelpers.MakeButton

**Files:**
- Modify: `WrathTactics/UI/UIHelpers.cs:106-113`

`MakeButton` is the funnel — every action button in `TacticsPanel`, `PresetPanel`, `RuleEditorWidget` flows through it. One change covers all three call sites.

- [ ] **Step 1: Update MakeButton to opt into themed visuals**

In `WrathTactics/UI/UIHelpers.cs`, replace the body of `MakeButton` (lines 106-113):

```csharp
        public static GameObject MakeButton(Transform parent, string name, string label, float fontSize,
            Color bgColor, UnityEngine.Events.UnityAction onClick) {
            var (btn, btnRect) = Create(name, parent);

            if (ThemeProvider.ActionButtonNormal != null) {
                ThemeProvider.ApplyActionButton(btn);
            } else {
                AddBackground(btn, bgColor);
                btn.AddComponent<Button>();
            }

            AddLabel(btn, label, fontSize, TextAlignmentOptions.Midline);

            // Re-fetch the Button — ApplyActionButton may have added it, or the else-branch did.
            var button = btn.GetComponent<Button>();
            button.onClick.AddListener(onClick);

            return btn;
        }
```

Key change: when a themed sprite is available, the legacy `AddBackground(btn, bgColor)` is skipped (sprite-tinting wouldn't combine with the bgColor cleanly). The label is added on top regardless. The caller's `bgColor` parameter becomes ignored when themed — preserved as a no-op for source compatibility with all existing call sites.

- [ ] **Step 2: Build**

```bash
cd /home/pascal/Code/wrath-mods/wrath-tactics
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
```

Expected: `Build succeeded. 0 Error(s)`. Compilation errors here would mean the existing `Button.AddComponent` returned-value pattern doesn't match — adjust accordingly.

- [ ] **Step 3: Deploy + visual smoke test**

```bash
./deploy.sh
```

In-game: open Tactics panel, verify "Add Rule", "Save Preset", and any other action buttons now show themed sprites with hover/pressed transitions. If a button looks wrong, the issue is most likely:
- Sprite border Vector4 wrong → fix in `ThemeProvider.Init()`
- Label color not contrasting against new sprite → keep caller's text color logic, separate fix

- [ ] **Step 4: Commit**

```bash
git add WrathTactics/UI/UIHelpers.cs
git commit -m "feat(ui): route MakeButton through ThemeProvider for themed visuals

Every action button created via UIHelpers.MakeButton now uses the themed
sprite set when ThemeProvider has loaded the PNGs. Caller's bgColor
parameter becomes a no-op when themed, preserved for source compat.
Falls back to flat-color background when sprites are missing."
```

---

## Task 6: Restyle tab headers in TacticsPanel

**Files:**
- Modify: `WrathTactics/UI/TacticsPanel.cs` (RebuildTabs and tab-button creation)

The tab strip selects companion / global rules. Each tab is a button with a label; the active tab needs a visually distinct sprite.

Reference: actual tab construction is in `WrathTactics/UI/TacticsPanel.cs:167-174` (`AddTab` method) and re-fired from `SelectTab` at line 185 (`RebuildTabs()`):

```csharp
void AddTab(GameObject parent, string label, string tabId, UnityAction onClick) {
    var (btn, _) = UIHelpers.Create($"Tab_{label}", parent.transform);
    bool isSelected = (tabId == null && selectedUnitId == null)
        || (tabId != null && tabId == selectedUnitId);
    UIHelpers.AddBackground(btn, isSelected ? TabSelected : TabNormal);   // line 171
    UIHelpers.AddLabel(btn, label, 16f, TextAlignmentOptions.Midline);
    btn.AddComponent<Button>().onClick.AddListener(onClick);
}
```

`SelectTab` already calls `RebuildTabs()` (line 185) which destroys all tabs and re-creates them via `AddTab` — so theming inside `AddTab` automatically refreshes on tab switch. No additional refresh code needed.

- [ ] **Step 1: Theme the tab background inside AddTab (line 171)**

Replace the `UIHelpers.AddBackground(...)` call at line 171 with the themed-or-fallback branch:

```csharp
        void AddTab(GameObject parent, string label, string tabId, UnityAction onClick) {
            var (btn, _) = UIHelpers.Create($"Tab_{label}", parent.transform);
            bool isSelected = (tabId == null && selectedUnitId == null)
                || (tabId != null && tabId == selectedUnitId);

            var themed = isSelected ? ThemeProvider.TabHeaderActive : ThemeProvider.TabHeaderInactive;
            if (themed != null) {
                ThemeProvider.ApplyTabHeader(btn, isSelected);
            } else {
                UIHelpers.AddBackground(btn, isSelected ? TabSelected : TabNormal);
            }

            UIHelpers.AddLabel(btn, label, 16f, TextAlignmentOptions.Midline);
            btn.AddComponent<Button>().onClick.AddListener(onClick);
        }
```

The `themed != null` check ensures partial asset extraction (only one of active/inactive sprite present) falls back to the colour pair for both states — avoids a half-themed mix. `TabSelected` and `TabNormal` are existing Color constants on the class; leave them untouched.

- [ ] **Step 2: Build**

```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
```

- [ ] **Step 3: Deploy + visual smoke test**

```bash
./deploy.sh
```

In-game: open Tactics panel. Verify:
1. The currently-selected tab visibly stands out from the inactive ones (active sprite vs. inactive sprite).
2. Clicking another tab swaps the active highlight.
3. The label text is still readable on top of the tab sprite.

- [ ] **Step 4: Commit**

```bash
git add WrathTactics/UI/TacticsPanel.cs
git commit -m "feat(ui): theme companion tab headers with active/inactive sprites

AddTab now applies ThemeProvider.TabHeader{Active,Inactive} via
ApplyTabHeader; falls back to the existing TabSelected/TabNormal flat
colour pair when either sprite is missing. SelectTab's existing
RebuildTabs() call covers re-theming on tab switch."
```

---

## Task 7: Restyle scrollbars (best-effort)

**Files:**
- Modify: `WrathTactics/UI/TacticsPanel.cs` (rule-list ScrollRect), `WrathTactics/UI/PresetPanel.cs` (preset-list ScrollRect)

Scrollbar restyling is optional — if the existing UI doesn't manually create Scrollbar components (and instead relies on Unity's default ScrollRect-without-scrollbar), there's nothing to theme. Find out first.

- [ ] **Step 1: Locate scrollbar references**

```bash
grep -rn "Scrollbar\|scrollbar" /home/pascal/Code/wrath-mods/wrath-tactics/WrathTactics/UI/*.cs
```

If there are no `Scrollbar`-component creations, skip this task entirely — go to Step 5 (commit empty change as a no-op or skip the commit). Document the skip in the commit message at Task 8.

If there are `Scrollbar` references (the more likely case), each one is a candidate for theming.

- [ ] **Step 2: Theme each scrollbar found**

For each scrollbar GameObject, after it's added to a ScrollRect:

```csharp
            // Existing: var scrollbar = scrollbarObj.AddComponent<Scrollbar>();
            // Existing: scrollRect.verticalScrollbar = scrollbar;

            // Add:
            if (ThemeProvider.ScrollbarTrack != null) {
                var trackImg = scrollbarObj.GetComponent<Image>() ?? scrollbarObj.AddComponent<Image>();
                trackImg.sprite = ThemeProvider.ScrollbarTrack;
                trackImg.type = Image.Type.Sliced;
                trackImg.color = Color.white;
            }
            if (ThemeProvider.ScrollbarHandle != null && scrollbar.handleRect != null) {
                var handleImg = scrollbar.handleRect.GetComponent<Image>() ?? scrollbar.handleRect.gameObject.AddComponent<Image>();
                handleImg.sprite = ThemeProvider.ScrollbarHandle;
                handleImg.type = Image.Type.Sliced;
                handleImg.color = Color.white;
            }
```

- [ ] **Step 3: Build**

```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
```

- [ ] **Step 4: Deploy + smoke test**

```bash
./deploy.sh
```

In-game: scroll the rule list (long enough that a scrollbar appears). Verify the scrollbar uses the new sprites. If no scrollbar appears, the rule list isn't long enough — add several rules first, or change the panel size.

- [ ] **Step 5: Commit (or skip)**

If scrollbars were themed:

```bash
git add WrathTactics/UI/TacticsPanel.cs WrathTactics/UI/PresetPanel.cs
git commit -m "feat(ui): theme scrollbar track and handle sprites"
```

If no scrollbar references existed, skip the commit and add a note at the end of `Assets/icons/SOURCES.md`:

```markdown
## Unused sprites

`scrollbar_track.png` / `scrollbar_handle.png` are bundled but not yet
applied — current TacticsPanel uses ScrollRect without manual Scrollbar
components. Will be applied in a future iteration if scrollbars are added.
```

Then commit the SOURCES.md update:

```bash
git add WrathTactics/Assets/icons/SOURCES.md
git commit -m "docs(assets): note scrollbar sprites as currently unused"
```

---

## Task 8: Full smoke test, code review, release

**Files:** none (verification + release pipeline)

- [ ] **Step 1: Verify deploy and DLL mtime**

```bash
ssh deck-direct "stat '/run/media/deck/3b03f019-ee3d-473e-beb1-98236afc5254/steamapps/common/Pathfinder Second Adventure/Mods/WrathTactics/WrathTactics.dll'"
ls -l /home/pascal/Code/wrath-mods/wrath-tactics/WrathTactics/bin/Debug/WrathTactics.dll
```

Deck DLL mtime should be ≥ local DLL mtime. Per CLAUDE.md gotcha §"Verify deploy before diagnosing": if mismatched, re-run `./deploy.sh`.

- [ ] **Step 2: Run the full smoke test from the spec**

Per `docs/superpowers/specs/2026-05-06-ui-restyle-and-modmenu-design.md` §"Smoke test on Steam Deck after deploy":

1. Launch the game, load a save.
2. Confirm `Player.log` and `Mods/WrathTactics/Logs/wrath-tactics-*.log` have no `[ERROR]` entries.
3. Press Ctrl+T → panel opens with new background + title bar.
4. Hover close button → sprite swaps.
5. Click close button → panel closes.
6. Re-open, switch tabs between companions → active tab visually highlighted.
7. Click "Add Rule" → rule appears in list (themed action button).
8. Edit a condition (HP %, threshold).
9. Save Preset → preset appears in PresetPanel; reload save → preset persisted.
10. Trigger a combat → tactics still execute (engine path untouched, but verify no regression).

If any step fails, **stop and diagnose before proceeding**. Common issues:
- Sprite stretched weirdly → 9-slice border wrong in `ThemeProvider.Init()`.
- Label invisible on themed button → label color not adjusted; check `AddLabel` call site.
- Hover state doesn't swap → `Button.transition` not set to `SpriteSwap`; check `ThemeProvider.ApplyButton`.

- [ ] **Step 3: Run code review**

```bash
cd /home/pascal/Code/wrath-mods/wrath-tactics
```

In the Claude session, run `/review` (or invoke `superpowers:requesting-code-review` if available). Address any blocking findings before release.

- [ ] **Step 4: Run /release for v1.10.0**

```bash
# In Claude session:
/release minor
```

The `/release` skill handles: version bump (`Info.json`, `WrathTactics.csproj`, `Repository.json`), Release-config build, ZIP packaging, push, tag, GitHub release, Nexus upload via GitHub Action, Discord post generation. Per CLAUDE.md §"`/release` Pre-condition", do NOT manually pre-bump the version — `/release` does it.

Per `wrath-mods/CLAUDE.md` §"Release Process", the `/release` skill includes a confirmation gate before push — review the proposed changes there.

- [ ] **Step 5: Post-release verification**

After `/release` reports success:
1. Visit https://github.com/Gh05d/wrath-tactics/releases/tag/v1.10.0 — release exists, ZIP attached.
2. Visit https://www.nexusmods.com/pathfinderwrathoftherighteous/mods/1005 — new file version visible.
3. Watch Discord/Nexus comments for the next 1–2 weeks for asset-loading reports across GPU/driver configurations. Address any v1.10.x patches before starting Pillar B.

---

## Out-of-band items / known unknowns

- **Sprite border metrics** — the Vector4 borders in `ThemeProvider.Init()` are best-guess defaults. Final values come from the actual sprite dimensions exported in Task 1. If a 9-slice sprite stretches weirdly in-game, the border for that sprite is wrong; AssetStudio shows the original border in the sprite metadata, copy from there.
- **Substitution risks** — if Wrath 1.4 doesn't expose all twelve target sprites under the names guessed in Task 1, `SOURCES.md` documents the substitution. A substitution that visually clashes with the rest needs a follow-up patch — not blocking for v1.10.0 if the panel is still functional.
- **DXT5 texture format** — BuffIt's loader uses DXT5; `Texture2D.LoadImage` ignores the constructor format and reads from PNG header. Kept for parity with BuffIt; revisit if any device renders sprites with banding.

---

## Spec coverage check (self-review)

| Spec section | Plan task |
|---|---|
| Architecture: Pillar A | Tasks 1–7 (all touch points) |
| Components: AssetLoader | Task 2 |
| Components: ThemeProvider | Task 3 |
| Components: PNG bundle | Task 1 |
| Components: csproj embed | Task 1, Step 3 |
| Components: Main.cs init | Tasks 2, 3 |
| Components: TacticsPanel modifications | Tasks 4, 6 |
| Components: UIHelpers.MakeButton | Task 5 |
| Components: PresetPanel / RuleEditorWidget | Task 5 (inherited via MakeButton); Task 7 (PresetPanel scrollbar) |
| Asset Bundle: 12 PNGs + 9-slice + sources | Task 1 |
| Migration: v1.10.0 release plan | Task 8 |
| Error Handling: null-sprite no-op | Task 3 (ApplyPanel/ApplyButton) |
| Error Handling: missing-PNG warn-once log | Task 2 (AssetLoader cache + log) |
| Testing: dotnet build green per task | After every code task |
| Testing: smoke test list | Task 8, Step 2 |
| Testing: code review | Task 8, Step 3 |
| Out-of-scope: Pickers, dropdowns, HUD button, ConditionRowWidget | Honoured — none of these files appear in any task |
| Out-of-scope: ModMenu integration | Pillar B is a separate plan; not in this file |
