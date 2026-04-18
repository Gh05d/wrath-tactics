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
        bool hideHeader;  // when true, skip the list-entry header (used by the preset editor)

        TextMeshProUGUI enabledLabel;
        LayoutElement layoutElement;

        // The body container that holds conditions + action + target rows
        GameObject bodyContainer;
        ScrollRect bodyScrollRect;

        // Spell/ability selector reference for refreshing
        PopupSelector spellSelector;
        List<SpellDropdownProvider.SpellEntry> currentSpellEntries;

        public void Init(TacticsRule rule, int index, List<TacticsRule> ruleList, Action onChanged, string unitId = null, bool hideHeader = false) {
            this.rule = rule;
            this.index = index;
            this.ruleList = ruleList;
            this.onChanged = onChanged;
            this.unitId = unitId;
            this.hideHeader = hideHeader;
            BuildUI();
        }

        void BuildUI() {
            var root = gameObject;
            UIHelpers.AddBackground(root, new Color(0.18f, 0.18f, 0.18f, 1f));
            layoutElement = root.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = 200;

            // ScrollRect wrapper — clips body content when card exceeds max height
            var (scrollObj, scrollObjRect) = UIHelpers.Create("BodyScroll", root.transform);
            scrollObjRect.FillParent();
            scrollObjRect.offsetMin = new Vector2(4, 4);
            scrollObjRect.offsetMax = new Vector2(-4, -4);

            var (viewport, viewportRect) = UIHelpers.Create("Viewport", scrollObj.transform);
            viewportRect.FillParent();
            viewport.AddComponent<RectMask2D>();

            // Body container — content inside the scroll viewport
            var (body, bodyRt) = UIHelpers.Create("Body", viewport.transform);
            bodyContainer = body;
            bodyRt.SetAnchor(0, 1, 1, 1);
            bodyRt.pivot = new Vector2(0.5f, 1f);
            bodyRt.sizeDelta = new Vector2(0, 0);

            var vlg = body.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 4;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.padding = new RectOffset(0, 0, 2, 2);

            var csf = body.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            bodyScrollRect = scrollObj.AddComponent<ScrollRect>();
            bodyScrollRect.viewport = viewportRect;
            bodyScrollRect.content = bodyRt;
            bodyScrollRect.horizontal = false;
            bodyScrollRect.vertical = true;
            bodyScrollRect.scrollSensitivity = 30f;
            bodyScrollRect.enabled = false; // only enabled when content overflows max height

            RebuildBody();
        }

        void RebuildBody() {
            if (bodyContainer == null) return;

            // Clear existing body children
            for (int i = bodyContainer.transform.childCount - 1; i >= 0; i--)
                Destroy(bodyContainer.transform.GetChild(i).gameObject);

            // Resolve once per rebuild — used by header tint, body branch, and UpdateHeight.
            var linkedPreset = !string.IsNullOrEmpty(rule.PresetId) ? Engine.PresetRegistry.Get(rule.PresetId) : null;

            // Header row — inside VLG as first child (skipped when embedded in the preset editor)
            if (!hideHeader)
                CreateHeader(bodyContainer.transform, linkedPreset);

            if (linkedPreset != null) {
                RenderLinkedSummary(bodyContainer.transform, linkedPreset);
                UpdateHeight(linkedPreset);
                return;
            }

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
            UpdateHeight(null);
        }

        void CreateHeader(Transform parent, TacticsRule linkedPreset) {
            bool isLinked = linkedPreset != null;

            var (header, _) = UIHelpers.Create("Header", parent);
            header.AddComponent<LayoutElement>().preferredHeight = 44;
            var headerBg = isLinked
                ? new Color(0.22f, 0.3f, 0.4f, 1f)   // blue-grey for linked
                : new Color(0.25f, 0.22f, 0.18f, 1f); // default brown
            UIHelpers.AddBackground(header, headerBg);

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

            // Copy
            var (copyObj, _4c) = UIHelpers.Create("Copy", header.transform);
            var copyLE = copyObj.AddComponent<LayoutElement>();
            copyLE.preferredWidth = 48;
            copyLE.flexibleWidth = 0;
            UIHelpers.AddBackground(copyObj, new Color(0.2f, 0.35f, 0.5f, 1f));
            UIHelpers.AddLabel(copyObj, "Copy", 14f, TextAlignmentOptions.Midline);
            copyObj.AddComponent<Button>().onClick.AddListener(() => CloneRule());

            // Export (clipboard) — wraps the resolved rule in a 1-element JSON array
            var (exportObj, _4e) = UIHelpers.Create("Export", header.transform);
            var exportLE = exportObj.AddComponent<LayoutElement>();
            exportLE.preferredWidth = 56;
            exportLE.flexibleWidth = 0;
            UIHelpers.AddBackground(exportObj, new Color(0.3f, 0.3f, 0.5f, 1f));
            UIHelpers.AddLabel(exportObj, "Export", 13f, TextAlignmentOptions.Midline);
            exportObj.AddComponent<Button>().onClick.AddListener(() => ExportRuleToClipboard());

            // Promote to preset — only for unlinked character rules
            bool canPromote = !isLinked && !string.IsNullOrEmpty(unitId);
            if (canPromote) {
                var (promoteObj, _4p) = UIHelpers.Create("Promote", header.transform);
                var promoteLE = promoteObj.AddComponent<LayoutElement>();
                promoteLE.preferredWidth = 64;
                promoteLE.flexibleWidth = 0;
                UIHelpers.AddBackground(promoteObj, new Color(0.25f, 0.45f, 0.3f, 1f));
                UIHelpers.AddLabel(promoteObj, "↥ Preset", 13f, TextAlignmentOptions.Midline);
                promoteObj.AddComponent<Button>().onClick.AddListener(() => PromoteToPreset());
            }

            // Delete
            var (delObj, _4) = UIHelpers.Create("Del", header.transform);
            var delLE = delObj.AddComponent<LayoutElement>();
            delLE.preferredWidth = 36;
            delLE.flexibleWidth = 0;
            UIHelpers.AddBackground(delObj, new Color(0.6f, 0.2f, 0.2f, 1f));
            UIHelpers.AddLabel(delObj, "X", 18f, TextAlignmentOptions.Midline);
            delObj.AddComponent<Button>().onClick.AddListener(() => DeleteRule());

            // Name input — fills remaining space on the right
            string displayName = isLinked
                ? $"🔗 {linkedPreset.Name}"
                : $"{index + 1}. {rule.Name}";

            var nameInput = UIHelpers.CreateTMPInputField(header, "NameInput",
                0, 1, displayName, 18f);
            var nameLE = nameInput.gameObject.GetComponent<LayoutElement>();
            if (nameLE == null) nameLE = nameInput.gameObject.AddComponent<LayoutElement>();
            nameLE.flexibleWidth = 1;
            nameLE.preferredWidth = 200;

            nameInput.interactable = !isLinked;  // linked: name comes from preset, not editable here
            if (!isLinked) {
                nameInput.onEndEdit.AddListener(v => {
                    string prefix = $"{index + 1}. ";
                    rule.Name = v.StartsWith(prefix) ? v.Substring(prefix.Length) : v;
                    ConfigManager.Save();
                });
            }
        }

        void RenderLinkedSummary(Transform parent, TacticsRule preset) {
            // Badge
            var (badge, _) = UIHelpers.Create("LinkedBadge", parent);
            badge.AddComponent<LayoutElement>().preferredHeight = 26;
            UIHelpers.AddBackground(badge, new Color(0.22f, 0.3f, 0.4f, 1f));
            UIHelpers.AddLabel(badge, $"Linked to preset: {preset.Name}", 14f,
                TextAlignmentOptions.MidlineLeft, new Color(0.85f, 0.9f, 1f));

            // Summary
            int condCount = 0;
            if (preset.ConditionGroups != null) {
                foreach (var g in preset.ConditionGroups)
                    if (g?.Conditions != null) condCount += g.Conditions.Count;
            }
            string abilityInfo = string.IsNullOrEmpty(preset.Action.AbilityId)
                ? ""
                : $" ({preset.Action.AbilityId.Substring(0, System.Math.Min(8, preset.Action.AbilityId.Length))}…)";
            string summary = $"IF: {condCount} condition(s) | THEN: {preset.Action.Type}{abilityInfo} | Target: {preset.Target.Type}";

            var (sumObj, _s) = UIHelpers.Create("Summary", parent);
            sumObj.AddComponent<LayoutElement>().preferredHeight = 22;
            UIHelpers.AddLabel(sumObj, summary, 13f, TextAlignmentOptions.MidlineLeft, Color.gray);

            // Unlink & Edit button
            var (unlinkBtn, _u) = UIHelpers.Create("UnlinkBtn", parent);
            unlinkBtn.AddComponent<LayoutElement>().preferredHeight = 28;
            UIHelpers.AddBackground(unlinkBtn, new Color(0.45f, 0.35f, 0.15f));
            UIHelpers.AddLabel(unlinkBtn, "Unlink & Edit (break link)", 14f, TextAlignmentOptions.Midline);
            unlinkBtn.AddComponent<Button>().onClick.AddListener(() => {
                Engine.PresetRegistry.BreakLink(rule);
                ConfigManager.Save();
                RebuildBody();
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

            if (rule.Action.Type == ActionType.ThrowSplash) {
                var splashModeNames = Enum.GetNames(typeof(ThrowSplashMode)).ToList();
                PopupSelector.Create(row, "SplashMode", 0.39f, 0.7f, splashModeNames,
                    (int)rule.Action.SplashMode, idx => {
                        rule.Action.SplashMode = (ThrowSplashMode)idx;
                        ConfigManager.Save();
                    });
                return;
            }

            // ToggleActivatable: mode dropdown (On/Off) + ability picker side by side
            if (rule.Action.Type == ActionType.ToggleActivatable) {
                var toggleModeNames = Enum.GetNames(typeof(ToggleMode)).ToList();
                PopupSelector.Create(row, "ToggleMode", 0.39f, 0.52f, toggleModeNames,
                    (int)rule.Action.ToggleMode, idx => {
                        rule.Action.ToggleMode = (ToggleMode)idx;
                        ConfigManager.Save();
                    });

                var tEntries = GetSpellEntries(rule.Action.Type);
                currentSpellEntries = tEntries;
                var tOptions = tEntries.Select(e => e.Name).ToList();
                var tIcons = tEntries.Select(e => e.Icon).ToList();
                int tInitialIndex = 0;
                if (!string.IsNullOrEmpty(rule.Action.AbilityId)) {
                    int idx = tEntries.FindIndex(e => e.Guid == rule.Action.AbilityId);
                    if (idx >= 0) tInitialIndex = idx;
                }
                if (tEntries.Count > 0 && string.IsNullOrEmpty(rule.Action.AbilityId)) {
                    rule.Action.AbilityId = tEntries[tInitialIndex].Guid;
                    ConfigManager.Save();
                }
                spellSelector = PopupSelector.CreateWithIcons(row, "SpellPick", 0.53f, 1.0f,
                    tOptions, tIcons, tInitialIndex, idx => {
                        if (idx < currentSpellEntries.Count)
                            rule.Action.AbilityId = currentSpellEntries[idx].Guid;
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
                                rule.Action.Type != ActionType.DoNothing &&
                                rule.Action.Type != ActionType.ThrowSplash;
            spellSelector.gameObject.SetActive(showSelector);
        }

        void RefreshSpellSelector(ActionType actionType) {
            // For Heal/ThrowSplash, rebuild body to show mode selector instead
            if (actionType == ActionType.Heal || actionType == ActionType.ThrowSplash || actionType == ActionType.ToggleActivatable) {
                RebuildBody();
                return;
            }

            if (spellSelector == null) return;

            bool showSpell = actionType != ActionType.AttackTarget &&
                             actionType != ActionType.DoNothing &&
                             actionType != ActionType.ThrowSplash;
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
                || actionType == ActionType.Heal || actionType == ActionType.ThrowSplash)
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

        void UpdateHeight(TacticsRule linkedPreset) {
            if (layoutElement == null) return;
            float headerH = hideHeader ? 0f : 44f;
            // VLG spacing between body children (see BuildUI: vlg.spacing = 4).
            const float bodySpacing = 4f;
            if (linkedPreset != null) {
                // header + badge (26) + summary (22) + unlink btn (28) + 3 VLG gaps + padding
                float gaps = (hideHeader ? 2 : 3) * bodySpacing;
                layoutElement.preferredHeight = headerH + 26f + 22f + 28f + gaps + 12f;
                return;
            }
            int condCount = rule.ConditionGroups.Sum(g => g.Conditions.Count);
            int groupCount = rule.ConditionGroups.Count;
            // Child count reflects the widgets rendered below — ~condCount + groupCount*2
            // rows plus 6 fixed sections. Close enough to estimate VLG gaps.
            int childEstimate = condCount + groupCount * 2 + 7 + (hideHeader ? 0 : 1);
            float height = headerH
                + 20f           // IF: label
                + condCount * 34f
                + groupCount * 26f   // add-cond buttons
                + (groupCount > 1 ? (groupCount - 1) * 20f : 0f)  // OR separators
                + 26f           // add-or button
                + 4f            // spacer
                + 28f           // action row
                + 28f           // target row
                + 28f           // cooldown row
                + Mathf.Max(0, childEstimate - 1) * bodySpacing
                + 12f;          // VLG padding
            layoutElement.preferredHeight = Mathf.Clamp(height, 160f, 500f);
            if (bodyScrollRect != null)
                bodyScrollRect.enabled = height > 500f;
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

        void CloneRule() {
            // Resolve first so the clone holds materialized logic (not just a linked pointer).
            var source = Engine.PresetRegistry.Resolve(rule);
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(source);
            var copy = Newtonsoft.Json.JsonConvert.DeserializeObject<TacticsRule>(json);
            copy.Id = System.Guid.NewGuid().ToString();
            copy.Name = source.Name + " (copy)";
            copy.PresetId = null;  // standalone copy; never inherit the link
            ruleList.Insert(index + 1, copy);
            ConfigManager.Save();
            onChanged?.Invoke();
        }

        void PromoteToPreset() {
            var preset = Engine.PresetRegistry.PromoteRuleToPreset(rule);
            if (preset == null) return;
            ConfigManager.Save();
            onChanged?.Invoke();
        }

        void ExportRuleToClipboard() {
            var source = Engine.PresetRegistry.Resolve(rule);
            var array = new System.Collections.Generic.List<TacticsRule> { source };
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(array, Newtonsoft.Json.Formatting.Indented);
            UnityEngine.GUIUtility.systemCopyBuffer = json;
            Logging.Log.UI.Info($"Copied rule '{source.Name}' to clipboard");
        }
    }
}
