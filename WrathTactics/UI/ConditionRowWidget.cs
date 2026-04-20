using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WrathTactics.Engine;
using WrathTactics.Models;
using WrathTactics.Persistence;

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
            var subjectNames = Enum.GetNames(typeof(ConditionSubject)).ToList();
            PopupSelector.Create(root, "Subject", 0f, 0.15f, subjectNames,
                (int)condition.Subject, v => {
                    condition.Subject = (ConditionSubject)v;
                    // Reset property to first valid for new subject
                    var validProps = GetPropertiesForSubject(condition.Subject);
                    if (!validProps.Contains(condition.Property) && validProps.Count > 0)
                        condition.Property = validProps[0];
                    ConfigManager.Save();
                    Rebuild();
                });

            // Property popup selector (for non-count: 0.16→0.37; repositioned below for count)
            var props = GetPropertiesForSubject(condition.Subject);
            var propNames = props.Select(p => p.ToString()).ToList();
            int propIdx = props.IndexOf(condition.Property);
            if (propIdx < 0) propIdx = 0;
            propertySelector = PopupSelector.Create(root, "Property", 0.16f, 0.37f,
                propNames, propIdx, v => {
                    var currentProps = GetPropertiesForSubject(condition.Subject);
                    if (v < currentProps.Count) condition.Property = currentProps[v];
                    ConfigManager.Save();
                    Rebuild();
                });

            bool isCountSubject = condition.Subject == ConditionSubject.AllyCount
                || condition.Subject == ConditionSubject.EnemyCount;
            bool isHasCondition = condition.Property == ConditionProperty.HasCondition;

            if (isCountSubject) {
                // Layout: [Subject 0→0.15] [">=" 0.16→0.2] [count 0.21→0.3] ["with" 0.31→0.37]
                //         [Property 0.38→0.58] [Op/Value 0.58→0.88] [X 0.9→1.0]
                // Reads: "AllyCount >= 2 with HpPercent < 60"

                // ">=" label
                var (gteLbl, gteLblRect) = UIHelpers.Create("GteLabel", root.transform);
                gteLblRect.SetAnchor(0.16, 0.20, 0, 1);
                gteLblRect.sizeDelta = Vector2.zero;
                UIHelpers.AddLabel(gteLbl, ">=", 14f, TextAlignmentOptions.Midline,
                    new Color(0.7f, 0.7f, 0.7f));

                // Value2 = count threshold
                var countInput = UIHelpers.CreateTMPInputField(root, "CountValue",
                    0.21, 0.30, condition.Value2 ?? "1", 16f,
                    TMP_InputField.ContentType.IntegerNumber);
                countInput.onEndEdit.AddListener(v => {
                    condition.Value2 = v;
                    ConfigManager.Save();
                });

                // "with" label
                var (withLbl, withLblRect) = UIHelpers.Create("WithLabel", root.transform);
                withLblRect.SetAnchor(0.31, 0.37, 0, 1);
                withLblRect.sizeDelta = Vector2.zero;
                UIHelpers.AddLabel(withLbl, "with", 14f, TextAlignmentOptions.Midline,
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
                    || condition.Property == ConditionProperty.SpellDCMinusSave;

                if (propNeedsOperator) {
                    // Operator selector where the "<" label was
                    var opNames = new List<string> { "<", ">", "=", "!=", ">=", "<=" };
                    PopupSelector.Create(root, "CountOperator", 0.58f, 0.66f, opNames,
                        (int)condition.Operator, v => {
                            condition.Operator = (ConditionOperator)v;
                            ConfigManager.Save();
                        });

                    // Value input on the right
                    var valueInput = UIHelpers.CreateTMPInputField(root, "Value",
                        0.67, 0.88, condition.Value ?? "", 16f);
                    valueInput.onEndEdit.AddListener(v => {
                        condition.Value = v;
                        ConfigManager.Save();
                    });
                } else if (condition.Property == ConditionProperty.CreatureType
                    || condition.Property == ConditionProperty.Alignment
                    || condition.Property == ConditionProperty.HasBuff
                    || condition.Property == ConditionProperty.HasCondition
                    || condition.Property == ConditionProperty.HasClass
                    || condition.Property == ConditionProperty.WithinRange) {
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
                                ConfigManager.Save();
                            });
                        } else {
                            PopupSelector.Create(root, "CountHasClassValue", 0.65f, 0.88f, labels, idx, v => {
                                condition.Value = entries[v].Value;
                                ConfigManager.Save();
                            });
                        }
                    } else if (condition.Property == ConditionProperty.WithinRange) {
                        var bracketNames = RangeBracketNames;
                        var bracketLabels = GetValueOptionsForProperty(ConditionProperty.WithinRange);
                        int brIdx = bracketNames.IndexOf(condition.Value);
                        if (brIdx < 0) { brIdx = 2; condition.Value = bracketNames[brIdx]; } // default: Short
                        PopupSelector.Create(root, "CountRangeBracketValue", 0.65f, 0.88f, bracketLabels, brIdx, v => {
                            condition.Value = bracketNames[v];
                            ConfigManager.Save();
                        });
                    } else {
                        var valueOptions = GetValueOptionsForProperty(condition.Property);
                        int valIdx = valueOptions.IndexOf(condition.Value);
                        if (valIdx < 0) { valIdx = 0; condition.Value = valueOptions[0]; }
                        PopupSelector.Create(root, "CountValueDropdown", 0.65f, 0.88f, valueOptions, valIdx, v => {
                            condition.Value = valueOptions[v];
                            ConfigManager.Save();
                        });
                    }
                } else {
                    // IsDead — fall back to text input
                    condition.Operator = ConditionOperator.Equal;
                    var valueInput = UIHelpers.CreateTMPInputField(root, "Value",
                        0.58, 0.88, condition.Value ?? "", 16f);
                    valueInput.onEndEdit.AddListener(v => {
                        condition.Value = v;
                        ConfigManager.Save();
                    });
                }
            } else {
                bool isCreatureType = condition.Property == ConditionProperty.CreatureType;
                bool isAlignment = condition.Property == ConditionProperty.Alignment;
                bool isBuffProp = condition.Property == ConditionProperty.HasBuff;
                bool isInCombat = condition.Property == ConditionProperty.IsInCombat;
                bool isHasClass = condition.Property == ConditionProperty.HasClass;
                bool isWithinRange = condition.Property == ConditionProperty.WithinRange;
                bool usesEqOp = isHasCondition || isCreatureType || isBuffProp || isAlignment || isHasClass || isWithinRange;
                bool needsOperator = !usesEqOp && !isInCombat;

                // Operator popup selector
                if (needsOperator) {
                    var opNames = new List<string> { "<", ">", "=", "!=", ">=", "<=" };
                    PopupSelector.Create(root, "Operator", 0.38f, 0.50f, opNames,
                        (int)condition.Operator, v => {
                            condition.Operator = (ConditionOperator)v;
                            ConfigManager.Save();
                        });
                } else if (usesEqOp) {
                    CreateEqOperator(root, 0.38f, 0.44f, "EqOperator");
                } else {
                    condition.Operator = ConditionOperator.Equal;
                }

                if (isCreatureType) {
                    var creatureTypes = GetValueOptionsForProperty(ConditionProperty.CreatureType);
                    int ctIdx = creatureTypes.IndexOf(condition.Value);
                    if (ctIdx < 0) { ctIdx = 0; condition.Value = creatureTypes[0]; }
                    PopupSelector.Create(root, "CreatureTypeValue", 0.45f, 0.88f, creatureTypes, ctIdx, v => {
                        condition.Value = creatureTypes[v];
                        ConfigManager.Save();
                    });
                } else if (isAlignment) {
                    var alignmentValues = GetValueOptionsForProperty(ConditionProperty.Alignment);
                    int aIdx = alignmentValues.IndexOf(condition.Value);
                    if (aIdx < 0) { aIdx = 0; condition.Value = alignmentValues[0]; }
                    PopupSelector.Create(root, "AlignmentValue", 0.45f, 0.88f, alignmentValues, aIdx, v => {
                        condition.Value = alignmentValues[v];
                        ConfigManager.Save();
                    });
                } else if (isHasCondition) {
                    var condNames = GetValueOptionsForProperty(ConditionProperty.HasCondition);
                    int condIdx = condNames.IndexOf(condition.Value);
                    if (condIdx < 0) { condIdx = 0; condition.Value = condNames[0]; }
                    PopupSelector.Create(root, "CondValue", 0.45f, 0.88f, condNames, condIdx, v => {
                        condition.Value = condNames[v];
                        ConfigManager.Save();
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
                            ConfigManager.Save();
                        });
                    } else {
                        int idx = -1;
                        for (int i = 0; i < entries.Count; i++) {
                            if (entries[i].Value == condition.Value) { idx = i; break; }
                        }
                        if (idx < 0) { idx = 0; condition.Value = entries[0].Value; }
                        PopupSelector.Create(root, "HasClassValue", 0.45f, 0.88f, labels, idx, v => {
                            condition.Value = entries[v].Value;
                            ConfigManager.Save();
                        });
                    }
                } else if (condition.Property == ConditionProperty.HasBuff) {
                    CreateBuffSelector(root, 0.45f, 0.88f);
                } else if (isWithinRange) {
                    var bracketNames = RangeBracketNames;
                    var bracketLabels = GetValueOptionsForProperty(ConditionProperty.WithinRange);
                    int brIdx = bracketNames.IndexOf(condition.Value);
                    if (brIdx < 0) { brIdx = 2; condition.Value = bracketNames[brIdx]; } // default: Short
                    PopupSelector.Create(root, "RangeBracketValue", 0.45f, 0.88f, bracketLabels, brIdx, v => {
                        condition.Value = bracketNames[v];
                        ConfigManager.Save();
                    });
                } else if (condition.Property == ConditionProperty.IsInCombat) {
                    var yesNo = new List<string> { "Yes", "No" };
                    // Map: "true" -> index 0 (Yes), anything else -> index 1 (No)
                    int yIdx = string.Equals(condition.Value, "true", StringComparison.OrdinalIgnoreCase)
                        ? 0 : 1;
                    if (string.IsNullOrEmpty(condition.Value)) condition.Value = "true";
                    PopupSelector.Create(root, "IsInCombatValue", 0.38f, 0.88f, yesNo, yIdx, v => {
                        condition.Value = v == 0 ? "true" : "false";
                        ConfigManager.Save();
                    });
                } else {
                    // Normal single value input
                    var valueInput = UIHelpers.CreateTMPInputField(root, "Value",
                        0.51, 0.88, condition.Value ?? "", 16f);
                    valueInput.onEndEdit.AddListener(v => {
                        condition.Value = v;
                        ConfigManager.Save();
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
                ConfigManager.Save();
            });
        }

        static List<string> GetValueOptionsForProperty(ConditionProperty property) {
            switch (property) {
                case ConditionProperty.CreatureType:
                    return new List<string> {
                        "Aberration", "Animal", "Construct", "Dragon", "Fey",
                        "Humanoid", "MagicalBeast", "MonstrousHumanoid", "Ooze",
                        "Outsider", "Plant", "Swarm", "Undead", "Vermin",
                        "Incorporeal"
                    };
                case ConditionProperty.Alignment:
                    return new List<string> { "Good", "Evil", "Lawful", "Chaotic", "Neutral" };
                case ConditionProperty.HasCondition:
                    return new List<string> {
                        "Paralyzed", "Stunned", "Frightened", "Nauseated", "Confused",
                        "Blinded", "Prone", "Entangled", "Exhausted", "Fatigued",
                        "Shaken", "Sickened", "Sleeping", "Petrified"
                    };
                case ConditionProperty.WithinRange:
                    return new List<string> {
                        RangeBrackets.Label(RangeBracket.Melee),
                        RangeBrackets.Label(RangeBracket.Cone),
                        RangeBrackets.Label(RangeBracket.Short),
                        RangeBrackets.Label(RangeBracket.Medium),
                        RangeBrackets.Label(RangeBracket.Long),
                    };
                default:
                    return new List<string>();
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
                    ConfigManager.Save();
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
                currentLabel = string.IsNullOrEmpty(condition.Value) ? "(pick a buff)" : currentLabel;
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
                    ConfigManager.Save();
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
                        ConditionProperty.HasClass
                    };
                case ConditionSubject.Ally:
                    return new List<ConditionProperty> {
                        ConditionProperty.HpPercent, ConditionProperty.HasBuff,
                        ConditionProperty.HasCondition, ConditionProperty.IsDead,
                        ConditionProperty.Alignment,
                        ConditionProperty.HasClass,
                        ConditionProperty.WithinRange
                    };
                case ConditionSubject.AllyCount:
                    return new List<ConditionProperty> {
                        ConditionProperty.HpPercent, ConditionProperty.HasBuff,
                        ConditionProperty.HasCondition, ConditionProperty.IsDead,
                        ConditionProperty.Alignment,
                        ConditionProperty.HasClass,
                        ConditionProperty.WithinRange
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
                        ConditionProperty.HasClass,
                        ConditionProperty.WithinRange
                    };
                case ConditionSubject.EnemyCount:
                    return new List<ConditionProperty> {
                        ConditionProperty.HpPercent, ConditionProperty.AC, ConditionProperty.HasBuff,
                        ConditionProperty.HasCondition, ConditionProperty.CreatureType,
                        ConditionProperty.Alignment,
                        ConditionProperty.HitDice,
                        ConditionProperty.SpellDCMinusSave,
                        ConditionProperty.HasClass,
                        ConditionProperty.WithinRange
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
