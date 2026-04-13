using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using TMPro;
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

        TextMeshProUGUI enabledLabel;
        LayoutElement layoutElement;

        // The body container that holds conditions + action + target rows
        GameObject bodyContainer;

        // Spell/ability selector reference for refreshing
        PopupSelector spellSelector;
        List<SpellDropdownProvider.SpellEntry> currentSpellEntries;

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
            headerRect.anchorMin = new Vector2(0.01f, 1f);
            headerRect.anchorMax = new Vector2(0.99f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.sizeDelta = new Vector2(0, 36);

            UIHelpers.AddBackground(header, new Color(0.25f, 0.22f, 0.18f, 1f));

            // Name input field (editable rule name)
            var nameInput = UIHelpers.CreateTMPInputField(header, "NameInput",
                0, 0.65, $"{index + 1}. {rule.Name}", 18f);
            nameInput.onEndEdit.AddListener(v => {
                string prefix = $"{index + 1}. ";
                rule.Name = v.StartsWith(prefix) ? v.Substring(prefix.Length) : v;
                ConfigManager.Save();
            });

            // Enable toggle button
            var (enableBtn, enableRect) = UIHelpers.Create("EnableBtn", header.transform);
            enableRect.SetAnchor(0.65, 0.73, 0, 1);
            enableRect.sizeDelta = Vector2.zero;
            enabledLabel = UIHelpers.AddLabel(enableBtn, rule.Enabled ? "[ON]" : "[OFF]", 16f,
                TextAlignmentOptions.Midline, rule.Enabled ? Color.green : Color.gray);
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
            UIHelpers.AddLabel(upBtn, "^", 18f, TextAlignmentOptions.Midline);
            upBtn.AddComponent<Button>().onClick.AddListener(() => MoveRule(-1));

            // Move down button
            var (downBtn, downRect) = UIHelpers.Create("DownBtn", header.transform);
            downRect.SetAnchor(0.83, 0.91, 0, 1);
            downRect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(downBtn, new Color(0.3f, 0.3f, 0.3f, 1f));
            UIHelpers.AddLabel(downBtn, "v", 18f, TextAlignmentOptions.Midline);
            downBtn.AddComponent<Button>().onClick.AddListener(() => MoveRule(1));

            // Delete button
            var (delBtn, delRect) = UIHelpers.Create("DeleteBtn", header.transform);
            delRect.SetAnchor(0.92, 1, 0, 1);
            delRect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(delBtn, new Color(0.6f, 0.2f, 0.2f, 1f));
            UIHelpers.AddLabel(delBtn, "X", 18f, TextAlignmentOptions.Midline);
            delBtn.AddComponent<Button>().onClick.AddListener(() => DeleteRule());

            // --- Body: vertical layout below header ---
            var (body, bodyRt) = UIHelpers.Create("Body", root.transform);
            bodyContainer = body;
            bodyRt.SetAnchor(0, 1, 0, 1);
            bodyRt.offsetMin = new Vector2(4, 4);
            bodyRt.offsetMax = new Vector2(-4, -38);

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

            // IF: label row
            AddSectionLabel(bodyContainer.transform, "IF:");

            // Condition groups
            for (int gi = 0; gi < rule.ConditionGroups.Count; gi++) {
                var group = rule.ConditionGroups[gi];
                var capturedGi = gi;

                // OR separator (between groups)
                if (gi > 0) {
                    AddSectionLabel(bodyContainer.transform, "-- OR --");
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

                // "+ Condition" button for this group
                var (addCondBtn, _) = UIHelpers.Create($"AddCond_G{gi}", bodyContainer.transform);
                addCondBtn.AddComponent<LayoutElement>().preferredHeight = 22;
                UIHelpers.AddBackground(addCondBtn, new Color(0.2f, 0.3f, 0.2f, 1f));
                UIHelpers.AddLabel(addCondBtn, "+ Condition", 15f, TextAlignmentOptions.Midline);
                addCondBtn.AddComponent<Button>().onClick.AddListener(() => {
                    group.Conditions.Add(new Condition());
                    ConfigManager.Save();
                    RebuildBody();
                });
            }

            // If no groups exist, show a button to add the first condition
            if (rule.ConditionGroups.Count == 0) {
                var (addFirstBtn, _) = UIHelpers.Create("AddFirstCond", bodyContainer.transform);
                addFirstBtn.AddComponent<LayoutElement>().preferredHeight = 26;
                UIHelpers.AddBackground(addFirstBtn, new Color(0.2f, 0.3f, 0.2f, 1f));
                UIHelpers.AddLabel(addFirstBtn, "+ Condition", 16f, TextAlignmentOptions.Midline);
                addFirstBtn.AddComponent<Button>().onClick.AddListener(() => {
                    rule.ConditionGroups.Add(new ConditionGroup { Conditions = { new Condition() } });
                    ConfigManager.Save();
                    RebuildBody();
                });
            }

            // "+ OR" button (adds a new condition group)
            var (addOrBtn, _2) = UIHelpers.Create("AddOrBtn", bodyContainer.transform);
            addOrBtn.AddComponent<LayoutElement>().preferredHeight = 22;
            UIHelpers.AddBackground(addOrBtn, new Color(0.2f, 0.25f, 0.35f, 1f));
            UIHelpers.AddLabel(addOrBtn, "+ OR (new group)", 15f, TextAlignmentOptions.Midline);
            addOrBtn.AddComponent<Button>().onClick.AddListener(() => {
                rule.ConditionGroups.Add(new ConditionGroup { Conditions = { new Condition() } });
                ConfigManager.Save();
                RebuildBody();
            });

            // Separator
            AddSpacer(bodyContainer.transform, 4);

            // THEN: action row
            SetupActionRow(bodyContainer.transform);

            // TARGET: target row
            SetupTargetRow(bodyContainer.transform);

            // Cooldown row
            SetupCooldownRow(bodyContainer.transform);

            // Update card height based on content
            UpdateHeight();
        }

        void AddSectionLabel(Transform parent, string text) {
            var (labelObj, _) = UIHelpers.Create("SectionLabel_" + text, parent);
            labelObj.AddComponent<LayoutElement>().preferredHeight = 20;
            UIHelpers.AddLabel(labelObj, text, 15f, TextAlignmentOptions.MidlineLeft,
                new Color(0.7f, 0.7f, 0.5f));
        }

        void AddSpacer(Transform parent, float height) {
            var (spacer, _) = UIHelpers.Create("Spacer", parent);
            spacer.AddComponent<LayoutElement>().preferredHeight = height;
        }

        // ---- Action row ----

        void SetupActionRow(Transform parent) {
            var (row, rowRect) = UIHelpers.Create("ActionRow", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 28;

            // "THEN:" label
            var (lbl, lblRect) = UIHelpers.Create("ThenLabel", row.transform);
            lblRect.SetAnchor(0, 0.1, 0, 1);
            lblRect.sizeDelta = Vector2.zero;
            UIHelpers.AddLabel(lbl, "THEN:", 16f, TextAlignmentOptions.MidlineLeft,
                new Color(0.7f, 0.7f, 0.5f));

            // Action type popup selector
            var actionNames = Enum.GetNames(typeof(ActionType)).ToList();
            PopupSelector.Create(row, "ActionType", 0.11f, 0.38f, actionNames,
                (int)rule.Action.Type, idx => {
                    rule.Action.Type = (ActionType)idx;
                    rule.Action.AbilityId = "";
                    RefreshSpellSelector((ActionType)idx);
                    ConfigManager.Save();
                });

            // Spell/ability popup selector (right side)
            SetupSpellSelector(row);
        }

        void SetupSpellSelector(GameObject row) {
            // Global rules have no character context — can't show spells
            if (string.IsNullOrEmpty(unitId)) {
                bool showSpell = rule.Action.Type != ActionType.AttackTarget &&
                                 rule.Action.Type != ActionType.DoNothing;
                if (showSpell) {
                    var (msgObj, msgRect) = UIHelpers.Create("NoCharMsg", row.transform);
                    msgRect.SetAnchor(0.39f, 1.0f, 0, 1);
                    msgRect.sizeDelta = Vector2.zero;
                    UIHelpers.AddLabel(msgObj, "(select a character tab first)", 14f,
                        TextAlignmentOptions.MidlineLeft, new Color(0.6f, 0.6f, 0.6f));
                }
                return;
            }

            var entries = GetSpellEntries(rule.Action.Type);
            currentSpellEntries = entries;
            var options = entries.Select(e => e.Name).ToList();
            int initialIndex = 0;
            if (!string.IsNullOrEmpty(rule.Action.AbilityId)) {
                int idx = entries.FindIndex(e => e.Guid == rule.Action.AbilityId);
                if (idx >= 0) initialIndex = idx;
            }

            spellSelector = PopupSelector.Create(row, "SpellPick", 0.39f, 1.0f, options,
                initialIndex, idx => {
                    if (idx < currentSpellEntries.Count)
                        rule.Action.AbilityId = currentSpellEntries[idx].Guid;
                    ConfigManager.Save();
                });

            // Hide if not applicable
            bool showSelector = rule.Action.Type != ActionType.AttackTarget &&
                                rule.Action.Type != ActionType.DoNothing;
            spellSelector.gameObject.SetActive(showSelector);
        }

        void RefreshSpellSelector(ActionType actionType) {
            if (spellSelector == null) return;

            bool showSpell = actionType != ActionType.AttackTarget &&
                             actionType != ActionType.DoNothing;
            spellSelector.gameObject.SetActive(showSpell);

            if (!showSpell) return;

            var entries = GetSpellEntries(actionType);
            currentSpellEntries = entries;
            var options = entries.Select(e => e.Name).ToList();
            spellSelector.SetOptions(options, 0);
        }

        List<SpellDropdownProvider.SpellEntry> GetSpellEntries(ActionType actionType) {
            if (actionType == ActionType.AttackTarget || actionType == ActionType.DoNothing)
                return new List<SpellDropdownProvider.SpellEntry>();

            var unit = GetUnit(unitId);
            if (unit == null) {
                return new List<SpellDropdownProvider.SpellEntry> {
                    new SpellDropdownProvider.SpellEntry("(no character selected)", "")
                };
            }

            List<SpellDropdownProvider.SpellEntry> entries;
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
                entries.Add(new SpellDropdownProvider.SpellEntry("(none available)", ""));

            return entries;
        }

        // ---- Target row ----

        void SetupTargetRow(Transform parent) {
            var (row, rowRect) = UIHelpers.Create("TargetRow", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 28;

            // "TARGET:" label
            var (lbl, lblRect) = UIHelpers.Create("TargetLabel", row.transform);
            lblRect.SetAnchor(0, 0.1, 0, 1);
            lblRect.sizeDelta = Vector2.zero;
            UIHelpers.AddLabel(lbl, "TARGET:", 16f, TextAlignmentOptions.MidlineLeft,
                new Color(0.7f, 0.7f, 0.5f));

            // Target type popup selector — rebuilds body so filter shows/hides
            var targetNames = Enum.GetNames(typeof(TargetType)).ToList();
            PopupSelector.Create(row, "TargetType", 0.11f, 0.5f, targetNames,
                (int)rule.Target.Type, idx => {
                    rule.Target.Type = (TargetType)idx;
                    ConfigManager.Save();
                    RebuildBody();
                });

            // Filter input — only show for target types that need it
            bool needsFilter = rule.Target.Type == TargetType.AllyWithCondition
                || rule.Target.Type == TargetType.AllyMissingBuff
                || rule.Target.Type == TargetType.EnemyCreatureType;

            if (needsFilter) {
                string filterLabel = rule.Target.Type == TargetType.AllyWithCondition ? "Condition:"
                    : rule.Target.Type == TargetType.AllyMissingBuff ? "Buff GUID:"
                    : "Creature Type:";

                var (filterLbl, filterLblRect) = UIHelpers.Create("FilterLabel", row.transform);
                filterLblRect.SetAnchor(0.51, 0.65, 0, 1);
                filterLblRect.sizeDelta = Vector2.zero;
                UIHelpers.AddLabel(filterLbl, filterLabel, 15f, TextAlignmentOptions.MidlineLeft,
                    new Color(0.7f, 0.7f, 0.7f));

                var filterInput = UIHelpers.CreateTMPInputField(row, "TargetFilter",
                    0.66, 1.0, rule.Target.Filter ?? "", 15f);
                filterInput.onEndEdit.AddListener(v => {
                    rule.Target.Filter = v;
                    ConfigManager.Save();
                });
            }
        }

        // ---- Cooldown row ----

        void SetupCooldownRow(Transform parent) {
            var (row, rowRect) = UIHelpers.Create("CooldownRow", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 28;

            var (lbl, lblRect) = UIHelpers.Create("CdLabel", row.transform);
            lblRect.SetAnchor(0, 0.25, 0, 1);
            lblRect.sizeDelta = Vector2.zero;
            UIHelpers.AddLabel(lbl, "Cooldown (rounds):", 15f, TextAlignmentOptions.MidlineLeft,
                new Color(0.7f, 0.7f, 0.7f));

            var cdInput = UIHelpers.CreateTMPInputField(row, "CdInput",
                0.26, 0.45, rule.CooldownRounds.ToString(), 16f,
                TMP_InputField.ContentType.IntegerNumber);
            // Adjust vertical anchors for padding
            var cdRect = cdInput.GetComponent<RectTransform>();
            cdRect.SetAnchor(0.26, 0.45, 0.1, 0.9);
            cdInput.onEndEdit.AddListener(v => {
                if (int.TryParse(v, out int rounds))
                    rule.CooldownRounds = Mathf.Max(0, rounds);
                ConfigManager.Save();
            });
        }

        // ---- Helpers ----

        UnitEntityData GetUnit(string uid) {
            if (string.IsNullOrEmpty(uid)) return null;
            var party = Game.Instance?.Player?.Party;
            if (party == null) return null;
            var unit = party.FirstOrDefault(u => u.UniqueId == uid);
            if (unit == null)
                Main.Log($"[UI] GetUnit failed for unitId={uid}");
            return unit;
        }

        void UpdateHeight() {
            if (layoutElement == null) return;
            int condCount = rule.ConditionGroups.Sum(g => g.Conditions.Count);
            int groupCount = rule.ConditionGroups.Count;
            float height = 28f  // header
                + 20f           // IF: label
                + condCount * 34f
                + groupCount * 26f   // add-cond buttons
                + (groupCount > 1 ? (groupCount - 1) * 20f : 0f)  // OR separators
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
