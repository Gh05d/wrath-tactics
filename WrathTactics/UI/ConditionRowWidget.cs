using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WrathTactics.Engine;
using WrathTactics.Localization;
using WrathTactics.Models;

namespace WrathTactics.UI {
    public class ConditionRowWidget : MonoBehaviour {
        Condition condition;
        Action onChanged;
        Action onDelete;

        PopupSelector propertySelector;

        static readonly List<string> RangeBracketNames = new List<string> {
            nameof(RangeBracket.Melee),
            nameof(RangeBracket.Cone),
            nameof(RangeBracket.Short),
            nameof(RangeBracket.Medium),
            nameof(RangeBracket.Far),
            nameof(RangeBracket.Long)
        };

        public void Init(Condition condition, Action onChanged, Action onDelete) {
            this.condition = condition;
            this.onChanged = onChanged;
            this.onDelete = onDelete;
            BuildUI();
        }

        void Rebuild() {
            // Detach before Destroy: Destroy lands end-of-frame and the doomed children would
            // still take HLG slots for that frame (one-frame squeeze on every Subject change).
            for (int i = transform.childCount - 1; i >= 0; i--) {
                var child = transform.GetChild(i);
                child.SetParent(null, false);
                Destroy(child.gameObject);
            }
            var le = GetComponent<LayoutElement>();
            if (le != null) Destroy(le);
            // DestroyImmediate: a deferred Destroy leaves two layout controllers for a frame.
            var hlg = GetComponent<HorizontalLayoutGroup>();
            if (hlg != null) DestroyImmediate(hlg);
            propertySelector = null;
            BuildUI();
        }

        void BuildUI() {
            var root = gameObject;

            root.AddComponent<LayoutElement>().preferredHeight = Theme.RowHeight;
            // Layout-group row: children carry flexibleWidth = their old anchor span, so the
            // proportions survive; sibling order is the visual order (SetSiblingIndex, not anchors).
            var hlg = root.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = Theme.RowGap;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.padding = new RectOffset((int)Theme.BandPaddingX, 0, 2, 2);

            // Subject popup selector — narrow enough to leave room for count layout
            var subjectNames = EnumLabels.NamesFor<ConditionSubject>();
            PopupSelector.CreateInRow(root, "Subject", 0.15f, subjectNames,
                (int)condition.Subject, v => {
                    condition.Subject = (ConditionSubject)v;
                    // Reset property to first valid for new subject
                    var validProps = GetPropertiesForSubject(condition.Subject);
                    if (!validProps.Contains(condition.Property) && validProps.Count > 0)
                        condition.Property = validProps[0];
                    onChanged?.Invoke();
                    Rebuild();
                });

            // Property popup selector (for non-count: 0.16→0.37; repositioned below for count)
            var props = GetPropertiesForSubject(condition.Subject);
            var propNames = props.Select(p => EnumLabels.For(p)).ToList();
            int propIdx = props.IndexOf(condition.Property);
            if (propIdx < 0) propIdx = 0;
            propertySelector = PopupSelector.CreateInRow(root, "Property", 0.21f,
                propNames, propIdx, v => {
                    var currentProps = GetPropertiesForSubject(condition.Subject);
                    if (v < currentProps.Count) condition.Property = currentProps[v];
                    onChanged?.Invoke();
                    Rebuild();
                });

            bool isCountSubject = condition.Subject == ConditionSubject.AllyCount
                || condition.Subject == ConditionSubject.EnemyCount;
            bool isAllyByNameSubject = condition.Subject == ConditionSubject.AllyByName;
            bool isHasCondition = condition.Property == ConditionProperty.HasCondition;

            if (isCountSubject || isAllyByNameSubject) {
                // Compressed layout shared with count subjects:
                // [Subject 0→0.15] [LeftWidget 0.16→0.30] ["with" 0.31→0.37]
                //   [Property 0.38→0.58] [Op/Value 0.58→0.88] [X 0.9→1.0]
                // Count form reads "AllyCount >= 2 with HpPercent < 60";
                // AllyByName form reads "AllyByName: Pet1 with HpPercent < 60".

                if (isCountSubject) {
                    // Count-threshold operator + integer input occupies 0.16-0.30
                    var countOpNames = new List<string> { "<", ">", "=", "!=", ">=", "<=" };
                    PopupSelector.CreateInRow(root, "CountThresholdOperator", 0.06f,
                        countOpNames, (int)condition.CountOperator, v => {
                            condition.CountOperator = (ConditionOperator)v;
                            onChanged?.Invoke();
                        });

                    var countInput = UIHelpers.CreateTMPInputFieldInRow(root, "CountValue", 60f, 0.07f, condition.Value2 ?? "1", 16f,
                        TMP_InputField.ContentType.IntegerNumber);
                    countInput.onEndEdit.AddListener(v => {
                        condition.Value2 = v;
                        onChanged?.Invoke();
                    });
                } else {
                    // AllyByName: ally picker (live PartyAndPets). Stored in Value2 as UniqueId.
                    CreateAllyPicker(root, 0.14f, "AllyByNamePicker",
                        allowAny: false, condition.Value2,
                        v => { condition.Value2 = v; onChanged?.Invoke(); });
                }

                // "with" label
                var (withLbl, _w) = UIHelpers.Create("WithLabel", root.transform);
                Widgets.InRow(withLbl, 0f, 0.06f);
                Widgets.InkLabel(withLbl, "condition.with".i18n(), 14f, TextAlignmentOptions.Midline,
                    Theme.InkMuted, italic: true);

                // Property selector already placed at 0.16→0.37 above — move it to 0.38→0.58
                // (propertySelector was created before this block, so we reposition it)
                if (propertySelector != null) {
                    propertySelector.transform.SetSiblingIndex(withLbl.transform.GetSiblingIndex() + 1);
                    Widgets.InRow(propertySelector.gameObject, 60f, 0.20f);
                }

                // Determine which value widget to show based on property type
                bool propNeedsOperator = condition.Property == ConditionProperty.HpPercent
                    || condition.Property == ConditionProperty.HpFlat
                    || condition.Property == ConditionProperty.AC
                    || condition.Property == ConditionProperty.HitDice
                    || condition.Property == ConditionProperty.SpellDCMinusSave
                    || condition.Property == ConditionProperty.ABMinusAC
                    || condition.Property == ConditionProperty.EnemyHDMinusPartyLevel
                    || condition.Property == ConditionProperty.WithinRange;

                if (propNeedsOperator) {
                    // Operator selector where the "<" label was
                    var opNames = new List<string> { "<", ">", "=", "!=", ">=", "<=" };
                    PopupSelector.CreateInRow(root, "CountOperator", 0.08f, opNames,
                        (int)condition.Operator, v => {
                            condition.Operator = (ConditionOperator)v;
                            onChanged?.Invoke();
                            // Bracket labels are operator-dependent — rebuild so
                            // they refresh live (same pattern as Subject/Property).
                            if (condition.Property == ConditionProperty.WithinRange) Rebuild();
                        });

                    if (condition.Property == ConditionProperty.WithinRange) {
                        var bracketNames = RangeBracketNames;
                        var bracketLabels = GetRangeBracketLabels(condition.Operator);
                        int brIdx = bracketNames.IndexOf(condition.Value);
                        if (brIdx < 0) { brIdx = 2; condition.Value = bracketNames[brIdx]; } // default: Short
                        PopupSelector.CreateInRow(root, "CountRangeBracketValue", 0.21f, bracketLabels, brIdx, v => {
                            condition.Value = bracketNames[v];
                            onChanged?.Invoke();
                        });
                    } else {
                        // Value input on the right
                        var valueInput = UIHelpers.CreateTMPInputFieldInRow(root, "Value", 60f, 0.21f, condition.Value ?? "", 16f);
                        valueInput.onEndEdit.AddListener(v => {
                            condition.Value = v;
                            onChanged?.Invoke();
                        });
                    }
                } else if (condition.Property == ConditionProperty.CreatureType
                    || condition.Property == ConditionProperty.Alignment
                    || condition.Property == ConditionProperty.HasBuff
                    || condition.Property == ConditionProperty.HasCondition
                    || condition.Property == ConditionProperty.HasDescriptorEffect
                    || condition.Property == ConditionProperty.ImmuneToEnergy
                    || condition.Property == ConditionProperty.HasClass) {
                    CreateEqOperator(root, 0.06f, "CountEqOp");

                    if (condition.Property == ConditionProperty.HasBuff) {
                        CreateBuffSelector(root, 0.23f);
                    } else if (condition.Property == ConditionProperty.HasClass) {
                        var entries = ClassProvider.GetAll();
                        var labels = entries.Select(e => e.Label).ToList();
                        int idx = -1;
                        for (int i = 0; i < entries.Count; i++) {
                            if (entries[i].Value == condition.Value) { idx = i; break; }
                        }
                        if (idx < 0 && entries.Count > 0) { idx = 0; condition.Value = entries[0].Value; }
                        if (labels.Count == 0) {
                            var valueInput = UIHelpers.CreateTMPInputFieldInRow(root, "Value", 60f, 0.23f, condition.Value ?? "", 16f);
                            valueInput.onEndEdit.AddListener(v => {
                                condition.Value = v;
                                onChanged?.Invoke();
                            });
                        } else {
                            PopupSelector.CreateInRow(root, "CountHasClassValue", 0.23f, labels, idx, v => {
                                condition.Value = entries[v].Value;
                                onChanged?.Invoke();
                            });
                        }
                    } else {
                        var valueKeys = GetValueKeysForProperty(condition.Property);
                        var valueLabels = GetValueLabelsForProperty(condition.Property);
                        int valIdx = valueKeys.IndexOf(condition.Value);
                        if (valIdx < 0) { valIdx = 0; condition.Value = valueKeys[0]; }
                        PopupSelector.CreateInRow(root, "CountValueDropdown", 0.23f, valueLabels, valIdx, v => {
                            condition.Value = valueKeys[v];
                            onChanged?.Invoke();
                        });
                    }
                } else {
                    // Bool props (IsDead/IsSummon/IsPet/etc.): Yes/No dropdown for both Count
                    // and AllyByName subjects. Free-text fallback covers anything not classified.
                    bool isBool = condition.Property == ConditionProperty.IsDead
                        || condition.Property == ConditionProperty.IsInCombat
                        || condition.Property == ConditionProperty.IsTargetingSelf
                        || condition.Property == ConditionProperty.IsTargetingAlly
                        || condition.Property == ConditionProperty.IsTargetedByAlly
                        || condition.Property == ConditionProperty.IsTargetedByEnemy
                        || condition.Property == ConditionProperty.IsSummon
                        || condition.Property == ConditionProperty.IsPet
                        || condition.Property == ConditionProperty.IsFlanked
                        || condition.Property == ConditionProperty.AbilityDamage
                        || condition.Property == ConditionProperty.NegativeLevels
                        || condition.Property == ConditionProperty.WieldsRangedWeapon;
                    condition.Operator = ConditionOperator.Equal;
                    if (isBool) {
                        var yesNo = new List<string> { "bool.yes".i18n(), "bool.no".i18n() };
                        int yIdx = string.Equals(condition.Value, "true", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
                        if (string.IsNullOrEmpty(condition.Value)) condition.Value = "true";
                        PopupSelector.CreateInRow(root, "CompactBoolValue", 0.30f, yesNo, yIdx, v => {
                            condition.Value = v == 0 ? "true" : "false";
                            onChanged?.Invoke();
                        });
                    } else {
                        var valueInput = UIHelpers.CreateTMPInputFieldInRow(root, "Value", 60f, 0.30f, condition.Value ?? "", 16f);
                        valueInput.onEndEdit.AddListener(v => {
                            condition.Value = v;
                            onChanged?.Invoke();
                        });
                    }
                }
            } else {
                bool isCreatureType = condition.Property == ConditionProperty.CreatureType;
                bool isAlignment = condition.Property == ConditionProperty.Alignment;
                bool isBuffProp = condition.Property == ConditionProperty.HasBuff;
                bool isHasClass = condition.Property == ConditionProperty.HasClass;
                bool isWithinRange = condition.Property == ConditionProperty.WithinRange;
                bool isDescOrEnergy = condition.Property == ConditionProperty.HasDescriptorEffect
                    || condition.Property == ConditionProperty.ImmuneToEnergy;
                bool usesEqOp = isHasCondition || isCreatureType || isBuffProp || isAlignment || isHasClass || isDescOrEnergy;
                bool isBoolProperty = condition.Property == ConditionProperty.IsDead
                    || condition.Property == ConditionProperty.IsInCombat
                    || condition.Property == ConditionProperty.IsTargetingSelf
                    || condition.Property == ConditionProperty.IsTargetingAlly
                    || condition.Property == ConditionProperty.IsTargetedByAlly
                    || condition.Property == ConditionProperty.IsTargetedByEnemy
                    || condition.Property == ConditionProperty.IsSummon
                    || condition.Property == ConditionProperty.IsPet
                    || condition.Property == ConditionProperty.IsFlanked
                    || condition.Property == ConditionProperty.AbilityDamage
                    || condition.Property == ConditionProperty.NegativeLevels
                    || condition.Property == ConditionProperty.WieldsRangedWeapon;
                bool needsOperator = !usesEqOp && !isBoolProperty;

                // Operator popup selector
                if (needsOperator) {
                    var opNames = new List<string> { "<", ">", "=", "!=", ">=", "<=" };
                    PopupSelector.CreateInRow(root, "Operator", 0.12f, opNames,
                        (int)condition.Operator, v => {
                            condition.Operator = (ConditionOperator)v;
                            onChanged?.Invoke();
                            // Bracket labels are operator-dependent — rebuild so
                            // they refresh live (same pattern as Subject/Property).
                            if (condition.Property == ConditionProperty.WithinRange) Rebuild();
                        });
                } else if (usesEqOp) {
                    CreateEqOperator(root, 0.06f, "EqOperator");
                } else {
                    condition.Operator = ConditionOperator.Equal;
                }

                if (isCreatureType) {
                    var ctKeys = EnumLabels.KeysForCreatureType();
                    var ctLabels = EnumLabels.LabelsForCreatureType();
                    int ctIdx = ctKeys.IndexOf(condition.Value);
                    if (ctIdx < 0) { ctIdx = 0; condition.Value = ctKeys[0]; }
                    PopupSelector.CreateInRow(root, "CreatureTypeValue", 0.43f, ctLabels, ctIdx, v => {
                        condition.Value = ctKeys[v];
                        onChanged?.Invoke();
                    });
                } else if (isAlignment) {
                    var aKeys = EnumLabels.KeysForAlignment();
                    var aLabels = EnumLabels.LabelsForAlignment();
                    int aIdx = aKeys.IndexOf(condition.Value);
                    if (aIdx < 0) { aIdx = 0; condition.Value = aKeys[0]; }
                    PopupSelector.CreateInRow(root, "AlignmentValue", 0.43f, aLabels, aIdx, v => {
                        condition.Value = aKeys[v];
                        onChanged?.Invoke();
                    });
                } else if (isHasCondition) {
                    var condKeys = EnumLabels.KeysForCondition();
                    var condLabels = EnumLabels.LabelsForCondition();
                    int condIdx = condKeys.IndexOf(condition.Value);
                    if (condIdx < 0) { condIdx = 0; condition.Value = condKeys[0]; }
                    PopupSelector.CreateInRow(root, "CondValue", 0.43f, condLabels, condIdx, v => {
                        condition.Value = condKeys[v];
                        onChanged?.Invoke();
                    });
                } else if (isDescOrEnergy) {
                    var deKeys = GetValueKeysForProperty(condition.Property);
                    var deLabels = GetValueLabelsForProperty(condition.Property);
                    int deIdx = deKeys.IndexOf(condition.Value);
                    if (deIdx < 0) { deIdx = 0; condition.Value = deKeys[0]; }
                    PopupSelector.CreateInRow(root, "DescEnergyValue", 0.43f, deLabels, deIdx, v => {
                        condition.Value = deKeys[v];
                        onChanged?.Invoke();
                    });
                } else if (isHasClass) {
                    var entries = ClassProvider.GetAll();
                    var labels = entries.Select(e => e.Label).ToList();

                    if (labels.Count == 0) {
                        // Safety fallback: blueprint root not yet available (e.g. main menu).
                        var valueInput = UIHelpers.CreateTMPInputFieldInRow(root, "Value", 60f, 0.43f, condition.Value ?? "", 16f);
                        valueInput.onEndEdit.AddListener(v => {
                            condition.Value = v;
                            onChanged?.Invoke();
                        });
                    } else {
                        int idx = -1;
                        for (int i = 0; i < entries.Count; i++) {
                            if (entries[i].Value == condition.Value) { idx = i; break; }
                        }
                        if (idx < 0) { idx = 0; condition.Value = entries[0].Value; }
                        PopupSelector.CreateInRow(root, "HasClassValue", 0.43f, labels, idx, v => {
                            condition.Value = entries[v].Value;
                            onChanged?.Invoke();
                        });
                    }
                } else if (condition.Property == ConditionProperty.HasBuff) {
                    CreateBuffSelector(root, 0.43f);
                } else if (isWithinRange) {
                    var bracketNames = RangeBracketNames;
                    var bracketLabels = GetRangeBracketLabels(condition.Operator);
                    int brIdx = bracketNames.IndexOf(condition.Value);
                    if (brIdx < 0) { brIdx = 2; condition.Value = bracketNames[brIdx]; } // default: Short
                    PopupSelector.CreateInRow(root, "RangeBracketValue", 0.37f, bracketLabels, brIdx, v => {
                        condition.Value = bracketNames[v];
                        onChanged?.Invoke();
                    });
                } else if (isBoolProperty) {
                    // IsTargetingAlly / IsTargetedByAlly accept an optional Value2 ally pin —
                    // empty = any ally (legacy), set = only that specific ally counts.
                    bool hasAllyFilter = condition.Property == ConditionProperty.IsTargetingAlly
                        || condition.Property == ConditionProperty.IsTargetedByAlly;

                    var yesNo = new List<string> { "bool.yes".i18n(), "bool.no".i18n() };
                    int yIdx = string.Equals(condition.Value, "true", StringComparison.OrdinalIgnoreCase)
                        ? 0 : 1;
                    if (string.IsNullOrEmpty(condition.Value)) condition.Value = "true";

                    float yesNoEnd = hasAllyFilter ? 0.55f : 0.88f;
                    PopupSelector.CreateInRow(root, "BoolPropertyValue", yesNoEnd - 0.38f, yesNo, yIdx, v => {
                        condition.Value = v == 0 ? "true" : "false";
                        onChanged?.Invoke();
                    });

                    if (hasAllyFilter) {
                        CreateAllyPicker(root, 0.32f, "TargetingAllyFilter",
                            allowAny: true, condition.Value2,
                            v => { condition.Value2 = v; onChanged?.Invoke(); });
                    }
                } else {
                    // Normal numeric input. ABMinusAC additionally exposes an optional
                    // Value2 ally-pin: empty = use party-best AB (legacy), set = compute
                    // margin against THIS ally's AB ("Wenduag struggles to hit → buff her").
                    bool hasAbAllyPin = condition.Property == ConditionProperty.ABMinusAC;
                    float valFlex = hasAbAllyPin ? 0.14f : 0.37f;   // old spans 0.51–0.65 / 0.51–0.88

                    var valueInput = UIHelpers.CreateTMPInputFieldInRow(root, "Value",
                        60f, valFlex, condition.Value ?? "", 16f);
                    valueInput.onEndEdit.AddListener(v => {
                        condition.Value = v;
                        onChanged?.Invoke();
                    });

                    if (hasAbAllyPin) {
                        CreateAllyPicker(root, 0.22f, "ABAllyPin",
                            allowAny: true, condition.Value2,
                            v => { condition.Value2 = v; onChanged?.Invoke(); });
                    }
                }
            }

            // Delete button
            Widgets.IconButton(root.transform, "DelBtn", Icon.X, Theme.IconSmall, () => onDelete?.Invoke());
        }

        void CreateEqOperator(GameObject root, float flexibleWidth, string name) {
            var eqOpNames = new List<string> { "=", "!=" };
            int eqOpIdx = condition.Operator == ConditionOperator.NotEqual ? 1 : 0;
            PopupSelector.CreateInRow(root, name, flexibleWidth, eqOpNames, eqOpIdx, v => {
                condition.Operator = v == 1 ? ConditionOperator.NotEqual : ConditionOperator.Equal;
                onChanged?.Invoke();
            });
        }

        // Storage-side keys (English, persisted to JSON, never localized).
        static List<string> GetValueKeysForProperty(ConditionProperty property) {
            switch (property) {
                case ConditionProperty.CreatureType: return EnumLabels.KeysForCreatureType();
                case ConditionProperty.Alignment:    return EnumLabels.KeysForAlignment();
                case ConditionProperty.HasCondition: return EnumLabels.KeysForCondition();
                case ConditionProperty.HasDescriptorEffect: return EnumLabels.KeysForDescriptorEffect();
                case ConditionProperty.ImmuneToEnergy:      return EnumLabels.KeysForEnergy();
                case ConditionProperty.WithinRange:  return RangeBracketNames;
                default:                             return new List<string>();
            }
        }

        // Display-side labels — locale-dependent. Same order/length as the keys list.
        // Bracket labels are operator-dependent (EffectiveLabel): the same
        // bracket reads "Short (≤ 5 m)" under "<" but "Short (≤ 10 m)" under
        // "<=". Order must match RangeBracketNames (index-mapped).
        static List<string> GetRangeBracketLabels(ConditionOperator op) {
            return new List<string> {
                RangeBrackets.EffectiveLabel(RangeBracket.Melee, op),
                RangeBrackets.EffectiveLabel(RangeBracket.Cone, op),
                RangeBrackets.EffectiveLabel(RangeBracket.Short, op),
                RangeBrackets.EffectiveLabel(RangeBracket.Medium, op),
                RangeBrackets.EffectiveLabel(RangeBracket.Far, op),
                RangeBrackets.EffectiveLabel(RangeBracket.Long, op),
            };
        }

        static List<string> GetValueLabelsForProperty(ConditionProperty property) {
            switch (property) {
                case ConditionProperty.CreatureType: return EnumLabels.LabelsForCreatureType();
                case ConditionProperty.Alignment:    return EnumLabels.LabelsForAlignment();
                case ConditionProperty.HasCondition: return EnumLabels.LabelsForCondition();
                case ConditionProperty.HasDescriptorEffect: return EnumLabels.LabelsForDescriptorEffect();
                case ConditionProperty.ImmuneToEnergy:      return EnumLabels.LabelsForEnergy();
                // WithinRange deliberately absent: bracket labels are operator-
                // dependent — use GetRangeBracketLabels(condition.Operator).
                default: return new List<string>();
            }
        }

        void CreateAllyPicker(GameObject root, float flexibleWidth, string name,
                              bool allowAny, string current, Action<string> onSelect) {
            var entries = AllyProvider.GetAll();
            var labels = new List<string>();
            var values = new List<string>();
            if (allowAny) {
                labels.Add("ally.picker.any".i18n());
                values.Add("");
            }
            foreach (var e in entries) {
                labels.Add(e.DisplayName);
                values.Add(e.UniqueId);
            }

            // Fallback to text input when not in-game (main menu): party list is empty.
            if (labels.Count == 0) {
                var input = UIHelpers.CreateTMPInputFieldInRow(root, name, 60f, flexibleWidth, current ?? "", 14f);
                input.onEndEdit.AddListener(v => onSelect(v));
                return;
            }

            int idx = values.IndexOf(current ?? "");
            if (idx < 0) {
                idx = 0;
                onSelect(values[0]);
            }
            PopupSelector.CreateInRow(root, name, flexibleWidth, labels, idx, v => onSelect(values[v]));
        }

        void CreateBuffSelector(GameObject root, float flexibleWidth) {
            var buffs = BuffBlueprintProvider.GetBuffs();

            // Fallback to text input if blueprint cache is empty (e.g. main-menu state).
            if (buffs.Count == 0) {
                var valueInput = UIHelpers.CreateTMPInputFieldInRow(root, "Value",
                    60f, flexibleWidth, condition.Value ?? "", 16f);
                valueInput.onEndEdit.AddListener(v => {
                    condition.Value = v;
                    onChanged?.Invoke();
                });
                return;
            }

            // Button showing the current selection, click opens BuffPickerOverlay.
            string currentLabel = BuffBlueprintProvider.GetDisplayLabel(condition.Value);
            if (string.IsNullOrEmpty(currentLabel) || currentLabel == condition.Value)
                currentLabel = string.IsNullOrEmpty(condition.Value) ? "placeholder.pick_buff".i18n() : currentLabel;
            var btnObj = Widgets.BandDropdownShell(root.transform, "BuffPickerButton", currentLabel,
                withIcon: false, out var label, out _);
            Widgets.InRow(btnObj, 120f, flexibleWidth);
            // The internal-id suffix can overrun this narrow button — clip rather than overlap the chevron.
            label.overflowMode = TextOverflowModes.Ellipsis;
            var buffBtn = btnObj.AddComponent<Button>();
            buffBtn.targetGraphic = btnObj.GetComponent<Image>();
            Widgets.ApplyColorTint(buffBtn);

            var subjectForPicker = condition.Subject;
            buffBtn.onClick.AddListener(() => {
                BuffPickerOverlay.Open(condition.Value, subjectForPicker, guid => {
                    condition.Value = guid;
                    onChanged?.Invoke();
                    label.text = BuffBlueprintProvider.GetDisplayLabel(guid);
                });
            });
        }

        static List<ConditionProperty> GetPropertiesForSubject(ConditionSubject subject) {
            switch (subject) {
                case ConditionSubject.Self:
                    return new List<ConditionProperty> {
                        ConditionProperty.HpPercent, ConditionProperty.HpFlat, ConditionProperty.HasBuff,
                        ConditionProperty.HasCondition,
                        ConditionProperty.HasDescriptorEffect,
                        ConditionProperty.ImmuneToEnergy,
                        ConditionProperty.SpellSlotsAtLevel, ConditionProperty.SpellSlotsAboveLevel,
                        ConditionProperty.Alignment,
                        ConditionProperty.HasClass,
                        ConditionProperty.IsSummon,
                        ConditionProperty.IsPet,
                        ConditionProperty.WieldsRangedWeapon,
                        ConditionProperty.AbilityDamage,
                        ConditionProperty.NegativeLevels,
                        ConditionProperty.IsFlanked,
                        ConditionProperty.AdjacentEnemyCount
                    };
                case ConditionSubject.Ally:
                case ConditionSubject.AllyByName:
                    return new List<ConditionProperty> {
                        ConditionProperty.HpPercent, ConditionProperty.HpFlat, ConditionProperty.HasBuff,
                        ConditionProperty.HasCondition, ConditionProperty.IsDead,
                        ConditionProperty.HasDescriptorEffect,
                        ConditionProperty.ImmuneToEnergy,
                        ConditionProperty.Alignment,
                        ConditionProperty.HasClass,
                        ConditionProperty.WithinRange,
                        ConditionProperty.IsTargetedByEnemy,
                        ConditionProperty.IsSummon,
                        ConditionProperty.IsPet,
                        ConditionProperty.WieldsRangedWeapon,
                        ConditionProperty.AbilityDamage,
                        ConditionProperty.NegativeLevels,
                        ConditionProperty.IsFlanked,
                        ConditionProperty.AdjacentEnemyCount
                    };
                case ConditionSubject.AllyCount:
                    return new List<ConditionProperty> {
                        ConditionProperty.HpPercent, ConditionProperty.HpFlat, ConditionProperty.HasBuff,
                        ConditionProperty.HasCondition, ConditionProperty.IsDead,
                        ConditionProperty.HasDescriptorEffect,
                        ConditionProperty.ImmuneToEnergy,
                        ConditionProperty.Alignment,
                        ConditionProperty.HasClass,
                        ConditionProperty.WithinRange,
                        ConditionProperty.IsSummon,
                        ConditionProperty.IsPet,
                        ConditionProperty.WieldsRangedWeapon,
                        ConditionProperty.AbilityDamage,
                        ConditionProperty.NegativeLevels,
                        ConditionProperty.IsFlanked,
                        ConditionProperty.AdjacentEnemyCount
                    };
                case ConditionSubject.Enemy:
                case ConditionSubject.EnemyBiggestThreat:
                case ConditionSubject.EnemyLowestThreat:
                case ConditionSubject.EnemyHighestHp:
                case ConditionSubject.EnemyLowestHp:
                case ConditionSubject.EnemyLowestAC:
                case ConditionSubject.EnemyHighestAC:
                case ConditionSubject.EnemyLowestFort:
                case ConditionSubject.EnemyHighestFort:
                case ConditionSubject.EnemyLowestReflex:
                case ConditionSubject.EnemyHighestReflex:
                case ConditionSubject.EnemyLowestWill:
                case ConditionSubject.EnemyHighestWill:
                case ConditionSubject.EnemyHighestHD:
                case ConditionSubject.EnemyLowestHD:
                case ConditionSubject.EnemyNearest:
                    return new List<ConditionProperty> {
                        ConditionProperty.HpPercent, ConditionProperty.HpFlat, ConditionProperty.AC,
                        ConditionProperty.SaveFortitude, ConditionProperty.SaveReflex, ConditionProperty.SaveWill,
                        ConditionProperty.HasBuff, ConditionProperty.HasCondition,
                        ConditionProperty.HasDescriptorEffect,
                        ConditionProperty.ImmuneToEnergy,
                        ConditionProperty.CreatureType,
                        ConditionProperty.Alignment,
                        ConditionProperty.HitDice,
                        ConditionProperty.SpellDCMinusSave,
                        ConditionProperty.ABMinusAC,
                        ConditionProperty.EnemyHDMinusPartyLevel,
                        ConditionProperty.HasClass,
                        ConditionProperty.WithinRange,
                        ConditionProperty.IsTargetingSelf,
                        ConditionProperty.IsTargetingAlly,
                        ConditionProperty.IsTargetedByAlly,
                        ConditionProperty.IsSummon,
                        ConditionProperty.IsPet,
                        ConditionProperty.WieldsRangedWeapon,
                        ConditionProperty.IsFlanked,
                        ConditionProperty.AdjacentEnemyCount
                    };
                case ConditionSubject.EnemyCount:
                    return new List<ConditionProperty> {
                        ConditionProperty.HpPercent, ConditionProperty.HpFlat, ConditionProperty.AC, ConditionProperty.HasBuff,
                        ConditionProperty.HasCondition, ConditionProperty.CreatureType,
                        ConditionProperty.HasDescriptorEffect,
                        ConditionProperty.ImmuneToEnergy,
                        ConditionProperty.Alignment,
                        ConditionProperty.HitDice,
                        ConditionProperty.SpellDCMinusSave,
                        ConditionProperty.ABMinusAC,
                        ConditionProperty.EnemyHDMinusPartyLevel,
                        ConditionProperty.HasClass,
                        ConditionProperty.WithinRange,
                        ConditionProperty.IsSummon,
                        ConditionProperty.IsPet,
                        ConditionProperty.WieldsRangedWeapon,
                        ConditionProperty.IsFlanked,
                        ConditionProperty.AdjacentEnemyCount
                    };
                case ConditionSubject.Combat:
                    return new List<ConditionProperty> {
                        ConditionProperty.CombatRounds,
                        ConditionProperty.IsInCombat
                    };
                default:
                    return new List<ConditionProperty>();
            }
        }
    }
}
