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
            nameof(RangeBracket.Long)
        };

        public void Init(Condition condition, Action onChanged, Action onDelete) {
            this.condition = condition;
            this.onChanged = onChanged;
            this.onDelete = onDelete;
            BuildUI();
        }

        void Rebuild() {
            for (int i = transform.childCount - 1; i >= 0; i--)
                Destroy(transform.GetChild(i).gameObject);
            var le = GetComponent<LayoutElement>();
            if (le != null) Destroy(le);
            propertySelector = null;
            BuildUI();
        }

        void BuildUI() {
            var root = gameObject;

            root.AddComponent<LayoutElement>().preferredHeight = 30;

            // Subject popup selector — narrow enough to leave room for count layout
            var subjectNames = EnumLabels.NamesFor<ConditionSubject>();
            PopupSelector.Create(root, "Subject", 0f, 0.15f, subjectNames,
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
            propertySelector = PopupSelector.Create(root, "Property", 0.16f, 0.37f,
                propNames, propIdx, v => {
                    var currentProps = GetPropertiesForSubject(condition.Subject);
                    if (v < currentProps.Count) condition.Property = currentProps[v];
                    onChanged?.Invoke();
                    Rebuild();
                });

            bool isCountSubject = condition.Subject == ConditionSubject.AllyCount
                || condition.Subject == ConditionSubject.EnemyCount;
            bool isHasCondition = condition.Property == ConditionProperty.HasCondition;

            if (isCountSubject) {
                // Layout: [Subject 0→0.15] [op 0.16→0.22] [count 0.23→0.30] ["with" 0.31→0.37]
                //         [Property 0.38→0.58] [Op/Value 0.58→0.88] [X 0.9→1.0]
                // Reads: "AllyCount >= 2 with HpPercent < 60" (operator now user-selectable)

                // Count-threshold operator dropdown (replaces fixed ">=" label)
                var countOpNames = new List<string> { "<", ">", "=", "!=", ">=", "<=" };
                PopupSelector.Create(root, "CountThresholdOperator", 0.16f, 0.22f,
                    countOpNames, (int)condition.CountOperator, v => {
                        condition.CountOperator = (ConditionOperator)v;
                        onChanged?.Invoke();
                    });

                // Value2 = count threshold
                var countInput = UIHelpers.CreateTMPInputField(root, "CountValue",
                    0.23, 0.30, condition.Value2 ?? "1", 16f,
                    TMP_InputField.ContentType.IntegerNumber);
                countInput.onEndEdit.AddListener(v => {
                    condition.Value2 = v;
                    onChanged?.Invoke();
                });

                // "with" label
                var (withLbl, withLblRect) = UIHelpers.Create("WithLabel", root.transform);
                withLblRect.SetAnchor(0.31, 0.37, 0, 1);
                withLblRect.sizeDelta = Vector2.zero;
                UIHelpers.AddLabel(withLbl, "condition.with".i18n(), 14f, TextAlignmentOptions.Midline,
                    new Color(0.7f, 0.7f, 0.7f));

                // Property selector already placed at 0.16→0.37 above — move it to 0.38→0.58
                // (propertySelector was created before this block, so we reposition it)
                if (propertySelector != null) {
                    var psRect = propertySelector.GetComponent<RectTransform>();
                    if (psRect != null) psRect.SetAnchor(0.38, 0.58, 0, 1);
                }

                // Determine which value widget to show based on property type
                bool propNeedsOperator = condition.Property == ConditionProperty.HpPercent
                    || condition.Property == ConditionProperty.AC
                    || condition.Property == ConditionProperty.HitDice
                    || condition.Property == ConditionProperty.SpellDCMinusSave
                    || condition.Property == ConditionProperty.ABMinusAC
                    || condition.Property == ConditionProperty.WithinRange;

                if (propNeedsOperator) {
                    // Operator selector where the "<" label was
                    var opNames = new List<string> { "<", ">", "=", "!=", ">=", "<=" };
                    PopupSelector.Create(root, "CountOperator", 0.58f, 0.66f, opNames,
                        (int)condition.Operator, v => {
                            condition.Operator = (ConditionOperator)v;
                            onChanged?.Invoke();
                        });

                    if (condition.Property == ConditionProperty.WithinRange) {
                        var bracketNames = RangeBracketNames;
                        var bracketLabels = GetValueLabelsForProperty(ConditionProperty.WithinRange);
                        int brIdx = bracketNames.IndexOf(condition.Value);
                        if (brIdx < 0) { brIdx = 2; condition.Value = bracketNames[brIdx]; } // default: Short
                        PopupSelector.Create(root, "CountRangeBracketValue", 0.67f, 0.88f, bracketLabels, brIdx, v => {
                            condition.Value = bracketNames[v];
                            onChanged?.Invoke();
                        });
                    } else {
                        // Value input on the right
                        var valueInput = UIHelpers.CreateTMPInputField(root, "Value",
                            0.67, 0.88, condition.Value ?? "", 16f);
                        valueInput.onEndEdit.AddListener(v => {
                            condition.Value = v;
                            onChanged?.Invoke();
                        });
                    }
                } else if (condition.Property == ConditionProperty.CreatureType
                    || condition.Property == ConditionProperty.Alignment
                    || condition.Property == ConditionProperty.HasBuff
                    || condition.Property == ConditionProperty.HasCondition
                    || condition.Property == ConditionProperty.HasClass) {
                    CreateEqOperator(root, 0.58f, 0.64f, "CountEqOp");

                    if (condition.Property == ConditionProperty.HasBuff) {
                        CreateBuffSelector(root, 0.65f, 0.88f);
                    } else if (condition.Property == ConditionProperty.HasClass) {
                        var entries = ClassProvider.GetAll();
                        var labels = entries.Select(e => e.Label).ToList();
                        int idx = -1;
                        for (int i = 0; i < entries.Count; i++) {
                            if (entries[i].Value == condition.Value) { idx = i; break; }
                        }
                        if (idx < 0 && entries.Count > 0) { idx = 0; condition.Value = entries[0].Value; }
                        if (labels.Count == 0) {
                            var valueInput = UIHelpers.CreateTMPInputField(root, "Value",
                                0.65, 0.88, condition.Value ?? "", 16f);
                            valueInput.onEndEdit.AddListener(v => {
                                condition.Value = v;
                                onChanged?.Invoke();
                            });
                        } else {
                            PopupSelector.Create(root, "CountHasClassValue", 0.65f, 0.88f, labels, idx, v => {
                                condition.Value = entries[v].Value;
                                onChanged?.Invoke();
                            });
                        }
                    } else {
                        var valueKeys = GetValueKeysForProperty(condition.Property);
                        var valueLabels = GetValueLabelsForProperty(condition.Property);
                        int valIdx = valueKeys.IndexOf(condition.Value);
                        if (valIdx < 0) { valIdx = 0; condition.Value = valueKeys[0]; }
                        PopupSelector.Create(root, "CountValueDropdown", 0.65f, 0.88f, valueLabels, valIdx, v => {
                            condition.Value = valueKeys[v];
                            onChanged?.Invoke();
                        });
                    }
                } else {
                    // Bool / free-text props (IsDead, IsInCombat etc.) on count subjects:
                    // fall back to a plain text input — the count path doesn't render a
                    // Yes/No dropdown here, so users type "true"/"false" directly.
                    condition.Operator = ConditionOperator.Equal;
                    var valueInput = UIHelpers.CreateTMPInputField(root, "Value",
                        0.58, 0.88, condition.Value ?? "", 16f);
                    valueInput.onEndEdit.AddListener(v => {
                        condition.Value = v;
                        onChanged?.Invoke();
                    });
                }
            } else {
                bool isCreatureType = condition.Property == ConditionProperty.CreatureType;
                bool isAlignment = condition.Property == ConditionProperty.Alignment;
                bool isBuffProp = condition.Property == ConditionProperty.HasBuff;
                bool isHasClass = condition.Property == ConditionProperty.HasClass;
                bool isWithinRange = condition.Property == ConditionProperty.WithinRange;
                bool usesEqOp = isHasCondition || isCreatureType || isBuffProp || isAlignment || isHasClass;
                bool isBoolProperty = condition.Property == ConditionProperty.IsDead
                    || condition.Property == ConditionProperty.IsInCombat
                    || condition.Property == ConditionProperty.IsTargetingSelf
                    || condition.Property == ConditionProperty.IsTargetingAlly
                    || condition.Property == ConditionProperty.IsTargetedByAlly
                    || condition.Property == ConditionProperty.IsTargetedByEnemy
                    || condition.Property == ConditionProperty.IsSummon;
                bool needsOperator = !usesEqOp && !isBoolProperty;

                // Operator popup selector
                if (needsOperator) {
                    var opNames = new List<string> { "<", ">", "=", "!=", ">=", "<=" };
                    PopupSelector.Create(root, "Operator", 0.38f, 0.50f, opNames,
                        (int)condition.Operator, v => {
                            condition.Operator = (ConditionOperator)v;
                            onChanged?.Invoke();
                        });
                } else if (usesEqOp) {
                    CreateEqOperator(root, 0.38f, 0.44f, "EqOperator");
                } else {
                    condition.Operator = ConditionOperator.Equal;
                }

                if (isCreatureType) {
                    var ctKeys = EnumLabels.KeysForCreatureType();
                    var ctLabels = EnumLabels.LabelsForCreatureType();
                    int ctIdx = ctKeys.IndexOf(condition.Value);
                    if (ctIdx < 0) { ctIdx = 0; condition.Value = ctKeys[0]; }
                    PopupSelector.Create(root, "CreatureTypeValue", 0.45f, 0.88f, ctLabels, ctIdx, v => {
                        condition.Value = ctKeys[v];
                        onChanged?.Invoke();
                    });
                } else if (isAlignment) {
                    var aKeys = EnumLabels.KeysForAlignment();
                    var aLabels = EnumLabels.LabelsForAlignment();
                    int aIdx = aKeys.IndexOf(condition.Value);
                    if (aIdx < 0) { aIdx = 0; condition.Value = aKeys[0]; }
                    PopupSelector.Create(root, "AlignmentValue", 0.45f, 0.88f, aLabels, aIdx, v => {
                        condition.Value = aKeys[v];
                        onChanged?.Invoke();
                    });
                } else if (isHasCondition) {
                    var condKeys = EnumLabels.KeysForCondition();
                    var condLabels = EnumLabels.LabelsForCondition();
                    int condIdx = condKeys.IndexOf(condition.Value);
                    if (condIdx < 0) { condIdx = 0; condition.Value = condKeys[0]; }
                    PopupSelector.Create(root, "CondValue", 0.45f, 0.88f, condLabels, condIdx, v => {
                        condition.Value = condKeys[v];
                        onChanged?.Invoke();
                    });
                } else if (isHasClass) {
                    var entries = ClassProvider.GetAll();
                    var labels = entries.Select(e => e.Label).ToList();

                    if (labels.Count == 0) {
                        // Safety fallback: blueprint root not yet available (e.g. main menu).
                        var valueInput = UIHelpers.CreateTMPInputField(root, "Value",
                            0.45, 0.88, condition.Value ?? "", 16f);
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
                        PopupSelector.Create(root, "HasClassValue", 0.45f, 0.88f, labels, idx, v => {
                            condition.Value = entries[v].Value;
                            onChanged?.Invoke();
                        });
                    }
                } else if (condition.Property == ConditionProperty.HasBuff) {
                    CreateBuffSelector(root, 0.45f, 0.88f);
                } else if (isWithinRange) {
                    var bracketNames = RangeBracketNames;
                    var bracketLabels = GetValueLabelsForProperty(ConditionProperty.WithinRange);
                    int brIdx = bracketNames.IndexOf(condition.Value);
                    if (brIdx < 0) { brIdx = 2; condition.Value = bracketNames[brIdx]; } // default: Short
                    PopupSelector.Create(root, "RangeBracketValue", 0.51f, 0.88f, bracketLabels, brIdx, v => {
                        condition.Value = bracketNames[v];
                        onChanged?.Invoke();
                    });
                } else if (isBoolProperty) {
                    var yesNo = new List<string> { "bool.yes".i18n(), "bool.no".i18n() };
                    // Map: "true" -> index 0 (Yes), anything else -> index 1 (No)
                    int yIdx = string.Equals(condition.Value, "true", StringComparison.OrdinalIgnoreCase)
                        ? 0 : 1;
                    if (string.IsNullOrEmpty(condition.Value)) condition.Value = "true";
                    PopupSelector.Create(root, "BoolPropertyValue", 0.38f, 0.88f, yesNo, yIdx, v => {
                        condition.Value = v == 0 ? "true" : "false";
                        onChanged?.Invoke();
                    });
                } else {
                    // Normal single value input
                    var valueInput = UIHelpers.CreateTMPInputField(root, "Value",
                        0.51, 0.88, condition.Value ?? "", 16f);
                    valueInput.onEndEdit.AddListener(v => {
                        condition.Value = v;
                        onChanged?.Invoke();
                    });
                }
            }

            // Delete button
            var (delBtn, delRect) = UIHelpers.Create("DelBtn", root.transform);
            delRect.SetAnchor(0.9, 1, 0, 1);
            delRect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(delBtn, new Color(0.5f, 0.15f, 0.15f, 1f));
            UIHelpers.AddLabel(delBtn, "X", 16f, TextAlignmentOptions.Midline);
            delBtn.AddComponent<Button>().onClick.AddListener(() => onDelete?.Invoke());
        }

        void CreateEqOperator(GameObject root, float xMin, float xMax, string name) {
            var eqOpNames = new List<string> { "=", "!=" };
            int eqOpIdx = condition.Operator == ConditionOperator.NotEqual ? 1 : 0;
            PopupSelector.Create(root, name, xMin, xMax, eqOpNames, eqOpIdx, v => {
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
                case ConditionProperty.WithinRange:  return RangeBracketNames;
                default:                             return new List<string>();
            }
        }

        // Display-side labels — locale-dependent. Same order/length as the keys list.
        static List<string> GetValueLabelsForProperty(ConditionProperty property) {
            switch (property) {
                case ConditionProperty.CreatureType: return EnumLabels.LabelsForCreatureType();
                case ConditionProperty.Alignment:    return EnumLabels.LabelsForAlignment();
                case ConditionProperty.HasCondition: return EnumLabels.LabelsForCondition();
                case ConditionProperty.WithinRange:
                    return new List<string> {
                        RangeBrackets.Label(RangeBracket.Melee),
                        RangeBrackets.Label(RangeBracket.Cone),
                        RangeBrackets.Label(RangeBracket.Short),
                        RangeBrackets.Label(RangeBracket.Medium),
                        RangeBrackets.Label(RangeBracket.Long),
                    };
                default: return new List<string>();
            }
        }

        void CreateBuffSelector(GameObject root, float xMin, float xMax) {
            var buffs = BuffBlueprintProvider.GetBuffs();

            // Fallback to text input if blueprint cache is empty (e.g. main-menu state).
            if (buffs.Count == 0) {
                var valueInput = UIHelpers.CreateTMPInputField(root, "Value",
                    xMin, xMax, condition.Value ?? "", 16f);
                valueInput.onEndEdit.AddListener(v => {
                    condition.Value = v;
                    onChanged?.Invoke();
                });
                return;
            }

            // Button showing the current selection, click opens BuffPickerOverlay.
            var (btnObj, btnRect) = UIHelpers.Create("BuffPickerButton", root.transform);
            btnRect.SetAnchor(xMin, xMax, 0, 1);
            btnRect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(btnObj, new Color(0.22f, 0.22f, 0.22f, 1f));

            string currentLabel = BuffBlueprintProvider.GetName(condition.Value);
            if (string.IsNullOrEmpty(currentLabel) || currentLabel == condition.Value)
                currentLabel = string.IsNullOrEmpty(condition.Value) ? "placeholder.pick_buff".i18n() : currentLabel;
            var label = UIHelpers.AddLabel(btnObj, currentLabel, 14f, TextAlignmentOptions.MidlineLeft);
            label.margin = new Vector4(6, 0, 20, 0);

            var (arrow, arrowRect) = UIHelpers.Create("Arrow", btnObj.transform);
            arrowRect.SetAnchor(0.88, 1, 0, 1);
            arrowRect.sizeDelta = Vector2.zero;
            UIHelpers.AddLabel(arrow, "v", 14f, TextAlignmentOptions.Midline,
                new Color(0.6f, 0.6f, 0.6f));

            var subjectForPicker = condition.Subject;
            btnObj.AddComponent<Button>().onClick.AddListener(() => {
                BuffPickerOverlay.Open(condition.Value, subjectForPicker, guid => {
                    condition.Value = guid;
                    onChanged?.Invoke();
                    label.text = BuffBlueprintProvider.GetName(guid);
                });
            });
        }

        static List<ConditionProperty> GetPropertiesForSubject(ConditionSubject subject) {
            switch (subject) {
                case ConditionSubject.Self:
                    return new List<ConditionProperty> {
                        ConditionProperty.HpPercent, ConditionProperty.HasBuff,
                        ConditionProperty.HasCondition,
                        ConditionProperty.SpellSlotsAtLevel, ConditionProperty.SpellSlotsAboveLevel,
                        ConditionProperty.Alignment,
                        ConditionProperty.HasClass,
                        ConditionProperty.IsSummon
                    };
                case ConditionSubject.Ally:
                    return new List<ConditionProperty> {
                        ConditionProperty.HpPercent, ConditionProperty.HasBuff,
                        ConditionProperty.HasCondition, ConditionProperty.IsDead,
                        ConditionProperty.Alignment,
                        ConditionProperty.HasClass,
                        ConditionProperty.WithinRange,
                        ConditionProperty.IsTargetedByEnemy,
                        ConditionProperty.IsSummon
                    };
                case ConditionSubject.AllyCount:
                    return new List<ConditionProperty> {
                        ConditionProperty.HpPercent, ConditionProperty.HasBuff,
                        ConditionProperty.HasCondition, ConditionProperty.IsDead,
                        ConditionProperty.Alignment,
                        ConditionProperty.HasClass,
                        ConditionProperty.WithinRange,
                        ConditionProperty.IsSummon
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
                    return new List<ConditionProperty> {
                        ConditionProperty.HpPercent, ConditionProperty.AC,
                        ConditionProperty.SaveFortitude, ConditionProperty.SaveReflex, ConditionProperty.SaveWill,
                        ConditionProperty.HasBuff, ConditionProperty.HasCondition,
                        ConditionProperty.CreatureType,
                        ConditionProperty.Alignment,
                        ConditionProperty.HitDice,
                        ConditionProperty.SpellDCMinusSave,
                        ConditionProperty.ABMinusAC,
                        ConditionProperty.HasClass,
                        ConditionProperty.WithinRange,
                        ConditionProperty.IsTargetingSelf,
                        ConditionProperty.IsTargetingAlly,
                        ConditionProperty.IsTargetedByAlly,
                        ConditionProperty.IsSummon
                    };
                case ConditionSubject.EnemyCount:
                    return new List<ConditionProperty> {
                        ConditionProperty.HpPercent, ConditionProperty.AC, ConditionProperty.HasBuff,
                        ConditionProperty.HasCondition, ConditionProperty.CreatureType,
                        ConditionProperty.Alignment,
                        ConditionProperty.HitDice,
                        ConditionProperty.SpellDCMinusSave,
                        ConditionProperty.ABMinusAC,
                        ConditionProperty.HasClass,
                        ConditionProperty.WithinRange,
                        ConditionProperty.IsSummon
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
