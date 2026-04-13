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

            // Subject popup selector
            var subjectNames = Enum.GetNames(typeof(ConditionSubject)).ToList();
            PopupSelector.Create(root, "Subject", 0f, 0.2f, subjectNames,
                (int)condition.Subject, v => {
                    condition.Subject = (ConditionSubject)v;
                    RefreshPropertySelector();
                    ConfigManager.Save();
                    onChanged?.Invoke();
                });

            // Property popup selector
            var props = GetPropertiesForSubject(condition.Subject);
            var propNames = props.Select(p => p.ToString()).ToList();
            int propIdx = props.IndexOf(condition.Property);
            if (propIdx < 0) propIdx = 0;
            propertySelector = PopupSelector.Create(root, "Property", 0.21f, 0.42f,
                propNames, propIdx, v => {
                    var currentProps = GetPropertiesForSubject(condition.Subject);
                    if (v < currentProps.Count) condition.Property = currentProps[v];
                    ConfigManager.Save();
                    onChanged?.Invoke();
                });

            // Operator popup selector
            var opNames = new List<string> { "<", ">", "=", "!=", ">=", "<=" };
            PopupSelector.Create(root, "Operator", 0.43f, 0.55f, opNames,
                (int)condition.Operator, v => {
                    condition.Operator = (ConditionOperator)v;
                    ConfigManager.Save();
                });

            // Value input
            var valueInput = UIHelpers.CreateTMPInputField(root, "Value",
                0.56, 0.85, condition.Value ?? "", 12f);
            valueInput.onEndEdit.AddListener(v => {
                condition.Value = v;
                ConfigManager.Save();
            });

            // Delete button
            var (delBtn, delRect) = UIHelpers.Create("DelBtn", root.transform);
            delRect.SetAnchor(0.9, 1, 0, 1);
            delRect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(delBtn, new Color(0.5f, 0.15f, 0.15f, 1f));
            UIHelpers.AddLabel(delBtn, "X", 12f, TextAlignmentOptions.Midline);
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
                        ConditionProperty.HasCondition, ConditionProperty.SpellSlotsAtLevel,
                        ConditionProperty.SpellSlotsAboveLevel, ConditionProperty.Resource
                    };
                case ConditionSubject.Ally:
                    return new List<ConditionProperty> {
                        ConditionProperty.HpPercent, ConditionProperty.HasBuff, ConditionProperty.MissingBuff,
                        ConditionProperty.HasCondition, ConditionProperty.IsDead
                    };
                case ConditionSubject.AllyCount:
                    return new List<ConditionProperty> {
                        ConditionProperty.HpPercent, ConditionProperty.HasCondition, ConditionProperty.IsDead
                    };
                case ConditionSubject.Enemy:
                    return new List<ConditionProperty> {
                        ConditionProperty.HpPercent, ConditionProperty.AC, ConditionProperty.HasBuff,
                        ConditionProperty.HasCondition, ConditionProperty.CreatureType
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
