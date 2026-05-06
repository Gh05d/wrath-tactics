using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WrathTactics.Localization;
using WrathTactics.Logging;
using WrathTactics.Models;
using WrathTactics.Persistence;

namespace WrathTactics.UI {
    public class RuleEditorWidget : MonoBehaviour {
        TacticsRule rule;
        public TacticsRule Rule => rule;
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

        // Spell/ability picker — search-overlay button. Rebuilt on ActionType change;
        // icon/label refreshed when the user picks a new entry or when the entries list
        // is re-resolved (e.g. after loading a save).
        GameObject spellPickerButton;
        Image spellPickerIcon;
        TextMeshProUGUI spellPickerLabel;
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
            // Use the actual parchment sprite as background so each card has the
            // natural paper texture / gradient instead of a flat sampled colour.
            if (ThemeProvider.InnerParchment != null) {
                ThemeProvider.ApplyInnerParchment(root);
            } else {
                UIHelpers.AddBackground(root, new Color(0.824f, 0.804f, 0.769f, 1f));
            }
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
            AddSectionLabel(bodyContainer.transform, "section.if".i18n());

            // Condition groups
            for (int gi = 0; gi < rule.ConditionGroups.Count; gi++) {
                var group = rule.ConditionGroups[gi];
                var capturedGi = gi;

                // OR separator (between groups)
                if (gi > 0) {
                    AddSectionLabel(bodyContainer.transform, "section.or_separator".i18n());
                }

                // Condition rows in this group
                for (int ci = 0; ci < group.Conditions.Count; ci++) {
                    var condition = group.Conditions[ci];
                    var capturedCi = ci;

                    var (rowObj, _) = UIHelpers.Create($"CondRow_G{gi}_C{ci}", bodyContainer.transform);
                    var widget = rowObj.AddComponent<ConditionRowWidget>();
                    widget.Init(condition,
                        () => { PersistEdit(); onChanged?.Invoke(); },
                        () => {
                            group.Conditions.RemoveAt(capturedCi);
                            if (group.Conditions.Count == 0)
                                rule.ConditionGroups.RemoveAt(capturedGi);
                            PersistEdit();
                            RebuildBody();
                        });
                }

                // "+ Condition" button for this group
                var (addCondBtn, _) = UIHelpers.Create($"AddCond_G{gi}", bodyContainer.transform);
                addCondBtn.AddComponent<LayoutElement>().preferredHeight = 22;
                UIHelpers.AddBackground(addCondBtn, new Color(0.2f, 0.3f, 0.2f, 1f));
                UIHelpers.AddLabel(addCondBtn, "button.add_condition".i18n(), 15f, TextAlignmentOptions.Midline);
                addCondBtn.AddComponent<Button>().onClick.AddListener(() => {
                    group.Conditions.Add(new Condition());
                    PersistEdit();
                    RebuildBody();
                });
            }

            // If no groups exist, show a button to add the first condition
            if (rule.ConditionGroups.Count == 0) {
                var (addFirstBtn, _) = UIHelpers.Create("AddFirstCond", bodyContainer.transform);
                addFirstBtn.AddComponent<LayoutElement>().preferredHeight = 26;
                UIHelpers.AddBackground(addFirstBtn, new Color(0.2f, 0.3f, 0.2f, 1f));
                UIHelpers.AddLabel(addFirstBtn, "button.add_condition".i18n(), 16f, TextAlignmentOptions.Midline);
                addFirstBtn.AddComponent<Button>().onClick.AddListener(() => {
                    rule.ConditionGroups.Add(new ConditionGroup { Conditions = { new Condition() } });
                    PersistEdit();
                    RebuildBody();
                });
            }

            // "+ OR" button (adds a new condition group)
            var (addOrBtn, _2) = UIHelpers.Create("AddOrBtn", bodyContainer.transform);
            addOrBtn.AddComponent<LayoutElement>().preferredHeight = 22;
            UIHelpers.AddBackground(addOrBtn, new Color(0.2f, 0.25f, 0.35f, 1f));
            UIHelpers.AddLabel(addOrBtn, "button.add_or_group".i18n(), 15f, TextAlignmentOptions.Midline);
            addOrBtn.AddComponent<Button>().onClick.AddListener(() => {
                rule.ConditionGroups.Add(new ConditionGroup { Conditions = { new Condition() } });
                PersistEdit();
                RebuildBody();
            });

            // Separator
            AddSpacer(bodyContainer.transform, 4);

            // THEN: action row
            SetupActionRow(bodyContainer.transform);

            // Fallback chain rows (CastSpell only)
            SetupFallbackRows(bodyContainer.transform);

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
            enabledLabel = UIHelpers.AddLabel(enableBtnObj, (rule.Enabled ? "button.on" : "button.off").i18n(), 16f,
                TextAlignmentOptions.Midline, rule.Enabled ? Color.green : Color.gray);
            enableBtnObj.AddComponent<Button>().onClick.AddListener(() => {
                rule.Enabled = !rule.Enabled;
                enabledLabel.text = (rule.Enabled ? "button.on" : "button.off").i18n();
                enabledLabel.color = rule.Enabled ? Color.green : Color.gray;
                PersistEdit();
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
            UIHelpers.AddLabel(copyObj, "button.copy".i18n(), 14f, TextAlignmentOptions.Midline);
            copyObj.AddComponent<Button>().onClick.AddListener(() => CloneRule());

            // Export (clipboard) — wraps the resolved rule in a 1-element JSON array
            var (exportObj, _4e) = UIHelpers.Create("Export", header.transform);
            var exportLE = exportObj.AddComponent<LayoutElement>();
            exportLE.preferredWidth = 56;
            exportLE.flexibleWidth = 0;
            UIHelpers.AddBackground(exportObj, new Color(0.3f, 0.3f, 0.5f, 1f));
            UIHelpers.AddLabel(exportObj, "button.export".i18n(), 13f, TextAlignmentOptions.Midline);
            exportObj.AddComponent<Button>().onClick.AddListener(() => ExportRuleToClipboard());

            // Promote to preset — only for unlinked character rules
            bool canPromote = !isLinked && !string.IsNullOrEmpty(unitId);
            if (canPromote) {
                var (promoteObj, _4p) = UIHelpers.Create("Promote", header.transform);
                var promoteLE = promoteObj.AddComponent<LayoutElement>();
                promoteLE.preferredWidth = 64;
                promoteLE.flexibleWidth = 0;
                UIHelpers.AddBackground(promoteObj, new Color(0.25f, 0.45f, 0.3f, 1f));
                UIHelpers.AddLabel(promoteObj, "button.promote_to_preset".i18n(), 13f, TextAlignmentOptions.Midline);
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
                ? string.Format("linked.name_format".i18n(), linkedPreset.Name)
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
                    PersistEdit();
                });
            }
        }

        void RenderLinkedSummary(Transform parent, TacticsRule preset) {
            // Badge
            var (badge, _) = UIHelpers.Create("LinkedBadge", parent);
            badge.AddComponent<LayoutElement>().preferredHeight = 26;
            UIHelpers.AddBackground(badge, new Color(0.22f, 0.3f, 0.4f, 1f));
            UIHelpers.AddLabel(badge, string.Format("linked.badge".i18n(), preset.Name), 14f,
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
            string condCountText = string.Format("linked.summary.condition_count".i18n(), condCount);
            string summary = string.Format("linked.summary".i18n(),
                condCountText, preset.Action.Type, abilityInfo, preset.Target.Type);

            var (sumObj, _s) = UIHelpers.Create("Summary", parent);
            sumObj.AddComponent<LayoutElement>().preferredHeight = 22;
            UIHelpers.AddLabel(sumObj, summary, 13f, TextAlignmentOptions.MidlineLeft, Color.gray);

            // Unlink & Edit button
            var (unlinkBtn, _u) = UIHelpers.Create("UnlinkBtn", parent);
            unlinkBtn.AddComponent<LayoutElement>().preferredHeight = 28;
            UIHelpers.AddBackground(unlinkBtn, new Color(0.45f, 0.35f, 0.15f));
            UIHelpers.AddLabel(unlinkBtn, "button.unlink_edit".i18n(), 14f, TextAlignmentOptions.Midline);
            unlinkBtn.AddComponent<Button>().onClick.AddListener(() => {
                Engine.PresetRegistry.BreakLink(rule);
                PersistEdit();
                RebuildBody();
            });
        }

        void AddSectionLabel(Transform parent, string text) {
            var (labelObj, _) = UIHelpers.Create("SectionLabel_" + text, parent);
            labelObj.AddComponent<LayoutElement>().preferredHeight = 20;
            UIHelpers.AddLabel(labelObj, text, 15f, TextAlignmentOptions.MidlineLeft,
                new Color(0.15f, 0.10f, 0.06f));
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
            UIHelpers.AddLabel(lbl, "section.then".i18n(), 16f, TextAlignmentOptions.MidlineLeft,
                new Color(0.15f, 0.10f, 0.06f));

            // Action type popup selector
            var actionNames = EnumLabels.NamesFor<ActionType>();
            PopupSelector.Create(row, "ActionType", 0.11f, 0.38f, actionNames,
                (int)rule.Action.Type, idx => {
                    rule.Action.Type = (ActionType)idx;
                    rule.Action.AbilityId = "";
                    if ((ActionType)idx != ActionType.CastSpell) {
                        rule.Action.Sources = SpellSourceMask.All;
                        rule.Action.FallbackAbilityIds?.Clear();
                    }
                    RefreshSpellSelector((ActionType)idx);
                    PersistEdit();
                });

            // Spell/ability popup selector (right side)
            SetupSpellSelector(row);
        }

        void SetupSpellSelector(GameObject row) {
            // For Heal action, show HealMode + HealEnergy + HealSources selectors instead of
            // a spell picker. Three slots across the action row: Mode (0.39-0.50),
            // Energy (0.51-0.65), Sources (0.66-0.88).
            if (rule.Action.Type == ActionType.Heal) {
                var healModeNames = EnumLabels.NamesFor<HealMode>();
                PopupSelector.Create(row, "HealMode", 0.39f, 0.50f, healModeNames,
                    (int)rule.Action.HealMode, idx => {
                        rule.Action.HealMode = (HealMode)idx;
                        PersistEdit();
                    });

                // Energy pin: Auto (default) / Positive / Negative. None is a ClassifyHeal
                // sentinel and intentionally omitted from the dropdown.
                var energyValues = new List<HealEnergyType> {
                    HealEnergyType.Auto, HealEnergyType.Positive, HealEnergyType.Negative,
                };
                var energyLabels = new List<string> {
                    EnumLabels.For(HealEnergyType.Auto),
                    EnumLabels.For(HealEnergyType.Positive),
                    EnumLabels.For(HealEnergyType.Negative),
                };
                int energyIdx = energyValues.IndexOf(rule.Action.HealEnergy);
                if (energyIdx < 0) energyIdx = 0;
                PopupSelector.Create(row, "HealEnergy", 0.51f, 0.65f, energyLabels, energyIdx, idx => {
                    rule.Action.HealEnergy = energyValues[idx];
                    PersistEdit();
                });

                // Source mask selector — 7 curated combinations (2^3 - "None" sentinel).
                var sourceLabels = new List<string> {
                    "source.all".i18n(), "source.spell_only".i18n(), "source.scroll_only".i18n(), "source.potion_only".i18n(),
                    "source.spell_scroll".i18n(), "source.spell_potion".i18n(), "source.scroll_potion".i18n(),
                };
                var sourceValues = new List<HealSourceMask> {
                    HealSourceMask.All,
                    HealSourceMask.Spell,
                    HealSourceMask.Scroll,
                    HealSourceMask.Potion,
                    HealSourceMask.Spell  | HealSourceMask.Scroll,
                    HealSourceMask.Spell  | HealSourceMask.Potion,
                    HealSourceMask.Scroll | HealSourceMask.Potion,
                };
                int srcIdx = sourceValues.IndexOf(rule.Action.HealSources);
                if (srcIdx < 0) srcIdx = 0;
                PopupSelector.Create(row, "HealSources", 0.66f, 0.88f, sourceLabels, srcIdx, idx => {
                    rule.Action.HealSources = sourceValues[idx];
                    PersistEdit();
                });
                return;
            }

            if (rule.Action.Type == ActionType.ThrowSplash) {
                var splashModeNames = EnumLabels.NamesFor<ThrowSplashMode>();
                PopupSelector.Create(row, "SplashMode", 0.39f, 0.7f, splashModeNames,
                    (int)rule.Action.SplashMode, idx => {
                        rule.Action.SplashMode = (ThrowSplashMode)idx;
                        PersistEdit();
                    });
                return;
            }

            // ToggleActivatable: mode dropdown (On/Off) + ability picker side by side
            if (rule.Action.Type == ActionType.ToggleActivatable) {
                var toggleModeNames = EnumLabels.NamesFor<ToggleMode>();
                PopupSelector.Create(row, "ToggleMode", 0.39f, 0.52f, toggleModeNames,
                    (int)rule.Action.ToggleMode, idx => {
                        rule.Action.ToggleMode = (ToggleMode)idx;
                        PersistEdit();
                    });

                BuildSpellPickerButton(row, 0.53f, 1.0f);
                return;
            }

            bool isCastSpell = rule.Action.Type == ActionType.CastSpell;
            float pickerXMax = isCastSpell ? 0.55f : 1.0f;
            BuildSpellPickerButton(row, 0.39f, pickerXMax);

            if (isCastSpell) {
                // Rod dropdown — index 0 = (none) -> Action.MetamagicRod = null,
                // indices 1..10 = MetamagicValues[i-1].
                var rodLabels = EnumLabels.RodDropdownLabels();
                int rodIdx = rule.Action.MetamagicRod == null
                    ? 0
                    : System.Array.IndexOf(EnumLabels.MetamagicValues, rule.Action.MetamagicRod.Value) + 1;
                if (rodIdx < 0) rodIdx = 0;
                PopupSelector.Create(row, "MetamagicRod", 0.56f, 0.72f, rodLabels, rodIdx, idx => {
                    rule.Action.MetamagicRod = idx == 0
                        ? (Kingmaker.UnitLogic.Abilities.Metamagic?)null
                        : EnumLabels.MetamagicValues[idx - 1];
                    PersistEdit();
                });

                // ⓘ info icon explaining the quickslot requirement (hover tooltip).
                var (infoObj, infoRect) = UIHelpers.Create("RodInfo", row.transform);
                infoRect.SetAnchor(0.73f, 0.76f, 0, 1);
                infoRect.sizeDelta = Vector2.zero;
                UIHelpers.AddLabel(infoObj, "ⓘ", 18f, TMPro.TextAlignmentOptions.Midline,
                    new Color(0.15f, 0.10f, 0.06f));
                // Image so EventTrigger has a raycast target.
                var infoBg = infoObj.AddComponent<Image>();
                infoBg.color = new Color(0, 0, 0, 0); // invisible; raycast only
                infoBg.raycastTarget = true;
                UIHelpers.AddSimpleTooltip(infoObj, "cast.rod.tooltip".i18n());

                // Source mask dropdown — 7 curated combinations, same pattern as HealSources.
                var sourceLabels = new List<string> {
                    "source.all".i18n(), "source.spell_only".i18n(), "source.scroll_only".i18n(), "source.potion_only".i18n(),
                    "source.spell_scroll".i18n(), "source.spell_potion".i18n(), "source.scroll_potion".i18n(),
                };
                var sourceValues = new List<SpellSourceMask> {
                    SpellSourceMask.All,
                    SpellSourceMask.Spell,
                    SpellSourceMask.Scroll,
                    SpellSourceMask.Potion,
                    SpellSourceMask.Spell  | SpellSourceMask.Scroll,
                    SpellSourceMask.Spell  | SpellSourceMask.Potion,
                    SpellSourceMask.Scroll | SpellSourceMask.Potion,
                };
                int srcIdx = sourceValues.IndexOf(rule.Action.Sources);
                if (srcIdx < 0) srcIdx = 0;
                PopupSelector.Create(row, "SpellSources", 0.77f, 1.0f, sourceLabels, srcIdx, idx => {
                    rule.Action.Sources = sourceValues[idx];
                    PersistEdit();
                });
            }

            // Hide if not applicable
            bool showSelector = rule.Action.Type != ActionType.AttackTarget &&
                                rule.Action.Type != ActionType.DoNothing &&
                                rule.Action.Type != ActionType.ThrowSplash;
            if (spellPickerButton != null)
                spellPickerButton.SetActive(showSelector);
        }

        // Builds the spell-picker button (icon + label + arrow) that opens SpellPickerOverlay
        // on click. Resolves the current SpellEntry list for the rule's ActionType up front,
        // and auto-persists the first entry when no AbilityId is set yet.
        void BuildSpellPickerButton(GameObject row, float xMin, float xMax) {
            var entries = GetSpellEntries(rule.Action.Type);
            currentSpellEntries = entries;

            SpellDropdownProvider.SpellEntry selected = default;
            bool found = false;
            if (!string.IsNullOrEmpty(rule.Action.AbilityId)) {
                foreach (var e in entries) {
                    if (e.Guid == rule.Action.AbilityId) { selected = e; found = true; break; }
                }
            }
            if (!found && entries.Count > 0) {
                selected = entries[0];
                if (string.IsNullOrEmpty(rule.Action.AbilityId)) {
                    rule.Action.AbilityId = selected.Guid;
                    PersistEdit();
                }
            }

            var (btnObj, btnRect) = UIHelpers.Create("SpellPick", row.transform);
            btnRect.SetAnchor(xMin, xMax, 0, 1);
            btnRect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(btnObj, new Color(0.22f, 0.22f, 0.22f, 1f));

            // Icon slot (left, 24x24) — added regardless so UpdateSpellPickerButton
            // can toggle the sprite on/off without re-parenting.
            var (iconGO, iconRect) = UIHelpers.Create("Icon", btnObj.transform);
            iconRect.SetAnchor(0, 0, 0.5, 0.5);
            iconRect.pivot = new Vector2(0, 0.5f);
            iconRect.anchoredPosition = new Vector2(4, 0);
            iconRect.sizeDelta = new Vector2(24, 24);
            spellPickerIcon = iconGO.AddComponent<Image>();
            spellPickerIcon.raycastTarget = false;

            spellPickerLabel = UIHelpers.AddLabel(btnObj,
                found || entries.Count > 0 ? selected.Name : "placeholder.none_available".i18n(),
                15f, TextAlignmentOptions.MidlineLeft);
            spellPickerLabel.margin = new Vector4(32, 0, 20, 0);

            var (arrow, arrowRect) = UIHelpers.Create("Arrow", btnObj.transform);
            arrowRect.SetAnchor(0.88, 1, 0, 1);
            arrowRect.sizeDelta = Vector2.zero;
            UIHelpers.AddLabel(arrow, "v", 14f, TextAlignmentOptions.Midline,
                new Color(0.6f, 0.6f, 0.6f));

            spellPickerButton = btnObj;
            UpdateSpellPickerButton(selected, entries.Count > 0);

            btnObj.AddComponent<Button>().onClick.AddListener(() => {
                if (currentSpellEntries == null || currentSpellEntries.Count == 0) return;
                SpellPickerOverlay.Open(currentSpellEntries, rule.Action.AbilityId, picked => {
                    rule.Action.AbilityId = picked.Guid;
                    UpdateSpellPickerButton(picked, true);
                    PersistEdit();
                });
            });
        }

        void UpdateSpellPickerButton(SpellDropdownProvider.SpellEntry entry, bool haveEntries) {
            if (spellPickerLabel != null)
                spellPickerLabel.text = haveEntries ? entry.Name : "placeholder.none_available".i18n();
            if (spellPickerIcon != null) {
                spellPickerIcon.sprite = haveEntries ? entry.Icon : null;
                spellPickerIcon.enabled = haveEntries && entry.Icon != null;
            }
        }

        // Fallback-chain rendering for CastSpell rules. Renders one row per fallback id
        // (indent arrow + picker button + delete X) plus a "+ Fallback" button at the bottom.
        // Rebuilds the entire body on add/delete to keep index-capture semantics simple.
        void SetupFallbackRows(Transform parent) {
            if (rule.Action.Type != ActionType.CastSpell) return;
            if (rule.Action.FallbackAbilityIds == null)
                rule.Action.FallbackAbilityIds = new List<string>();

            for (int i = 0; i < rule.Action.FallbackAbilityIds.Count; i++) {
                int captured = i;
                BuildFallbackRow(parent, captured);
            }

            var (addBtn, _) = UIHelpers.Create("AddFallback", parent);
            addBtn.AddComponent<LayoutElement>().preferredHeight = 22;
            UIHelpers.AddBackground(addBtn, new Color(0.2f, 0.25f, 0.35f, 1f));
            UIHelpers.AddLabel(addBtn, "button.add_fallback".i18n(), 14f, TextAlignmentOptions.Midline);
            addBtn.AddComponent<Button>().onClick.AddListener(() => {
                rule.Action.FallbackAbilityIds.Add("");
                PersistEdit();
                RebuildBody();
            });
        }

        void BuildFallbackRow(Transform parent, int index) {
            var entries = GetSpellEntries(ActionType.CastSpell);
            string current = rule.Action.FallbackAbilityIds[index];
            SpellDropdownProvider.SpellEntry selected = default;
            bool found = false;
            if (!string.IsNullOrEmpty(current)) {
                foreach (var e in entries) {
                    if (e.Guid == current) { selected = e; found = true; break; }
                }
            }
            if (!found && entries.Count > 0) {
                selected = entries[0];
                if (string.IsNullOrEmpty(current)) {
                    rule.Action.FallbackAbilityIds[index] = selected.Guid;
                    PersistEdit();
                }
            }

            var (row, _) = UIHelpers.Create($"Fallback_{index}", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 26;

            var (arrowLbl, arrowRect) = UIHelpers.Create("ArrowLbl", row.transform);
            arrowRect.SetAnchor(0.11, 0.17, 0, 1);
            arrowRect.sizeDelta = Vector2.zero;
            UIHelpers.AddLabel(arrowLbl, "\u21B3", 18f, TextAlignmentOptions.Midline,
                new Color(0.6f, 0.6f, 0.5f));

            var (btn, btnRect) = UIHelpers.Create("FallbackPick", row.transform);
            btnRect.SetAnchor(0.18, 0.9, 0, 1);
            btnRect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(btn, new Color(0.22f, 0.22f, 0.22f, 1f));

            var (iconGO, iconRect) = UIHelpers.Create("Icon", btn.transform);
            iconRect.SetAnchor(0, 0, 0.5, 0.5);
            iconRect.pivot = new Vector2(0, 0.5f);
            iconRect.anchoredPosition = new Vector2(4, 0);
            iconRect.sizeDelta = new Vector2(20, 20);
            var icon = iconGO.AddComponent<Image>();
            icon.raycastTarget = false;
            icon.sprite = entries.Count > 0 ? selected.Icon : null;
            icon.enabled = entries.Count > 0 && selected.Icon != null;

            var label = UIHelpers.AddLabel(btn,
                entries.Count > 0 ? selected.Name : "placeholder.none_available".i18n(),
                14f, TextAlignmentOptions.MidlineLeft);
            label.margin = new Vector4(28, 0, 16, 0);

            btn.AddComponent<Button>().onClick.AddListener(() => {
                if (entries.Count == 0) return;
                SpellPickerOverlay.Open(entries, rule.Action.FallbackAbilityIds[index], picked => {
                    rule.Action.FallbackAbilityIds[index] = picked.Guid;
                    label.text = picked.Name;
                    icon.sprite = picked.Icon;
                    icon.enabled = picked.Icon != null;
                    PersistEdit();
                });
            });

            var (delBtn, delRect) = UIHelpers.Create("DeleteFallback", row.transform);
            delRect.SetAnchor(0.92, 1.0, 0, 1);
            delRect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(delBtn, new Color(0.4f, 0.2f, 0.2f, 1f));
            UIHelpers.AddLabel(delBtn, "X", 14f, TextAlignmentOptions.Midline);
            delBtn.AddComponent<Button>().onClick.AddListener(() => {
                rule.Action.FallbackAbilityIds.RemoveAt(index);
                PersistEdit();
                RebuildBody();
            });
        }

        void RefreshSpellSelector(ActionType actionType) {
            // Heal/ThrowSplash/ToggleActivatable/CastSpell need a full body rebuild (different row shape).
            // CastSpell is in this list because the Sources dropdown is an extra sibling in the row —
            // without a full rebuild the dropdown becomes orphaned when switching away.
            if (actionType == ActionType.Heal || actionType == ActionType.ThrowSplash
                || actionType == ActionType.ToggleActivatable || actionType == ActionType.CastSpell) {
                RebuildBody();
                return;
            }

            if (spellPickerButton == null) return;

            bool showSpell = actionType != ActionType.AttackTarget &&
                             actionType != ActionType.DoNothing &&
                             actionType != ActionType.ThrowSplash;
            spellPickerButton.SetActive(showSpell);

            if (!showSpell) return;

            var entries = GetSpellEntries(actionType);
            currentSpellEntries = entries;

            SpellDropdownProvider.SpellEntry first = default;
            if (entries.Count > 0) {
                first = entries[0];
                rule.Action.AbilityId = first.Guid;
                PersistEdit();
            } else {
                rule.Action.AbilityId = "";
            }
            UpdateSpellPickerButton(first, entries.Count > 0);
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
            var party = Game.Instance?.Player?.AllCharacters;
            if (party == null) return combined;

            foreach (var unit in party) {
                if (!unit.IsInGame || unit.HPLeft <= 0) continue;
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
            UIHelpers.AddLabel(lbl, "section.target".i18n(), 16f, TextAlignmentOptions.MidlineLeft,
                new Color(0.15f, 0.10f, 0.06f));

            // Target type popup selector — rebuilds body so filter shows/hides
            var targetNames = EnumLabels.NamesFor<TargetType>();
            PopupSelector.Create(row, "TargetType", 0.11f, 0.5f, targetNames,
                (int)rule.Target.Type, idx => {
                    rule.Target.Type = (TargetType)idx;
                    PersistEdit();
                    RebuildBody();
                });

            // Filter input — only show for target types that need it
            bool needsFilter = rule.Target.Type == TargetType.AllyWithCondition
                || rule.Target.Type == TargetType.AllyMissingBuff
                || rule.Target.Type == TargetType.EnemyCreatureType;

            if (needsFilter) {
                string filterLabel = rule.Target.Type == TargetType.AllyWithCondition ? "target.filter.condition".i18n()
                    : rule.Target.Type == TargetType.AllyMissingBuff ? "target.filter.buff_guid".i18n()
                    : "target.filter.creature_type".i18n();

                var (filterLbl, filterLblRect) = UIHelpers.Create("FilterLabel", row.transform);
                filterLblRect.SetAnchor(0.51, 0.65, 0, 1);
                filterLblRect.sizeDelta = Vector2.zero;
                UIHelpers.AddLabel(filterLbl, filterLabel, 15f, TextAlignmentOptions.MidlineLeft,
                    new Color(0.7f, 0.7f, 0.7f));

                var filterInput = UIHelpers.CreateTMPInputField(row, "TargetFilter",
                    0.66, 1.0, rule.Target.Filter ?? "", 15f);
                filterInput.onEndEdit.AddListener(v => {
                    rule.Target.Filter = v;
                    PersistEdit();
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
            UIHelpers.AddLabel(lbl, "cooldown.label".i18n(), 15f, TextAlignmentOptions.MidlineLeft,
                new Color(0.15f, 0.10f, 0.06f));

            var cdInput = UIHelpers.CreateTMPInputField(row, "CdInput",
                0.26, 0.45, rule.CooldownRounds.ToString(), 16f,
                TMP_InputField.ContentType.IntegerNumber);
            // Adjust vertical anchors for padding
            var cdRect = cdInput.GetComponent<RectTransform>();
            cdRect.SetAnchor(0.26, 0.45, 0.1, 0.9);
            cdInput.onEndEdit.AddListener(v => {
                if (int.TryParse(v, out int rounds))
                    rule.CooldownRounds = Mathf.Max(0, rounds);
                PersistEdit();
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
            int fallbackCount = rule.Action.Type == ActionType.CastSpell
                ? (rule.Action.FallbackAbilityIds?.Count ?? 0)
                : 0;
            bool showAddFallback = rule.Action.Type == ActionType.CastSpell;
            float height = headerH
                + 20f           // IF: label
                + condCount * 34f
                + groupCount * 26f   // add-cond buttons
                + (groupCount > 1 ? (groupCount - 1) * 20f : 0f)  // OR separators
                + 26f           // add-or button
                + 4f            // spacer
                + 28f           // action row
                + fallbackCount * 26f                 // fallback rows
                + (showAddFallback ? 22f : 0f)        // + Fallback button
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
            PersistEdit();
            onChanged?.Invoke();
        }

        void DeleteRule() {
            ruleList.Remove(rule);
            PersistEdit();
            onChanged?.Invoke();
        }

        void CloneRule() {
            // Resolve first so the clone holds materialized logic (not just a linked pointer).
            var source = Engine.PresetRegistry.Resolve(rule);
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(source);
            var copy = Newtonsoft.Json.JsonConvert.DeserializeObject<TacticsRule>(json);
            copy.Id = System.Guid.NewGuid().ToString();
            copy.Name = source.Name + "clone.suffix".i18n();
            copy.PresetId = null;  // standalone copy; never inherit the link
            ruleList.Insert(index + 1, copy);
            PersistEdit();
            onChanged?.Invoke();
        }

        void PromoteToPreset() {
            var preset = Engine.PresetRegistry.PromoteRuleToPreset(rule);
            if (preset == null) return;
            PersistEdit();
            onChanged?.Invoke();
        }

        /// <summary>
        /// Persists a field edit based on the widget's mode. When unitId is null the widget is
        /// editing a preset directly (from PresetPanel), so the preset file must be saved via
        /// PresetRegistry; ConfigManager.Save would write the character-rules config, which
        /// doesn't contain the preset body. Without this split, preset edits silently reset
        /// after a reload because only the character config got touched.
        ///
        /// In preset mode we also fire onChanged so the parent PresetPanel can re-save and
        /// surface any write error in its status line. In character-rule mode we skip onChanged
        /// to avoid rebuilding the rule list on every dropdown click.
        /// </summary>
        void PersistEdit() {
            if (string.IsNullOrEmpty(unitId)) {
                Engine.PresetRegistry.Save(rule);
                onChanged?.Invoke();
            } else {
                ConfigManager.Save();
            }
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
