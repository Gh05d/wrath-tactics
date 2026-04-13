using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WrathTactics.Models;
using WrathTactics.Persistence;

namespace WrathTactics.UI {
    public class PresetPanel : MonoBehaviour {
        string lastCharacterId; // The character tab that was active before switching to Presets
        Action onRulesLoaded;   // Callback to refresh rule list after loading a preset
        Transform contentParent;

        public void Init(string lastCharacterId, Transform contentParent, Action onRulesLoaded) {
            this.lastCharacterId = lastCharacterId;
            this.contentParent = contentParent;
            this.onRulesLoaded = onRulesLoaded;
            BuildUI();
        }

        void BuildUI() {
            var root = gameObject;

            // Title
            var (titleObj, titleRect) = UIHelpers.Create("PresetTitle", root.transform);
            titleRect.SetAnchor(0, 1, 0.9, 1);
            titleRect.sizeDelta = Vector2.zero;
            string context = lastCharacterId == null ? "Global Rules" : "Character Rules";
            UIHelpers.AddLabel(titleObj, $"Presets (apply to: {context})", 17f,
                TextAlignmentOptions.MidlineLeft, Color.white);
            titleObj.AddComponent<LayoutElement>().preferredHeight = 30;

            // Preset list
            var presets = PresetManager.GetPresetNames();
            foreach (var presetName in presets) {
                CreatePresetRow(root.transform, presetName);
            }

            if (presets.Count == 0) {
                var (emptyObj, _) = UIHelpers.Create("NoPresets", root.transform);
                UIHelpers.AddLabel(emptyObj, "No presets saved.", 16f,
                    TextAlignmentOptions.MidlineLeft, Color.gray);
                emptyObj.AddComponent<LayoutElement>().preferredHeight = 28;
            }

            // Separator
            var (sep, sepRect) = UIHelpers.Create("Separator", root.transform);
            sep.AddComponent<LayoutElement>().preferredHeight = 20;

            // Save section
            var (saveRow, saveRect) = UIHelpers.Create("SaveRow", root.transform);
            saveRow.AddComponent<LayoutElement>().preferredHeight = 35;

            // Name input using TMP_InputField
            var nameInput = UIHelpers.CreateTMPInputField(saveRow, "NameInput",
                0.02, 0.6, "", 14f);
            // Adjust vertical anchors
            var nameRect = nameInput.GetComponent<RectTransform>();
            nameRect.SetAnchor(0.02, 0.6, 0.1, 0.9);

            // Placeholder
            var (phObj, phRect) = UIHelpers.Create("Placeholder", nameInput.textViewport);
            phRect.FillParent();
            var phText = phObj.AddComponent<TextMeshProUGUI>();
            phText.text = "Enter preset name...";
            phText.fontSize = 14f;
            phText.alignment = TextAlignmentOptions.MidlineLeft;
            phText.color = new Color(0.5f, 0.5f, 0.5f);
            phText.enableWordWrapping = false;
            phText.overflowMode = TextOverflowModes.Ellipsis;
            phText.raycastTarget = false;
            nameInput.placeholder = phText;

            // Save button
            var (saveBtn, saveBtnRect) = UIHelpers.Create("SaveBtn", saveRow.transform);
            saveBtnRect.SetAnchor(0.65, 0.98, 0.1, 0.9);
            saveBtnRect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(saveBtn, new Color(0.2f, 0.4f, 0.2f, 1f));
            UIHelpers.AddLabel(saveBtn, "Save", 16f, TextAlignmentOptions.Midline);
            saveBtn.AddComponent<Button>().onClick.AddListener(() => {
                var name = nameInput.text?.Trim();
                if (string.IsNullOrEmpty(name)) return;

                var rules = GetCurrentRules();
                if (rules == null || rules.Count == 0) {
                    Main.Log("[Presets] No rules to save");
                    return;
                }

                PresetManager.SavePreset(name, rules);
                Rebuild();
            });
        }

        void CreatePresetRow(Transform parent, string presetName) {
            var (row, rowRect) = UIHelpers.Create($"Preset_{presetName}", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 32;
            UIHelpers.AddBackground(row, new Color(0.18f, 0.18f, 0.18f, 1f));

            // Name label
            var (nameObj, nameRect) = UIHelpers.Create("Name", row.transform);
            nameRect.SetAnchor(0.02, 0.5, 0, 1);
            nameRect.sizeDelta = Vector2.zero;
            UIHelpers.AddLabel(nameObj, presetName, 16f, TextAlignmentOptions.MidlineLeft);

            // Load button
            var (loadBtn, loadRect) = UIHelpers.Create("LoadBtn", row.transform);
            loadRect.SetAnchor(0.55, 0.75, 0.1, 0.9);
            loadRect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(loadBtn, new Color(0.2f, 0.35f, 0.5f, 1f));
            UIHelpers.AddLabel(loadBtn, "Load", 14f, TextAlignmentOptions.Midline);
            loadBtn.AddComponent<Button>().onClick.AddListener(() => LoadPreset(presetName));

            // Delete button
            var (delBtn, delRect) = UIHelpers.Create("DelBtn", row.transform);
            delRect.SetAnchor(0.8, 0.98, 0.1, 0.9);
            delRect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(delBtn, new Color(0.5f, 0.15f, 0.15f, 1f));
            UIHelpers.AddLabel(delBtn, "Delete", 14f, TextAlignmentOptions.Midline);
            delBtn.AddComponent<Button>().onClick.AddListener(() => {
                PresetManager.DeletePreset(presetName);
                Rebuild();
            });
        }

        void LoadPreset(string presetName) {
            var rules = PresetManager.LoadPreset(presetName);
            if (rules == null) return;

            var config = ConfigManager.Current;
            if (lastCharacterId == null) {
                config.GlobalRules.Clear();
                config.GlobalRules.AddRange(rules);
            } else {
                config.CharacterRules[lastCharacterId] = new List<TacticsRule>(rules);
            }

            ConfigManager.Save();
            Main.Log($"[Presets] Loaded '{presetName}' into {(lastCharacterId == null ? "global" : lastCharacterId)}");
            onRulesLoaded?.Invoke();
        }

        List<TacticsRule> GetCurrentRules() {
            var config = ConfigManager.Current;
            if (lastCharacterId == null) return config.GlobalRules;
            return config.GetRulesForCharacter(lastCharacterId);
        }

        void Rebuild() {
            for (int i = transform.childCount - 1; i >= 0; i--)
                Destroy(transform.GetChild(i).gameObject);
            BuildUI();
        }
    }
}
