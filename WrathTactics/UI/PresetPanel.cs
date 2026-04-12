using System;
using System.Collections.Generic;
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
            string context = lastCharacterId == null ? "Globale Regeln" : "Character-Regeln";
            UIHelpers.AddLabel(titleObj, $"Presets (anwenden auf: {context})", 14, TextAnchor.MiddleLeft, Color.white);
            titleObj.AddComponent<LayoutElement>().preferredHeight = 30;

            // Preset list
            var presets = PresetManager.GetPresetNames();
            foreach (var presetName in presets) {
                CreatePresetRow(root.transform, presetName);
            }

            if (presets.Count == 0) {
                var (emptyObj, _) = UIHelpers.Create("NoPresets", root.transform);
                UIHelpers.AddLabel(emptyObj, "Keine Presets vorhanden.", 13, TextAnchor.MiddleLeft, Color.gray);
                emptyObj.AddComponent<LayoutElement>().preferredHeight = 28;
            }

            // Separator
            var (sep, sepRect) = UIHelpers.Create("Separator", root.transform);
            sep.AddComponent<LayoutElement>().preferredHeight = 20;

            // Save section
            var (saveRow, saveRect) = UIHelpers.Create("SaveRow", root.transform);
            saveRow.AddComponent<LayoutElement>().preferredHeight = 35;

            var (nameObj, nameRect) = UIHelpers.Create("NameInput", saveRow.transform);
            nameRect.SetAnchor(0.02, 0.6, 0.1, 0.9);
            nameRect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(nameObj, new Color(0.15f, 0.15f, 0.15f, 1f));
            var nameInput = nameObj.AddComponent<InputField>();
            var nameText = UIHelpers.AddLabel(nameObj, "", 12);
            nameInput.textComponent = nameText;
            nameInput.text = "";

            // Placeholder text
            var placeholderText = UIHelpers.AddLabel(nameObj, "Preset-Name eingeben...", 12, TextAnchor.MiddleLeft,
                new Color(0.5f, 0.5f, 0.5f));
            nameInput.placeholder = placeholderText;

            var (saveBtn, saveBtnRect) = UIHelpers.Create("SaveBtn", saveRow.transform);
            saveBtnRect.SetAnchor(0.65, 0.98, 0.1, 0.9);
            saveBtnRect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(saveBtn, new Color(0.2f, 0.4f, 0.2f, 1f));
            UIHelpers.AddLabel(saveBtn, "Speichern", 13, TextAnchor.MiddleCenter);
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
            UIHelpers.AddLabel(nameObj, presetName, 13, TextAnchor.MiddleLeft);

            // Load button
            var (loadBtn, loadRect) = UIHelpers.Create("LoadBtn", row.transform);
            loadRect.SetAnchor(0.55, 0.75, 0.1, 0.9);
            loadRect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(loadBtn, new Color(0.2f, 0.35f, 0.5f, 1f));
            UIHelpers.AddLabel(loadBtn, "Laden", 12, TextAnchor.MiddleCenter);
            loadBtn.AddComponent<Button>().onClick.AddListener(() => LoadPreset(presetName));

            // Delete button
            var (delBtn, delRect) = UIHelpers.Create("DelBtn", row.transform);
            delRect.SetAnchor(0.8, 0.98, 0.1, 0.9);
            delRect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(delBtn, new Color(0.5f, 0.15f, 0.15f, 1f));
            UIHelpers.AddLabel(delBtn, "Loeschen", 12, TextAnchor.MiddleCenter);
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
                // Load into global rules — replace completely
                config.GlobalRules.Clear();
                config.GlobalRules.AddRange(rules);
            } else {
                // Load into character rules — replace completely
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
            // Destroy all children and rebuild
            for (int i = transform.childCount - 1; i >= 0; i--)
                Destroy(transform.GetChild(i).gameObject);
            BuildUI();
        }
    }
}
