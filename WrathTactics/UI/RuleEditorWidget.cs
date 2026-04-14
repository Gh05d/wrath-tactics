using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WrathTactics.Logging;
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
            UIHelpers.AddBackground(root, new Color(0.18f, 0.18f, 0.18f, 1f));
            layoutElement = root.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = 200;

            // Body container fills entire card — header is INSIDE the VLG
            var (body, bodyRt) = UIHelpers.Create("Body", root.transform);
            bodyContainer = body;
            bodyRt.FillParent();
            bodyRt.offsetMin = new Vector2(4, 4);
            bodyRt.offsetMax = new Vector2(-4, -4);

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

            // Header row — inside VLG as first child
            CreateHeader(bodyContainer.transform);

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

        void CreateHeader(Transform parent) {
            var (header, _) = UIHelpers.Create("Header", parent);
            header.AddComponent<LayoutElement>().preferredHeight = 44;
            UIHelpers.AddBackground(header, new Color(0.25f, 0.22f, 0.18f, 1f));

            // HLG — childControlWidth=true so LayoutElement.preferredWidth/flexibleWidth work
            var hlg = header.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4;
            hlg.childForceExpandHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childControlHeight = true;
            hlg.childControlWidth = true;
            hlg.padding = new RectOffset(4, 4, 4, 4);
            hlg.childAlignment = TextAnchor.MiddleLeft;

            // Order: [ON] [^] [v] [X] [Name input (flexible)]

            // ON/OFF button
            var (enableBtnObj, _1) = UIHelpers.Create("EnableBtn", header.transform);
            var enableLE = enableBtnObj.AddComponent<LayoutElement>();
            enableLE.preferredWidth = 50;
            enableLE.flexibleWidth = 0;
            UIHelpers.AddBackground(enableBtnObj, new Color(0.25f, 0.25f, 0.25f, 1f));
            enabledLabel = UIHelpers.AddLabel(enableBtnObj, rule.Enabled ? "ON" : "OFF", 16f,
                TextAlignmentOptions.Midline, rule.Enabled ? Color.green : Color.gray);
            enableBtnObj.AddComponent<Button>().onClick.AddListener(() => {
                rule.Enabled = !rule.Enabled;
                enabledLabel.text = rule.Enabled ? "ON" : "OFF";
                enabledLabel.color = rule.Enabled ? Color.green : Color.gray;
                ConfigManager.Save();
            });

            // Move up
            var (upObj, _2) = UIHelpers.Create("Up", header.transform);
            var upLE = upObj.AddComponent<LayoutElement>();
            upLE.preferredWidth = 36;
            upLE.flexibleWidth = 0;
            UIHelpers.AddBackground(upObj, new Color(0.3f, 0.3f, 0.3f, 1f));
            UIHelpers.AddLabel(upObj, "^", 18f, TextAlignmentOptions.Midline);
            upObj.AddComponent<Button>().onClick.AddListener(() => MoveRule(-1));

            // Move down
            var (downObj, _3) = UIHelpers.Create("Down", header.transform);
            var downLE = downObj.AddComponent<LayoutElement>();
            downLE.preferredWidth = 36;
            downLE.flexibleWidth = 0;
            UIHelpers.AddBackground(downObj, new Color(0.3f, 0.3f, 0.3f, 1f));
            UIHelpers.AddLabel(downObj, "v", 18f, TextAlignmentOptions.Midline);
            downObj.AddComponent<Button>().onClick.AddListener(() => MoveRule(1));

            // Delete
            var (delObj, _4) = UIHelpers.Create("Del", header.transform);
            var delLE = delObj.AddComponent<LayoutElement>();
            delLE.preferredWidth = 36;
            delLE.flexibleWidth = 0;
            UIHelpers.AddBackground(delObj, new Color(0.6f, 0.2f, 0.2f, 1f));
            UIHelpers.AddLabel(delObj, "X", 18f, TextAlignmentOptions.Midline);
            delObj.AddComponent<Button>().onClick.AddListener(() => DeleteRule());

            // Name input — fills remaining space on the right
            var nameInput = UIHelpers.CreateTMPInputField(header, "NameInput",
                0, 1, $"{index + 1}. {rule.Name}", 18f);
            var nameLE = nameInput.gameObject.GetComponent<LayoutElement>();
            if (nameLE == null) nameLE = nameInput.gameObject.AddComponent<LayoutElement>();
            nameLE.flexibleWidth = 1;
            nameLE.preferredWidth = 200;
            nameInput.onEndEdit.AddListener(v => {
                string prefix = $"{index + 1}. ";
                rule.Name = v.StartsWith(prefix) ? v.Substring(prefix.Length) : v;
                ConfigManager.Save();
            });
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
            // For Heal action, show HealMode selector instead of spell picker
            if (rule.Action.Type == ActionType.Heal) {
                var healModeNames = Enum.GetNames(typeof(HealMode)).ToList();
                PopupSelector.Create(row, "HealMode", 0.39f, 0.7f, healModeNames,
                    (int)rule.Action.HealMode, idx => {
                        rule.Action.HealMode = (HealMode)idx;
                        ConfigManager.Save();
                    });
                return;
            }

            var entries = GetSpellEntries(rule.Action.Type);
            currentSpellEntries = entries;
            var options = entries.Select(e => e.Name).ToList();
            var icons = entries.Select(e => e.Icon).ToList();
            int initialIndex = 0;
            if (!string.IsNullOrEmpty(rule.Action.AbilityId)) {
                int idx = entries.FindIndex(e => e.Guid == rule.Action.AbilityId);
                if (idx >= 0) initialIndex = idx;
            }

            // Auto-save the displayed selection so the AbilityId matches what the dropdown shows
            if (entries.Count > 0 && string.IsNullOrEmpty(rule.Action.AbilityId)) {
                rule.Action.AbilityId = entries[initialIndex].Guid;
                ConfigManager.Save();
            }

            spellSelector = PopupSelector.CreateWithIcons(row, "SpellPick", 0.39f, 1.0f,
                options, icons, initialIndex, idx => {
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
            // For Heal, rebuild body to show HealMode selector instead
            if (actionType == ActionType.Heal) {
                RebuildBody();
                return;
            }

            if (spellSelector == null) return;

            bool showSpell = actionType != ActionType.AttackTarget &&
                             actionType != ActionType.DoNothing;
            spellSelector.gameObject.SetActive(showSpell);

            if (!showSpell) return;

            var entries = GetSpellEntries(actionType);
            currentSpellEntries = entries;
            var options = entries.Select(e => e.Name).ToList();
            var icons = entries.Select(e => e.Icon).ToList();
            spellSelector.SetOptions(options, 0, icons);

            // Auto-save the first entry so AbilityId matches the displayed dropdown value
            if (entries.Count > 0) {
                rule.Action.AbilityId = entries[0].Guid;
                ConfigManager.Save();
            }
        }

        List<SpellDropdownProvider.SpellEntry> GetSpellEntries(ActionType actionType) {
            if (actionType == ActionType.AttackTarget || actionType == ActionType.DoNothing
                || actionType == ActionType.Heal)
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
            var party = Game.Instance?.Player?.Party;
            if (party == null) return combined;

            foreach (var unit in party) {
                if (!unit.IsInGame) continue;
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
                Log.UI.Debug($"GetUnit failed for unitId={uid}");
            return unit;
        }

        void UpdateHeight() {
            if (layoutElement == null) return;
            int condCount = rule.ConditionGroups.Sum(g => g.Conditions.Count);
            int groupCount = rule.ConditionGroups.Count;
            float height = 44f  // header (inside VLG)
                + 20f           // IF: label
                + condCount * 34f
                + groupCount * 26f   // add-cond buttons
                + (groupCount > 1 ? (groupCount - 1) * 20f : 0f)  // OR separators
                + 26f           // add-or button
                + 4f            // spacer
                + 28f           // action row
                + 28f           // target row
                + 28f           // cooldown row
                + 20f;          // padding + VLG spacing
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
