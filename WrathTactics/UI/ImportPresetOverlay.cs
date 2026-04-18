using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WrathTactics.Engine;
using WrathTactics.Logging;
using WrathTactics.Models;

namespace WrathTactics.UI {
    /// <summary>
    /// Full-screen modal that lets the user paste a JSON array of TacticsRule. On confirm,
    /// each rule is saved as a new preset with a fresh Guid and a (imported) suffix on name
    /// collision. Inline validation; modal stays open on parse failure so the user can fix.
    /// </summary>
    public class ImportPresetOverlay : MonoBehaviour {
        Action onImported;
        TMP_InputField input;
        TextMeshProUGUI errorLabel;

        public static ImportPresetOverlay Show(Transform canvasParent, Action onImported) {
            var (obj, rect) = UIHelpers.Create("ImportPresetOverlay", canvasParent);
            rect.FillParent();
            var overlay = obj.AddComponent<ImportPresetOverlay>();
            overlay.onImported = onImported;
            overlay.BuildUI();
            return overlay;
        }

        void BuildUI() {
            UIHelpers.AddBackground(gameObject, new Color(0, 0, 0, 0.6f));

            // Click-outside-to-close backdrop
            var closeBtn = gameObject.AddComponent<Button>();
            closeBtn.onClick.AddListener(Close);

            // Centered panel
            var (panel, panelRect) = UIHelpers.Create("Panel", transform);
            panelRect.SetAnchor(0.2, 0.8, 0.15, 0.85);
            panelRect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(panel, new Color(0.12f, 0.12f, 0.14f, 0.98f));
            // Block click-through on the panel
            panel.AddComponent<Image>().color = new Color(0.12f, 0.12f, 0.14f, 0.98f);

            var vlg = panel.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 8;
            vlg.padding = new RectOffset(14, 14, 14, 14);
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;

            // Title
            var (title, _t) = UIHelpers.Create("Title", panel.transform);
            title.AddComponent<LayoutElement>().preferredHeight = 32;
            UIHelpers.AddLabel(title, "Import Presets", 20f,
                TextAlignmentOptions.MidlineLeft, Color.white);

            // Hint
            var (hint, _h) = UIHelpers.Create("Hint", panel.transform);
            hint.AddComponent<LayoutElement>().preferredHeight = 32;
            UIHelpers.AddLabel(hint,
                "Paste a JSON array of rules (from another user's Export). Duplicates get a \"(imported)\" suffix.",
                13f, TextAlignmentOptions.TopLeft, new Color(0.7f, 0.7f, 0.7f));

            // Multiline input (large, fills most of the panel)
            input = UIHelpers.CreateTMPInputField(panel, "Json", 0, 1, "", 13f);
            input.lineType = TMP_InputField.LineType.MultiLineNewline;
            var inputLE = input.gameObject.AddComponent<LayoutElement>();
            inputLE.flexibleHeight = 1;
            inputLE.preferredHeight = 300;

            // Error label (empty by default)
            var (errorObj, _err) = UIHelpers.Create("Error", panel.transform);
            errorObj.AddComponent<LayoutElement>().preferredHeight = 40;
            errorLabel = UIHelpers.AddLabel(errorObj, "", 13f,
                TextAlignmentOptions.MidlineLeft, new Color(1f, 0.5f, 0.4f));

            // Buttons row
            var (buttons, _btn) = UIHelpers.Create("Buttons", panel.transform);
            buttons.AddComponent<LayoutElement>().preferredHeight = 40;
            var hlg = buttons.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;

            var (cancelBtn, _c) = UIHelpers.Create("CancelBtn", buttons.transform);
            UIHelpers.AddBackground(cancelBtn, new Color(0.35f, 0.35f, 0.35f));
            UIHelpers.AddLabel(cancelBtn, "Cancel", 15f, TextAlignmentOptions.Midline);
            cancelBtn.AddComponent<Button>().onClick.AddListener(Close);

            var (importBtn, _i) = UIHelpers.Create("ImportBtn", buttons.transform);
            UIHelpers.AddBackground(importBtn, new Color(0.2f, 0.45f, 0.2f));
            UIHelpers.AddLabel(importBtn, "Import", 15f, TextAlignmentOptions.Midline);
            importBtn.AddComponent<Button>().onClick.AddListener(OnImportClicked);
        }

        void OnImportClicked() {
            var text = input.text?.Trim() ?? "";
            if (string.IsNullOrEmpty(text)) {
                errorLabel.text = "Paste a JSON array first.";
                return;
            }

            List<TacticsRule> parsed;
            try {
                parsed = JsonConvert.DeserializeObject<List<TacticsRule>>(text);
            } catch (JsonException ex) {
                errorLabel.text = $"Invalid JSON: {ex.Message}";
                return;
            }
            if (parsed == null) {
                errorLabel.text = "Expected a JSON array.";
                return;
            }

            var existingNames = new HashSet<string>(
                PresetRegistry.All().Select(p => p.Name),
                StringComparer.OrdinalIgnoreCase);

            int imported = 0, renamed = 0;
            foreach (var preset in parsed) {
                if (preset == null) continue;
                preset.Id = Guid.NewGuid().ToString();
                preset.PresetId = null;
                string baseName = string.IsNullOrEmpty(preset.Name) ? "Imported Preset" : preset.Name;
                string finalName = baseName;
                if (existingNames.Contains(finalName)) {
                    int n = 1;
                    finalName = $"{baseName} (imported)";
                    while (existingNames.Contains(finalName)) {
                        n++;
                        finalName = $"{baseName} (imported {n})";
                    }
                    renamed++;
                }
                preset.Name = finalName;
                existingNames.Add(finalName);
                PresetRegistry.Save(preset);
                imported++;
            }

            Log.UI.Info($"Imported {imported} preset(s) ({renamed} renamed due to name conflicts)");
            onImported?.Invoke();
            Close();
        }

        void Close() {
            Destroy(gameObject);
        }
    }
}
