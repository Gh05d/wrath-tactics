using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WrathTactics.Models;
using WrathTactics.Persistence;

namespace WrathTactics.UI {
    public class ConditionRowWidget : MonoBehaviour {
        Condition condition;
        Action onChanged;
        Action onDelete;

        PopupSelector propertySelector;

        public void Init(Condition condition, Action onChanged, Action onDelete) {
            this.condition = condition;
            this.onChanged = onChanged;
            this.onDelete = onDelete;
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
                    RefreshPropertySelector();
                    ConfigManager.Save();
                    onChanged?.Invoke();
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
                    onChanged?.Invoke();
                });

            bool isCountSubject = condition.Subject == ConditionSubject.AllyCount
                || condition.Subject == ConditionSubject.EnemyCount;
            bool isHasCondition = condition.Property == ConditionProperty.HasCondition;
            bool isHasDebuff = condition.Property == ConditionProperty.HasDebuff;

            if (isCountSubject) {
                // Layout: [Subject 0→0.15] [">=" 0.16→0.2] [count 0.21→0.3] ["with" 0.31→0.37]
                //         [Property 0.38→0.58] ["<" 0.59→0.63] [value 0.64→0.78] [X 0.9→1.0]
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

                // Property selector already placed at 0.21→0.42 above — move it to 0.38→0.58
                // (propertySelector was created before this block, so we reposition it)
                if (propertySelector != null) {
                    var psRect = propertySelector.GetComponent<RectTransform>();
                    if (psRect != null) psRect.SetAnchor(0.38, 0.58, 0, 1);
                }

                // "<" label
                var (ltLbl, ltLblRect) = UIHelpers.Create("LtLabel", root.transform);
                ltLblRect.SetAnchor(0.59, 0.63, 0, 1);
                ltLblRect.sizeDelta = Vector2.zero;
                UIHelpers.AddLabel(ltLbl, "<", 14f, TextAlignmentOptions.Midline,
                    new Color(0.7f, 0.7f, 0.7f));

                // Ensure operator is LessThan for count subjects
                condition.Operator = ConditionOperator.LessThan;

                // Value = property threshold (HP %)
                var valueInput = UIHelpers.CreateTMPInputField(root, "Value",
                    0.64, 0.78, condition.Value ?? "", 16f);
                valueInput.onEndEdit.AddListener(v => {
                    condition.Value = v;
                    ConfigManager.Save();
                });
            } else {
                bool isCreatureType = condition.Property == ConditionProperty.CreatureType;
                bool needsOperator = !isHasCondition && !isHasDebuff && !isCreatureType;

                // Operator popup selector (hidden for dropdown-based properties)
                if (needsOperator) {
                    var opNames = new List<string> { "<", ">", "=", "!=", ">=", "<=" };
                    PopupSelector.Create(root, "Operator", 0.38f, 0.50f, opNames,
                        (int)condition.Operator, v => {
                            condition.Operator = (ConditionOperator)v;
                            ConfigManager.Save();
                        });
                } else {
                    // Use Equal for dropdown matches
                    condition.Operator = ConditionOperator.Equal;
                }

                if (isCreatureType) {
                    var creatureTypes = new List<string> {
                        "Aberration", "Animal", "Construct", "Dragon", "Fey",
                        "Humanoid", "MagicalBeast", "MonstrousHumanoid", "Ooze",
                        "Outsider", "Plant", "Undead", "Vermin"
                    };
                    int ctIdx = creatureTypes.IndexOf(condition.Value);
                    if (ctIdx < 0) { ctIdx = 0; condition.Value = creatureTypes[0]; }
                    PopupSelector.Create(root, "CreatureTypeValue", 0.38f, 0.88f, creatureTypes, ctIdx, v => {
                        condition.Value = creatureTypes[v];
                        ConfigManager.Save();
                    });
                } else if (isHasCondition) {
                    // Dropdown for known condition names
                    var condNames = new List<string> {
                        "Paralyzed", "Stunned", "Frightened", "Nauseated", "Confused",
                        "Blinded", "Prone", "Entangled", "Exhausted", "Fatigued",
                        "Shaken", "Sickened", "Sleeping", "Petrified"
                    };
                    int condIdx = condNames.IndexOf(condition.Value);
                    if (condIdx < 0) { condIdx = 0; condition.Value = condNames[0]; }
                    PopupSelector.Create(root, "CondValue", 0.38f, 0.88f, condNames, condIdx, v => {
                        condition.Value = condNames[v];
                        ConfigManager.Save();
                    });
                } else if (isHasDebuff) {
                    var debuffNames = new List<string> {
                        "EvilEyeACBuff",
                        "EvilEyeAttackBuff",
                        "EvilEyeSavesBuff",
                        "MisfortuneBuff",
                        "VulnerabilityCurseBuff",
                        "Shaken",
                        "Sickened",
                        "Frightened",
                        "Dazzled",
                        "Fatigued",
                        "Exhausted",
                        "Staggered",
                        "DirgeOfDoom",
                        "ProtectiveLuck",
                        "FortuneHex"
                    };
                    var displayNames = new List<string> {
                        "Evil Eye - AC",
                        "Evil Eye - Attack",
                        "Evil Eye - Saves",
                        "Misfortune",
                        "Vulnerability Curse",
                        "Shaken",
                        "Sickened",
                        "Frightened",
                        "Dazzled",
                        "Fatigued",
                        "Exhausted",
                        "Staggered",
                        "Dirge of Doom",
                        "Protective Luck",
                        "Fortune Hex"
                    };
                    int debuffIdx = debuffNames.IndexOf(condition.Value);
                    if (debuffIdx < 0) { debuffIdx = 0; condition.Value = debuffNames[0]; }
                    PopupSelector.Create(root, "DebuffValue", 0.38f, 0.88f, displayNames, debuffIdx, v => {
                        condition.Value = debuffNames[v];
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

        void RefreshPropertySelector() {
            if (propertySelector == null) return;
            var props = GetPropertiesForSubject(condition.Subject);
            var propNames = props.Select(p => p.ToString()).ToList();
            int idx = props.IndexOf(condition.Property);
            if (idx < 0) idx = 0;
            if (idx < props.Count) condition.Property = props[idx];
            propertySelector.SetOptions(propNames, idx);
        }

        static List<ConditionProperty> GetPropertiesForSubject(ConditionSubject subject) {
            switch (subject) {
                case ConditionSubject.Self:
                    return new List<ConditionProperty> {
                        ConditionProperty.HpPercent, ConditionProperty.HasBuff, ConditionProperty.MissingBuff,
                        ConditionProperty.HasCondition, ConditionProperty.HasDebuff,
                        ConditionProperty.SpellSlotsAtLevel, ConditionProperty.SpellSlotsAboveLevel
                    };
                case ConditionSubject.Ally:
                    return new List<ConditionProperty> {
                        ConditionProperty.HpPercent, ConditionProperty.HasBuff, ConditionProperty.MissingBuff,
                        ConditionProperty.HasCondition, ConditionProperty.HasDebuff, ConditionProperty.IsDead
                    };
                case ConditionSubject.AllyCount:
                    return new List<ConditionProperty> {
                        ConditionProperty.HpPercent, ConditionProperty.HasCondition, ConditionProperty.HasDebuff,
                        ConditionProperty.IsDead
                    };
                case ConditionSubject.Enemy:
                    return new List<ConditionProperty> {
                        ConditionProperty.HpPercent, ConditionProperty.AC, ConditionProperty.HasBuff,
                        ConditionProperty.HasCondition, ConditionProperty.HasDebuff, ConditionProperty.CreatureType
                    };
                case ConditionSubject.EnemyCount:
                    return new List<ConditionProperty> { ConditionProperty.HpPercent };
                case ConditionSubject.Combat:
                    return new List<ConditionProperty> { ConditionProperty.CombatRounds };
                default:
                    return new List<ConditionProperty>();
            }
        }
    }
}
