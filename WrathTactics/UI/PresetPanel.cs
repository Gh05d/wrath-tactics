using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WrathTactics.Engine;
using WrathTactics.Localization;
using WrathTactics.Logging;
using WrathTactics.Models;
using WrathTactics.Persistence;

namespace WrathTactics.UI {
    public class PresetPanel : MonoBehaviour {
        Action onPresetsChanged;
        readonly HashSet<string> expandedIds = new HashSet<string>();
        // Static, like PackPanel.expandedPackId: the import path invokes onPresetsChanged ->
        // TacticsPanel.RefreshRuleList, which destroys this PresetPanel and builds a fresh
        // one before the status is ever read back. An instance field loses the message on
        // every import; only used within this class (verified no other reader).
        static string lastIOStatus;
        static Color lastIOStatusColor = Theme.StatusMuted;

        /// <summary>
        /// Clears the static IO status. Being static, it now outlives not just the
        /// import-triggered rebuild (the point of making it static) but the tab switch and
        /// even a savegame load — without this, a stale "Imported 5 preset(s)." resurfaces
        /// out of context the next time the Presets tab is opened. Call this on tab ENTRY
        /// (TacticsPanel.SelectTab), never from Init/BuildUI — those also run on the very
        /// rebuild this status must survive.
        /// </summary>
        public static void ClearIOStatus() {
            lastIOStatus = null;
            lastIOStatusColor = Theme.StatusMuted;
        }

        // Filter state — driven from TacticsPanel via ApplyFilter(string).
        string currentFilter = "";
        readonly List<(GameObject entry, string name)> entries = new List<(GameObject, string)>();
        GameObject emptyMatchLabel;

        // Invariant: must not invoke onPresetsChanged synchronously. TacticsPanel.RefreshRuleList
        // assigns currentPresetPanel after Init returns; a re-entrant RefreshRuleList during Init
        // would see currentPresetPanel = null (just cleared) and destroy this half-built panel.
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

            Widgets.SectionLabelRow(root.transform, "PresetTitle", "tab.presets".i18n());
            Widgets.HintCard(root.transform, "preset.hint".i18n(), Theme.HintHeightShort);

            // One action row: New / Export all / Import as Owlcat buttons, folder as a link.
            var actions = Widgets.Row(root.transform, "PresetActions", Theme.ControlRowHeight);
            Widgets.ActionButton(actions.transform, "NewPresetBtn", "preset.button.new".i18n(), 15f, () => {
                var preset = new TacticsRule {
                    Name = "preset.default_name".i18n(),
                    ConditionGroups = new List<ConditionGroup> {
                        new ConditionGroup { Conditions = { new Condition() } }
                    }
                };
                if (!PresetRegistry.Save(preset)) {
                    SetStatus(string.Format("status.save_failed".i18n(), "status.context.new_preset".i18n()), Theme.StatusError);
                    return;
                }
                expandedIds.Add(preset.Id);
                Rebuild();
            }, 180f);
            Widgets.ActionButton(actions.transform, "ExportAllBtn", "preset.button.export_all".i18n(), 15f,
                () => ExportAllToClipboard(), 180f);
            Widgets.ActionButton(actions.transform, "ImportBtn", "preset.button.import".i18n(), 15f,
                () => ImportFromClipboard(), 180f);
            // Open Presets folder (manual file-based sharing / backup)
            Widgets.InlineLink(actions.transform, "FolderBtn", "preset.button.open_folder".i18n(), () => {
                var dir = System.IO.Path.Combine(Main.ModPath, "Presets");
                try {
                    System.IO.Directory.CreateDirectory(dir);
                    Application.OpenURL("file://" + dir);
                } catch (System.IO.IOException ex) {
                    Log.UI.Warn($"Could not open presets folder '{dir}': {ex.Message}");
                } catch (UnauthorizedAccessException ex) {
                    Log.UI.Warn($"Could not open presets folder '{dir}' (permission): {ex.Message}");
                }
            });

            // Status line — shows success/error of the last Export or Import click.
            // Rendered unconditionally, even when empty: SetStatus's save-failure callers
            // return without a rebuild, so the strip must already exist. The GameObject keeps
            // the name "IOStatus" and its label child stays "Label" — SetStatus finds it by
            // that path.
            Widgets.StatusLabel(root.transform, "IOStatus", lastIOStatus ?? "", lastIOStatusColor, Theme.StatusHeight);
            Widgets.FlourishDivider(root.transform);

            var presets = PresetRegistry.All();
            if (presets.Count == 0) {
                Widgets.StatusLabel(root.transform, "Empty", "preset.empty".i18n(), Theme.InkMuted,
                    Theme.StatusHeight + 4f, 15f);
                // Intentionally bail before emptyMatchLabel setup — nothing to filter,
                // ApplyFilter would dereference a null label. Rebuild on first preset
                // creation re-enters BuildUI and wires the filter normally.
                return;
            }

            foreach (var preset in presets) {
                CreatePresetEntry(root.transform, preset);
            }

            // Empty-match label — shown by ApplyFilter when the filter hides every entry.
            // The GameObject itself is kept in emptyMatchLabel for SetActive toggling.
            var (emptyObj, _em) = UIHelpers.Create("EmptyMatch", root.transform);
            emptyObj.AddComponent<LayoutElement>().preferredHeight = Theme.StatusHeight + 4f;
            Widgets.InkLabel(emptyObj, "filter.no_matching_presets".i18n(), 15f,
                TextAlignmentOptions.MidlineLeft, Theme.InkMuted, italic: true).margin = new Vector4(6, 0, 6, 0);
            emptyObj.SetActive(false);
            emptyMatchLabel = emptyObj;

            ApplyFilter(currentFilter);
        }

        void CreatePresetEntry(Transform parent, TacticsRule preset) {
            var row = Widgets.BandRow(parent, $"Preset_{preset.Id}", BandStyle.Mauve, Theme.HeaderHeight);
            entries.Add((row, preset.Name ?? ""));

            // Name — editable inline, renames on end-edit
            var nameInput = UIHelpers.CreateTMPInputFieldInRow(row, "Name", 200f, 1f, preset.Name, 17f);
            nameInput.onEndEdit.AddListener(v => {
                var trimmed = v?.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed == preset.Name) return;
                preset.Name = trimmed;
                if (!PresetRegistry.Save(preset)) {
                    SetStatus(string.Format("status.save_failed".i18n(), "status.context.rename".i18n()), Theme.StatusError);
                    return;
                }
                // Defer — Rebuild destroys the TMP_InputField and its teardown must not
                // race with the onEndEdit callback still on the stack.
                StartCoroutine(DeferredRebuild());
            });

            // Edit toggle
            bool expanded = expandedIds.Contains(preset.Id);
            Widgets.InlineLink(row.transform, "EditBtn", (expanded ? "button.close" : "button.edit").i18n(), () => {
                if (expandedIds.Contains(preset.Id)) expandedIds.Remove(preset.Id);
                else expandedIds.Add(preset.Id);
                Rebuild();
            }, onBand: true);

            // Delete (cascade)
            Widgets.IconButton(row.transform, "DelBtn", Icon.Delete, Theme.IconMedium, () => {
                bool fileRemoved = PresetRegistry.Delete(preset.Id, ConfigManager.Current);
                ConfigManager.Save();
                if (!fileRemoved) SetStatus(string.Format("status.save_failed".i18n(), "status.context.delete".i18n()), Theme.StatusError);
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
                        SetStatus(string.Format("status.save_failed".i18n(), "status.context.edit".i18n()), Theme.StatusError);
                }, unitId: null, hideHeader: true);
            }
        }

        void ExportAllToClipboard() {
            var all = new List<TacticsRule>(PresetRegistry.All());
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(all, Newtonsoft.Json.Formatting.Indented);
            UnityEngine.GUIUtility.systemCopyBuffer = json;
            SetStatus(string.Format("status.export_copied".i18n(), all.Count), Theme.StatusOk);
            Log.UI.Info($"Copied {all.Count} preset(s) to clipboard");
        }

        void ImportFromClipboard() {
            var text = UnityEngine.GUIUtility.systemCopyBuffer?.Trim() ?? "";
            if (string.IsNullOrEmpty(text)) {
                SetStatus("status.clipboard_empty".i18n(), Theme.StatusError);
                return;
            }

            // A pack bundle is a JSON object; the legacy preset export is a JSON array.
            // Sniff the first non-whitespace character rather than attempting both parses.
            if (text[0] == '{') {
                if (TryImportPackBundle(text, out var packStatus, out var packColor)) {
                    SetStatus(packStatus, packColor);
                    onPresetsChanged?.Invoke();
                    Rebuild();
                } else {
                    SetStatus(packStatus, packColor);
                }
                return;
            }
            List<TacticsRule> parsed;
            try {
                parsed = Newtonsoft.Json.JsonConvert.DeserializeObject<List<TacticsRule>>(text);
            } catch (Newtonsoft.Json.JsonException ex) {
                SetStatus(string.Format("status.clipboard_invalid_json".i18n(), ex.Message), Theme.StatusError);
                return;
            }
            if (parsed == null) {
                SetStatus("status.clipboard_not_array".i18n(), Theme.StatusError);
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
                preset.PackId = null;  // same leak class as the bundle import path below — a
                                        // preset is never pack-owned (see PromoteRuleToPreset)
                string baseName = string.IsNullOrEmpty(preset.Name) ? "preset.imported_default_name".i18n() : preset.Name;
                string finalName = baseName;
                bool wasRenamed = false;
                if (existingNames.Contains(finalName)) {
                    int n = 1;
                    finalName = baseName + "preset.imported_suffix".i18n();
                    while (existingNames.Contains(finalName)) {
                        n++;
                        finalName = baseName + string.Format("preset.imported_suffix_n".i18n(), n);
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
                SetStatus(string.Format("status.import_partial_failure".i18n(), imported, failed), Theme.StatusError);
            } else {
                SetStatus(
                    renamed > 0
                        ? string.Format("status.import_with_renames".i18n(), imported, renamed)
                        : string.Format("status.import_success".i18n(), imported),
                    Theme.StatusOk);
            }
            onPresetsChanged?.Invoke();
            Rebuild();
        }

        /// <summary>
        /// Imports a pack bundle produced by PackPanel's Export. Member presets are imported
        /// as new presets (fresh ids, so an existing preset is never overwritten) and the pack
        /// is rewritten to point at those new ids.
        /// </summary>
        bool TryImportPackBundle(string text, out string status, out Color color) {
            PackPanel.PackBundle bundle;
            try {
                bundle = Newtonsoft.Json.JsonConvert.DeserializeObject<PackPanel.PackBundle>(text);
            } catch (Newtonsoft.Json.JsonException ex) {
                status = string.Format("status.clipboard_invalid_json".i18n(), ex.Message);
                color = Theme.StatusError;
                return false;
            }
            if (bundle?.Pack == null || bundle.Presets == null) {
                status = "status.clipboard_not_array".i18n();
                color = Theme.StatusError;
                return false;
            }

            // oldId -> newId, so the pack's member list can be remapped after import.
            var idMap = new Dictionary<string, string>();
            int failed = 0;
            foreach (var preset in bundle.Presets) {
                if (preset == null || string.IsNullOrEmpty(preset.Id)) continue;
                var oldId = preset.Id;
                preset.Id = Guid.NewGuid().ToString();
                preset.PresetId = null;
                preset.PackId = null;
                if (PresetRegistry.Save(preset)) idMap[oldId] = preset.Id;
                else failed++;
            }

            var pack = bundle.Pack;
            pack.Id = Guid.NewGuid().ToString();
            // Newtonsoft overwrites the field initializer when the JSON explicitly carries
            // "PresetIds": null (hand-edited or truncated bundle) — repair it before the
            // remap loop dereferences it, same as PackManager.LoadAllFrom does on disk load.
            if (pack.PresetIds == null) pack.PresetIds = new List<string>();
            var remapped = new List<string>();
            foreach (var oldId in pack.PresetIds) {
                if (oldId != null && idMap.TryGetValue(oldId, out var newId)) remapped.Add(newId);
            }
            pack.PresetIds = remapped;
            // The presets above are already committed (in-memory, and on disk where the
            // per-preset write succeeded) — a failure here means the pack record itself
            // didn't persist. Still return true so the caller refreshes the UI to match
            // that partially-committed state, but route the failure through the status
            // line rather than reporting a success that isn't durable.
            if (!PackRegistry.Save(pack)) {
                status = string.Format("status.save_failed".i18n(), pack.Name);
                color = Theme.StatusError;
                return true;
            }

            // A failed per-preset save (above) never entered idMap, so it silently fell out
            // of remapped too — surface it rather than reporting an all-green import that
            // undercounts what actually landed on disk.
            if (failed > 0) {
                status = string.Format("status.import_partial_failure".i18n(), remapped.Count, failed);
                color = Theme.StatusError;
            } else {
                status = string.Format("status.pack_import_success".i18n(), pack.Name, remapped.Count);
                color = Theme.StatusOk;
            }
            return true;
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
