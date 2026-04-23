using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
        string lastIOStatus;
        Color lastIOStatusColor = Color.gray;

        // Filter state — driven from TacticsPanel via ApplyFilter(string).
        string currentFilter = "";
        readonly List<(GameObject entry, string name)> entries = new List<(GameObject, string)>();
        GameObject emptyMatchLabel;

        public void Init(string _unusedCharacterId, Transform _unusedParent, Action onPresetsChanged) {
            this.onPresetsChanged = onPresetsChanged;
            BuildUI();
        }

        void BuildUI() {
            entries.Clear();
            emptyMatchLabel = null;
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

            // Export All Presets — copies the whole collection as a JSON array
            var (exportAllBtn, _ea) = UIHelpers.Create("ExportAllBtn", root.transform);
            exportAllBtn.AddComponent<LayoutElement>().preferredHeight = 36;
            UIHelpers.AddBackground(exportAllBtn, new Color(0.3f, 0.3f, 0.5f, 1f));
            UIHelpers.AddLabel(exportAllBtn, "Export All Presets to Clipboard", 15f, TextAlignmentOptions.Midline);
            exportAllBtn.AddComponent<Button>().onClick.AddListener(() => ExportAllToClipboard());

            // Import Presets — reads directly from clipboard and imports
            var (importBtn, _imp) = UIHelpers.Create("ImportBtn", root.transform);
            importBtn.AddComponent<LayoutElement>().preferredHeight = 36;
            UIHelpers.AddBackground(importBtn, new Color(0.2f, 0.45f, 0.3f, 1f));
            UIHelpers.AddLabel(importBtn, "Import Presets from Clipboard", 15f, TextAlignmentOptions.Midline);
            importBtn.AddComponent<Button>().onClick.AddListener(() => ImportFromClipboard());

            // Status line — shows success/error of the last Export or Import click
            var (statusObj, _st) = UIHelpers.Create("IOStatus", root.transform);
            statusObj.AddComponent<LayoutElement>().preferredHeight = 24;
            var statusLabel = UIHelpers.AddLabel(statusObj, lastIOStatus ?? "", 13f,
                TextAlignmentOptions.MidlineLeft, lastIOStatusColor);

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
                if (!PresetRegistry.Save(preset)) {
                    SetStatus("Save failed — check mod log: new preset", new Color(1f, 0.5f, 0.4f));
                    return;
                }
                expandedIds.Add(preset.Id);
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

            // Open Presets folder (manual file-based sharing / backup)
            var (folderBtn, _folder) = UIHelpers.Create("FolderBtn", root.transform);
            folderBtn.AddComponent<LayoutElement>().preferredHeight = 32;
            UIHelpers.AddBackground(folderBtn, new Color(0.25f, 0.25f, 0.3f, 1f));
            UIHelpers.AddLabel(folderBtn, "Open Presets Folder", 14f, TextAlignmentOptions.Midline);
            folderBtn.AddComponent<Button>().onClick.AddListener(() => {
                var dir = System.IO.Path.Combine(Main.ModPath, "Presets");
                try {
                    System.IO.Directory.CreateDirectory(dir);
                    Application.OpenURL("file://" + dir);
                } catch (Exception ex) {
                    Log.UI.Warn($"Could not open presets folder '{dir}': {ex.Message}");
                }
            });

            // Empty-match label — shown by ApplyFilter when the filter hides every entry.
            var (emptyObj, _em) = UIHelpers.Create("EmptyMatch", root.transform);
            emptyObj.AddComponent<LayoutElement>().preferredHeight = 28;
            UIHelpers.AddLabel(emptyObj, "No matching presets", 15f,
                TextAlignmentOptions.MidlineLeft, new Color(0.6f, 0.6f, 0.6f));
            emptyObj.SetActive(false);
            emptyMatchLabel = emptyObj;

            ApplyFilter(currentFilter);
        }

        void CreatePresetEntry(Transform parent, TacticsRule preset) {
            var (row, _) = UIHelpers.Create($"Preset_{preset.Id}", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 40;
            UIHelpers.AddBackground(row, new Color(0.18f, 0.18f, 0.18f, 1f));
            entries.Add((row, preset.Name ?? ""));

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
                if (!PresetRegistry.Save(preset)) {
                    SetStatus("Save failed — check mod log: rename", new Color(1f, 0.5f, 0.4f));
                    return;
                }
                // Defer — Rebuild destroys the TMP_InputField and its teardown must not
                // race with the onEndEdit callback still on the stack.
                StartCoroutine(DeferredRebuild());
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
                bool fileRemoved = PresetRegistry.Delete(preset.Id, ConfigManager.Current);
                ConfigManager.Save();
                if (!fileRemoved) SetStatus("Save failed — check mod log: delete", new Color(1f, 0.5f, 0.4f));
                expandedIds.Remove(preset.Id);
                Rebuild();
            });

            // Expanded editor — inline RuleEditorWidget bound to the preset itself.
            // hideHeader:true so the widget renders only the body; rename/delete live on the row above.
            if (expanded) {
                var (editorObj, _eo) = UIHelpers.Create($"Editor_{preset.Id}", parent);
                var widget = editorObj.AddComponent<RuleEditorWidget>();
                var solo = new List<TacticsRule> { preset };
                widget.Init(preset, 0, solo, () => {
                    if (!PresetRegistry.Save(preset))
                        SetStatus("Save failed — check mod log: edit", new Color(1f, 0.5f, 0.4f));
                }, unitId: null, hideHeader: true);
            }
        }

        void ExportAllToClipboard() {
            var all = new List<TacticsRule>(PresetRegistry.All());
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(all, Newtonsoft.Json.Formatting.Indented);
            UnityEngine.GUIUtility.systemCopyBuffer = json;
            SetStatus($"Copied {all.Count} preset(s) to clipboard.", new Color(0.6f, 0.85f, 0.6f));
            Log.UI.Info($"Copied {all.Count} preset(s) to clipboard");
        }

        void ImportFromClipboard() {
            var text = UnityEngine.GUIUtility.systemCopyBuffer?.Trim() ?? "";
            if (string.IsNullOrEmpty(text)) {
                SetStatus("Clipboard is empty. Copy a preset JSON array first.", new Color(1f, 0.5f, 0.4f));
                return;
            }
            List<TacticsRule> parsed;
            try {
                parsed = Newtonsoft.Json.JsonConvert.DeserializeObject<List<TacticsRule>>(text);
            } catch (Newtonsoft.Json.JsonException ex) {
                SetStatus($"Invalid JSON on clipboard: {ex.Message}", new Color(1f, 0.5f, 0.4f));
                return;
            }
            if (parsed == null) {
                SetStatus("Clipboard does not contain a JSON array of presets.", new Color(1f, 0.5f, 0.4f));
                return;
            }

            var existingNames = new HashSet<string>(
                PresetRegistry.All().Select(p => p.Name),
                StringComparer.OrdinalIgnoreCase);
            int imported = 0, renamed = 0, failed = 0;
            foreach (var preset in parsed) {
                if (preset == null) continue;
                preset.Id = Guid.NewGuid().ToString();
                preset.PresetId = null;
                string baseName = string.IsNullOrEmpty(preset.Name) ? "Imported Preset" : preset.Name;
                string finalName = baseName;
                bool wasRenamed = false;
                if (existingNames.Contains(finalName)) {
                    int n = 1;
                    finalName = $"{baseName} (imported)";
                    while (existingNames.Contains(finalName)) {
                        n++;
                        finalName = $"{baseName} (imported {n})";
                    }
                    wasRenamed = true;
                }
                preset.Name = finalName;
                if (PresetRegistry.Save(preset)) {
                    existingNames.Add(finalName);
                    imported++;
                    if (wasRenamed) renamed++;
                } else {
                    failed++;
                }
            }
            Log.UI.Info($"Imported {imported} preset(s) ({renamed} renamed, {failed} failed)");
            if (failed > 0) {
                SetStatus($"Imported {imported} preset(s), {failed} failed — check mod log.", new Color(1f, 0.5f, 0.4f));
            } else {
                SetStatus(
                    renamed > 0
                        ? $"Imported {imported} preset(s) — {renamed} renamed due to name conflicts."
                        : $"Imported {imported} preset(s).",
                    new Color(0.6f, 0.85f, 0.6f));
            }
            onPresetsChanged?.Invoke();
            Rebuild();
        }

        void SetStatus(string text, Color color) {
            lastIOStatus = text;
            lastIOStatusColor = color;
            // If we're not rebuilding immediately, reflect the change now.
            var label = transform.Find("IOStatus/Label")?.GetComponent<TMP_Text>();
            if (label != null) { label.text = text; label.color = color; }
        }

        IEnumerator DeferredRebuild() {
            yield return null;
            Rebuild();
        }

        void Rebuild() {
            for (int i = transform.childCount - 1; i >= 0; i--)
                Destroy(transform.GetChild(i).gameObject);
            // DestroyImmediate for layout components — deferred Destroy leaves duplicates
            // that fight over sizing for one frame, causing broken layout.
            var vlg = GetComponent<VerticalLayoutGroup>();
            if (vlg != null) DestroyImmediate(vlg);
            var csf = GetComponent<ContentSizeFitter>();
            if (csf != null) DestroyImmediate(csf);
            BuildUI();
        }

        public void ApplyFilter(string query) {
            currentFilter = query ?? "";
            int visible = 0;
            foreach (var pair in entries) {
                bool match = UIHelpers.StringMatchesFilter(pair.name, currentFilter);
                if (pair.entry != null) pair.entry.SetActive(match);
                if (match) visible++;
            }
            bool filterActive = !string.IsNullOrWhiteSpace(currentFilter);
            if (emptyMatchLabel != null)
                emptyMatchLabel.SetActive(filterActive && entries.Count > 0 && visible == 0);
        }
    }
}
