using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WrathTactics.Localization;
using WrathTactics.Logging;
using WrathTactics.Models;

namespace WrathTactics.UI {
    public partial class RuleEditorWidget {
        void SetupActionRow(Transform parent) {
            var (row, rowRect) = UIHelpers.Create("ActionRow", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 28;

            // "THEN:" label
            var (lbl, lblRect) = UIHelpers.Create("ThenLabel", row.transform);
            lblRect.SetAnchor(0, 0.1, 0, 1);
            lblRect.sizeDelta = Vector2.zero;
            UIHelpers.AddLabel(lbl, "section.then".i18n(), 16f, TextAlignmentOptions.MidlineLeft,
                new Color(0.15f, 0.10f, 0.06f));

            // Action type popup selector
            var actionNames = EnumLabels.NamesFor<ActionType>();
            PopupSelector.Create(row, "ActionType", 0.11f, 0.38f, actionNames,
                (int)rule.Action.Type, idx => {
                    rule.Action.Type = (ActionType)idx;
                    rule.Action.AbilityId = "";
                    if ((ActionType)idx != ActionType.CastSpell
                        && (ActionType)idx != ActionType.CastAbility) {
                        rule.Action.Sources = SpellSourceMask.All;
                        rule.Action.FallbackAbilityIds?.Clear();
                    }
                    RefreshSpellSelector((ActionType)idx);
                    PersistEdit();
                });

            // Spell/ability popup selector (right side)
            SetupSpellSelector(row);
        }

        void SetupSpellSelector(GameObject row) {
            // For Heal action, show HealMode + HealEnergy + HealSources selectors instead of
            // a spell picker. Three slots across the action row: Mode (0.39-0.50),
            // Energy (0.51-0.65), Sources (0.66-0.88).
            if (rule.Action.Type == ActionType.Heal) {
                var healModeNames = EnumLabels.NamesFor<HealMode>();
                PopupSelector.Create(row, "HealMode", 0.39f, 0.50f, healModeNames,
                    (int)rule.Action.HealMode, idx => {
                        rule.Action.HealMode = (HealMode)idx;
                        PersistEdit();
                    });

                // Energy pin: Auto (default) / Positive / Negative. None is a ClassifyHeal
                // sentinel and intentionally omitted from the dropdown.
                var energyValues = new List<HealEnergyType> {
                    HealEnergyType.Auto, HealEnergyType.Positive, HealEnergyType.Negative,
                };
                var energyLabels = new List<string> {
                    EnumLabels.For(HealEnergyType.Auto),
                    EnumLabels.For(HealEnergyType.Positive),
                    EnumLabels.For(HealEnergyType.Negative),
                };
                int energyIdx = energyValues.IndexOf(rule.Action.HealEnergy);
                if (energyIdx < 0) energyIdx = 0;
                PopupSelector.Create(row, "HealEnergy", 0.51f, 0.65f, energyLabels, energyIdx, idx => {
                    rule.Action.HealEnergy = energyValues[idx];
                    PersistEdit();
                });

                // Source mask selector — 7 curated combinations (2^3 - "None" sentinel).
                var sourceLabels = new List<string> {
                    "source.all".i18n(), "source.spell_only".i18n(), "source.scroll_only".i18n(), "source.potion_only".i18n(),
                    "source.spell_scroll".i18n(), "source.spell_potion".i18n(), "source.scroll_potion".i18n(),
                };
                var sourceValues = new List<HealSourceMask> {
                    HealSourceMask.All,
                    HealSourceMask.Spell,
                    HealSourceMask.Scroll,
                    HealSourceMask.Potion,
                    HealSourceMask.Spell  | HealSourceMask.Scroll,
                    HealSourceMask.Spell  | HealSourceMask.Potion,
                    HealSourceMask.Scroll | HealSourceMask.Potion,
                };
                int srcIdx = sourceValues.IndexOf(rule.Action.HealSources);
                if (srcIdx < 0) srcIdx = 0;
                PopupSelector.Create(row, "HealSources", 0.66f, 0.88f, sourceLabels, srcIdx, idx => {
                    rule.Action.HealSources = sourceValues[idx];
                    PersistEdit();
                });
                return;
            }

            if (rule.Action.Type == ActionType.ThrowSplash) {
                var splashModeNames = EnumLabels.NamesFor<ThrowSplashMode>();
                PopupSelector.Create(row, "SplashMode", 0.39f, 0.7f, splashModeNames,
                    (int)rule.Action.SplashMode, idx => {
                        rule.Action.SplashMode = (ThrowSplashMode)idx;
                        PersistEdit();
                    });
                return;
            }

            // ToggleActivatable: mode dropdown (On/Off) + ability picker side by side
            if (rule.Action.Type == ActionType.ToggleActivatable) {
                var toggleModeNames = EnumLabels.NamesFor<ToggleMode>();
                PopupSelector.Create(row, "ToggleMode", 0.39f, 0.52f, toggleModeNames,
                    (int)rule.Action.ToggleMode, idx => {
                        rule.Action.ToggleMode = (ToggleMode)idx;
                        PersistEdit();
                    });

                BuildSpellPickerButton(row, 0.53f, 1.0f);
                return;
            }

            bool isCastSpell = rule.Action.Type == ActionType.CastSpell;
            float pickerXMax = isCastSpell ? 0.55f : 1.0f;
            BuildSpellPickerButton(row, 0.39f, pickerXMax);

            if (isCastSpell) {
                // Rod dropdown — index 0 = (none) -> Action.MetamagicRod = null,
                // indices 1..10 = MetamagicValues[i-1].
                var rodLabels = EnumLabels.RodDropdownLabels();
                int rodIdx = rule.Action.MetamagicRod == null
                    ? 0
                    : System.Array.IndexOf(EnumLabels.MetamagicValues, rule.Action.MetamagicRod.Value) + 1;
                if (rodIdx < 0) rodIdx = 0;
                PopupSelector.Create(row, "MetamagicRod", 0.56f, 0.77f, rodLabels, rodIdx, idx => {
                    rule.Action.MetamagicRod = idx == 0
                        ? (Kingmaker.UnitLogic.Abilities.Metamagic?)null
                        : EnumLabels.MetamagicValues[idx - 1];
                    PersistEdit();
                });

                // Source mask dropdown — 7 curated combinations, same pattern as HealSources.
                var sourceLabels = new List<string> {
                    "source.all".i18n(), "source.spell_only".i18n(), "source.scroll_only".i18n(), "source.potion_only".i18n(),
                    "source.spell_scroll".i18n(), "source.spell_potion".i18n(), "source.scroll_potion".i18n(),
                };
                var sourceValues = new List<SpellSourceMask> {
                    SpellSourceMask.All,
                    SpellSourceMask.Spell,
                    SpellSourceMask.Scroll,
                    SpellSourceMask.Potion,
                    SpellSourceMask.Spell  | SpellSourceMask.Scroll,
                    SpellSourceMask.Spell  | SpellSourceMask.Potion,
                    SpellSourceMask.Scroll | SpellSourceMask.Potion,
                };
                int srcIdx = sourceValues.IndexOf(rule.Action.Sources);
                if (srcIdx < 0) srcIdx = 0;
                PopupSelector.Create(row, "SpellSources", 0.78f, 1.0f, sourceLabels, srcIdx, idx => {
                    rule.Action.Sources = sourceValues[idx];
                    PersistEdit();
                });
            }

            // Hide if not applicable
            bool showSelector = rule.Action.Type != ActionType.AttackTarget &&
                                rule.Action.Type != ActionType.DoNothing &&
                                rule.Action.Type != ActionType.ThrowSplash;
            if (spellPickerButton != null)
                spellPickerButton.SetActive(showSelector);
        }

        // Builds the spell-picker button (icon + label + arrow) that opens SpellPickerOverlay
        // on click. Resolves the current SpellEntry list for the rule's ActionType up front,
        // and auto-persists the first entry when no AbilityId is set yet.
        void BuildSpellPickerButton(GameObject row, float xMin, float xMax) {
            var entries = GetSpellEntries(rule.Action.Type);
            currentSpellEntries = entries;

            SpellDropdownProvider.SpellEntry selected = default;
            bool found = false;
            if (!string.IsNullOrEmpty(rule.Action.AbilityId)) {
                foreach (var e in entries) {
                    if (e.Guid == rule.Action.AbilityId) { selected = e; found = true; break; }
                }
            }
            if (!found && entries.Count > 0) {
                selected = entries[0];
                if (string.IsNullOrEmpty(rule.Action.AbilityId)) {
                    rule.Action.AbilityId = selected.Guid;
                    PersistEdit();
                }
            }

            var (btnObj, btnRect) = UIHelpers.Create("SpellPick", row.transform);
            btnRect.SetAnchor(xMin, xMax, 0, 1);
            btnRect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(btnObj, new Color(0.22f, 0.22f, 0.22f, 1f));

            // Icon slot (left, 24x24) — added regardless so UpdateSpellPickerButton
            // can toggle the sprite on/off without re-parenting.
            var (iconGO, iconRect) = UIHelpers.Create("Icon", btnObj.transform);
            iconRect.SetAnchor(0, 0, 0.5, 0.5);
            iconRect.pivot = new Vector2(0, 0.5f);
            iconRect.anchoredPosition = new Vector2(4, 0);
            iconRect.sizeDelta = new Vector2(24, 24);
            spellPickerIcon = iconGO.AddComponent<Image>();
            spellPickerIcon.raycastTarget = false;

            spellPickerLabel = UIHelpers.AddLabel(btnObj,
                found || entries.Count > 0 ? selected.Name : "placeholder.none_available".i18n(),
                15f, TextAlignmentOptions.MidlineLeft);
            spellPickerLabel.margin = new Vector4(32, 0, 20, 0);

            var (arrow, arrowRect) = UIHelpers.Create("Arrow", btnObj.transform);
            arrowRect.SetAnchor(0.88, 1, 0, 1);
            arrowRect.sizeDelta = Vector2.zero;
            UIHelpers.AddLabel(arrow, "v", 14f, TextAlignmentOptions.Midline,
                new Color(0.6f, 0.6f, 0.6f));

            spellPickerButton = btnObj;
            UpdateSpellPickerButton(selected, entries.Count > 0);

            btnObj.AddComponent<Button>().onClick.AddListener(() => {
                if (currentSpellEntries == null || currentSpellEntries.Count == 0) return;
                SpellPickerOverlay.Open(currentSpellEntries, rule.Action.AbilityId, picked => {
                    rule.Action.AbilityId = picked.Guid;
                    UpdateSpellPickerButton(picked, true);
                    PersistEdit();
                });
            });
        }

        void UpdateSpellPickerButton(SpellDropdownProvider.SpellEntry entry, bool haveEntries) {
            if (spellPickerLabel != null)
                spellPickerLabel.text = haveEntries ? entry.Name : "placeholder.none_available".i18n();
            if (spellPickerIcon != null) {
                spellPickerIcon.sprite = haveEntries ? entry.Icon : null;
                spellPickerIcon.enabled = haveEntries && entry.Icon != null;
            }
        }

        // Fallback-chain rendering for CastSpell / CastAbility rules. Renders one row per fallback id
        // (indent arrow + picker button + delete X) plus a "+ Fallback" button at the bottom.
        // Rebuilds the entire body on add/delete to keep index-capture semantics simple.
        void SetupFallbackRows(Transform parent) {
            if (rule.Action.Type != ActionType.CastSpell
                && rule.Action.Type != ActionType.CastAbility) return;
            if (rule.Action.FallbackAbilityIds == null)
                rule.Action.FallbackAbilityIds = new List<string>();

            for (int i = 0; i < rule.Action.FallbackAbilityIds.Count; i++) {
                int captured = i;
                BuildFallbackRow(parent, captured);
            }

            var (addBtn, _) = UIHelpers.Create("AddFallback", parent);
            addBtn.AddComponent<LayoutElement>().preferredHeight = 22;
            UIHelpers.AddBackground(addBtn, new Color(0.2f, 0.25f, 0.35f, 1f));
            UIHelpers.AddLabel(addBtn, "button.add_fallback".i18n(), 14f, TextAlignmentOptions.Midline);
            addBtn.AddComponent<Button>().onClick.AddListener(() => {
                rule.Action.FallbackAbilityIds.Add("");
                PersistEdit();
                RebuildBody();
            });
        }

        void BuildFallbackRow(Transform parent, int index) {
            var entries = GetSpellEntries(rule.Action.Type);
            string current = rule.Action.FallbackAbilityIds[index];
            SpellDropdownProvider.SpellEntry selected = default;
            bool found = false;
            if (!string.IsNullOrEmpty(current)) {
                foreach (var e in entries) {
                    if (e.Guid == current) { selected = e; found = true; break; }
                }
            }
            if (!found && entries.Count > 0) {
                selected = entries[0];
                if (string.IsNullOrEmpty(current)) {
                    rule.Action.FallbackAbilityIds[index] = selected.Guid;
                    PersistEdit();
                }
            }

            var (row, _) = UIHelpers.Create($"Fallback_{index}", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 26;

            var (arrowLbl, arrowRect) = UIHelpers.Create("ArrowLbl", row.transform);
            arrowRect.SetAnchor(0.11, 0.17, 0, 1);
            arrowRect.sizeDelta = Vector2.zero;
            UIHelpers.AddLabel(arrowLbl, "↳", 18f, TextAlignmentOptions.Midline,
                new Color(0.6f, 0.6f, 0.5f));

            var (btn, btnRect) = UIHelpers.Create("FallbackPick", row.transform);
            btnRect.SetAnchor(0.18, 0.9, 0, 1);
            btnRect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(btn, new Color(0.22f, 0.22f, 0.22f, 1f));

            var (iconGO, iconRect) = UIHelpers.Create("Icon", btn.transform);
            iconRect.SetAnchor(0, 0, 0.5, 0.5);
            iconRect.pivot = new Vector2(0, 0.5f);
            iconRect.anchoredPosition = new Vector2(4, 0);
            iconRect.sizeDelta = new Vector2(20, 20);
            var icon = iconGO.AddComponent<Image>();
            icon.raycastTarget = false;
            icon.sprite = entries.Count > 0 ? selected.Icon : null;
            icon.enabled = entries.Count > 0 && selected.Icon != null;

            var label = UIHelpers.AddLabel(btn,
                entries.Count > 0 ? selected.Name : "placeholder.none_available".i18n(),
                14f, TextAlignmentOptions.MidlineLeft);
            label.margin = new Vector4(28, 0, 16, 0);

            btn.AddComponent<Button>().onClick.AddListener(() => {
                if (entries.Count == 0) return;
                SpellPickerOverlay.Open(entries, rule.Action.FallbackAbilityIds[index], picked => {
                    rule.Action.FallbackAbilityIds[index] = picked.Guid;
                    label.text = picked.Name;
                    icon.sprite = picked.Icon;
                    icon.enabled = picked.Icon != null;
                    PersistEdit();
                });
            });

            var (delBtn, delRect) = UIHelpers.Create("DeleteFallback", row.transform);
            delRect.SetAnchor(0.92, 1.0, 0, 1);
            delRect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(delBtn, new Color(0.4f, 0.2f, 0.2f, 1f));
            UIHelpers.AddLabel(delBtn, "X", 14f, TextAlignmentOptions.Midline);
            delBtn.AddComponent<Button>().onClick.AddListener(() => {
                rule.Action.FallbackAbilityIds.RemoveAt(index);
                PersistEdit();
                RebuildBody();
            });
        }

        void RefreshSpellSelector(ActionType actionType) {
            // Heal/ThrowSplash/ToggleActivatable/CastSpell/CastAbility need a full body rebuild (different row shape).
            // CastSpell/CastAbility are in this list because they show fallback-chain rows (and CastSpell
            // adds a Sources dropdown sibling) — without a full rebuild those would not appear when
            // switching to these types.
            if (actionType == ActionType.Heal || actionType == ActionType.ThrowSplash
                || actionType == ActionType.ToggleActivatable
                || actionType == ActionType.CastSpell || actionType == ActionType.CastAbility) {
                RebuildBody();
                return;
            }

            if (spellPickerButton == null) return;

            bool showSpell = actionType != ActionType.AttackTarget &&
                             actionType != ActionType.DoNothing &&
                             actionType != ActionType.ThrowSplash;
            spellPickerButton.SetActive(showSpell);

            if (!showSpell) return;

            var entries = GetSpellEntries(actionType);
            currentSpellEntries = entries;

            SpellDropdownProvider.SpellEntry first = default;
            if (entries.Count > 0) {
                first = entries[0];
                rule.Action.AbilityId = first.Guid;
                PersistEdit();
            } else {
                rule.Action.AbilityId = "";
            }
            UpdateSpellPickerButton(first, entries.Count > 0);
        }

        List<SpellDropdownProvider.SpellEntry> GetSpellEntries(ActionType actionType) {
            if (actionType == ActionType.AttackTarget || actionType == ActionType.DoNothing
                || actionType == ActionType.Heal || actionType == ActionType.ThrowSplash)
                return new List<SpellDropdownProvider.SpellEntry>();

            var unit = GetUnit(unitId);
            List<SpellDropdownProvider.SpellEntry> entries;

            if (unit == null) {
                // Global rules: combine spells from ALL party members
                entries = GetAllPartySpells(actionType);
            } else {
                switch (actionType) {
                    case ActionType.CastSpell:
                        entries = SpellDropdownProvider.GetSpells(unit);
                        break;
                    case ActionType.CastAbility:
                        entries = SpellDropdownProvider.GetAbilities(unit);
                        break;
                    case ActionType.UseItem:
                        entries = SpellDropdownProvider.GetItemAbilities(unit);
                        break;
                    case ActionType.ToggleActivatable:
                        entries = SpellDropdownProvider.GetActivatables(unit);
                        break;
                    default:
                        entries = new List<SpellDropdownProvider.SpellEntry>();
                        break;
                }
            }

            if (entries.Count == 0)
                entries.Add(new SpellDropdownProvider.SpellEntry("(none available)", ""));

            return entries;
        }

        List<SpellDropdownProvider.SpellEntry> GetAllPartySpells(ActionType actionType) {
            var combined = new List<SpellDropdownProvider.SpellEntry>();
            var seen = new HashSet<string>();
            var party = Game.Instance?.Player?.AllCharacters;
            if (party == null) return combined;

            foreach (var unit in party) {
                if (!unit.IsInGame || unit.HPLeft <= 0) continue;
                List<SpellDropdownProvider.SpellEntry> unitEntries;
                switch (actionType) {
                    case ActionType.CastSpell:
                        unitEntries = SpellDropdownProvider.GetSpells(unit);
                        break;
                    case ActionType.CastAbility:
                        unitEntries = SpellDropdownProvider.GetAbilities(unit);
                        break;
                    case ActionType.UseItem:
                        unitEntries = SpellDropdownProvider.GetItemAbilities(unit);
                        break;
                    case ActionType.ToggleActivatable:
                        unitEntries = SpellDropdownProvider.GetActivatables(unit);
                        break;
                    default:
                        continue;
                }
                foreach (var entry in unitEntries) {
                    if (seen.Add(entry.Guid))
                        combined.Add(entry);
                }
            }
            return combined.OrderBy(e => e.Name).ToList();
        }

        UnitEntityData GetUnit(string uid) {
            if (string.IsNullOrEmpty(uid)) return null;
            var party = Game.Instance?.Player?.Party;
            if (party == null) return null;
            var unit = party.FirstOrDefault(u => u.UniqueId == uid);
            if (unit == null)
                Log.UI.Debug($"GetUnit failed for unitId={uid}");
            return unit;
        }
    }
}
