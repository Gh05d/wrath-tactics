using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WrathTactics.Engine;
using WrathTactics.Logging;
using WrathTactics.Models;
using WrathTactics.Persistence;

namespace WrathTactics.UI {
    public class PresetPanel : MonoBehaviour {
        Action onPresetsChanged;
        readonly HashSet<string> expandedIds = new HashSet<string>();

        public void Init(string _unusedCharacterId, Transform _unusedParent, Action onPresetsChanged) {
            this.onPresetsChanged = onPresetsChanged;
            BuildUI();
        }

        void BuildUI() {
            var root = gameObject;

            var vlg = root.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 6;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.padding = new RectOffset(6, 6, 6, 6);
            var csf = root.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Title
            var (titleObj, _) = UIHelpers.Create("PresetTitle", root.transform);
            titleObj.AddComponent<LayoutElement>().preferredHeight = 30;
            UIHelpers.AddLabel(titleObj, "Presets", 20f,
                TextAlignmentOptions.MidlineLeft, Color.white);

            // Hint
            var (hint, _h) = UIHelpers.Create("Hint", root.transform);
            hint.AddComponent<LayoutElement>().preferredHeight = 40;
            UIHelpers.AddLabel(hint,
                "Presets are reusable rules. Attach them on any character tab via \"+ From Preset\". Edits here propagate to every linked copy.",
                13f, TextAlignmentOptions.TopLeft, new Color(0.7f, 0.7f, 0.7f));

            // New preset button
            var (newBtn, _n) = UIHelpers.Create("NewPresetBtn", root.transform);
            newBtn.AddComponent<LayoutElement>().preferredHeight = 40;
            UIHelpers.AddBackground(newBtn, new Color(0.2f, 0.45f, 0.2f, 1f));
            UIHelpers.AddLabel(newBtn, "+ New Preset", 17f, TextAlignmentOptions.Midline);
            newBtn.AddComponent<Button>().onClick.AddListener(() => {
                var preset = new TacticsRule {
                    Name = "New Preset",
                    ConditionGroups = new List<ConditionGroup> {
                        new ConditionGroup { Conditions = { new Condition() } }
                    }
                };
                PresetRegistry.Save(preset);
                expandedIds.Add(preset.Id);
                onPresetsChanged?.Invoke();
                Rebuild();
            });

            // Separator
            var (sep, _s) = UIHelpers.Create("Sep", root.transform);
            sep.AddComponent<LayoutElement>().preferredHeight = 10;

            var presets = PresetRegistry.All();
            if (presets.Count == 0) {
                var (empty, _e) = UIHelpers.Create("Empty", root.transform);
                empty.AddComponent<LayoutElement>().preferredHeight = 28;
                UIHelpers.AddLabel(empty, "No presets yet.", 15f,
                    TextAlignmentOptions.MidlineLeft, Color.gray);
                return;
            }

            foreach (var preset in presets) {
                CreatePresetEntry(root.transform, preset);
            }
        }

        void CreatePresetEntry(Transform parent, TacticsRule preset) {
            var (row, _) = UIHelpers.Create($"Preset_{preset.Id}", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 40;
            UIHelpers.AddBackground(row, new Color(0.18f, 0.18f, 0.18f, 1f));

            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.padding = new RectOffset(8, 8, 4, 4);
            hlg.childAlignment = TextAnchor.MiddleLeft;

            // Name — editable inline, renames on end-edit
            var nameInput = UIHelpers.CreateTMPInputField(row, "Name", 0, 1, preset.Name, 17f);
            var nameLE = nameInput.gameObject.AddComponent<LayoutElement>();
            nameLE.flexibleWidth = 1;
            nameLE.preferredWidth = 200;
            nameInput.onEndEdit.AddListener(v => {
                var trimmed = v?.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed == preset.Name) return;
                preset.Name = trimmed;
                PresetRegistry.Save(preset);
                onPresetsChanged?.Invoke();
                Rebuild();
            });

            // Edit toggle
            bool expanded = expandedIds.Contains(preset.Id);
            var (editBtn, _e) = UIHelpers.Create("EditBtn", row.transform);
            var editLE = editBtn.AddComponent<LayoutElement>();
            editLE.preferredWidth = 80;
            editLE.flexibleWidth = 0;
            UIHelpers.AddBackground(editBtn, expanded ? new Color(0.4f, 0.35f, 0.2f) : new Color(0.25f, 0.3f, 0.35f));
            UIHelpers.AddLabel(editBtn, expanded ? "Close" : "Edit", 15f, TextAlignmentOptions.Midline);
            editBtn.AddComponent<Button>().onClick.AddListener(() => {
                if (expandedIds.Contains(preset.Id)) expandedIds.Remove(preset.Id);
                else expandedIds.Add(preset.Id);
                Rebuild();
            });

            // Delete (cascade)
            var (delBtn, _d) = UIHelpers.Create("DelBtn", row.transform);
            var delLE = delBtn.AddComponent<LayoutElement>();
            delLE.preferredWidth = 80;
            delLE.flexibleWidth = 0;
            UIHelpers.AddBackground(delBtn, new Color(0.5f, 0.15f, 0.15f));
            UIHelpers.AddLabel(delBtn, "Delete", 15f, TextAlignmentOptions.Midline);
            delBtn.AddComponent<Button>().onClick.AddListener(() => {
                PresetRegistry.Delete(preset.Id, ConfigManager.Current);
                ConfigManager.Save();
                expandedIds.Remove(preset.Id);
                onPresetsChanged?.Invoke();
                Rebuild();
            });

            // Expanded editor — inline RuleEditorWidget bound to the preset itself
            if (expanded) {
                var (editorObj, _eo) = UIHelpers.Create($"Editor_{preset.Id}", parent);
                var widget = editorObj.AddComponent<RuleEditorWidget>();
                var solo = new List<TacticsRule> { preset };
                widget.Init(preset, 0, solo, () => {
                    PresetRegistry.Save(preset);
                    onPresetsChanged?.Invoke();
                }, unitId: null);
            }
        }

        void Rebuild() {
            for (int i = transform.childCount - 1; i >= 0; i--)
                Destroy(transform.GetChild(i).gameObject);
            var vlg = GetComponent<VerticalLayoutGroup>();
            if (vlg != null) Destroy(vlg);
            var csf = GetComponent<ContentSizeFitter>();
            if (csf != null) Destroy(csf);
            BuildUI();
        }
    }
}
