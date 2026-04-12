using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using WrathTactics.Models;
using WrathTactics.Persistence;

namespace WrathTactics.UI {
    public class ConditionRowWidget : MonoBehaviour {
        Condition condition;
        Action onChanged;
        Action onDelete;

        Dropdown subjectDropdown;
        Dropdown propertyDropdown;
        Dropdown operatorDropdown;
        InputField valueInput;

        public void Init(Condition condition, Action onChanged, Action onDelete) {
            this.condition = condition;
            this.onChanged = onChanged;
            this.onDelete = onDelete;
            BuildUI();
        }

        void BuildUI() {
            var root = gameObject;

            root.AddComponent<LayoutElement>().preferredHeight = 30;

            // Subject dropdown
            subjectDropdown = CreateDropdown(root, "Subject", 0f, 0.2f);
            PopulateEnum<ConditionSubject>(subjectDropdown, (int)condition.Subject);
            subjectDropdown.onValueChanged.AddListener(v => {
                condition.Subject = (ConditionSubject)v;
                RefreshPropertyDropdown();
                ConfigManager.Save();
                onChanged?.Invoke();
            });

            // Property dropdown
            propertyDropdown = CreateDropdown(root, "Property", 0.21f, 0.42f);
            RefreshPropertyDropdown();
            propertyDropdown.onValueChanged.AddListener(v => {
                var props = GetPropertiesForSubject(condition.Subject);
                if (v < props.Count) condition.Property = props[v];
                ConfigManager.Save();
                onChanged?.Invoke();
            });

            // Operator dropdown
            operatorDropdown = CreateDropdown(root, "Operator", 0.43f, 0.55f);
            var opNames = new List<string> { "<", ">", "=", "!=", ">=", "<=" };
            operatorDropdown.ClearOptions();
            operatorDropdown.AddOptions(opNames);
            operatorDropdown.value = (int)condition.Operator;
            operatorDropdown.onValueChanged.AddListener(v => {
                condition.Operator = (ConditionOperator)v;
                ConfigManager.Save();
            });

            // Value input
            var (valObj, valRect) = UIHelpers.Create("Value", root.transform);
            valRect.SetAnchor(0.56, 0.85, 0, 1);
            valRect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(valObj, new Color(0.15f, 0.15f, 0.15f, 1f));
            var valText = UIHelpers.AddLabel(valObj, condition.Value ?? "", 12);
            valueInput = valObj.AddComponent<InputField>();
            valueInput.textComponent = valText;
            valueInput.text = condition.Value ?? "";
            valueInput.onEndEdit.AddListener(v => {
                condition.Value = v;
                ConfigManager.Save();
            });

            // Delete button
            var (delBtn, delRect) = UIHelpers.Create("DelBtn", root.transform);
            delRect.SetAnchor(0.9, 1, 0, 1);
            delRect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(delBtn, new Color(0.5f, 0.15f, 0.15f, 1f));
            UIHelpers.AddLabel(delBtn, "X", 12, TextAnchor.MiddleCenter);
            delBtn.AddComponent<Button>().onClick.AddListener(() => onDelete?.Invoke());
        }

        Dropdown CreateDropdown(GameObject parent, string name, float xMin, float xMax) {
            var (obj, rect) = UIHelpers.Create(name, parent.transform);
            rect.SetAnchor(xMin, xMax, 0, 1);
            rect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(obj, new Color(0.2f, 0.2f, 0.2f, 1f));

            var dropdown = obj.AddComponent<Dropdown>();

            // Caption text
            var label = UIHelpers.AddLabel(obj, "", 11, TextAnchor.MiddleLeft);
            dropdown.captionText = label;

            // Template (hidden, used when dropdown opens)
            var (template, templateRect) = UIHelpers.Create("Template", obj.transform);
            templateRect.SetAnchor(0, 1, 0, 0);
            templateRect.pivot = new Vector2(0.5f, 1f);
            templateRect.sizeDelta = new Vector2(0, 150);
            UIHelpers.AddBackground(template, new Color(0.15f, 0.15f, 0.15f, 1f));
            template.AddComponent<ScrollRect>();

            var (viewport, viewportRect) = UIHelpers.Create("Viewport", template.transform);
            viewportRect.FillParent();
            viewport.AddComponent<Mask>().showMaskGraphic = false;
            UIHelpers.AddBackground(viewport, new Color(0, 0, 0, 0.01f));

            var (content, contentRect) = UIHelpers.Create("Content", viewport.transform);
            contentRect.SetAnchor(0, 1, 1, 1);
            contentRect.pivot = new Vector2(0.5f, 1f);

            var scrollRect = template.GetComponent<ScrollRect>();
            scrollRect.viewport = viewportRect;
            scrollRect.content = contentRect;

            // Item template
            var (item, itemRect) = UIHelpers.Create("Item", content.transform);
            itemRect.SetAnchor(0, 1, 0, 1);
            item.AddComponent<LayoutElement>().preferredHeight = 25;
            item.AddComponent<Toggle>();
            UIHelpers.AddBackground(item, new Color(0.2f, 0.2f, 0.2f, 1f));
            var itemLabel = UIHelpers.AddLabel(item, "", 11);

            dropdown.template = templateRect;
            dropdown.itemText = itemLabel;

            template.SetActive(false);

            return dropdown;
        }

        void RefreshPropertyDropdown() {
            var props = GetPropertiesForSubject(condition.Subject);
            propertyDropdown.ClearOptions();
            propertyDropdown.AddOptions(props.Select(p => p.ToString()).ToList());
            int idx = props.IndexOf(condition.Property);
            propertyDropdown.value = idx >= 0 ? idx : 0;
        }

        void PopulateEnum<T>(Dropdown dropdown, int currentValue) where T : Enum {
            var names = Enum.GetNames(typeof(T)).ToList();
            dropdown.ClearOptions();
            dropdown.AddOptions(names);
            dropdown.value = currentValue;
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
