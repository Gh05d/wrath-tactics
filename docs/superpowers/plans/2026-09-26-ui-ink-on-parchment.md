# UI Ink-on-Parchment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the panel's mixed dark-admin / Owlcat look with one ink-on-parchment system built from Owlcat's own sprites, driven by central colour/metric tokens and role-named widget factories.

**Architecture:** `ThemeProvider` gains 17 new game sprites (bands, paper popup, annotation, icons, dividers). A new `Theme` static class holds every colour and every FontScale-multiplied metric. A new `Widgets` static class builds every control by role (band header, band dropdown, icon button, inline link, checkbox, ink input, hint card, paper popup). Existing widgets are migrated file by file onto those factories; condition/action/target rows move from fractional anchors to `HorizontalLayoutGroup` with proportional `flexibleWidth`. No behaviour, callback, persistence route, or enum index changes.

**Tech Stack:** C# / .NET Framework 4.8.1, Unity uGUI + TextMeshPro (game's Unity 2020.x), UnityModManager, UnityPy (Python venv at `~/.local/opt/unitypy-venv`) for sprite extraction. Build: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/` from the repo root. Deploy: `./deploy.sh`.

**Spec:** `docs/superpowers/specs/2026-09-26-ui-ink-on-parchment-design.md` (mockups in `docs/superpowers/specs/2026-09-26-ui-mockups/`).

## Global Constraints

- Every `new Color(...)` in `WrathTactics/UI/` must end up in `Theme.cs` or `PackPalette.cs` only (spec §3.4). `PackPalette.Colors` is append-only; do not touch it.
- Every pixel height/width for text rows multiplies `UIHelpers.FontScale` through a `Theme` metric (spec §3.5). Band text never below `14f` base size.
- Widgets keep calling `onChanged?.Invoke()` / `PersistEdit()`; nothing new calls `ConfigManager.Save()` (CLAUDE.md top gotcha).
- Inputs keep going through `UIHelpers.CreateTMPInputField` (ManualInputCaret, `onFocusSelectAll = false`).
- Every `Apply*` sprite helper is a no-op when its sprite is null; the flat fallback is a `Theme` token.
- No new localisation keys. `button.add_or_group` / `button.unlink_edit` wording is untouched.
- `PortraitToggleBadge` / `PortraitToggleOverlay` change only by dropping the outline copy (spec §4.5).
- `catch (Exception)` only in per-frame guards, persistence, static sprite init (CLAUDE.md).
- K&R braces, 4-space indent, `var` when apparent.
- Build check in chains: `OUT=$(~/.dotnet/dotnet build … 2>&1); echo "$OUT" | grep -q ' error ' && exit 1` (a plain grep exits 0 on errors).
- Commit after every task; conventional messages; trailer `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.

## Review Focus

The UI has no unit coverage; these are the play-test checks the reviewer must see evidence for (Deck screenshot or explicit "verified on Deck" from the human partner):

1. **Font scale 0.5 and 2.0** (Settings → Font size): band text must not clip at 0.5, rows must not overlap at 2.0. Owned by Task 8 (card height) and Task 3 (chrome) — each carries a "verify at 0.5 / 2.0" step.
2. **A rule with two OR groups, one fallback, and cooldown** must fit its card and the inner ScrollRect must only enable above `Theme.MaxCardHeight`. Owned by Task 8.
3. **Sprite missing on disk** (`Assets/icons/*.png` deleted or PNG corrupt): every factory must still render (flat fallback) and log one warning, never throw during `CreatePanel`. Owned by Task 2 (fallbacks in every `Apply*`).
4. **Buff picker with 300+ entries** scrolls on the paper popup and the search input still focuses next frame. Owned by Task 10.
5. **Pack-tinted header** (`Theme.PackBandTint`) must stay readable for all six palette entries — the dark palette colours would black out the band if multiplied directly. Owned by Task 2 (tint normalisation) and verified in Task 5.

---

### Task 1: Bundle the new Owlcat sprites

**Files:**
- Create: `tools/extract_sprites.py`
- Create: 17 PNGs in `WrathTactics/Assets/icons/` (names below)
- Modify: `WrathTactics/Assets/icons/SOURCES.md`
- Modify: `WrathTactics/UI/ThemeProvider.cs` (properties, `Init`, `all` array)

**Interfaces:**
- Produces: `ThemeProvider.BandMauve`, `BandBlue`, `PopupPaper`, `HintAnnotation`, `IconAdd`, `IconDelete`, `IconX`, `IconXHover`, `IconCheck`, `IconCheckHover`, `IconArrow`, `IconArrowHover`, `ToggleOn`, `ToggleOff`, `DividerFlourish`, `DividerLine`, `InputFrame` (all `Sprite`, may be null).

- [ ] **Step 1: Copy the game asset files from the Deck into the scratchpad** (skip if `sharedassets0.assets` + `sharedassets0.assets.resS` are already there from the brainstorm session)

```bash
S=/tmp/claude-1000/-home-pascal-Code-wrath-mods-wrath-tactics/*/scratchpad   # session scratchpad
G="/run/media/deck/3b03f019-ee3d-473e-beb1-98236afc5254/steamapps/common/Pathfinder Second Adventure/Wrath_Data"
scp "deck-direct:$G/sharedassets0.assets" "deck-direct:$G/sharedassets0.assets.resS" $S/
```

- [ ] **Step 2: Write the extraction script**

```python
#!/usr/bin/env python3
"""Extract the UI sprites Wrath Tactics bundles from the game's sharedassets0.assets.

Usage: ~/.local/opt/unitypy-venv/bin/python tools/extract_sprites.py <dir-with-sharedassets0.assets> [out-dir]
The .resS companion must sit next to the .assets file. Default out-dir: WrathTactics/Assets/icons.
Prints the Unity 9-slice border (left, bottom, right, top) of every sprite so ThemeProvider
can be checked against the game build.
"""
import sys, os, UnityPy

# target file name -> game sprite name
WANTED = {
    "band_mauve.png":        "UI_Settings_BackValue",
    "band_blue.png":         "UI_Settings_BackValueBlue",
    "popup_paper.png":       "UI_BackgroundTooltipPaper",
    "hint_annotation.png":   "UI_Journal_Annotation",
    "icon_add.png":          "UI_CharScreen_IconAdd",
    "icon_delete.png":       "UI_CharScreen_IconDelete",
    "icon_x.png":            "UI_EscIcon_Default",
    "icon_x_hover.png":      "UI_EscIcon_Hover",
    "icon_check.png":        "UI_CheckIcon_Default",
    "icon_check_hover.png":  "UI_CheckIcon_Hover",
    "icon_arrow.png":        "UI_RoundButtonNextIcon_Default",
    "icon_arrow_hover.png":  "UI_RoundButtonNextIcon_Hover",
    "toggle_on.png":         "UI_PointButtonBig_Hover",
    "toggle_off.png":        "UI_PointButtonBig_Default",
    "divider_flourish.png":  "UI_CharScreen_Separator2",
    "divider_line.png":      "UI_WightLine_Simple",
    "input_frame.png":       "UI_Loot_Slots",
}

def main():
    src_dir = sys.argv[1]
    out_dir = sys.argv[2] if len(sys.argv) > 2 else os.path.join(
        os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "WrathTactics", "Assets", "icons")
    env = UnityPy.load(os.path.join(src_dir, "sharedassets0.assets"))
    by_name = {v: k for k, v in WANTED.items()}
    done = {}
    for obj in env.objects:
        if obj.type.name != "Sprite":
            continue
        d = obj.read()
        target = by_name.get(d.m_Name)
        if target is None or target in done:
            continue
        d.image.save(os.path.join(out_dir, target))
        b = d.m_Border
        done[target] = (d.m_Rect.width, d.m_Rect.height, int(b.x), int(b.y), int(b.z), int(b.w))
        print(f"{target:24s} {d.m_Name:34s} {int(d.m_Rect.width)}x{int(d.m_Rect.height)}  border L,B,R,T = {int(b.x)},{int(b.y)},{int(b.z)},{int(b.w)}")
    missing = set(WANTED) - set(done)
    if missing:
        print("MISSING:", ", ".join(sorted(missing)), file=sys.stderr)
        sys.exit(1)

if __name__ == "__main__":
    main()
```

- [ ] **Step 3: Run it and check the output**

Run: `~/.local/opt/unitypy-venv/bin/python tools/extract_sprites.py "$S"` (scratchpad dir from Step 1)
Expected: 17 lines, no `MISSING`, and these borders (any deviation means the game build changed — update the `Load` lines in Step 5 to the printed values):

```
band_mauve.png       391x63   112,0,112,0
band_blue.png        391x63   112,0,112,0
popup_paper.png      554x358  139,101,145,89
hint_annotation.png  725x285  72,58,72,67
divider_flourish.png 1041x46  114,0,94,0
divider_line.png     586x7    16,0,16,0
input_frame.png      135x135  12,11,13,13
(all icon_*/toggle_* : 0,0,0,0)
```

Then: `du -ch WrathTactics/Assets/icons/*.png | tail -1` — expect roughly 3.6 MB total (was ~3.2 MB).

- [ ] **Step 4: Append the rows to `WrathTactics/Assets/icons/SOURCES.md`** (table section, after the `hud_button_hover.png` row)

```markdown
| band_mauve.png | `UI_Settings_BackValue` (391×63, border 112/0/112/0) | Brush-stroke value background of the Settings menu. Rule-card headers, dropdown triggers, selected popup rows. |
| band_blue.png | `UI_Settings_BackValueBlue` (391×63, border 112/0/112/0) | Blue variant: headers of preset-linked rules. |
| popup_paper.png | `UI_BackgroundTooltipPaper` (554×358, border 139/101/145/89) | Tooltip parchment sheet — every popup (dropdown list, pickers, save-as-pack). |
| hint_annotation.png | `UI_Journal_Annotation` (725×285, border 72/58/72/67) | Journal annotation box — hint cards. Drawn over `Theme.HintBacking`, its own fill is too transparent on parchment. |
| icon_add.png | `UI_CharScreen_IconAdd` (60×62) | Green plus — prefix of "+ Condition" style inline links. |
| icon_delete.png | `UI_CharScreen_IconDelete` (56×61) | Red brush X — delete rule / preset / pack. |
| icon_x.png / icon_x_hover.png | `UI_EscIcon_Default` / `_Hover` (49×49) | Ink X — delete condition / fallback, close popup, clear filter. |
| icon_check.png / icon_check_hover.png | `UI_CheckIcon_Default` / `_Hover` (40×43) | Chevron — dropdown arrow (Default, scaled), checkbox tick (Hover). |
| icon_arrow.png / icon_arrow_hover.png | `UI_RoundButtonNextIcon_Default` / `_Hover` (48×44) | Triangle — move up/down (rotated ∓90°). |
| toggle_on.png / toggle_off.png | `UI_PointButtonBig_Hover` / `_Default` (40×40) | Round point button — rule ON / OFF dot. |
| divider_flourish.png | `UI_CharScreen_Separator2` (1041×46, border 114/0/94/0) | Line with flourish ends — section dividers. |
| divider_line.png | `UI_WightLine_Simple` (586×7, border 16/0/16/0) | Thin line — OR divider. |
| input_frame.png | `UI_Loot_Slots` (135×135, border 12/11/13/13) | Sketched frame — text/number inputs (fallback: 1 px ink frame, see `Widgets.AddInkFrame`). |
```

Also replace the "Re-extraction procedure" paragraph's `/tmp/wrath-assets/extract.py` reference with `tools/extract_sprites.py <dir>`.

- [ ] **Step 5: Add the sprites to `ThemeProvider`**

In `WrathTactics/UI/ThemeProvider.cs`, after the `HudButtonHover` property add:

```csharp
        public static Sprite BandMauve { get; private set; }
        public static Sprite BandBlue { get; private set; }
        public static Sprite PopupPaper { get; private set; }
        public static Sprite HintAnnotation { get; private set; }
        public static Sprite IconAdd { get; private set; }
        public static Sprite IconDelete { get; private set; }
        public static Sprite IconX { get; private set; }
        public static Sprite IconXHover { get; private set; }
        public static Sprite IconCheck { get; private set; }
        public static Sprite IconCheckHover { get; private set; }
        public static Sprite IconArrow { get; private set; }
        public static Sprite IconArrowHover { get; private set; }
        public static Sprite ToggleOn { get; private set; }
        public static Sprite ToggleOff { get; private set; }
        public static Sprite DividerFlourish { get; private set; }
        public static Sprite DividerLine { get; private set; }
        public static Sprite InputFrame { get; private set; }
```

In `Init()`, after the `HudButtonHover = Load(...)` line:

```csharp
            // Ink-on-parchment set (spec 2026-09-26). Borders from tools/extract_sprites.py.
            BandMauve       = Load("band_mauve.png",       new Vector4(112,   0, 112,  0));
            BandBlue        = Load("band_blue.png",        new Vector4(112,   0, 112,  0));
            PopupPaper      = Load("popup_paper.png",      new Vector4(139, 101, 145, 89));
            HintAnnotation  = Load("hint_annotation.png",  new Vector4( 72,  58,  72, 67));
            IconAdd         = Load("icon_add.png",         Vector4.zero);
            IconDelete      = Load("icon_delete.png",      Vector4.zero);
            IconX           = Load("icon_x.png",           Vector4.zero);
            IconXHover      = Load("icon_x_hover.png",     Vector4.zero);
            IconCheck       = Load("icon_check.png",       Vector4.zero);
            IconCheckHover  = Load("icon_check_hover.png", Vector4.zero);
            IconArrow       = Load("icon_arrow.png",       Vector4.zero);
            IconArrowHover  = Load("icon_arrow_hover.png", Vector4.zero);
            ToggleOn        = Load("toggle_on.png",        Vector4.zero);
            ToggleOff       = Load("toggle_off.png",       Vector4.zero);
            DividerFlourish = Load("divider_flourish.png", new Vector4(114,   0,  94,  0));
            DividerLine     = Load("divider_line.png",     new Vector4( 16,   0,  16,  0));
            InputFrame      = Load("input_frame.png",      new Vector4( 12,  11,  13, 13));
```

Extend the `all` array with the 17 new names so the log line counts them (expected `32/32`).

- [ ] **Step 6: Build, deploy, verify the log**

```bash
cd /home/pascal/Code/wrath-mods/wrath-tactics
OUT=$(~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/ 2>&1); echo "$OUT" | grep -q ' error ' && { echo "$OUT" | grep ' error '; exit 1; }
./deploy.sh
```

Then have the panel opened once on the Deck (Ctrl+T) and check:

```bash
ssh deck-direct "grep -h 'ThemeProvider initialised' \"\$(ls -t '/run/media/deck/3b03f019-ee3d-473e-beb1-98236afc5254/steamapps/common/Pathfinder Second Adventure/Mods/WrathTactics/Logs/'wrath-tactics-*.log | head -1)\" | tail -1"
```

Expected: `ThemeProvider initialised — 32/32 sprites loaded.` (`deploy.sh` copies only DLL + Info.json — if the count is 15/32, copy the icons with `tar -C WrathTactics -cf - Assets | ssh deck-direct "tar -xf - -C '<game>/Mods/WrathTactics'"` and re-check.)

- [ ] **Step 7: Commit**

```bash
git add tools/extract_sprites.py WrathTactics/Assets/icons WrathTactics/UI/ThemeProvider.cs
git commit -m "feat(ui): bundle Owlcat band, paper, annotation and icon sprites

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 2: `Theme` tokens and `Widgets` factories

**Files:**
- Create: `WrathTactics/UI/Theme.cs`
- Create: `WrathTactics/UI/Widgets.cs`

**Interfaces:**
- Consumes: `ThemeProvider.*` sprites (Task 1), `UIHelpers.Create/AddLabel/AddBackground/FontScale`, `PackPalette.ColorAt`.
- Produces (used by every later task):
  - `Theme` colours: `Ink, InkMuted, InkLabel, InkFrame, InsetPaper, CardFill, BandText, HintBacking, HintText, StatusOk, StatusWarn, StatusError, StatusMuted, DimPopup, DimBackdrop, BandFallbackMauve, BandFallbackBlue, PaperFallback`; `Color PackBandTint(int colorIndex)`.
  - `Theme` metrics (all `× FontScale`): `RowHeight, HeaderHeight, CollapsedBodyHeight, InlineRowHeight, DividerHeight, IconSmall, IconMedium, BandPaddingX, RowGap, CardGap, MinCardHeight, MaxCardHeight, PopupWidth, PopupHeight, PopupListWidth, PopupListMaxHeight, PopupRowHeight, CardPadding`.
  - `Widgets`: `enum BandStyle { Mauve, Blue }`, `enum Icon { Add, Delete, X, ArrowUp, ArrowDown, Chevron, Check }`, `enum Status { Ok, Warn, Error, Muted }` and the methods listed in the code below.

- [ ] **Step 1: Write `Theme.cs`**

```csharp
using UnityEngine;

namespace WrathTactics.UI {
    /// <summary>
    /// Single source of colours and metrics for the panel (spec 2026-09-26 §3.4/§3.5).
    /// Metrics are properties so they follow UIHelpers.FontScale at access time; the panel
    /// is rebuilt on a font-scale change (TacticsPanel.Toggle), so nothing caches them.
    /// No other file under UI/ may construct a Color except PackPalette.
    /// </summary>
    static class Theme {
        static Color Rgb(int r, int g, int b, float a = 1f) => new Color(r / 255f, g / 255f, b / 255f, a);

        // ---- ink and paper ----
        public static readonly Color Ink        = Rgb(0x2B, 0x1D, 0x12);
        public static readonly Color InkMuted   = Rgb(0x6B, 0x4D, 0x33);
        public static readonly Color InkLabel   = Rgb(0x4A, 0x32, 0x20);
        public static readonly Color InkFrame   = Rgb(0x3C, 0x28, 0x14, 0.55f);
        public static readonly Color InsetPaper = Rgb(0xFF, 0xFA, 0xEE, 0.55f);
        public static readonly Color CardFill   = Rgb(0xFF, 0xFA, 0xF0, 0.18f);
        public static readonly Color ListRowFill = Rgb(0xFF, 0xFA, 0xEE, 0.35f);
        public static readonly Color BandText   = Rgb(0xF3, 0xE9, 0xD8);
        public static readonly Color HintBacking = Rgb(0x30, 0x28, 0x19, 0.90f);
        public static readonly Color HintText   = Rgb(0xE9, 0xE2, 0xD0);

        // ---- status ----
        public static readonly Color StatusOk    = Rgb(0x3F, 0x6B, 0x3A);
        public static readonly Color StatusWarn  = Rgb(0x8A, 0x5A, 0x1A);
        public static readonly Color StatusError = Rgb(0x8A, 0x2A, 0x2A);
        public static readonly Color StatusMuted = InkMuted;

        // ---- overlays ----
        public static readonly Color DimPopup    = new Color(0f, 0f, 0f, 0.35f);
        public static readonly Color DimBackdrop = new Color(0f, 0f, 0f, 0.60f);

        // ---- flat fallbacks when a sprite failed to load ----
        public static readonly Color BandFallbackMauve = Rgb(0x6E, 0x4A, 0x6E);
        public static readonly Color BandFallbackBlue  = Rgb(0x4A, 0x55, 0x74);
        public static readonly Color PaperFallback     = Rgb(0xE5, 0xDC, 0xC8, 0.98f);
        public static readonly Color TitleFallback     = Rgb(0x33, 0x26, 0x1A);

        // ---- hover tint for ColorTint buttons ----
        public static readonly Color HoverTint   = new Color(0.85f, 0.85f, 0.85f, 1f);
        public static readonly Color PressedTint = new Color(0.65f, 0.65f, 0.65f, 1f);
        public static readonly Color DisabledTint = new Color(0.6f, 0.6f, 0.6f, 0.6f);

        /// <summary>
        /// Image.color multiplies the band sprite, so the dark PackPalette entries would
        /// black it out. Normalise the palette colour to a max channel of 1 — the hue
        /// survives, the brightness stays the band's own.
        /// </summary>
        public static Color PackBandTint(int colorIndex) {
            var c = PackPalette.ColorAt(colorIndex);
            float m = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            if (m <= 0f) return Color.white;
            return new Color(c.r / m, c.g / m, c.b / m, 1f);
        }

        // ---- metrics (base px × FontScale) ----
        static float S => UIHelpers.FontScale;
        public static float RowHeight           => 30f * S;
        public static float HeaderHeight        => 40f * S;
        public static float CollapsedBodyHeight => 30f * S;
        public static float InlineRowHeight     => 24f * S;
        public static float DividerHeight       => 14f * S;
        public static float IconSmall           => 18f * S;
        public static float IconMedium          => 24f * S;
        public static float BandPaddingX        => 14f * S;
        public static float RowGap              => 6f * S;
        public static float CardGap             => 12f * S;
        public static float CardPadding         => 8f * S;
        public static float MinCardHeight       => 160f * S;
        public static float MaxCardHeight       => 500f * S;
        public static float PopupWidth          => 480f * S;
        public static float PopupHeight         => 540f * S;
        public static float PopupListWidth      => 350f * S;
        public static float PopupListMaxHeight  => 400f * S;
        public static float PopupRowHeight      => 32f * S;
        public static float ControlRowHeight    => 36f * S;
        public static float FilterRowHeight     => 32f * S;
        public static float HintHeight          => 52f * S;
        public static float HintHeightShort     => 40f * S;
        public static float StatusHeight        => 24f * S;
        public static float SectionLabelHeight  => 20f * S;
        public static float PaperInset          => 34f * S;   // popup content inset past the paper's torn edge
    }
}
```

- [ ] **Step 2: Write `Widgets.cs`**

```csharp
using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace WrathTactics.UI {
    public enum BandStyle { Mauve, Blue }
    public enum Icon { Add, Delete, X, ArrowUp, ArrowDown, Chevron, Check }
    public enum Status { Ok, Warn, Error, Muted }

    /// <summary>
    /// Role-named factories for every control on the panel (spec 2026-09-26 §4.3).
    /// Together with Theme this is the only place under UI/ that touches colours or
    /// AddBackground. Every sprite path has a flat Theme fallback so a missing PNG
    /// degrades to a readable panel, never to an exception.
    /// </summary>
    static class Widgets {
        // ====================================================================
        // Surfaces
        // ====================================================================

        /// <summary>Sliced brush-band image on obj. Returns the Image (never null).</summary>
        public static Image ApplyBand(GameObject obj, BandStyle style, Color? tint = null) {
            var sprite = style == BandStyle.Blue ? ThemeProvider.BandBlue : ThemeProvider.BandMauve;
            var img = obj.GetComponent<Image>() ?? obj.AddComponent<Image>();
            img.raycastTarget = true;
            if (sprite != null) {
                img.sprite = sprite;
                img.type = Image.Type.Sliced;
                img.color = tint ?? Color.white;
            } else {
                var flat = style == BandStyle.Blue ? Theme.BandFallbackBlue : Theme.BandFallbackMauve;
                img.color = tint.HasValue ? flat * tint.Value : flat;
            }
            return img;
        }

        /// <summary>
        /// Full-width band row with an HLG inside (spacing RowGap, padding BandPaddingX).
        /// Children: add LayoutElements via InRow().
        /// </summary>
        public static GameObject BandRow(Transform parent, string name, BandStyle style, float height, Color? tint = null) {
            var (row, _) = UIHelpers.Create(name, parent);
            row.AddComponent<LayoutElement>().preferredHeight = height;
            ApplyBand(row, style, tint);
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = Theme.RowGap;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.padding = new RectOffset((int)Theme.BandPaddingX, (int)Theme.BandPaddingX, 4, 4);
            hlg.childAlignment = TextAnchor.MiddleLeft;
            return row;
        }

        /// <summary>Plain HLG row on the paper (no surface). Same spacing as BandRow.</summary>
        public static GameObject Row(Transform parent, string name, float height) {
            var (row, _) = UIHelpers.Create(name, parent);
            row.AddComponent<LayoutElement>().preferredHeight = height;
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = Theme.RowGap;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.padding = new RectOffset(0, 0, 2, 2);
            hlg.childAlignment = TextAnchor.MiddleLeft;
            return row;
        }

        /// <summary>LayoutElement for a child of an HLG/VLG row. Proportional widths via flexibleWidth.</summary>
        public static LayoutElement InRow(GameObject obj, float preferredWidth, float flexibleWidth) {
            var le = obj.GetComponent<LayoutElement>() ?? obj.AddComponent<LayoutElement>();
            le.preferredWidth = preferredWidth;
            le.flexibleWidth = flexibleWidth;
            return le;
        }

        /// <summary>Four 1 px ink edges. Children ignore layout so the host can sit in an HLG.</summary>
        public static void AddInkFrame(GameObject obj, float thickness = 1f) {
            void Edge(string name, Vector2 aMin, Vector2 aMax, Vector2 size) {
                var (e, r) = UIHelpers.Create(name, obj.transform);
                r.anchorMin = aMin; r.anchorMax = aMax; r.sizeDelta = size; r.anchoredPosition = Vector2.zero;
                r.pivot = new Vector2(aMin.x == aMax.x ? aMin.x : 0.5f, aMin.y == aMax.y ? aMin.y : 0.5f);
                var img = e.AddComponent<Image>();
                img.color = Theme.InkFrame;
                img.raycastTarget = false;
                e.AddComponent<LayoutElement>().ignoreLayout = true;
            }
            Edge("FrameTop",    new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, thickness));
            Edge("FrameBottom", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, thickness));
            Edge("FrameLeft",   new Vector2(0, 0), new Vector2(0, 1), new Vector2(thickness, 0));
            Edge("FrameRight",  new Vector2(1, 0), new Vector2(1, 1), new Vector2(thickness, 0));
        }

        /// <summary>Paper inset (inputs, checkbox box): InsetPaper fill + ink frame.</summary>
        public static Image AddInset(GameObject obj) {
            var img = UIHelpers.AddBackground(obj, Theme.InsetPaper);
            AddInkFrame(obj);
            return img;
        }

        /// <summary>Rule card / list card surface: faint fill + ink frame.</summary>
        public static Image ApplyCard(GameObject obj) {
            var img = obj.GetComponent<Image>() ?? obj.AddComponent<Image>();
            img.sprite = null;
            img.color = Theme.CardFill;
            img.raycastTarget = true;
            AddInkFrame(obj);
            return img;
        }

        // ====================================================================
        // Text
        // ====================================================================

        public static TextMeshProUGUI InkLabel(GameObject parent, string text, float fontSize,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, Color? color = null, bool italic = false) {
            var tmp = UIHelpers.AddLabel(parent, text, fontSize, align, color ?? Theme.Ink);
            if (italic) tmp.fontStyle |= FontStyles.Italic;
            return tmp;
        }

        public static TextMeshProUGUI BandLabel(GameObject parent, string text, float fontSize,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, bool italic = false) {
            var tmp = UIHelpers.AddLabel(parent, text, Mathf.Max(14f, fontSize), align, Theme.BandText);
            if (italic) tmp.fontStyle |= FontStyles.Italic;
            return tmp;
        }

        /// <summary>IF / THEN / TARGET / PACKS: small caps, letter-spaced, ink.</summary>
        public static TextMeshProUGUI SectionLabel(GameObject parent, string text, float fontSize = 15f) {
            var tmp = UIHelpers.AddLabel(parent, text, fontSize, TextAlignmentOptions.MidlineLeft, Theme.InkLabel);
            tmp.fontStyle = FontStyles.SmallCaps | FontStyles.Bold;
            tmp.characterSpacing = 8f;
            return tmp;
        }

        /// <summary>Full-width section label row (VLG child).</summary>
        public static GameObject SectionLabelRow(Transform parent, string name, string text) {
            var (obj, _) = UIHelpers.Create(name, parent);
            obj.AddComponent<LayoutElement>().preferredHeight = Theme.SectionLabelHeight;
            SectionLabel(obj, text);
            return obj;
        }

        public static Color StatusColor(Status kind) {
            switch (kind) {
                case Status.Ok: return Theme.StatusOk;
                case Status.Warn: return Theme.StatusWarn;
                case Status.Error: return Theme.StatusError;
                default: return Theme.StatusMuted;
            }
        }

        /// <summary>Status / empty-state line in ink, italic. Keeps the "Label" child name (PresetPanel.SetStatus finds it).</summary>
        public static TextMeshProUGUI StatusLabel(Transform parent, string name, string text, Color color,
            float preferredHeight, float fontSize = 13f) {
            var (obj, _) = UIHelpers.Create(name, parent);
            obj.AddComponent<LayoutElement>().preferredHeight = preferredHeight;
            var tmp = InkLabel(obj, text, fontSize, TextAlignmentOptions.MidlineLeft, color, italic: true);
            tmp.margin = new Vector4(6, 0, 6, 0);
            tmp.raycastTarget = true;   // wheel reaches the ScrollRect
            return tmp;
        }

        /// <summary>Journal-annotation hint card (replaces UIHelpers.AddHintCard).</summary>
        public static TextMeshProUGUI HintCard(Transform parent, string text, float preferredHeight) {
            var (obj, _) = UIHelpers.Create("Hint", parent);
            obj.AddComponent<LayoutElement>().preferredHeight = preferredHeight;
            UIHelpers.AddBackground(obj, Theme.HintBacking);
            if (ThemeProvider.HintAnnotation != null) {
                var (art, artRect) = UIHelpers.Create("Annotation", obj.transform);
                artRect.FillParent();
                var img = art.AddComponent<Image>();
                img.sprite = ThemeProvider.HintAnnotation;
                img.type = Image.Type.Sliced;
                img.color = Color.white;
                img.raycastTarget = false;
            }
            var tmp = UIHelpers.AddLabel(obj, text, 13f, TextAlignmentOptions.MidlineLeft, Theme.HintText);
            tmp.fontStyle |= FontStyles.Italic;
            tmp.enableWordWrapping = true;
            tmp.raycastTarget = true;
            tmp.margin = new Vector4(14, 6, 14, 6);
            return tmp;
        }

        // ====================================================================
        // Controls
        // ====================================================================

        static Sprite IconSprite(Icon icon, out Sprite hover, out bool rotateUp, out bool rotateDown) {
            hover = null; rotateUp = rotateDown = false;
            switch (icon) {
                case Icon.Add: return ThemeProvider.IconAdd;
                case Icon.Delete: return ThemeProvider.IconDelete;
                case Icon.X: hover = ThemeProvider.IconXHover; return ThemeProvider.IconX;
                case Icon.ArrowUp: hover = ThemeProvider.IconArrowHover; rotateUp = true; return ThemeProvider.IconArrow;
                case Icon.ArrowDown: hover = ThemeProvider.IconArrowHover; rotateDown = true; return ThemeProvider.IconArrow;
                case Icon.Chevron: return ThemeProvider.IconCheck;
                case Icon.Check: return ThemeProvider.IconCheckHover;
                default: return null;
            }
        }

        static string IconFallbackGlyph(Icon icon) {
            switch (icon) {
                case Icon.Add: return "+";
                case Icon.Delete: case Icon.X: return "✕";
                case Icon.ArrowUp: return "▲";
                case Icon.ArrowDown: return "▼";
                case Icon.Chevron: return "▾";
                case Icon.Check: return "✓";
                default: return "?";
            }
        }

        /// <summary>Non-interactive icon image sized `size`, aspect preserved. Falls back to an ink glyph.</summary>
        public static GameObject IconImage(Transform parent, string name, Icon icon, float size, Color? tint = null) {
            var (obj, rect) = UIHelpers.Create(name, parent);
            rect.sizeDelta = new Vector2(size, size);
            var le = obj.AddComponent<LayoutElement>();
            le.preferredWidth = size; le.preferredHeight = size; le.flexibleWidth = 0;
            var sprite = IconSprite(icon, out _, out bool up, out bool down);
            if (sprite != null) {
                var img = obj.AddComponent<Image>();
                img.sprite = sprite;
                img.preserveAspect = true;
                img.raycastTarget = false;
                img.color = tint ?? Color.white;
                if (up) rect.localRotation = Quaternion.Euler(0, 0, 90f);
                if (down) rect.localRotation = Quaternion.Euler(0, 0, -90f);
            } else {
                UIHelpers.AddLabel(obj, IconFallbackGlyph(icon), size * 0.7f / UIHelpers.FontScale,
                    TextAlignmentOptions.Midline, tint ?? Theme.Ink);
            }
            return obj;
        }

        /// <summary>Clickable icon (delete, X, arrows). SpriteSwap when a hover sprite exists, else ColorTint.</summary>
        public static GameObject IconButton(Transform parent, string name, Icon icon, float size, UnityAction onClick) {
            var (obj, rect) = UIHelpers.Create(name, parent);
            var le = obj.AddComponent<LayoutElement>();
            le.preferredWidth = size; le.preferredHeight = size; le.flexibleWidth = 0;
            var sprite = IconSprite(icon, out var hover, out bool up, out bool down);
            Image img;
            if (sprite != null) {
                img = obj.AddComponent<Image>();
                img.sprite = sprite;
                img.preserveAspect = true;
                img.raycastTarget = true;
                if (up) rect.localRotation = Quaternion.Euler(0, 0, 90f);
                if (down) rect.localRotation = Quaternion.Euler(0, 0, -90f);
            } else {
                img = UIHelpers.AddBackground(obj, Theme.InsetPaper);
                UIHelpers.AddLabel(obj, IconFallbackGlyph(icon), 14f, TextAlignmentOptions.Midline, Theme.Ink);
            }
            var btn = obj.AddComponent<Button>();
            btn.targetGraphic = img;
            if (hover != null) {
                btn.transition = Selectable.Transition.SpriteSwap;
                btn.spriteState = new SpriteState { highlightedSprite = hover, pressedSprite = hover, selectedSprite = sprite, disabledSprite = sprite };
            } else {
                ApplyColorTint(btn);
            }
            btn.onClick.AddListener(onClick);
            return obj;
        }

        public static void ApplyColorTint(Button btn) {
            btn.transition = Selectable.Transition.ColorTint;
            var c = btn.colors;
            c.normalColor = Color.white;
            c.highlightedColor = Theme.HoverTint;
            c.pressedColor = Theme.PressedTint;
            c.selectedColor = Color.white;
            c.disabledColor = Theme.DisabledTint;
            c.fadeDuration = 0.1f;
            btn.colors = c;
        }

        /// <summary>Round ON/OFF dot. Use SetToggleDot to flip without rebuilding.</summary>
        public static GameObject ToggleDot(Transform parent, string name, bool on, UnityAction onClick) {
            var (obj, _) = UIHelpers.Create(name, parent);
            float size = Theme.IconMedium;
            var le = obj.AddComponent<LayoutElement>();
            le.preferredWidth = size; le.preferredHeight = size; le.flexibleWidth = 0;
            var img = obj.AddComponent<Image>();
            img.preserveAspect = true;
            img.raycastTarget = true;
            var btn = obj.AddComponent<Button>();
            btn.targetGraphic = img;
            ApplyColorTint(btn);
            btn.onClick.AddListener(onClick);
            SetToggleDot(obj, on);
            return obj;
        }

        public static void SetToggleDot(GameObject dot, bool on) {
            var img = dot.GetComponent<Image>();
            if (img == null) return;
            var sprite = on ? ThemeProvider.ToggleOn : ThemeProvider.ToggleOff;
            if (sprite != null) {
                img.sprite = sprite;
                img.color = on ? Color.white : new Color(1f, 1f, 1f, 0.7f);
            } else {
                img.sprite = null;
                img.color = on ? Theme.StatusOk : Theme.InkFrame;
            }
        }

        /// <summary>
        /// Italic text link, optional icon prefix. Sizes itself from the text (the TMP sits on
        /// the root so an HLG parent reads its preferred width). onBand=true renders in BandText.
        /// </summary>
        public static GameObject InlineLink(Transform parent, string name, string text, UnityAction onClick,
            Icon? prefix = null, float fontSize = 14f, bool onBand = false) {
            var (root, _) = UIHelpers.Create(name, parent);
            var hlg = root.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.padding = new RectOffset(2, 4, 0, 0);
            InRow(root, 0f, 0f);
            if (prefix.HasValue) IconImage(root.transform, "Prefix", prefix.Value, Theme.IconSmall);

            var (txt, _t) = UIHelpers.Create("Text", root.transform);
            var tmp = txt.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize * UIHelpers.FontScale;
            tmp.fontStyle = FontStyles.Italic;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.color = onBand ? Theme.BandText : Theme.InkMuted;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.raycastTarget = true;

            var btn = root.AddComponent<Button>();
            btn.targetGraphic = tmp;
            ApplyColorTint(btn);
            btn.onClick.AddListener(onClick);
            return root;
        }

        /// <summary>Ink-framed box with a check tick + label. onChanged receives the new value.</summary>
        public static GameObject Checkbox(Transform parent, string name, string label, bool value, Action<bool> onChanged) {
            var row = Row(parent, name, Theme.InlineRowHeight);
            var (box, boxRect) = UIHelpers.Create("Box", row.transform);
            float size = Theme.IconSmall;
            InRow(box, size, 0f).preferredHeight = size;
            AddInset(box);
            var tick = IconImage(box.transform, "Tick", Icon.Check, size * 0.8f);
            tick.GetComponent<LayoutElement>().ignoreLayout = true;
            tick.Rect().anchorMin = new Vector2(0.5f, 0.5f);
            tick.Rect().anchorMax = new Vector2(0.5f, 0.5f);
            tick.Rect().anchoredPosition = Vector2.zero;
            tick.SetActive(value);

            var (lbl, _) = UIHelpers.Create("Label", row.transform);
            InRow(lbl, 200f, 1f);
            InkLabel(lbl, label, 14f);

            bool current = value;
            var btn = row.AddComponent<Button>();
            btn.targetGraphic = box.GetComponent<Image>();
            ApplyColorTint(btn);
            btn.onClick.AddListener(() => {
                current = !current;
                tick.SetActive(current);
                onChanged?.Invoke(current);
            });
            return row;
        }

        /// <summary>Owlcat action button (UI_Button_*) with band text — primary actions only.</summary>
        public static GameObject ActionButton(Transform parent, string name, string label, float fontSize,
            UnityAction onClick, float preferredWidth = 160f, float flexibleWidth = 0f) {
            var (btn, _) = UIHelpers.Create(name, parent);
            InRow(btn, preferredWidth, flexibleWidth);
            if (ThemeProvider.ActionButtonNormal != null) {
                ThemeProvider.ApplyActionButton(btn);
            } else {
                UIHelpers.AddBackground(btn, Theme.BandFallbackMauve);
                var b = btn.AddComponent<Button>();
                ApplyColorTint(b);
            }
            BandLabel(btn, label, fontSize, TextAlignmentOptions.Midline);
            btn.GetComponent<Button>().onClick.AddListener(onClick);
            return btn;
        }

        /// <summary>
        /// Band-styled dropdown trigger: [icon?] label ……… chevron. Used by PopupSelector and the
        /// spell/buff picker buttons. Caller adds the Button and its listener.
        /// </summary>
        public static GameObject BandDropdownShell(Transform parent, string name, string text, bool withIcon,
            out TextMeshProUGUI label, out Image icon) {
            var (obj, _) = UIHelpers.Create(name, parent);
            ApplyBand(obj, BandStyle.Mauve);

            icon = null;
            float leftMargin = Theme.BandPaddingX;
            if (withIcon) {
                var (iconGO, iconRect) = UIHelpers.Create("Icon", obj.transform);
                float s = Theme.IconMedium;
                iconRect.anchorMin = new Vector2(0, 0.5f);
                iconRect.anchorMax = new Vector2(0, 0.5f);
                iconRect.pivot = new Vector2(0, 0.5f);
                iconRect.anchoredPosition = new Vector2(Theme.BandPaddingX * 0.6f, 0);
                iconRect.sizeDelta = new Vector2(s, s);
                icon = iconGO.AddComponent<Image>();
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                leftMargin = Theme.BandPaddingX * 0.6f + s + 6f;
            }

            label = BandLabel(obj, text, 15f);
            label.margin = new Vector4(leftMargin, 0f, Theme.BandPaddingX + Theme.IconSmall, 0f);

            var chev = IconImage(obj.transform, "Chevron", Icon.Chevron, Theme.IconSmall * 0.65f, Theme.BandText);
            chev.GetComponent<LayoutElement>().ignoreLayout = true;
            var cr = chev.Rect();
            cr.anchorMin = new Vector2(1, 0.5f);
            cr.anchorMax = new Vector2(1, 0.5f);
            cr.pivot = new Vector2(1, 0.5f);
            cr.anchoredPosition = new Vector2(-Theme.BandPaddingX * 0.7f, 0);
            return obj;
        }

        /// <summary>Thin line — "or" — thin line.</summary>
        public static GameObject OrDivider(Transform parent, string text) {
            var row = Row(parent, "OrDivider", Theme.DividerHeight);
            GameObject Line(string n) {
                var (l, _) = UIHelpers.Create(n, row.transform);
                InRow(l, 0f, 1f).preferredHeight = 4f;
                var img = l.AddComponent<Image>();
                img.raycastTarget = false;
                if (ThemeProvider.DividerLine != null) {
                    img.sprite = ThemeProvider.DividerLine;
                    img.type = Image.Type.Sliced;
                    img.color = new Color(1f, 1f, 1f, 0.7f);
                } else {
                    img.color = Theme.InkFrame;
                }
                return l;
            }
            Line("LineL");
            var (lbl, _) = UIHelpers.Create("Text", row.transform);
            InRow(lbl, 30f, 0f);
            InkLabel(lbl, text, 13f, TextAlignmentOptions.Midline, Theme.InkMuted, italic: true);
            Line("LineR");
            return row;
        }

        /// <summary>Flourish separator (between top controls and list, between IF and THEN).</summary>
        public static GameObject FlourishDivider(Transform parent) {
            var (obj, _) = UIHelpers.Create("Flourish", parent);
            obj.AddComponent<LayoutElement>().preferredHeight = Theme.DividerHeight;
            var img = obj.AddComponent<Image>();
            img.raycastTarget = false;
            if (ThemeProvider.DividerFlourish != null) {
                img.sprite = ThemeProvider.DividerFlourish;
                img.type = Image.Type.Sliced;
                img.color = new Color(1f, 1f, 1f, 0.55f);
            } else {
                img.color = new Color(0, 0, 0, 0);
            }
            return obj;
        }

        /// <summary>Popup list row: faint paper fill (so hover tints), or a band when selected.</summary>
        public static GameObject ListRow(Transform parent, string name, float height, bool selected) {
            var (row, _) = UIHelpers.Create(name, parent);
            row.AddComponent<LayoutElement>().preferredHeight = height;
            if (selected) ApplyBand(row, BandStyle.Mauve);
            else UIHelpers.AddBackground(row, Theme.ListRowFill);
            return row;
        }

        public static Color ListRowTextColor(bool selected) => selected ? Theme.BandText : Theme.Ink;

        // ====================================================================
        // Popups
        // ====================================================================

        public struct PaperPopupParts {
            public GameObject Overlay;   // full-screen dim, carries a Button (outside-click)
            public GameObject Popup;     // paper sheet, swallows clicks
            public GameObject Content;   // inset rect for the caller's layout
            public Button OverlayButton;
        }

        /// <summary>
        /// Dim + centred paper sheet + inset content rect. The caller attaches the outside-click
        /// handler to OverlayButton and its own Escape handling. Title is optional (null skips it).
        /// </summary>
        public static PaperPopupParts PaperPopup(string name, float width, float height, string title, UnityAction onClose) {
            var canvas = Kingmaker.Game.Instance.UI.Canvas.transform;
            var (overlay, overlayRect) = UIHelpers.Create(name, canvas);
            overlayRect.FillParent();
            UIHelpers.AddBackground(overlay, Theme.DimPopup);
            var overlayBtn = overlay.AddComponent<Button>();

            var (popup, popupRect) = UIHelpers.Create("Popup", overlay.transform);
            popupRect.anchorMin = new Vector2(0.5f, 0.5f);
            popupRect.anchorMax = new Vector2(0.5f, 0.5f);
            popupRect.pivot = new Vector2(0.5f, 0.5f);
            popupRect.anchoredPosition = Vector2.zero;
            popupRect.sizeDelta = new Vector2(width, height);
            var paper = popup.AddComponent<Image>();
            paper.raycastTarget = true;
            if (ThemeProvider.PopupPaper != null) {
                paper.sprite = ThemeProvider.PopupPaper;
                paper.type = Image.Type.Sliced;
                paper.color = Color.white;
            } else {
                paper.color = Theme.PaperFallback;
            }
            var swallow = popup.AddComponent<Button>();
            swallow.targetGraphic = paper;
            swallow.transition = Selectable.Transition.None;

            var (content, contentRect) = UIHelpers.Create("Content", popup.transform);
            contentRect.FillParent();
            float inset = Theme.PaperInset;
            contentRect.offsetMin = new Vector2(inset, inset);
            contentRect.offsetMax = new Vector2(-inset, -inset);

            if (title != null) {
                var (titleRow, titleRect) = UIHelpers.Create("Title", content.transform);
                titleRect.anchorMin = new Vector2(0, 1);
                titleRect.anchorMax = new Vector2(1, 1);
                titleRect.pivot = new Vector2(0.5f, 1);
                titleRect.sizeDelta = new Vector2(0, Theme.RowHeight);
                titleRect.anchoredPosition = Vector2.zero;
                var t = InkLabel(titleRow, title, 18f);
                t.fontStyle = FontStyles.Bold;
                t.margin = new Vector4(0, 0, Theme.IconSmall + 8f, 0);
                var close = IconButton(titleRow.transform, "Close", Icon.X, Theme.IconSmall, onClose);
                var closeRect = close.Rect();
                closeRect.anchorMin = new Vector2(1, 0.5f);
                closeRect.anchorMax = new Vector2(1, 0.5f);
                closeRect.pivot = new Vector2(1, 0.5f);
                closeRect.anchoredPosition = Vector2.zero;
                closeRect.sizeDelta = new Vector2(Theme.IconSmall, Theme.IconSmall);
            }

            return new PaperPopupParts { Overlay = overlay, Popup = popup, Content = content, OverlayButton = overlayBtn };
        }
    }
}
```

- [ ] **Step 3: Build**

Run the build command from the Global Constraints. Expected: `Build succeeded`, no ` error `. (Warnings about `findstr` and NU1900 are harmless.)

- [ ] **Step 4: Commit**

```bash
git add WrathTactics/UI/Theme.cs WrathTactics/UI/Widgets.cs
git commit -m "feat(ui): Theme tokens and role-named Widgets factories

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 3: Panel chrome — backdrop, page sheet, control and filter rows, title

**Files:**
- Modify: `WrathTactics/UI/TacticsPanel.cs` — `CreatePanel` (:112-204), `AddTab` (:232-252), `CreateControlRow` (:282-320), `CreateFilterStrip` (:322-353), `CreateRuleFilterEmptyLabel` (:376-389), `UpdateToggleLabel` (:945-965)
- Modify: `WrathTactics/UI/UIHelpers.cs` — `CreateTMPInputField` (:205-274) look, plus a new `CreateTMPInputFieldInRow` overload

**Interfaces:**
- Consumes: `Theme`, `Widgets.Checkbox/ActionButton/AddInset/IconButton/InkLabel/StatusLabel/InRow`.
- Produces: `UIHelpers.CreateTMPInputFieldInRow(GameObject parent, string name, float preferredWidth, float flexibleWidth, string initialText, float fontSize = 16f, TMP_InputField.ContentType contentType = Standard, string placeholderText = null)`; field `TacticsPanel.toggleCheckbox` (GameObject) replacing `toggleLabel` usage.

- [ ] **Step 1: Restyle `CreateTMPInputField` and add the row overload** (UIHelpers.cs)

Replace `AddBackground(obj, new Color(0.12f, 0.12f, 0.12f, 1f));` with `Widgets.AddInset(obj);`. Set `textTmp.color = Theme.Ink;` and `phTmp.color = Theme.InkMuted;` (placeholder gets `phTmp.fontStyle = FontStyles.Italic;`). In `ManualInputCaret.Start()` change `caretText.color = Color.white;` to `caretText.color = Theme.Ink;`. Then add after `CreateTMPInputField`:

```csharp
        /// <summary>Layout-group flavour of CreateTMPInputField: sized by LayoutElement, not anchors.</summary>
        public static TMP_InputField CreateTMPInputFieldInRow(GameObject parent, string name,
            float preferredWidth, float flexibleWidth, string initialText, float fontSize = 16f,
            TMP_InputField.ContentType contentType = TMP_InputField.ContentType.Standard,
            string placeholderText = null) {
            var field = CreateTMPInputField(parent, name, 0, 1, initialText, fontSize, contentType, placeholderText);
            Widgets.InRow(field.gameObject, preferredWidth, flexibleWidth);
            return field;
        }
```

- [ ] **Step 2: Backdrop, page sheet and title** (`CreatePanel`)

Replace `UIHelpers.AddBackground(root, new Color(0, 0, 0, 1f));` with `UIHelpers.AddBackground(root, Theme.DimBackdrop);` and update the comment ("dims the game like Owlcat's own modals; the book letterbox shows the world through"). Replace the book fallback colour with `Theme.PaperFallback`, the title-bar fallback with `Theme.TitleFallback`, the close fallback with `Theme.BandFallbackMauve`.

Title label: replace the three lines (`AddLabel` + `outlineWidth` + `outlineColor`) with

```csharp
            Widgets.InkLabel(titleBar, "panel.title".i18n(), 26f, TextAlignmentOptions.Midline);
```

Close button: only add the `"X"` label in the fallback branch (the Esc sprite draws its own X):

```csharp
            if (ThemeProvider.CloseButtonNormal != null) {
                ThemeProvider.ApplyCloseButton(closeBtn);
            } else {
                UIHelpers.AddBackground(closeBtn, Theme.BandFallbackMauve);
                closeBtn.AddComponent<Button>();
                Widgets.BandLabel(closeBtn, "X", 22f, TextAlignmentOptions.Midline);
            }
```

Page sheet — insert right after `RebuildTabs();` and before `CreateControlRow`, so it draws under everything below the tab bar:

```csharp
            // One continuous parchment sheet under controls, filter and list: nothing below
            // the tab bar is drawn on the book illustration itself (spec §3.1).
            var (sheet, sheetRect) = UIHelpers.Create("PageSheet", bookContent.transform);
            sheetRect.SetAnchor(0.005, 0.995, 0.01, 0.835);
            sheetRect.sizeDelta = Vector2.zero;
            if (ThemeProvider.InnerParchment != null) {
                ThemeProvider.ApplyInnerParchment(sheet);
            } else {
                UIHelpers.AddBackground(sheet, Theme.PaperFallback);
            }
```

Delete the `UIHelpers.EnsureAllHoverable(panelRoot);` call at the end of `CreatePanel` (every factory wires its own hover now).

- [ ] **Step 3: Tabs** — in `AddTab` replace the fallback line with `UIHelpers.AddBackground(btn, isSelected ? Theme.BandFallbackMauve : Theme.BandFallbackBlue);`, the label with `Widgets.BandLabel(btn, label, 16f, TextAlignmentOptions.Midline);`, and delete the `TabNormal` / `TabSelected` statics.

- [ ] **Step 4: Control row** — replace the body of `CreateControlRow` with an HLG row: checkbox/label on the left, two Owlcat buttons on the right. (Deviation from spec §4.4, agreed at planning: the control row and the filter row stay two rows — the character checkbox needs the first one — but both now sit on the page sheet.)

```csharp
        void CreateControlRow(Transform parent) {
            var (row, rowRect) = UIHelpers.Create("ControlRow", parent);
            rowRect.SetAnchor(0.02, 0.98, 0.77, 0.83);
            rowRect.sizeDelta = Vector2.zero;
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = Theme.RowGap;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            // Left slot: rebuilt per tab by UpdateToggleLabel (checkbox on character tabs,
            // plain section label on Global / Presets / Packs).
            var (slot, _) = UIHelpers.Create("ToggleSlot", row.transform);
            Widgets.InRow(slot, 300f, 1f);
            toggleSlot = slot;

            Widgets.ActionButton(row.transform, "AddRuleBtn", "button.new_rule".i18n(), 17f, AddNewRule, 200f);
            Widgets.ActionButton(row.transform, "FromPresetBtn", "button.from_preset".i18n() + " ▾", 16f,
                AddFromPreset, 200f);
        }
```

Replace the field `TextMeshProUGUI toggleLabel;` with `GameObject toggleSlot;` and rewrite `UpdateToggleLabel` to rebuild the slot's single child:

```csharp
        void UpdateToggleLabel() {
            if (toggleSlot == null) return;
            for (int i = toggleSlot.transform.childCount - 1; i >= 0; i--)
                Destroy(toggleSlot.transform.GetChild(i).gameObject);

            string plain = selectedUnitId == null ? "toggle.global_rules".i18n()
                : selectedUnitId == "presets" ? "tab.presets".i18n()
                : selectedUnitId == "packs" ? "tab.packs".i18n()
                : null;
            if (plain != null) {
                var (lbl, r) = UIHelpers.Create("Label", toggleSlot.transform);
                r.FillParent();
                Widgets.SectionLabel(lbl, plain, 17f);
                return;
            }

            var config = ConfigManager.Current;
            bool enabled = config.IsEnabled(selectedUnitId);
            var charName = GetCharacterName(selectedUnitId);
            var template = (enabled ? "toggle.tactics.enabled" : "toggle.tactics.disabled").i18n();
            var box = Widgets.Checkbox(toggleSlot.transform, "TacticsToggle", string.Format(template, charName),
                enabled, _ => ToggleTactics());
            box.Rect().FillParent();
        }
```

(`ToggleTactics` already flips the config and calls `RefreshRuleList`, which calls `UpdateToggleLabel`.)

- [ ] **Step 5: Filter row** — replace the body of `CreateFilterStrip`:

```csharp
        void CreateFilterStrip(Transform parent) {
            var (strip, stripRect) = UIHelpers.Create("FilterStrip", parent);
            stripRect.SetAnchor(0.02, 0.98, 0.72, 0.76);
            stripRect.sizeDelta = Vector2.zero;
            var hlg = strip.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = Theme.RowGap;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.padding = new RectOffset(0, 0, 3, 3);

            ruleFilterInput = UIHelpers.CreateTMPInputFieldInRow(strip, "FilterInput", 300f, 1f, "", 15f,
                placeholderText: "filter.rules.placeholder".i18n());
            ruleFilterInput.onValueChanged.AddListener(v => {
                currentRuleFilter = v ?? "";
                UpdateFilterClearButton();
                ApplyFilter();
            });

            var clearBtn = Widgets.IconButton(strip.transform, "FilterClear", Icon.X, Theme.IconSmall, () => {
                ruleFilterInput.text = "";  // triggers onValueChanged -> ApplyFilter
            });
            ruleFilterClearButton = clearBtn.GetComponent<Button>();
            clearBtn.SetActive(false);
        }
```

- [ ] **Step 6: Empty label** — in `CreateRuleFilterEmptyLabel` replace the `AddLabel` + two outline lines with

```csharp
            Widgets.InkLabel(obj, "filter.no_matching_rules".i18n(), 16f, TextAlignmentOptions.Midline,
                Theme.InkMuted, italic: true);
```

- [ ] **Step 7: Build, deploy, smoke check**

Build (Global Constraints), `./deploy.sh`. Human check on the Deck (blocking): game visible but dimmed behind the book; parchment sheet under controls; title in dark ink; "Tactics enabled for <name>" is a checkbox that toggles; `+ New Rule` / `+ From Preset` still work; filter input is paper with ink text, the X clears it; font scale 0.5 and 2.0 — control row and filter row do not overlap the tab bar or the list.

- [ ] **Step 8: Commit**

```bash
git add WrathTactics/UI/TacticsPanel.cs WrathTactics/UI/UIHelpers.cs
git commit -m "feat(ui): parchment page sheet, dimmed backdrop, ink control and filter rows

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 4: Hint card, HUD checkbox, pack row, list dividers

**Files:**
- Modify: `WrathTactics/UI/TacticsPanel.cs` — `RefreshRuleList` (:540-575), `AddHudButtonToggleRow` (:581-603), `AddPackRow` (:608-682), `UpdateSaveListAsPackButton` (:365-374), `SetPackStatus` callers (:694, :738, :744, :750, :806, :821, :837, :847, :913, :920, :932, :936), scrollbar fallbacks (:469, :480)

**Interfaces:**
- Consumes: `Widgets.HintCard/Checkbox/SectionLabel/InlineLink/InkLabel/FlourishDivider/InRow`, `Theme.Status*`.
- Produces: `TacticsPanel.saveListAsPackButton` stays a `Button`; `saveListAsPackBackground` (Image) is removed — dimming goes through `Button.interactable` + `CanvasGroup.alpha`.

- [ ] **Step 1: Hints** — replace both `UIHelpers.AddHintCard(ruleListContent, …)` calls in `RefreshRuleList`:

```csharp
                Widgets.HintCard(ruleListContent, "global.priority_hint".i18n(), Theme.HintHeight);
```
and
```csharp
                    Widgets.HintCard(ruleListContent,
                        Strings.Format("global.preempt_hint", enabledGlobals), Theme.HintHeightShort);
```

After `AddPackRow(rules);` add `Widgets.FlourishDivider(ruleListContent);`.

- [ ] **Step 2: HUD checkbox** — replace the body of `AddHudButtonToggleRow`:

```csharp
        void AddHudButtonToggleRow() {
            var settings = ModSettingsManager.Current;
            // Checkbox semantics: checked = HUD button hidden (the setting is "ShowHudButton").
            var row = Widgets.Checkbox(ruleListContent, "HudButtonToggle", "hud_button.hide".i18n(),
                !settings.ShowHudButton, hidden => {
                    var s = ModSettingsManager.Current;
                    s.ShowHudButton = !hidden;
                    bool saved = ModSettingsManager.Save();
                    // Re-enabling should give instant feedback: push the floating-fallback
                    // retry timer past its threshold so Update() recreates the button on
                    // the next frame instead of after the 5 s BubbleBuffs grace period.
                    if (s.ShowHudButton) hudButtonRetrySeconds = 6f;
                    if (!saved) SetPackStatus("hud_button.save_failed".i18n(), Theme.StatusError);
                });
            row.GetComponent<LayoutElement>().preferredHeight = Theme.InlineRowHeight;
        }
```

Delete `HudToggleLabel()` (:942) — `hud_button.show` stays in the locale files unused; no key removal.

- [ ] **Step 3: Pack row** — replace the body of `AddPackRow` up to (not including) the status label with:

```csharp
        void AddPackRow(List<TacticsRule> rules) {
            var row = Widgets.Row(ruleListContent, "PackRow", Theme.RowHeight);
            row.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(4, 4, 2, 2);

            var (labelObj, _l) = UIHelpers.Create("PackRowLabel", row.transform);
            Widgets.InRow(labelObj, 60f, 0f);
            Widgets.SectionLabel(labelObj, "pack.row_label".i18n(), 14f);

            foreach (var packId in Engine.PackRegistry.AppliedPackIds(rules)) {
                var pack = Engine.PackRegistry.Get(packId);
                if (pack == null) continue;  // pack deleted — rules keep working, no chip
                var captured = pack;
                AddPackChip(row.transform, pack, () => ShowPackChipMenu(captured));
            }

            Widgets.InlineLink(row.transform, "ApplyPackBtn", "pack.button.apply".i18n(), ShowPackPicker, Icon.Add);

            // SaveListAsPack always claims the WHOLE list (see its own comment) — while a
            // filter is active that would silently promote/stamp rules the user can't even
            // see right now, so the link goes dim and non-interactable instead of firing.
            var saveLink = Widgets.InlineLink(row.transform, "SaveListAsPackBtn", "pack.button.save_list".i18n(),
                SaveListAsPack);
            saveListAsPackButton = saveLink.GetComponent<Button>();
            saveListAsPackGroup = saveLink.AddComponent<CanvasGroup>();
            UpdateSaveListAsPackButton();
```

Keep the status-label block that follows but render it with `Widgets.InkLabel(statusObj, lastPackStatus, 12f, TextAlignmentOptions.MidlineLeft, lastPackStatusColor, italic: true);` and delete the trailing `UIHelpers.EnsureAllHoverable(row);`.

Add the chip helper (chip = ink pill with colour dot):

```csharp
        void AddPackChip(Transform parent, TacticsPack pack, UnityEngine.Events.UnityAction onClick) {
            var (chip, _) = UIHelpers.Create($"PackChip_{pack.Id}", parent);
            Widgets.InRow(chip, 130f * UIHelpers.FontScale, 0f);
            Widgets.AddInset(chip);
            var hlg = chip.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6f;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.padding = new RectOffset(8, 8, 3, 3);

            var (dot, dotRect) = UIHelpers.Create("Dot", chip.transform);
            float d = Theme.IconSmall * 0.6f;
            Widgets.InRow(dot, d, 0f).preferredHeight = d;
            var dotImg = dot.AddComponent<Image>();
            dotImg.color = PackPalette.ColorAt(pack.ColorIndex);
            dotImg.raycastTarget = false;
            if (ThemeProvider.ToggleOn != null) { dotImg.sprite = ThemeProvider.ToggleOn; dotImg.preserveAspect = true; }

            var (lbl, _t) = UIHelpers.Create("Label", chip.transform);
            Widgets.InRow(lbl, 80f, 1f);
            Widgets.InkLabel(lbl, pack.Name + "  ▾", 13f);

            var btn = chip.AddComponent<Button>();
            btn.targetGraphic = chip.GetComponent<Image>();
            Widgets.ApplyColorTint(btn);
            // One click opens a menu with both actions. The menu IS the confirmation:
            // the previous design deleted every rule of the pack on a single click of a
            // control that read as a label (play-test finding).
            btn.onClick.AddListener(onClick);
        }
```

Replace the field `Image saveListAsPackBackground;` with `CanvasGroup saveListAsPackGroup;` (also the two `= null` resets in `RefreshRuleList`) and rewrite:

```csharp
        void UpdateSaveListAsPackButton() {
            if (saveListAsPackButton == null) return;
            bool filterActive = !string.IsNullOrWhiteSpace(currentRuleFilter);
            saveListAsPackButton.interactable = !filterActive;
            if (saveListAsPackGroup != null) saveListAsPackGroup.alpha = filterActive ? 0.45f : 1f;
        }
```

- [ ] **Step 4: Status colours** — every `SetPackStatus(…, new Color(1f, 0.5f, 0.4f))` → `Theme.StatusError`; `new Color(1f, 0.8f, 0.4f)` → `Theme.StatusWarn`; `new Color(0.6f, 0.85f, 0.6f)` → `Theme.StatusOk`; `Color.gray` → `Theme.StatusMuted`. Also the field initialiser `lastPackStatusColor` if it has one. Scrollbar fallbacks (:469, :480): `Theme.InkFrame` for the track, `Theme.InkMuted` for the handle.

- [ ] **Step 5: Build, deploy, smoke check** — Global tab: annotation hint readable, HUD checkbox flips the HUD button (check the HUD after closing the panel), pack row shows chips as pills with a dot, `+ Apply pack` opens the picker, `Save list as pack` dims while a filter is typed; character tab with globals enabled: short hint shows.

- [ ] **Step 6: Commit**

```bash
git add WrathTactics/UI/TacticsPanel.cs
git commit -m "feat(ui): annotation hint cards, HUD checkbox, ink pack row

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 5: Rule card surface, band header, linked summary

**Files:**
- Modify: `WrathTactics/UI/RuleEditorWidget.cs` — `BuildUI` (:49-98)
- Modify: `WrathTactics/UI/RuleEditorWidget.Header.cs` — whole file

**Interfaces:**
- Consumes: `Widgets.ApplyCard/BandRow/ToggleDot/SetToggleDot/BandLabel/InlineLink/IconButton/InRow/InkLabel`, `Theme.PackBandTint/HeaderHeight/CollapsedBodyHeight/IconMedium/IconSmall`, `UIHelpers.CreateTMPInputFieldInRow`.
- Produces: field `GameObject enabledDot;` replaces `TextMeshProUGUI enabledLabel;`.

- [ ] **Step 1: Card surface** — in `BuildUI` replace the parchment branch:

```csharp
            // Card = ink frame on the page sheet (spec §3.1). The sheet supplies the paper.
            Widgets.ApplyCard(root);
```

Change the BodyScroll insets from 4 to `Theme.CardPadding` (`offsetMin = new Vector2(Theme.CardPadding, Theme.CardPadding); offsetMax = -that`). Add a field `float bodySpacing;` next to `layoutElement`, set `bodySpacing = Theme.RowGap * 0.66f; vlg.spacing = bodySpacing;` (≈4 at scale 1) — `UpdateHeight` reads it (Task 8).

- [ ] **Step 2: Header** — replace `CreateHeader` entirely:

```csharp
        void CreateHeader(Transform parent, TacticsRule linkedPreset) {
            bool isLinked = linkedPreset != null;
            var pack = Engine.PackRegistry.Get(rule.PackId);
            // Pack origin wins over the generic linked tint so a character running several
            // packs can tell at a glance which rules belong together. A dangling PackId
            // (pack deleted) resolves to null and falls back to the normal band.
            var style = isLinked ? BandStyle.Blue : BandStyle.Mauve;
            Color? tint = pack != null ? Theme.PackBandTint(pack.ColorIndex) : (Color?)null;
            var header = Widgets.BandRow(parent, "Header", style, Theme.HeaderHeight, tint);

            // [dot] [N. name (flex)] [origin] [copy] [export] [preset?] [^] [v] [delete]
            enabledDot = Widgets.ToggleDot(header.transform, "EnableBtn", rule.Enabled, () => {
                rule.Enabled = !rule.Enabled;
                Widgets.SetToggleDot(enabledDot, rule.Enabled);
                PersistEdit();
            });

            string displayName = isLinked
                ? string.Format("linked.name_format".i18n(), linkedPreset.Name)
                : $"{index + 1}. {rule.Name}";
            var nameInput = UIHelpers.CreateTMPInputFieldInRow(header, "NameInput", 200f, 1f, displayName, 18f);
            nameInput.interactable = !isLinked;  // linked: name comes from preset, not editable here
            if (!isLinked) {
                nameInput.onEndEdit.AddListener(v => {
                    string prefix = $"{index + 1}. ";
                    rule.Name = v.StartsWith(prefix) ? v.Substring(prefix.Length) : v;
                    PersistEdit();
                });
            }

            // No new locale keys: pack origin reuses the "Packs:" row label, linked origin the badge text.
            string origin = pack != null ? $"{"pack.row_label".i18n().TrimEnd(':', ' ')}: {pack.Name}"
                : isLinked ? string.Format("linked.badge".i18n(), linkedPreset.Name)
                : null;
            if (origin != null) {
                var (o, _) = UIHelpers.Create("Origin", header.transform);
                Widgets.InRow(o, 160f, 0f);
                Widgets.BandLabel(o, origin, 13f, TextAlignmentOptions.MidlineRight, italic: true);
            }

            Widgets.InlineLink(header.transform, "Copy", "button.copy".i18n(), () => CloneRule(), onBand: true, fontSize: 13f);
            Widgets.InlineLink(header.transform, "Export", "button.export".i18n(), () => ExportRuleToClipboard(), onBand: true, fontSize: 13f);
            bool canPromote = !isLinked && !string.IsNullOrEmpty(unitId);
            if (canPromote)
                Widgets.InlineLink(header.transform, "Promote", "button.promote_to_preset".i18n(), () => PromoteToPreset(), onBand: true, fontSize: 13f);

            Widgets.IconButton(header.transform, "Up", Icon.ArrowUp, Theme.IconSmall, () => MoveRule(-1));
            Widgets.IconButton(header.transform, "Down", Icon.ArrowDown, Theme.IconSmall, () => MoveRule(1));
            Widgets.IconButton(header.transform, "Del", Icon.Delete, Theme.IconMedium, () => DeleteRule());
        }
```

Replace the field declaration `TextMeshProUGUI enabledLabel;` with `GameObject enabledDot;` in `RuleEditorWidget.cs`.

- [ ] **Step 3: Linked summary** — replace `RenderLinkedSummary`:

```csharp
        void RenderLinkedSummary(Transform parent, TacticsRule preset) {
            int condCount = 0;
            if (preset.ConditionGroups != null) {
                foreach (var g in preset.ConditionGroups)
                    if (g?.Conditions != null) condCount += g.Conditions.Count;
            }
            string abilityInfo = string.IsNullOrEmpty(preset.Action.AbilityId)
                ? ""
                : $" ({preset.Action.AbilityId.Substring(0, System.Math.Min(8, preset.Action.AbilityId.Length))}…)";
            string condCountText = string.Format("linked.summary.condition_count".i18n(), condCount);
            string summary = string.Format("linked.summary".i18n(),
                condCountText, preset.Action.Type, abilityInfo, preset.Target.Type);

            // One ink line: summary on the left, "Unlink & edit" link on the right.
            var row = Widgets.Row(parent, "LinkedSummary", Theme.CollapsedBodyHeight);
            row.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset((int)Theme.BandPaddingX, (int)Theme.BandPaddingX, 2, 2);
            var (sumObj, _s) = UIHelpers.Create("Summary", row.transform);
            Widgets.InRow(sumObj, 200f, 1f);
            Widgets.InkLabel(sumObj, summary, 13f, TextAlignmentOptions.MidlineLeft, Theme.InkLabel);
            Widgets.InlineLink(row.transform, "UnlinkBtn", "button.unlink_edit".i18n(), () => {
                Engine.PresetRegistry.BreakLink(rule);
                PersistEdit();
                RebuildBody();
            });
        }
```

- [ ] **Step 4: Build, deploy, smoke check** — Global tab (linked cards): blue band, dot toggles and persists after close/reopen, copy/export/preset links work, arrows reorder, red X deletes, `Unlink & edit` breaks the link; a pack rule shows a tinted band that is still readable for each of the six palette colours (cycle a pack's swatch on the Packs tab).

- [ ] **Step 5: Commit**

```bash
git add WrathTactics/UI/RuleEditorWidget.cs WrathTactics/UI/RuleEditorWidget.Header.cs
git commit -m "feat(ui): ink-framed rule cards with brush-band headers

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 6: Band dropdowns, row overloads, body rows (IF / THEN / TARGET / cooldown / fallbacks)

**Files:**
- Modify: `WrathTactics/UI/UIHelpers.cs` — `PopupSelector.CreateWithIcons` (:400-435), new `PopupSelector.CreateInRow` / `CreateWithIconsInRow`
- Modify: `WrathTactics/UI/RuleEditorWidget.cs` — `RebuildBody` (:100-204), `AddSectionLabel` (:206-211), `AddSpacer`
- Modify: `WrathTactics/UI/RuleEditorWidget.Action.cs` — `SetupActionRow`, `SetupSpellSelector`, `BuildSpellPickerButton`, `SetupFallbackRows`, `BuildFallbackRow`
- Modify: `WrathTactics/UI/RuleEditorWidget.Target.cs`, `RuleEditorWidget.Cooldown.cs`

**Interfaces:**
- Consumes: `Widgets.BandDropdownShell/Row/InRow/SectionLabelRow/InlineLink/OrDivider/FlourishDivider/IconButton/InkLabel`, `UIHelpers.CreateTMPInputFieldInRow`.
- Produces: `PopupSelector.CreateInRow(GameObject parent, string name, float flexibleWidth, List<string> options, int initialIndex, Action<int> onSelected)` and `CreateWithIconsInRow(…, List<Sprite> icons, …)`. Rule: `flexibleWidth` = the old `(xMax − xMin)` so proportions are preserved.

- [ ] **Step 1: PopupSelector trigger on a band** — in `CreateWithIcons` replace everything from `UIHelpers.AddBackground(obj, new Color(0.22f…` through the arrow block with:

```csharp
            var shell = Widgets.BandDropdownShell(parent.transform, name, "", withIcon: false,
                out var shellLabel, out _);
            var obj = shell;
            var rect = obj.Rect();
            rect.SetAnchor(xMin, xMax, 0, 1);
            rect.sizeDelta = Vector2.zero;

            var selector = obj.AddComponent<PopupSelector>();
            selector.options = options ?? new List<string>();
            selector.icons = icons;
            selector.selectedIndex = Mathf.Clamp(initialIndex, 0, Mathf.Max(0, (options?.Count ?? 1) - 1));
            selector.onSelected = onSelected;
            selector.buttonLabel = shellLabel;
            selector.UpdateLabel();

            var btn = obj.AddComponent<Button>();
            btn.targetGraphic = obj.GetComponent<Image>();
            Widgets.ApplyColorTint(btn);
            btn.onClick.AddListener(selector.TogglePopup);
            return selector;
```

(remove the now-unused `var (obj, rect) = UIHelpers.Create(name, parent.transform);` at the top). Then add the row overloads right after `Create`:

```csharp
        /// <summary>Layout-group flavour: flexibleWidth replaces the anchor span (pass the old xMax − xMin).</summary>
        public static PopupSelector CreateInRow(GameObject parent, string name, float flexibleWidth,
            List<string> options, int initialIndex, Action<int> onSelected) =>
            CreateWithIconsInRow(parent, name, flexibleWidth, options, null, initialIndex, onSelected);

        public static PopupSelector CreateWithIconsInRow(GameObject parent, string name, float flexibleWidth,
            List<string> options, List<Sprite> icons, int initialIndex, Action<int> onSelected) {
            var selector = CreateWithIcons(parent, name, 0f, 1f, options, icons, initialIndex, onSelected);
            Widgets.InRow(selector.gameObject, 60f, flexibleWidth);
            return selector;
        }
```

- [ ] **Step 2: Body composition** (`RebuildBody`) — replace the four flat buttons and the labels:

- `AddSectionLabel(bodyContainer.transform, "section.if".i18n());` → `Widgets.SectionLabelRow(bodyContainer.transform, "IfLabel", "section.if".i18n());`
- OR separator between groups → `Widgets.OrDivider(bodyContainer.transform, "section.or_label".i18n());` — key `section.or_label` does not exist: use the literal from `section.or_separator` with the dashes stripped: `"section.or_separator".i18n().Trim('-', ' ')`.
- `+ Condition` per group (the `addCondBtn` block) →

```csharp
                var addRow = Widgets.Row(bodyContainer.transform, $"AddCond_G{gi}", Theme.InlineRowHeight);
                addRow.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset((int)Theme.BandPaddingX, 0, 0, 0);
                Widgets.InlineLink(addRow.transform, "AddCondition", "button.add_condition".i18n().TrimStart('+', ' '), () => {
                    group.Conditions.Add(new Condition());
                    PersistEdit();
                    RebuildBody();
                }, Icon.Add);
                // Last group also carries "+ OR" on the same row.
                if (gi == rule.ConditionGroups.Count - 1)
                    Widgets.InlineLink(addRow.transform, "AddOr", "button.add_or_group".i18n().TrimStart('+', ' '), () => {
                        rule.ConditionGroups.Add(new ConditionGroup { Conditions = { new Condition() } });
                        PersistEdit();
                        RebuildBody();
                    }, Icon.Add);
```

- `addFirstBtn` (no groups) → same `Widgets.Row` + one `InlineLink` with `Icon.Add`, name `AddFirstCond`.
- Delete the separate `addOrBtn` block (it now lives on the last group's row; when there are no groups it is not shown — `AddFirstCond` creates the first group).
- `AddSpacer(bodyContainer.transform, 4);` → `Widgets.FlourishDivider(bodyContainer.transform);`
- Delete `AddSectionLabel` and `AddSpacer` methods.

- [ ] **Step 3: Action row** (`SetupActionRow`, `SetupSpellSelector`, `BuildSpellPickerButton`)

`SetupActionRow`: `var row = Widgets.Row(parent, "ActionRow", Theme.RowHeight);` then the label:

```csharp
            var (lbl, _) = UIHelpers.Create("ThenLabel", row.transform);
            Widgets.InRow(lbl, 0f, 0.10f);
            Widgets.SectionLabel(lbl, "section.then".i18n());
```

Every `PopupSelector.Create(row, "<Name>", xMin, xMax, …)` in this file becomes `PopupSelector.CreateInRow(row, "<Name>", xMax - xMin, …)` with the arithmetic done: ActionType `0.27f`; HealMode `0.11f`; HealEnergy `0.14f`; HealSources `0.22f`; SplashMode `0.31f`; WeaponSet `0.31f`; MoveWithin `0.31f`; ToggleMode `0.13f`; MetamagicRod `0.21f`; SpellSources `0.22f`. `BuildSpellPickerButton(row, xMin, xMax)` → `BuildSpellPickerButton(row, xMax - xMin)`: ToggleActivatable `0.47f`; CastSpell `0.16f`; others `0.61f`.

`BuildSpellPickerButton(GameObject row, float flexibleWidth)`: replace the button construction (from `var (btnObj, btnRect) = …` through the arrow block) with

```csharp
            var btnObj = Widgets.BandDropdownShell(row.transform, "SpellPick",
                found || entries.Count > 0 ? selected.Name : "placeholder.none_available".i18n(),
                withIcon: true, out spellPickerLabel, out spellPickerIcon);
            Widgets.InRow(btnObj, 120f, flexibleWidth);
            var pickBtn = btnObj.AddComponent<Button>();
            pickBtn.targetGraphic = btnObj.GetComponent<Image>();
            Widgets.ApplyColorTint(pickBtn);
            spellPickerButton = btnObj;
            UpdateSpellPickerButton(selected, entries.Count > 0);
            pickBtn.onClick.AddListener(() => { …existing listener body… });
```

- [ ] **Step 4: Fallback rows** — `SetupFallbackRows`: the `+ Fallback` button becomes

```csharp
            var addRow = Widgets.Row(parent, "AddFallback", Theme.InlineRowHeight);
            addRow.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset((int)(Theme.BandPaddingX * 2), 0, 0, 0);
            Widgets.InlineLink(addRow.transform, "AddFallbackLink", "button.add_fallback".i18n().TrimStart('+', ' '), () => {
                rule.Action.FallbackAbilityIds.Add("");
                PersistEdit();
                RebuildBody();
            }, Icon.Add);
```

`BuildFallbackRow`: `var row = Widgets.Row(parent, $"Fallback_{index}", Theme.RowHeight);` the arrow label → `var (arrowLbl, _) = UIHelpers.Create("ArrowLbl", row.transform); Widgets.InRow(arrowLbl, 0f, 0.10f); Widgets.InkLabel(arrowLbl, "↳", 18f, TextAlignmentOptions.MidlineRight, Theme.InkMuted);` the picker → `Widgets.BandDropdownShell(row.transform, "FallbackPick", …, withIcon: true, out var label, out var icon); Widgets.InRow(btn, 120f, 0.72f);` + Button as in Step 3; keep the icon/label updates in the listener (they now target the `out` variables); delete → `Widgets.IconButton(row.transform, "DeleteFallback", Icon.X, Theme.IconSmall, () => { … });`.

- [ ] **Step 5: Target row** — `Widgets.Row(parent, "TargetRow", Theme.RowHeight)`; label as in Step 3 (`section.target`, flex 0.10); `TargetType` → `CreateInRow(row, "TargetType", 0.39f, …)`; filter/ally labels → `Widgets.InRow(filterLbl, 0f, 0.14f); Widgets.InkLabel(filterLbl, filterLabel, 14f, TextAlignmentOptions.MidlineRight, Theme.InkMuted, italic: true);`; filter inputs → `UIHelpers.CreateTMPInputFieldInRow(row, "TargetFilter", 80f, 0.34f, rule.Target.Filter ?? "", 15f)`; `TargetSpecificAlly` → `CreateInRow(row, "TargetSpecificAlly", 0.34f, …)`.

- [ ] **Step 6: Cooldown row** — `Widgets.Row(parent, "CooldownRow", Theme.RowHeight)`; label `Widgets.InRow(lbl, 0f, 0.10f); Widgets.SectionLabel(lbl, "cooldown.label".i18n());` (the key's text is "Cooldown (rounds):" — keep it; the small-caps rendering handles it); input `UIHelpers.CreateTMPInputFieldInRow(row, "CdInput", 90f, 0f, rule.CooldownRounds.ToString(), 16f, TMP_InputField.ContentType.IntegerNumber)`; drop the `cdRect.SetAnchor` line.

- [ ] **Step 7: Build, deploy, smoke check** — an unlinked character rule: IF small caps, band dropdowns open and select, `+ Condition` / `+ OR` / `+ Fallback` links add rows, `or` divider between groups, flourish before THEN, spell picker band shows icon + name, target filter input and cooldown input accept text. Switch action type through every value once (Heal / ThrowSplash / SwitchWeaponSet / MoveToTarget / ToggleActivatable / CastSpell / UseItem / Attack) — no exception in the mod log (`grep -c 'Exception' <latest log>` unchanged).

- [ ] **Step 8: Commit**

```bash
git add WrathTactics/UI
git commit -m "feat(ui): band dropdowns and layout-group body rows for the rule card

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 7: Condition rows on a layout group

**Files:**
- Modify: `WrathTactics/UI/ConditionRowWidget.cs` — `BuildUI` (:44-400), `CreateEqOperator` (:402), `CreateAllyPicker` (:452), `CreateBuffSelector` (:481-530)

**Interfaces:**
- Consumes: `PopupSelector.CreateInRow`, `UIHelpers.CreateTMPInputFieldInRow`, `Widgets.Row/InRow/InkLabel/IconButton/BandDropdownShell`, `BuffPickerOverlay.Open` (unchanged).

- [ ] **Step 1: Root becomes a row** — at the top of `BuildUI` replace `root.AddComponent<LayoutElement>().preferredHeight = 30;` with

```csharp
            root.AddComponent<LayoutElement>().preferredHeight = Theme.RowHeight;
            var hlg = root.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = Theme.RowGap;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.padding = new RectOffset((int)Theme.BandPaddingX, 0, 2, 2);
```

In `Rebuild()` also destroy the HLG: after the `LayoutElement` line add `var g = GetComponent<HorizontalLayoutGroup>(); if (g != null) DestroyImmediate(g);` (DestroyImmediate — a deferred Destroy leaves two layout controllers for a frame, `gotchas-ui.md`).

- [ ] **Step 2: Mechanical conversion** — every `PopupSelector.Create(root, "<Name>", xMin, xMax, …)` → `PopupSelector.CreateInRow(root, "<Name>", xMax - xMin, …)`; every `UIHelpers.CreateTMPInputField(root, "<Name>", xMin, xMax, …)` → `UIHelpers.CreateTMPInputFieldInRow(root, "<Name>", 60f, xMax - xMin, …)`; `CreateEqOperator(root, xMin, xMax, name)` and `CreateAllyPicker(root, xMin, xMax, …)` and `CreateBuffSelector(root, xMin, xMax)` change their signature to `(GameObject root, float flexibleWidth, …)` and forward the same way. Compute each `xMax − xMin` at the call site (e.g. Subject `0.15f`, Property `0.21f`, CountThresholdOperator `0.06f`, CountValue `0.07f`, "with" label `0.06f`, CountOperator `0.08f`, CountRangeBracketValue `0.21f`, Operator `0.12f`, CreatureTypeValue `0.43f`, …). Sibling order = creation order now, so:

  - The "with" label: `var (withLbl, _) = UIHelpers.Create("WithLabel", root.transform); Widgets.InRow(withLbl, 0f, 0.06f); Widgets.InkLabel(withLbl, "condition.with".i18n(), 14f, TextAlignmentOptions.Midline, Theme.InkMuted, italic: true);`
  - The property-selector repositioning block (`psRect.SetAnchor(0.38, 0.58, 0, 1)`) becomes `propertySelector.transform.SetSiblingIndex(withLbl.transform.GetSiblingIndex() + 1); Widgets.InRow(propertySelector.gameObject, 60f, 0.20f);`

- [ ] **Step 3: Delete button** — replace the `DelBtn` block (:394-400) with `Widgets.IconButton(root.transform, "DelBtn", Icon.X, Theme.IconSmall, () => onDelete?.Invoke());` (keep whatever the original listener did — read it before replacing).

- [ ] **Step 4: Buff selector** — in `CreateBuffSelector` replace the hand-built button (from `var (btnObj, btnRect)` through the arrow block) with `var btnObj = Widgets.BandDropdownShell(root.transform, "BuffPickerButton", currentLabel, withIcon: false, out var label, out _); Widgets.InRow(btnObj, 120f, flexibleWidth);` + Button with `ApplyColorTint`, then the existing `BuffPickerOverlay.Open(…)` listener updating `label.text`.

- [ ] **Step 5: Build, deploy, smoke check** — one row per subject: Self, Enemy, AllyCount (`>= 2 with HpPercent < 60`), AllyByName, EnemyNearest; properties: HpPercent, CreatureType (`=`/`!=`), HasBuff (opens buff picker), WithinRange, HasClass, a Yes/No property. Widths must read in the same proportions as before; the X sits at the right edge. Font scale 2.0: nothing wraps or overlaps.

- [ ] **Step 6: Commit**

```bash
git add WrathTactics/UI/ConditionRowWidget.cs
git commit -m "feat(ui): condition rows on a HorizontalLayoutGroup with band dropdowns

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 8: Card height on theme tokens

**Files:**
- Modify: `WrathTactics/UI/RuleEditorWidget.cs` — `UpdateHeight` (:218-257), `BuildUI` (`layoutElement.preferredHeight = 200` → `Theme.MinCardHeight`)

- [ ] **Step 1: Rewrite `UpdateHeight`**

```csharp
        void UpdateHeight(TacticsRule linkedPreset) {
            if (layoutElement == null) return;
            float headerH = hideHeader ? 0f : Theme.HeaderHeight;
            float pad = Theme.CardPadding * 2f + 4f;   // BodyScroll insets + VLG padding
            if (linkedPreset != null) {
                float gaps = (hideHeader ? 0 : 1) * bodySpacing;
                layoutElement.preferredHeight = headerH + Theme.CollapsedBodyHeight + gaps + pad;
                if (bodyScrollRect != null) bodyScrollRect.enabled = false;
                return;
            }
            int condCount = rule.ConditionGroups.Sum(g => g.Conditions.Count);
            int groupCount = rule.ConditionGroups.Count;
            bool chainCapable = rule.Action.Type == ActionType.CastSpell
                             || rule.Action.Type == ActionType.CastAbility;
            int fallbackCount = chainCapable ? (rule.Action.FallbackAbilityIds?.Count ?? 0) : 0;

            // Rows rendered by RebuildBody, in order (keep in sync with it):
            // IF label | cond rows | per-group add row | (groups-1) OR dividers | [AddFirstCond]
            // | flourish | action row | fallback rows | [+Fallback row] | target row | cooldown row
            int rows = 1 + condCount + Mathf.Max(groupCount, 1) + Mathf.Max(0, groupCount - 1)
                + 1 + 1 + fallbackCount + (chainCapable ? 1 : 0) + 1 + 1 + (hideHeader ? 0 : 1);
            float height = headerH
                + Theme.SectionLabelHeight
                + condCount * Theme.RowHeight
                + Mathf.Max(groupCount, 1) * Theme.InlineRowHeight
                + Mathf.Max(0, groupCount - 1) * Theme.DividerHeight
                + Theme.DividerHeight                       // flourish
                + Theme.RowHeight                           // action
                + fallbackCount * Theme.RowHeight
                + (chainCapable ? Theme.InlineRowHeight : 0f)
                + Theme.RowHeight                           // target
                + Theme.RowHeight                           // cooldown
                + Mathf.Max(0, rows - 1) * bodySpacing
                + pad;
            layoutElement.preferredHeight = Mathf.Clamp(height, Theme.MinCardHeight, Theme.MaxCardHeight);
            if (bodyScrollRect != null)
                bodyScrollRect.enabled = height > Theme.MaxCardHeight;
        }
```

- [ ] **Step 2: Build, deploy, measure on the Deck** — a rule with 2 OR groups × 2 conditions, CastSpell with 1 fallback, at font scale 0.5 / 1.0 / 2.0: the cooldown row is fully visible with no clipped bottom edge and no more than ~one row of empty space below it. If it clips, raise `pad` (add the measured delta as `Theme.CardSlack`); if it floats, lower it. Record the final values in the commit message.

- [ ] **Step 3: Commit**

```bash
git add WrathTactics/UI/RuleEditorWidget.cs
git commit -m "feat(ui): rule card height from Theme metrics (font-scale aware)

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 9: Paper popups for dropdown lists and transient pickers

**Files:**
- Modify: `WrathTactics/UI/UIHelpers.cs` — `PopupSelector.CreatePickerOverlay` (:494-622), `ShowPicker` (:472-485)

**Interfaces:**
- Consumes: `Widgets.PaperPopup/ListRow/ListRowTextColor/InkLabel`, `Theme.PopupListWidth/PopupListMaxHeight/PopupRowHeight/InkFrame/InkMuted`.
- Produces: unchanged public API (`Create`, `CreateInRow`, `ShowPicker`, `SetOptions`).

- [ ] **Step 1: Rewrite `CreatePickerOverlay`** — keep the scroll/viewport/content/scrollbar structure but:
  - overlay + popup come from `var parts = Widgets.PaperPopup("PopupOverlay", Theme.PopupListWidth, totalHeight, null, null);` where `totalHeight = Mathf.Min(options.Count * Theme.PopupRowHeight + Theme.PaperInset * 2f + 8f, Theme.PopupListMaxHeight)`; return `parts.Overlay`; the Scroll object parents to `parts.Content`.
  - scrollbar track `Theme.InkFrame`, handle `Theme.InkMuted`.
  - option rows: `var itemObj = Widgets.ListRow(content.transform, $"Option_{i}", Theme.PopupRowHeight, i == selectedIndex);` label `Widgets.InkLabel(itemObj, options[i], 15f, TextAlignmentOptions.MidlineLeft, Widgets.ListRowTextColor(i == selectedIndex));` icons unchanged; `var b = itemObj.AddComponent<Button>(); b.targetGraphic = itemObj.GetComponent<Image>(); Widgets.ApplyColorTint(b); b.onClick.AddListener(…)`.
  - Callers (`OpenPopup`, `ShowPicker`) already attach their outside-click handler to `overlay.GetComponent<Button>()` — that is `parts.OverlayButton`, so they need no change.

- [ ] **Step 2: Build, deploy, smoke check** — open any dropdown: paper sheet, ink rows, current value on a band, hover tints rows, outside click and Escape close it, selection persists. `+ From Preset` and the pack chip menu (both `ShowPicker`) look the same.

- [ ] **Step 3: Commit**

```bash
git add WrathTactics/UI/UIHelpers.cs
git commit -m "feat(ui): dropdown lists on paper popups

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 10: Spell picker and buff picker on paper

**Files:**
- Modify: `WrathTactics/UI/SpellPickerOverlay.cs` — `Open` (:25-58), `BuildUI` (:60-125), `AddInfoLabel`, `AddRow`
- Modify: `WrathTactics/UI/BuffPickerOverlay.cs` — `Open` (:30-70), `BuildUI` (:72-140), `AddSectionHeader` (:259), `AddInfoLabel` (:268), `AddRow` (:276)

- [ ] **Step 1: SpellPickerOverlay.Open** — replace overlay/popup creation (from `var canvas = …` through the `swallow` lines) with

```csharp
            // No title (no locale key for it); close = outside click / Escape, as before.
            var parts = Widgets.PaperPopup("SpellPickerOverlay", Theme.PopupWidth, Theme.PopupHeight, null, null);
            var overlay = parts.Overlay;
            var popup = parts.Popup;
            parts.OverlayButton.onClick.AddListener(() => Destroy(overlay));
```
The controller is added to `popup` as before; call `controller.BuildUI(parts.Content);` (BuildUI's `popup` parameter now receives the inset content rect).

- [ ] **Step 2: SpellPickerOverlay.BuildUI** — `headerHeight = Theme.RowHeight + 8f`; delete the header `AddBackground` line; search input unchanged (it is already paper-styled by Task 3); rows: `AddRow` → `var row = Widgets.ListRow(rowsContainer.transform, "Row_" + (entry.Guid ?? ""), Theme.PopupRowHeight, false);` label → `Widgets.InkLabel(row, entry.Name, 14f)`; Button with `targetGraphic` + `ApplyColorTint`. `AddInfoLabel` → `Widgets.InkLabel(info, text, 13f, TextAlignmentOptions.Midline, Theme.InkMuted, italic: true)`.

- [ ] **Step 3: BuffPickerOverlay** — same three changes: `Open` on `Widgets.PaperPopup("BuffPickerOverlay", Theme.PopupWidth * 0.9f, Theme.PopupHeight * 0.93f, null, null)`; `BuildUI` header without background, `headerHeight = Theme.RowHeight + 4f`; `AddSectionHeader` → `var hdr = …; hdr LayoutElement preferredHeight = Theme.SectionLabelHeight; Widgets.SectionLabel(hdr, text, 13f).margin = new Vector4(6, 0, 4, 0);` (no background); `AddInfoLabel` and `AddRow` as in Step 2 (row label uses `BuffBlueprintProvider.FormatDisplayLabel`).

- [ ] **Step 4: Build, deploy, smoke check** — CastSpell rule → spell picker: paper, search focuses, typing filters, click selects, Escape closes; HasBuff condition → buff picker: Recents / Defaults section labels in small caps, search over the full list (300+ rows) scrolls smoothly; both popups: outside click closes without selecting.

- [ ] **Step 5: Commit**

```bash
git add WrathTactics/UI/SpellPickerOverlay.cs WrathTactics/UI/BuffPickerOverlay.cs
git commit -m "feat(ui): spell and buff pickers on paper popups

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 11: Save-as-pack dialog on paper

**Files:**
- Modify: `WrathTactics/UI/SaveAsPackOverlay.cs` — `Open` (:23-56), `BuildUI` (:58-201)

- [ ] **Step 1: Open** — replace overlay/popup creation with

```csharp
            Widgets.PaperPopupParts parts = default;   // captured by the close lambda, assigned below
            parts = Widgets.PaperPopup("SaveAsPackOverlay", Theme.PopupWidth * 0.96f, Theme.PopupHeight * 0.96f,
                "pack.save_dialog.title".i18n(), () => Destroy(parts.Overlay));
            parts.OverlayButton.onClick.AddListener(() => Destroy(parts.Overlay));
            var overlay = parts.Overlay;
            var popup = parts.Popup;
```
controller on `popup`; `controller.BuildUI(parts.Content, suggestedName, rules, describe);`.

- [ ] **Step 2: BuildUI** — the VLG goes on the content rect and gets `padding = new RectOffset(0, 0, (int)Theme.RowHeight + 6, 0)` (room for the paper title row). Delete the `Title` block (PaperPopup drew it). Labels: `Widgets.InkLabel(nameLabel, "pack.save_dialog.name_label".i18n(), 14f, TextAlignmentOptions.MidlineLeft, Theme.InkLabel)` (same for rules label). Rule rows: `Widgets.ListRow(rowsContent.transform, $"Rule_{rule.Id}", Theme.InlineRowHeight + 4f, false)` keeping the HLG; the check box becomes

```csharp
                var (box, _b) = UIHelpers.Create("Check", row.transform);
                Widgets.InRow(box, Theme.IconSmall, 0f).preferredHeight = Theme.IconSmall;
                Widgets.AddInset(box);
                var tick = Widgets.IconImage(box.transform, "Tick", Icon.Check, Theme.IconSmall * 0.8f);
                tick.GetComponent<LayoutElement>().ignoreLayout = true;
                var tr = tick.Rect(); tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 0.5f); tr.anchoredPosition = Vector2.zero;
                tick.SetActive(true);
                var boxBtn = box.AddComponent<Button>();
                boxBtn.targetGraphic = box.GetComponent<Image>();
                Widgets.ApplyColorTint(boxBtn);
                boxBtn.onClick.AddListener(() => {
                    if (selected.Contains(captured)) { selected.Remove(captured); tick.SetActive(false); }
                    else { selected.Add(captured); tick.SetActive(true); }
                    if (errorLabel != null) errorLabel.text = "";
                });
```
rule name label → `Widgets.InkLabel(nameObj, describe(captured), 13f)`; error label colour → `Theme.StatusError`; Cancel/Confirm → `Widgets.ActionButton(buttons.transform, "Cancel", "pack.save_dialog.cancel".i18n(), 15f, () => Destroy(transform.parent.gameObject), 160f, 1f)` and the Confirm equivalent with the existing validation body. Delete `UIHelpers.EnsureAllHoverable(popup);`.

- [ ] **Step 3: Build, deploy, smoke check** — `Save list as pack` on a character with 3+ rules: title on paper, all rows pre-ticked, untick one, confirm → pack created with the ticked rules in list order; confirm with none ticked shows the red error line; Cancel and Escape close.

- [ ] **Step 4: Commit**

```bash
git add WrathTactics/UI/SaveAsPackOverlay.cs
git commit -m "feat(ui): save-as-pack dialog on paper

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 12: Presets tab

**Files:**
- Modify: `WrathTactics/UI/PresetPanel.cs` — `BuildUI` (:55-160), `CreatePresetEntry` (:162-240), `SetStatus` and its callers (`new Color(1f, 0.5f, 0.4f)` → `Theme.StatusError`, `new Color(0.6f, 0.85f, 0.6f)` → `Theme.StatusOk`)

- [ ] **Step 1: Header block** — replace title / hint / three buttons / status / new-preset with:

```csharp
            Widgets.SectionLabelRow(root.transform, "PresetTitle", "tab.presets".i18n());
            Widgets.HintCard(root.transform, "preset.hint".i18n(), Theme.HintHeightShort);

            var actions = Widgets.Row(root.transform, "PresetActions", Theme.ControlRowHeight);
            Widgets.ActionButton(actions.transform, "NewPresetBtn", "preset.button.new".i18n(), 15f, () => { …existing body… }, 180f);
            Widgets.ActionButton(actions.transform, "ExportAllBtn", "preset.button.export_all".i18n(), 15f, () => ExportAllToClipboard(), 180f);
            Widgets.ActionButton(actions.transform, "ImportBtn", "preset.button.import".i18n(), 15f, () => ImportFromClipboard(), 180f);
            Widgets.InlineLink(actions.transform, "FolderBtn", "preset.button.open_folder".i18n(), () => { …existing folder body… });

            // Rendered unconditionally, even when empty: SetStatus's save-failure callers
            // return without a rebuild, so the strip must already exist. Name "IOStatus" and
            // its "Label" child are what SetStatus finds.
            Widgets.StatusLabel(root.transform, "IOStatus", lastIOStatus ?? "", lastIOStatusColor, Theme.StatusHeight);
            Widgets.FlourishDivider(root.transform);
```

Delete the old `FolderBtn` block after the entries and the `Sep` spacer. Empty state: `Widgets.StatusLabel(root.transform, "Empty", "preset.empty".i18n(), Theme.InkMuted, Theme.StatusHeight + 4f, 15f);`. The `EmptyMatch` block keeps its shape (the GameObject is held in `emptyMatchLabel` for `SetActive`), minus the surface:

```csharp
            var (emptyObj, _em) = UIHelpers.Create("EmptyMatch", root.transform);
            emptyObj.AddComponent<LayoutElement>().preferredHeight = Theme.StatusHeight + 4f;
            Widgets.InkLabel(emptyObj, "filter.no_matching_presets".i18n(), 15f,
                TextAlignmentOptions.MidlineLeft, Theme.InkMuted, italic: true).margin = new Vector4(6, 0, 6, 0);
            emptyObj.SetActive(false);
            emptyMatchLabel = emptyObj;
```

- [ ] **Step 2: Preset rows** — `CreatePresetEntry`: `var row = Widgets.BandRow(parent, $"Preset_{preset.Id}", BandStyle.Mauve, Theme.HeaderHeight);` (drop the manual HLG); name input → `UIHelpers.CreateTMPInputFieldInRow(row, "Name", 200f, 1f, preset.Name, 17f)`; Edit toggle → `Widgets.InlineLink(row.transform, "EditBtn", (expanded ? "button.close" : "button.edit").i18n(), () => { … }, onBand: true)`; Delete → `Widgets.IconButton(row.transform, "DelBtn", Icon.Delete, Theme.IconMedium, () => { … })`. The expanded editor (`RuleEditorWidget`, `hideHeader: true`) is unchanged and already restyled; wrap it so it reads as attached: after creating `editorObj`, `Widgets.ApplyCard(editorObj)` is already applied by the widget's `BuildUI`, nothing to add.

- [ ] **Step 3: Build, deploy, smoke check** — Presets tab: title, hint, one action row with three Owlcat buttons and the folder link, status line after Export; rows as mauve bands with editable name, `edit` opens the inline editor under the row, red X deletes (cascade confirm is unchanged); filter hides non-matching rows and shows the empty line.

- [ ] **Step 4: Commit**

```bash
git add WrathTactics/UI/PresetPanel.cs
git commit -m "feat(ui): presets tab on band rows and ink

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 13: Packs tab and member editor

**Files:**
- Modify: `WrathTactics/UI/PackPanel.cs` — `Build` (:22-58), `CreatePackRow` (:60-150), `CreateMemberEditor` (:190-305), `AddMemberButton` (:307-315), `PackPanelHost.BuildUI` (:346-364), all `new Color(1f, 0.5f, 0.4f)` → `Theme.StatusError`, `(0.6f, 0.85f, 0.6f)` → `Theme.StatusOk`, `Color.gray` defaults → `Theme.StatusMuted`

- [ ] **Step 1: Host and header** — `PackPanelHost.BuildUI`: hint → `Widgets.HintCard(transform, "pack.tab_hint".i18n(), Theme.HintHeightShort)`; status → `Widgets.StatusLabel(transform, "PackTabStatus", lastStatus ?? "", lastStatusColor, Theme.StatusHeight)`. `PackPanel.Build`: title → `Widgets.SectionLabelRow(parent, "PackTitle", "pack.section_title".i18n())`; New Pack → `var actions = Widgets.Row(parent, "PackActions", Theme.ControlRowHeight); Widgets.ActionButton(actions.transform, "NewPackBtn", "pack.button.new".i18n(), 15f, () => { … }, 180f);`; empty → `Widgets.StatusLabel(parent, "PackEmpty", "pack.empty".i18n(), Theme.InkMuted, Theme.StatusHeight, 14f)`; trailing `PackSep` → `Widgets.FlourishDivider(parent)`.

- [ ] **Step 2: Pack rows** — `CreatePackRow`: `var row = Widgets.BandRow(parent, $"Pack_{pack.Id}", BandStyle.Mauve, Theme.HeaderHeight, Theme.PackBandTint(pack.ColorIndex));` swatch → a colour dot button:

```csharp
            var (swatch, _sw) = UIHelpers.Create("Swatch", row.transform);
            float d = Theme.IconMedium;
            Widgets.InRow(swatch, d, 0f).preferredHeight = d;
            var swImg = swatch.AddComponent<Image>();
            swImg.color = PackPalette.ColorAt(pack.ColorIndex);
            swImg.preserveAspect = true;
            if (ThemeProvider.ToggleOn != null) swImg.sprite = ThemeProvider.ToggleOn;
            var swBtn = swatch.AddComponent<Button>();
            swBtn.targetGraphic = swImg;
            Widgets.ApplyColorTint(swBtn);
            swBtn.onClick.AddListener(() => { …existing cycle body… });
```
name → `CreateTMPInputFieldInRow(row, "PackName", 160f, 1f, pack.Name, 16f)`; count → `Widgets.BandLabel(countObj, …, 13f, TextAlignmentOptions.Midline, italic: true)` with `InRow(countObj, 90f, 0f)`; Members / Export → `Widgets.InlineLink(…, onBand: true)`; Delete → `Widgets.IconButton(row.transform, "PackDelBtn", Icon.Delete, Theme.IconMedium, …)`.

- [ ] **Step 3: Member editor** — keep the computed `boxHeight` (reason documented in the code) but use theme rows: `headerH = Theme.SectionLabelHeight, memberH = Theme.InlineRowHeight + 4f, availH = Theme.InlineRowHeight, gap = 2f`; box surface → `Widgets.AddInset(box)` instead of `PanelSurface`; section titles → `Widgets.SectionLabelRow(box.transform, $"Members_{pack.Id}", "pack.members_title".i18n())` (same for Available); empty → `Widgets.StatusLabel(box.transform, $"MembersEmpty_{pack.Id}", "pack.members_empty".i18n(), Theme.InkMuted, headerH)`; member rows → `Widgets.Row(box.transform, $"Member_{pack.Id}_{idx}", memberH)` with `padding = new RectOffset(16, 8, 2, 2)`, label `Widgets.InkLabel(label, preset?.Name ?? pack.PresetIds[idx], 14f, TextAlignmentOptions.MidlineLeft, preset != null ? Theme.Ink : Theme.StatusError)`; up/down → `Widgets.IconButton(memberRow.transform, "MemberUp", Icon.ArrowUp, Theme.IconSmall, …)` / `ArrowDown`; remove → `Widgets.IconButton(…, "MemberRemove", Icon.X, Theme.IconSmall, …)`; available rows → `Widgets.Row` + `Widgets.InkLabel(label, captured.Name, 13f)` + `Widgets.InlineLink(availRow.transform, "AvailAdd", "pack.member_add".i18n(), () => { … }, Icon.Add)`. Delete `AddMemberButton`.

- [ ] **Step 4: Build, deploy, smoke check** — Packs tab: New Pack creates a tinted band row; swatch click cycles the tint through all six colours (each readable); Members opens the inset box with ink rows; arrows reorder, X removes, `+` under Available adds; Export copies; red X deletes the pack; status line shows Ok / Error colours.

- [ ] **Step 5: Commit**

```bash
git add WrathTactics/UI/PackPanel.cs
git commit -m "feat(ui): packs tab and member editor on ink rows

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 14: Remove the old helpers, verify the colour sweep, document

**Files:**
- Modify: `WrathTactics/UI/UIHelpers.cs` — delete `AddPageLabel`, `AddHintCard`, `AddSurfaceLabel`, `PanelSurface`, `PanelHeaderSurface`, `EnsureAllHoverable`, `MakeButton`
- Modify: `WrathTactics/UI/PortraitToggleOverlay.cs` (:73-76) — backing stays, outline lines go: `UIHelpers.AddBackground(go, Theme.DimBackdrop);` then `var label = Widgets.BandLabel(go, "T", 14f, TextAlignmentOptions.Midline);`
- Modify: `WrathTactics/UI/PortraitToggleBadge.cs:16` — `OnColor` → `Theme.StatusOk` is too dark on the HUD; keep the badge colour but move it to `Theme.BadgeOn = new Color(0.35f, 0.9f, 0.35f)` and reference it.
- Modify: `WrathTactics/UI/TacticsPanel.cs:1115, :1154` — HUD-button fallback tints → `Theme.BandFallbackMauve` / `Theme.TitleFallback`
- Modify: `claude-context/gotchas-ui.md`, `docs/wrath-api-deep-dive.md` (one line: the v1.26.0 outline incident is superseded), `WrathTactics/Assets/icons/SOURCES.md` (remove rows of any sprite no longer referenced — check `inner_parchment.png` is still used by the page sheet: yes, keep)

- [ ] **Step 1: Delete the helpers and fix the build** — remove the members listed above; build; fix every remaining caller by routing it through `Widgets` (there should be none left — if the build names one, the task that owned it was incomplete: fix it here and note it in the commit).

- [ ] **Step 2: Colour sweep**

```bash
cd /home/pascal/Code/wrath-mods/wrath-tactics
rtk proxy grep -rn 'new Color' WrathTactics/UI | grep -v -E 'UI/(Theme|PackPalette)\.cs'
```
Expected: no output. Also `rtk proxy grep -rn 'AddBackground' WrathTactics/UI | grep -v -E 'UI/(Widgets|UIHelpers)\.cs'` — expected: only the `TacticsPanel` backdrop / page-sheet fallback lines and `PaperPopup`; anything else moves into a `Widgets` call.

- [ ] **Step 3: Update `claude-context/gotchas-ui.md`** — replace the three bullets "Hint/explainer strips…", "Readability on the book-page art…", "A group of related rows belongs in ONE dark container…" with:

```markdown
- **Every surface and control comes from `UI/Widgets.cs`, every colour and metric from `UI/Theme.cs`** (spec `docs/superpowers/specs/2026-09-26-ui-ink-on-parchment-design.md`). No `new Color` and no `AddBackground` outside those two files (`PackPalette` excepted); regression check before merge: `grep -rn 'new Color' WrathTactics/UI | grep -v -E 'Theme|PackPalette'` must be empty.
- **Nothing is drawn on the book illustration** — the page sheet (`TacticsPanel.CreatePanel`, "PageSheet") sits under everything below the tab bar. Text goes on the sheet in ink (`Widgets.InkLabel`), on a band in `BandText`, or in a hint card. The 2026-07 outline/dark-surface experiments are history.
- **Band sprites multiply `Image.color`** — tint a band only through `Theme.PackBandTint` (channel-normalised), never with a raw palette colour: rust/plum would black the band out.
- **Rows are HorizontalLayoutGroups with proportional `flexibleWidth`** (`Widgets.Row` + `Widgets.InRow`; `PopupSelector.CreateInRow`, `CreateTMPInputFieldInRow`). Sibling order = visual order — reposition with `SetSiblingIndex`, not anchors. The old fractional-anchor call sites are gone; do not reintroduce `SetAnchor` inside a row.
- **Popups are `Widgets.PaperPopup`** — dim + paper + inset content; callers attach outside-click to `OverlayButton` and their own Escape handler (`EscapeCloser` for transient pickers).
- **`Widgets.HintCard` keeps a fixed `preferredHeight`** (`Theme.HintHeight` / `HintHeightShort`): a longer string clips silently — keep replacements no longer, or raise the height.
```

Keep the `AddPageLabel` sentence out entirely (it no longer exists). Update the `Widgets & Helpers` input-field bullet to name `CreateTMPInputFieldInRow` as the row variant.

- [ ] **Step 4: Final smoke and screenshots** — full pass of spec §6 on the Deck: Ctrl+T / HUD button / ESC / X; every tab; font scale 0.5 / 1.0 / 2.0; all popups; preset link/unlink; pack apply/detach/remove; save list as pack; export/import round-trip. Missing-sprite drill (Review Focus 3): on the Deck `mv Mods/WrathTactics/Assets/icons Mods/WrathTactics/Assets/icons.off`, open the panel — flat fallbacks everywhere, `grep -c 'Sprite .* missing' <latest log>` = 32, `grep -c 'Exception' <latest log>` unchanged; move the folder back. Save two Deck screenshots (Steam + R1) as `docs/superpowers/specs/2026-09-26-ui-mockups/after-global.png` and `after-rule.png` (pull with `scp deck-direct:~/.local/share/Steam/userdata/48988922/760/remote/1184370/screenshots/<file>.jpg …`, convert with PIL).

- [ ] **Step 5: Commit**

```bash
git add -A WrathTactics/UI claude-context/gotchas-ui.md docs
git commit -m "refactor(ui): drop pre-theme helpers, document the Widgets/Theme rule

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

Version bump and release are not part of this plan — run `/release minor` afterwards (csproj must still hold 1.31.1 when it runs).
