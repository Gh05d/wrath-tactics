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
    public partial class RuleEditorWidget : MonoBehaviour {
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
