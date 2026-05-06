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

## Re-extraction procedure

Source: `Wrath_Data/sharedassets0.assets` (and `.resS` companion). On Linux:
```bash
python3 -m venv ~/.local/opt/unitypy-venv
~/.local/opt/unitypy-venv/bin/pip install UnityPy Pillow
# copy sharedassets0.assets + .resS from the game install (we used scp from Steam Deck)
~/.local/opt/unitypy-venv/bin/python /tmp/wrath-assets/extract.py
```

## Re-export when Wrath updates

If a Wrath patch breaks visual consistency (sprite hash changed),
re-extract using the procedure above. The `Sprite.border` Vector4 values
are hardcoded in `WrathTactics/UI/ThemeProvider.cs` — if the new sprite has
different 9-slice metrics, update the border constants there.
