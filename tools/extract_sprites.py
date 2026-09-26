#!/usr/bin/env python3
"""Extract the UI sprites Wrath Tactics bundles from the game's sharedassets0.assets.

Usage:
  ~/.local/opt/unitypy-venv/bin/python tools/extract_sprites.py <dir-with-sharedassets0.assets> [out-dir]

The .resS companion must sit next to the .assets file. Default out-dir is
WrathTactics/Assets/icons. Prints the Unity 9-slice border (left, bottom, right, top)
of every sprite so ThemeProvider.cs can be checked against the current game build.
"""
import os
import sys

import UnityPy

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
    if len(sys.argv) < 2:
        print(__doc__, file=sys.stderr)
        sys.exit(2)
    src_dir = sys.argv[1]
    repo_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    out_dir = sys.argv[2] if len(sys.argv) > 2 else os.path.join(repo_root, "WrathTactics", "Assets", "icons")
    os.makedirs(out_dir, exist_ok=True)

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
        done[target] = True
        print(f"{target:24s} {d.m_Name:34s} {int(d.m_Rect.width)}x{int(d.m_Rect.height)}"
              f"  border L,B,R,T = {int(b.x)},{int(b.y)},{int(b.z)},{int(b.w)}")

    missing = sorted(set(WANTED) - set(done))
    if missing:
        print("MISSING: " + ", ".join(missing), file=sys.stderr)
        sys.exit(1)


if __name__ == "__main__":
    main()
