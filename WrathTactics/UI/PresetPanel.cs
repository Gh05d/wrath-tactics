using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WrathTactics.Logging;
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

            // Stack children vertically with preferred heights
            var vlg = root.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 6;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.padding = new RectOffset(6, 6, 6, 6);

            var csf = root.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Title + workflow hint
            var (titleObj, _) = UIHelpers.Create("PresetTitle", root.transform);
            titleObj.AddComponent<LayoutElement>().preferredHeight = 30;
            string context = lastCharacterId == null ? "Global Rules" : "Character Rules";
            UIHelpers.AddLabel(titleObj, $"Presets (apply to: {context})", 20f,
                TextAlignmentOptions.MidlineLeft, Color.white);

            var (hintObj, _h) = UIHelpers.Create("PresetHint", root.transform);
            hintObj.AddComponent<LayoutElement>().preferredHeight = 40;
            string hintSource = lastCharacterId == null ? "global rules" : "this character's rules";
            UIHelpers.AddLabel(hintObj,
                $"Type a name below and click Save to store {hintSource} as a preset. Load copies a preset onto the active tab (overwrites its rules).",
                13f, TextAlignmentOptions.TopLeft, new Color(0.7f, 0.7f, 0.7f));

            // Save section — larger, input fills row
            var (saveRow, _s) = UIHelpers.Create("SaveRow", root.transform);
            saveRow.AddComponent<LayoutElement>().preferredHeight = 44;
            var saveHlg = saveRow.AddComponent<HorizontalLayoutGroup>();
            saveHlg.spacing = 6;
            saveHlg.childForceExpandWidth = false;
            saveHlg.childForceExpandHeight = true;
            saveHlg.childControlWidth = true;
            saveHlg.childControlHeight = true;
            saveHlg.padding = new RectOffset(0, 0, 2, 2);

            var nameInput = UIHelpers.CreateTMPInputField(saveRow, "NameInput",
                0, 1, "", 18f);
            var nameRect = nameInput.GetComponent<RectTransform>();
            nameRect.SetAnchor(0, 1, 0, 1);
            nameRect.offsetMin = Vector2.zero;
            nameRect.offsetMax = Vector2.zero;
            var nameLE = nameInput.gameObject.AddComponent<LayoutElement>();
            nameLE.flexibleWidth = 1;
            nameLE.preferredWidth = 300;
            UIHelpers.AddBackground(nameInput.gameObject, new Color(0.12f, 0.12f, 0.12f, 1f));

            // Placeholder
            var (phObj, phRect) = UIHelpers.Create("Placeholder", nameInput.textViewport);
            phRect.FillParent();
            var phText = phObj.AddComponent<TextMeshProUGUI>();
            phText.text = "Enter preset name…";
            phText.fontSize = 16f;
            phText.alignment = TextAlignmentOptions.MidlineLeft;
            phText.color = new Color(0.5f, 0.5f, 0.5f);
            phText.enableWordWrapping = false;
            phText.overflowMode = TextOverflowModes.Ellipsis;
            phText.raycastTarget = false;
            nameInput.placeholder = phText;

            // Save button
            var (saveBtn, _sb) = UIHelpers.Create("SaveBtn", saveRow.transform);
            var saveLE = saveBtn.AddComponent<LayoutElement>();
            saveLE.preferredWidth = 100;
            saveLE.flexibleWidth = 0;
            UIHelpers.AddBackground(saveBtn, new Color(0.2f, 0.4f, 0.2f, 1f));
            UIHelpers.AddLabel(saveBtn, "Save", 18f, TextAlignmentOptions.Midline);
            saveBtn.AddComponent<Button>().onClick.AddListener(() => {
                var name = nameInput.text?.Trim();
                if (string.IsNullOrEmpty(name)) return;

                var rules = GetCurrentRules();
                if (rules == null || rules.Count == 0) {
                    Log.UI.Warn("No rules to save");
                    return;
                }

                PresetManager.SavePreset(name, rules);
                Rebuild();
            });

            // Separator
            var (sep, _sr) = UIHelpers.Create("Separator", root.transform);
            sep.AddComponent<LayoutElement>().preferredHeight = 14;

            // Saved presets list label
            var (listLabel, _ll) = UIHelpers.Create("ListLabel", root.transform);
            listLabel.AddComponent<LayoutElement>().preferredHeight = 22;
            UIHelpers.AddLabel(listLabel, "Saved presets:", 16f,
                TextAlignmentOptions.MidlineLeft, new Color(0.7f, 0.7f, 0.5f));

            // Preset list
            var presets = PresetManager.GetPresetNames();
            foreach (var presetName in presets) {
                CreatePresetRow(root.transform, presetName);
            }

            if (presets.Count == 0) {
                var (emptyObj, _e) = UIHelpers.Create("NoPresets", root.transform);
                emptyObj.AddComponent<LayoutElement>().preferredHeight = 28;
                UIHelpers.AddLabel(emptyObj, "No presets saved yet.", 15f,
                    TextAlignmentOptions.MidlineLeft, Color.gray);
            }
        }

        void CreatePresetRow(Transform parent, string presetName) {
            var (row, _) = UIHelpers.Create($"Preset_{presetName}", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 36;
            UIHelpers.AddBackground(row, new Color(0.18f, 0.18f, 0.18f, 1f));

            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.padding = new RectOffset(8, 8, 2, 2);
            hlg.childAlignment = TextAnchor.MiddleLeft;

            // Name label — flexible
            var (nameObj, _n) = UIHelpers.Create("Name", row.transform);
            var nameLE = nameObj.AddComponent<LayoutElement>();
            nameLE.flexibleWidth = 1;
            nameLE.preferredWidth = 200;
            UIHelpers.AddLabel(nameObj, presetName, 17f, TextAlignmentOptions.MidlineLeft);

            // Load button
            var (loadBtn, _l) = UIHelpers.Create("LoadBtn", row.transform);
            var loadLE = loadBtn.AddComponent<LayoutElement>();
            loadLE.preferredWidth = 80;
            loadLE.flexibleWidth = 0;
            UIHelpers.AddBackground(loadBtn, new Color(0.2f, 0.35f, 0.5f, 1f));
            UIHelpers.AddLabel(loadBtn, "Load", 15f, TextAlignmentOptions.Midline);
            loadBtn.AddComponent<Button>().onClick.AddListener(() => LoadPreset(presetName));

            // Delete button
            var (delBtn, _d) = UIHelpers.Create("DelBtn", row.transform);
            var delLE = delBtn.AddComponent<LayoutElement>();
            delLE.preferredWidth = 80;
            delLE.flexibleWidth = 0;
            UIHelpers.AddBackground(delBtn, new Color(0.5f, 0.15f, 0.15f, 1f));
            UIHelpers.AddLabel(delBtn, "Delete", 15f, TextAlignmentOptions.Midline);
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
            Log.UI.Info($"Loaded preset '{presetName}' into {(lastCharacterId == null ? "global" : lastCharacterId)}");
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
            var vlg = GetComponent<VerticalLayoutGroup>();
            if (vlg != null) Destroy(vlg);
            var csf = GetComponent<ContentSizeFitter>();
            if (csf != null) Destroy(csf);
            BuildUI();
        }
    }
}
