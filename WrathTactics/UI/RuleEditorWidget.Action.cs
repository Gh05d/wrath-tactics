using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.EntitySystem.Entities;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WrathTactics.Localization;
using WrathTactics.Logging;
using WrathTactics.Models;

namespace WrathTactics.UI {
    public partial class RuleEditorWidget {
        static readonly RangeBracket[] MoveWithinBrackets = {
            RangeBracket.Melee, RangeBracket.Cone, RangeBracket.Short,
            RangeBracket.Medium, RangeBracket.Far, RangeBracket.Long
        };

        void SetupActionRow(Transform parent) {
            var row = Widgets.Row(parent, "ActionRow", Theme.RowHeight);

            // "THEN" label
            var (lbl, _) = UIHelpers.Create("ThenLabel", row.transform);
            Widgets.InRow(lbl, 0f, 0.10f);
            Widgets.SectionLabel(lbl, "section.then".i18n());

            // Action type popup selector
            var actionNames = EnumLabels.NamesFor<ActionType>();
            PopupSelector.CreateInRow(row, "ActionType", 0.27f, actionNames,
                (int)rule.Action.Type, idx => {
                    rule.Action.Type = (ActionType)idx;
                    rule.Action.AbilityId = "";
                    if ((ActionType)idx != ActionType.CastSpell
                        && (ActionType)idx != ActionType.CastAbility) {
                        rule.Action.Sources = SpellSourceMask.All;
                        rule.Action.FallbackAbilityIds?.Clear();
                    }
                    // Offensive actions flip the constructor default (Self) to a threat-based
                    // selector; the whole body is rebuilt so the target row shows the new value.
                    var defaultTarget = TargetDefaults.ForAction((ActionType)idx, rule.Target.Type);
                    if (defaultTarget.HasValue) {
                        rule.Target.Type = defaultTarget.Value;
                        PersistEdit();
                        RebuildBody();
                        return;
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
                PopupSelector.CreateInRow(row, "HealMode", 0.11f, healModeNames,
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
                PopupSelector.CreateInRow(row, "HealEnergy", 0.14f, energyLabels, energyIdx, idx => {
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
                PopupSelector.CreateInRow(row, "HealSources", 0.22f, sourceLabels, srcIdx, idx => {
                    rule.Action.HealSources = sourceValues[idx];
                    PersistEdit();
                });
                return;
            }

            if (rule.Action.Type == ActionType.ThrowSplash) {
                var splashModeNames = EnumLabels.NamesFor<ThrowSplashMode>();
                PopupSelector.CreateInRow(row, "SplashMode", 0.31f, splashModeNames,
                    (int)rule.Action.SplashMode, idx => {
                        rule.Action.SplashMode = (ThrowSplashMode)idx;
                        PersistEdit();
                    });
                return;
            }

            // SwitchWeaponSet: Set 1-4 dropdown. UnitBody allocates exactly 4 HandsEquipmentSets
            // (verified in Kingmaker.Items.UnitBody ctor IL — `ldc.i4.4 / newarr HandsEquipmentSet`).
            // We render all 4 unconditionally — empty sets are valid (unarmed fallback) and the
            // validator catches out-of-bounds at runtime if a future patch ever changes the count.
            if (rule.Action.Type == ActionType.SwitchWeaponSet) {
                var setLabels = new List<string>(4);
                for (int i = 0; i < 4; i++) setLabels.Add(Strings.Format("weapon_set.label", i + 1));
                int selectedIdx = rule.Action.WeaponSetIndex;
                if (selectedIdx < 0 || selectedIdx >= 4) selectedIdx = 0;
                PopupSelector.CreateInRow(row, "WeaponSet", 0.31f, setLabels, selectedIdx, idx => {
                    rule.Action.WeaponSetIndex = idx;
                    PersistEdit();
                });
                return;
            }

            // MoveToTarget: bracket dropdown ("stop within ..."), same display order as the
            // range conditions (Far sits between Medium and Long by distance).
            if (rule.Action.Type == ActionType.MoveToTarget) {
                var brackets = MoveWithinBrackets;
                var bracketLabels = new List<string>(brackets.Length);
                foreach (var b in brackets) bracketLabels.Add(EnumLabels.For(b));
                int selectedIdx = System.Array.IndexOf(brackets, rule.Action.MoveWithin);
                if (selectedIdx < 0) selectedIdx = 0;
                PopupSelector.CreateInRow(row, "MoveWithin", 0.31f, bracketLabels, selectedIdx, idx => {
                    rule.Action.MoveWithin = brackets[idx];
                    PersistEdit();
                });
                return;
            }

            // ToggleActivatable: mode dropdown (On/Off) + ability picker side by side
            if (rule.Action.Type == ActionType.ToggleActivatable) {
                var toggleModeNames = EnumLabels.NamesFor<ToggleMode>();
                PopupSelector.CreateInRow(row, "ToggleMode", 0.13f, toggleModeNames,
                    (int)rule.Action.ToggleMode, idx => {
                        rule.Action.ToggleMode = (ToggleMode)idx;
                        PersistEdit();
                    });

                BuildSpellPickerButton(row, 0.47f);
                return;
            }

            bool isCastSpell = rule.Action.Type == ActionType.CastSpell;
            float pickerFlex = isCastSpell ? 0.16f : 0.61f;
            BuildSpellPickerButton(row, pickerFlex);

            if (isCastSpell) {
                // Rod dropdown — index 0 = (none) -> Action.MetamagicRod = null,
                // indices 1..10 = MetamagicValues[i-1].
                var rodLabels = EnumLabels.RodDropdownLabels();
                int rodIdx = rule.Action.MetamagicRod == null
                    ? 0
                    : System.Array.IndexOf(EnumLabels.MetamagicValues, rule.Action.MetamagicRod.Value) + 1;
                if (rodIdx < 0) rodIdx = 0;
                PopupSelector.CreateInRow(row, "MetamagicRod", 0.21f, rodLabels, rodIdx, idx => {
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
                PopupSelector.CreateInRow(row, "SpellSources", 0.22f, sourceLabels, srcIdx, idx => {
                    rule.Action.Sources = sourceValues[idx];
                    PersistEdit();
                });
            }

            // Hide if not applicable
            bool showSelector = rule.Action.Type != ActionType.AttackTarget &&
                                rule.Action.Type != ActionType.DoNothing &&
                                rule.Action.Type != ActionType.ThrowSplash &&
                                rule.Action.Type != ActionType.SwitchWeaponSet &&
                                rule.Action.Type != ActionType.MoveToTarget;
            if (spellPickerButton != null)
                spellPickerButton.SetActive(showSelector);
        }

        // Builds the spell-picker button (icon + label + arrow) that opens SpellPickerOverlay
        // on click. Resolves the current SpellEntry list for the rule's ActionType up front,
        // and auto-persists the first entry when no AbilityId is set yet.
        void BuildSpellPickerButton(GameObject row, float flexibleWidth) {
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

            var btnObj = Widgets.BandDropdownShell(row.transform, "SpellPick",
                found || entries.Count > 0 ? selected.Name : "placeholder.none_available".i18n(),
                withIcon: true, out spellPickerLabel, out spellPickerIcon);
            Widgets.InRow(btnObj, 120f, flexibleWidth);
            var pickBtn = btnObj.AddComponent<Button>();
            pickBtn.targetGraphic = btnObj.GetComponent<Image>();
            Widgets.ApplyColorTint(pickBtn);

            spellPickerButton = btnObj;
            UpdateSpellPickerButton(selected, entries.Count > 0);

            pickBtn.onClick.AddListener(() => {
                if (currentSpellEntries == null || currentSpellEntries.Count == 0) return;
                SpellPickerOverlay.Open(currentSpellEntries, rule.Action.AbilityId, picked => {
                    rule.Action.AbilityId = picked.Guid;
                    UpdateSpellPickerButton(picked, true);
                    PersistEdit();
                    if (ApplyAbilityTargetDefault(picked.Guid)) RebuildBody();
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

            var addRow = Widgets.Row(parent, "AddFallback", Theme.InlineRowHeight);
            addRow.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset((int)(Theme.BandPaddingX * 2), 0, 0, 0);
            Widgets.InlineLink(addRow.transform, "AddFallbackLink", "button.add_fallback".i18n().TrimStart('+', ' '), () => {
                rule.Action.FallbackAbilityIds.Add("");
                PersistEdit();
                RebuildBody();
            }, Icon.Add);
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

            var row = Widgets.Row(parent, $"Fallback_{index}", Theme.RowHeight);

            var (arrowLbl, _) = UIHelpers.Create("ArrowLbl", row.transform);
            Widgets.InRow(arrowLbl, 0f, 0.10f);
            Widgets.InkLabel(arrowLbl, "\u21B3", 18f, TextAlignmentOptions.MidlineRight, Theme.InkMuted);

            var btn = Widgets.BandDropdownShell(row.transform, "FallbackPick",
                entries.Count > 0 ? selected.Name : "placeholder.none_available".i18n(),
                withIcon: true, out var label, out var icon);
            Widgets.InRow(btn, 120f, 0.72f);
            icon.sprite = entries.Count > 0 ? selected.Icon : null;
            icon.enabled = entries.Count > 0 && selected.Icon != null;
            var fbBtn = btn.AddComponent<Button>();
            fbBtn.targetGraphic = btn.GetComponent<Image>();
            Widgets.ApplyColorTint(fbBtn);

            fbBtn.onClick.AddListener(() => {
                if (entries.Count == 0) return;
                SpellPickerOverlay.Open(entries, rule.Action.FallbackAbilityIds[index], picked => {
                    rule.Action.FallbackAbilityIds[index] = picked.Guid;
                    label.text = picked.Name;
                    icon.sprite = picked.Icon;
                    icon.enabled = picked.Icon != null;
                    PersistEdit();
                });
            });

            Widgets.IconButton(row.transform, "DeleteFallback", Icon.X, Theme.IconSmall, () => {
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
                || actionType == ActionType.CastSpell || actionType == ActionType.CastAbility
                || actionType == ActionType.SwitchWeaponSet || actionType == ActionType.MoveToTarget) {
                RebuildBody();
                return;
            }

            if (spellPickerButton == null) return;

            bool showSpell = actionType != ActionType.AttackTarget &&
                             actionType != ActionType.DoNothing &&
                             actionType != ActionType.ThrowSplash &&
                             actionType != ActionType.SwitchWeaponSet &&
                             actionType != ActionType.MoveToTarget;
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

        /// <summary>
        /// Enemy-only ability picked while the target still sits on the constructor default
        /// (Self): switch to the threat selector. Returns true when the target changed.
        /// Resolves the variant blueprint when the key carries one — variants can differ
        /// from their parent in targeting.
        /// </summary>
        bool ApplyAbilityTargetDefault(string abilityKey) {
            if (rule.Target.Type != TargetType.Self || string.IsNullOrEmpty(abilityKey)) return false;
            var parsed = SpellDropdownProvider.ParseKey(abilityKey);
            var guid = string.IsNullOrEmpty(parsed.VariantGuid) ? parsed.BlueprintGuid : parsed.VariantGuid;
            var bp = ResourcesLibrary.TryGetBlueprint<BlueprintAbility>(guid);
            if (bp == null) return false;
            var defaultTarget = TargetDefaults.ForAbility(rule.Target.Type, bp.CanTargetEnemies, bp.CanTargetFriends, bp.CanTargetSelf);
            if (!defaultTarget.HasValue) return false;
            rule.Target.Type = defaultTarget.Value;
            PersistEdit();
            return true;
        }

        List<SpellDropdownProvider.SpellEntry> GetSpellEntries(ActionType actionType) {
            if (actionType == ActionType.AttackTarget || actionType == ActionType.DoNothing
                || actionType == ActionType.Heal || actionType == ActionType.ThrowSplash
                || actionType == ActionType.SwitchWeaponSet || actionType == ActionType.MoveToTarget)
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
