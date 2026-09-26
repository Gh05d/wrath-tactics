# Sprite Sources

Sprites extracted from Wrath of the Righteous 1.4 via UnityPy
(`Wrath_Data/sharedassets0.assets`).

| File | Source asset | Substitution / notes |
|---|---|---|
| panel_background.png | `UI_BookEvent_Book_3796_1972` Sprite (4096×2048 native, downscaled to 2048×1024 in the bundled PNG to keep the mod ZIP small) | Used by the in-game Book Event illustration system — an open-book illustration, not a stretchable parchment. Rendered with `Image.Type.Simple` inside an `AspectRatioFitter` (mode=FitInParent, ratio=2.0). Earlier candidates (`UI_BackgroundModalWindow`, `UI_BackgroundTutorialPaper`, `UI_BackgroundPaper_Console_3756_2000`) all looked flat-tan because they're meant to be 9-sliced or panel-tiled, not standalone book illustrations. |
| titlebar_background.png | `UI_Loot_BackgroundCaption` (740×134) | Substitute: no UI_Window_Title_Bg in 1.4 atlas; loot caption is the closest title-bar style |
| close_button_normal.png | `UI_EscButton_Default` (75×77) |  |
| close_button_hover.png | `UI_EscButton_Hover` (75×77) |  |
| close_button_pressed.png | `UI_EscButton_Hover` (75×77) | Substitute: atlas has no pressed variant for EscButton; reuse hover sprite |
| action_button_normal.png | `UI_Button_Default` (532×116) |  |
| action_button_hover.png | `UI_Button_Hover` (532×116) |  |
| action_button_pressed.png | `UI_Button_Click` (532×116) | Substitute: 'Click' is Owlcat's term for the pressed/down state |
| tab_header_active.png | `UI_BoxButton_Hover` (75×77) | Substitute: no UI_Tab_Active in 1.4; BoxButton_Hover gives a highlighted-tab look |
| tab_header_inactive.png | `UI_BoxButton_Default` (75×77) | Substitute: no UI_Tab_Inactive in 1.4; BoxButton_Default gives a flat-tab look |
| scrollbar_track.png | `UI_ScrollVertical_BackLine` (3×241) |  |
| scrollbar_handle.png | `UI_ScrollVertical_Handl_Default` (23×95) |  |
| hud_button.png | `UI_HudIcon_Character_Default` (106×106) | From the `Bundles/ui` bundle (not sharedassets0). The helmet icon of the vanilla ingame-menu Character button — bundled so the HUD button never falls back to a flat brown square when runtime canvas extraction fails (e.g. controller/console UI mode). |
| hud_button_hover.png | `UI_HudIcon_Character_Hover` (106×106) | Hover state for the above (SpriteSwap). |
| band_mauve.png | `UI_Settings_BackValue` (391×63, border 112/0/112/0) | Brush-stroke value background of the Settings menu. Rule-card headers, dropdown triggers, selected popup rows. |
| band_blue.png | `UI_Settings_BackValueBlue` (391×63, border 112/0/112/0) | Blue variant: headers of preset-linked rules. |
| popup_paper.png | `UI_BackgroundTooltipPaper` (554×358, border 139/101/145/89) | Tooltip parchment sheet — every popup (dropdown list, pickers, save-as-pack). |
| hint_annotation.png | `UI_Journal_Annotation` (725×285, border 72/58/72/67) | Journal annotation box — hint cards. Drawn over `Theme.HintBacking`, its own fill is too transparent on parchment. |
| icon_add.png | `UI_CharScreen_IconAdd` (60×62) | Green plus — prefix of "+ Condition" style inline links. |
| icon_delete.png | `UI_CharScreen_IconDelete` (56×61) | Red brush X — delete rule / preset / pack. |
| icon_x.png / icon_x_hover.png | `UI_EscIcon_Default` / `_Hover` (49×49) | Ink X — delete condition / fallback, close popup, clear filter. |
| icon_check.png / icon_check_hover.png | `UI_CheckIcon_Default` / `_Hover` (40×43) | Chevron — dropdown arrow (Default, scaled), checkbox tick (Hover). |
| icon_arrow.png / icon_arrow_hover.png | `UI_RoundButtonNextIcon_Default` / `_Hover` (48×44) | Triangle — move up/down (rotated ∓90°). |
| toggle_on.png / toggle_off.png | `UI_PointButtonBig_Hover` / `_Default` (40×40) | Round point button — rule ON / OFF dot, pack colour dot. |
| divider_flourish.png | `UI_CharScreen_Separator2` (1041×46, border 114/0/94/0) | Line with flourish ends — section dividers. |
| divider_line.png | `UI_WightLine_Simple` (586×7, border 16/0/16/0) | Thin line — OR divider. |
| input_frame.png | `UI_Loot_Slots` (135×135, border 12/11/13/13) | Sketched frame — reserved for text inputs (currently inputs use `Widgets.AddInkFrame`). |

## Re-extraction procedure

Source: `Wrath_Data/sharedassets0.assets` (and `.resS` companion). On Linux:
```bash
python3 -m venv ~/.local/opt/unitypy-venv
~/.local/opt/unitypy-venv/bin/pip install UnityPy Pillow
# copy sharedassets0.assets + .resS from the game install (we used scp from Steam Deck)
~/.local/opt/unitypy-venv/bin/python tools/extract_sprites.py <dir-with-sharedassets0.assets>   # prints each sprite's border
```

## Re-export when Wrath updates

If a Wrath patch breaks visual consistency (sprite hash changed),
re-extract using the procedure above. The `Sprite.border` Vector4 values
are hardcoded in `WrathTactics/UI/ThemeProvider.cs` — if the new sprite has
different 9-slice metrics, update the border constants there.
