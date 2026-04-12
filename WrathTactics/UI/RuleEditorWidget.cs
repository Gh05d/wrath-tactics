using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using UnityEngine;
using UnityEngine.UI;
using WrathTactics.Models;
using WrathTactics.Persistence;

namespace WrathTactics.UI {
    public class RuleEditorWidget : MonoBehaviour {
        TacticsRule rule;
        int index;
        List<TacticsRule> ruleList;
        Action onChanged;
        string unitId;

        Text enabledLabel;
        LayoutElement layoutElement;

        // The body container that holds conditions + action + target rows
        GameObject bodyContainer;

        public void Init(TacticsRule rule, int index, List<TacticsRule> ruleList, Action onChanged, string unitId = null) {
            this.rule = rule;
            this.index = index;
            this.ruleList = ruleList;
            this.onChanged = onChanged;
            this.unitId = unitId;
            BuildUI();
        }

        void BuildUI() {
            var root = gameObject;

            // Card background
            UIHelpers.AddBackground(root, new Color(0.18f, 0.18f, 0.18f, 1f));

            layoutElement = root.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = 200;

            // --- Header row ---
            var (header, headerRect) = UIHelpers.Create("Header", root.transform);
            headerRect.SetAnchor(0.01, 0.99, 0, 0);
            headerRect.anchorMin = new Vector2(0.01f, 1f);
            headerRect.anchorMax = new Vector2(0.99f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.sizeDelta = new Vector2(0, 28);

            UIHelpers.AddBackground(header, new Color(0.25f, 0.22f, 0.18f, 1f));

            UIHelpers.AddLabel(header, $"{index + 1}. {rule.Name}", 13, TextAnchor.MiddleLeft);

            // Enable toggle button
            var (enableBtn, enableRect) = UIHelpers.Create("EnableBtn", header.transform);
            enableRect.SetAnchor(0.65, 0.73, 0, 1);
            enableRect.sizeDelta = Vector2.zero;
            enabledLabel = UIHelpers.AddLabel(enableBtn, rule.Enabled ? "[ON]" : "[OFF]", 12, TextAnchor.MiddleCenter,
                rule.Enabled ? Color.green : Color.gray);
            enableBtn.AddComponent<Button>().onClick.AddListener(() => {
                rule.Enabled = !rule.Enabled;
                enabledLabel.text = rule.Enabled ? "[ON]" : "[OFF]";
                enabledLabel.color = rule.Enabled ? Color.green : Color.gray;
                ConfigManager.Save();
            });

            // Move up button
            var (upBtn, upRect) = UIHelpers.Create("UpBtn", header.transform);
            upRect.SetAnchor(0.74, 0.82, 0, 1);
            upRect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(upBtn, new Color(0.3f, 0.3f, 0.3f, 1f));
            UIHelpers.AddLabel(upBtn, "^", 13, TextAnchor.MiddleCenter);
            upBtn.AddComponent<Button>().onClick.AddListener(() => MoveRule(-1));

            // Move down button
            var (downBtn, downRect) = UIHelpers.Create("DownBtn", header.transform);
            downRect.SetAnchor(0.83, 0.91, 0, 1);
            downRect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(downBtn, new Color(0.3f, 0.3f, 0.3f, 1f));
            UIHelpers.AddLabel(downBtn, "v", 13, TextAnchor.MiddleCenter);
            downBtn.AddComponent<Button>().onClick.AddListener(() => MoveRule(1));

            // Delete button
            var (delBtn, delRect) = UIHelpers.Create("DeleteBtn", header.transform);
            delRect.SetAnchor(0.92, 1, 0, 1);
            delRect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(delBtn, new Color(0.6f, 0.2f, 0.2f, 1f));
            UIHelpers.AddLabel(delBtn, "X", 13, TextAnchor.MiddleCenter);
            delBtn.AddComponent<Button>().onClick.AddListener(() => DeleteRule());

            // --- Body: vertical layout below header ---
            var (body, bodyRt) = UIHelpers.Create("Body", root.transform);
            bodyContainer = body;
            bodyRt.SetAnchor(0, 1, 0, 1);
            bodyRt.offsetMin = new Vector2(4, 4);
            bodyRt.offsetMax = new Vector2(-4, -30);

            var vlg = body.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 4;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.padding = new RectOffset(0, 0, 2, 2);

            var csf = body.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            RebuildBody();
        }

        void RebuildBody() {
            if (bodyContainer == null) return;

            // Clear existing body children
            for (int i = bodyContainer.transform.childCount - 1; i >= 0; i--)
                Destroy(bodyContainer.transform.GetChild(i).gameObject);

            // WENN: label row
            AddSectionLabel(bodyContainer.transform, "WENN:");

            // Condition groups
            for (int gi = 0; gi < rule.ConditionGroups.Count; gi++) {
                var group = rule.ConditionGroups[gi];
                var capturedGi = gi;

                // ODER separator (between groups)
                if (gi > 0) {
                    AddSectionLabel(bodyContainer.transform, "-- ODER --");
                }

                // Condition rows in this group
                for (int ci = 0; ci < group.Conditions.Count; ci++) {
                    var condition = group.Conditions[ci];
                    var capturedCi = ci;

                    var (rowObj, _) = UIHelpers.Create($"CondRow_G{gi}_C{ci}", bodyContainer.transform);
                    var widget = rowObj.AddComponent<ConditionRowWidget>();
                    widget.Init(condition,
                        () => { ConfigManager.Save(); onChanged?.Invoke(); },
                        () => {
                            group.Conditions.RemoveAt(capturedCi);
                            if (group.Conditions.Count == 0)
                                rule.ConditionGroups.RemoveAt(capturedGi);
                            ConfigManager.Save();
                            RebuildBody();
                        });
                }

                // "+ Bedingung" button for this group
                var (addCondBtn, _) = UIHelpers.Create($"AddCond_G{gi}", bodyContainer.transform);
                addCondBtn.AddComponent<LayoutElement>().preferredHeight = 22;
                UIHelpers.AddBackground(addCondBtn, new Color(0.2f, 0.3f, 0.2f, 1f));
                UIHelpers.AddLabel(addCondBtn, "+ Bedingung", 11, TextAnchor.MiddleCenter);
                addCondBtn.AddComponent<Button>().onClick.AddListener(() => {
                    group.Conditions.Add(new Condition());
                    ConfigManager.Save();
                    RebuildBody();
                });
            }

            // "+ ODER" button (adds a new condition group)
            var (addOrBtn, _) = UIHelpers.Create("AddOrBtn", bodyContainer.transform);
            addOrBtn.AddComponent<LayoutElement>().preferredHeight = 22;
            UIHelpers.AddBackground(addOrBtn, new Color(0.2f, 0.25f, 0.35f, 1f));
            UIHelpers.AddLabel(addOrBtn, "+ ODER (neue Gruppe)", 11, TextAnchor.MiddleCenter);
            addOrBtn.AddComponent<Button>().onClick.AddListener(() => {
                rule.ConditionGroups.Add(new ConditionGroup { Conditions = { new Condition() } });
                ConfigManager.Save();
                RebuildBody();
            });

            // Separator
            AddSpacer(bodyContainer.transform, 4);

            // DANN: action row
            SetupActionRow(bodyContainer.transform);

            // AUF: target row
            SetupTargetRow(bodyContainer.transform);

            // Cooldown row
            SetupCooldownRow(bodyContainer.transform);

            // Update card height based on content
            UpdateHeight();
        }

        void AddSectionLabel(Transform parent, string text) {
            var (labelObj, _) = UIHelpers.Create("SectionLabel_" + text, parent);
            labelObj.AddComponent<LayoutElement>().preferredHeight = 20;
            UIHelpers.AddLabel(labelObj, text, 11, TextAnchor.MiddleLeft, new Color(0.7f, 0.7f, 0.5f));
        }

        void AddSpacer(Transform parent, float height) {
            var (spacer, _) = UIHelpers.Create("Spacer", parent);
            spacer.AddComponent<LayoutElement>().preferredHeight = height;
        }

        // ---- Action row ----

        Dropdown spellDropdown;

        void SetupActionRow(Transform parent) {
            var (row, rowRect) = UIHelpers.Create("ActionRow", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 28;

            // "DANN:" label
            var (lbl, lblRect) = UIHelpers.Create("DannLabel", row.transform);
            lblRect.SetAnchor(0, 0.1, 0, 1);
            lblRect.sizeDelta = Vector2.zero;
            UIHelpers.AddLabel(lbl, "DANN:", 12, TextAnchor.MiddleLeft, new Color(0.7f, 0.7f, 0.5f));

            // Action type dropdown
            var actionDd = CreateDropdown(row, "ActionType", 0.11, 0.38);
            PopulateEnum<ActionType>(actionDd, (int)rule.Action.Type);

            // Spell/ability dropdown (right side, only visible for some action types)
            spellDropdown = CreateDropdown(row, "SpellPick", 0.39, 1.0);
            RefreshSpellDropdown(spellDropdown, rule.Action.Type);

            actionDd.onValueChanged.AddListener(v => {
                rule.Action.Type = (ActionType)v;
                rule.Action.AbilityId = "";
                RefreshSpellDropdown(spellDropdown, (ActionType)v);
                ConfigManager.Save();
            });
        }

        void RefreshSpellDropdown(Dropdown dd, ActionType actionType) {
            dd.ClearOptions();

            if (actionType == ActionType.AttackTarget || actionType == ActionType.DoNothing) {
                dd.gameObject.SetActive(false);
                return;
            }

            dd.gameObject.SetActive(true);

            var unit = GetUnit(unitId);
            List<SpellDropdownProvider.SpellEntry> entries;

            if (unit == null) {
                entries = new List<SpellDropdownProvider.SpellEntry> {
                    new SpellDropdownProvider.SpellEntry("(kein Charakter gewählt)", "")
                };
            } else {
                switch (actionType) {
                    case ActionType.CastSpell:
                        entries = SpellDropdownProvider.GetSpells(unit);
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
                if (entries.Count == 0)
                    entries.Add(new SpellDropdownProvider.SpellEntry("(keine verfügbar)", ""));
            }

            var options = entries.Select(e => e.Name).ToList();
            dd.AddOptions(options);

            // Select current value if present
            if (!string.IsNullOrEmpty(rule.Action.AbilityId)) {
                int idx = entries.FindIndex(e => e.Guid == rule.Action.AbilityId);
                dd.value = idx >= 0 ? idx : 0;
            } else {
                dd.value = 0;
            }

            // Capture entries for listener — remove old listeners first by replacing
            dd.onValueChanged.RemoveAllListeners();
            var capturedEntries = entries;
            dd.onValueChanged.AddListener(v => {
                if (v < capturedEntries.Count)
                    rule.Action.AbilityId = capturedEntries[v].Guid;
                ConfigManager.Save();
            });
        }

        // ---- Target row ----

        void SetupTargetRow(Transform parent) {
            var (row, rowRect) = UIHelpers.Create("TargetRow", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 28;

            // "AUF:" label
            var (lbl, lblRect) = UIHelpers.Create("AufLabel", row.transform);
            lblRect.SetAnchor(0, 0.1, 0, 1);
            lblRect.sizeDelta = Vector2.zero;
            UIHelpers.AddLabel(lbl, "AUF:", 12, TextAnchor.MiddleLeft, new Color(0.7f, 0.7f, 0.5f));

            // Target type dropdown
            var targetDd = CreateDropdown(row, "TargetType", 0.11, 0.5);
            PopulateEnum<TargetType>(targetDd, (int)rule.Target.Type);
            targetDd.onValueChanged.AddListener(v => {
                rule.Target.Type = (TargetType)v;
                ConfigManager.Save();
            });

            // Filter input
            var (filterObj, filterRect) = UIHelpers.Create("TargetFilter", row.transform);
            filterRect.SetAnchor(0.51, 1.0, 0, 1);
            filterRect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(filterObj, new Color(0.15f, 0.15f, 0.15f, 1f));
            var filterText = UIHelpers.AddLabel(filterObj, rule.Target.Filter ?? "", 11);
            var filterInput = filterObj.AddComponent<InputField>();
            filterInput.textComponent = filterText;
            filterInput.text = rule.Target.Filter ?? "";
            filterInput.onEndEdit.AddListener(v => {
                rule.Target.Filter = v;
                ConfigManager.Save();
            });
        }

        // ---- Cooldown row ----

        void SetupCooldownRow(Transform parent) {
            var (row, rowRect) = UIHelpers.Create("CooldownRow", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 28;

            var (lbl, lblRect) = UIHelpers.Create("CdLabel", row.transform);
            lblRect.SetAnchor(0, 0.25, 0, 1);
            lblRect.sizeDelta = Vector2.zero;
            UIHelpers.AddLabel(lbl, "Cooldown (Runden):", 11, TextAnchor.MiddleLeft, new Color(0.7f, 0.7f, 0.7f));

            var (cdObj, cdRect) = UIHelpers.Create("CdInput", row.transform);
            cdRect.SetAnchor(0.26, 0.45, 0.1, 0.9);
            cdRect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(cdObj, new Color(0.15f, 0.15f, 0.15f, 1f));
            var cdText = UIHelpers.AddLabel(cdObj, rule.CooldownRounds.ToString(), 12);
            var cdInput = cdObj.AddComponent<InputField>();
            cdInput.textComponent = cdText;
            cdInput.text = rule.CooldownRounds.ToString();
            cdInput.contentType = InputField.ContentType.IntegerNumber;
            cdInput.onEndEdit.AddListener(v => {
                if (int.TryParse(v, out int rounds))
                    rule.CooldownRounds = Mathf.Max(0, rounds);
                ConfigManager.Save();
            });
        }

        // ---- Helpers ----

        Dropdown CreateDropdown(GameObject parent, string name, double xMin, double xMax) {
            var (obj, rect) = UIHelpers.Create(name, parent.transform);
            rect.SetAnchor(xMin, xMax, 0, 1);
            rect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(obj, new Color(0.2f, 0.2f, 0.2f, 1f));

            var dropdown = obj.AddComponent<Dropdown>();

            var label = UIHelpers.AddLabel(obj, "", 11, TextAnchor.MiddleLeft);
            dropdown.captionText = label;

            // Template
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

        void PopulateEnum<T>(Dropdown dropdown, int currentValue) where T : Enum {
            var names = Enum.GetNames(typeof(T)).ToList();
            dropdown.ClearOptions();
            dropdown.AddOptions(names);
            dropdown.value = currentValue;
        }

        UnitEntityData GetUnit(string uid) {
            if (string.IsNullOrEmpty(uid)) return null;
            var party = Game.Instance?.Player?.Party;
            if (party == null) return null;
            return party.FirstOrDefault(u => u.UniqueId == uid);
        }

        void UpdateHeight() {
            if (layoutElement == null) return;
            // Header (28) + per condition row (30 each) + buttons + action/target/cooldown rows + padding
            int condCount = rule.ConditionGroups.Sum(g => g.Conditions.Count);
            int groupCount = rule.ConditionGroups.Count;
            // WENN label + (groupCount-1 ODER labels) + condCount rows + groupCount add-cond buttons + add-or btn
            // + action row + target row + cooldown row + section label + spacer
            float height = 28f  // header
                + 20f           // WENN: label
                + condCount * 34f
                + groupCount * 26f   // add-cond buttons
                + (groupCount > 1 ? (groupCount - 1) * 20f : 0f)  // ODER separators
                + 26f           // add-or button
                + 4f            // spacer
                + 28f           // action row
                + 28f           // target row
                + 28f           // cooldown row
                + 12f;          // padding
            layoutElement.preferredHeight = Mathf.Max(160f, height);
        }

        void MoveRule(int direction) {
            int newIndex = index + direction;
            if (newIndex < 0 || newIndex >= ruleList.Count) return;
            ruleList.RemoveAt(index);
            ruleList.Insert(newIndex, rule);
            ConfigManager.Save();
            onChanged?.Invoke();
        }

        void DeleteRule() {
            ruleList.Remove(rule);
            ConfigManager.Save();
            onChanged?.Invoke();
        }
    }
}
