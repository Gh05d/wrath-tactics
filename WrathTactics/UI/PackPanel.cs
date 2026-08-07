using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WrathTactics.Engine;
using WrathTactics.Localization;
using WrathTactics.Models;

namespace WrathTactics.UI {
    /// <summary>
    /// The Packs section rendered at the top of the Presets tab. Stateless apart from which
    /// pack is expanded — the host (PresetPanel) owns the rebuild, so every mutation ends in
    /// onChanged() rather than touching the hierarchy directly.
    /// </summary>
    public static class PackPanel {
        // Which pack's member editor is open. Static so it survives the host's Rebuild,
        // matching PresetPanel.expandedIds semantics.
        static string expandedPackId;

        public static void Build(Transform parent, Action onChanged, Action<string, Color> setStatus) {
            var (titleObj, _) = UIHelpers.Create("PackTitle", parent);
            titleObj.AddComponent<LayoutElement>().preferredHeight = 26;
            UIHelpers.AddLabel(titleObj, "pack.section_title".i18n(), 18f,
                TextAlignmentOptions.MidlineLeft, Color.white);

            UIHelpers.AddHintCard(parent, "pack.hint".i18n(), 40f);

            var (newBtn, _n) = UIHelpers.Create("NewPackBtn", parent);
            newBtn.AddComponent<LayoutElement>().preferredHeight = 34;
            UIHelpers.AddBackground(newBtn, new Color(0.2f, 0.4f, 0.45f, 1f));
            UIHelpers.AddLabel(newBtn, "pack.button.new".i18n(), 15f, TextAlignmentOptions.Midline);
            newBtn.AddComponent<Button>().onClick.AddListener(() => {
                var pack = new TacticsPack { Name = "pack.default_name".i18n() };
                if (!PackRegistry.Save(pack)) {
                    setStatus(string.Format("status.save_failed".i18n(), "pack.section_title".i18n()),
                        new Color(1f, 0.5f, 0.4f));
                    return;
                }
                expandedPackId = pack.Id;
                onChanged();
            });

            var packs = PackRegistry.All();
            if (packs.Count == 0) {
                var (empty, _e) = UIHelpers.Create("PackEmpty", parent);
                empty.AddComponent<LayoutElement>().preferredHeight = 26;
                UIHelpers.AddLabel(empty, "pack.empty".i18n(), 14f,
                    TextAlignmentOptions.MidlineLeft, Color.gray);
            }

            foreach (var pack in packs) CreatePackRow(parent, pack, onChanged, setStatus);

            var (sep, _s) = UIHelpers.Create("PackSep", parent);
            sep.AddComponent<LayoutElement>().preferredHeight = 12;
        }

        static void CreatePackRow(Transform parent, TacticsPack pack, Action onChanged,
            Action<string, Color> setStatus) {

            var (row, _) = UIHelpers.Create($"Pack_{pack.Id}", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 38;
            UIHelpers.AddBackground(row, new Color(0.16f, 0.16f, 0.16f, 1f));

            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.padding = new RectOffset(8, 8, 4, 4);
            hlg.childAlignment = TextAnchor.MiddleLeft;

            // Colour swatch — click cycles to the next palette entry.
            var (swatch, _sw) = UIHelpers.Create("Swatch", row.transform);
            var swatchLE = swatch.AddComponent<LayoutElement>();
            swatchLE.preferredWidth = 34;
            swatchLE.flexibleWidth = 0;
            UIHelpers.AddBackground(swatch, PackPalette.ColorAt(pack.ColorIndex));
            UIHelpers.AddLabel(swatch, "●", 16f, TextAlignmentOptions.Midline, Color.white);
            swatch.AddComponent<Button>().onClick.AddListener(() => {
                pack.ColorIndex = PackPalette.Next(pack.ColorIndex);
                if (!PackRegistry.Save(pack))
                    setStatus(string.Format("status.save_failed".i18n(), pack.Name), new Color(1f, 0.5f, 0.4f));
                onChanged();
            });

            // Name — inline rename on end-edit.
            var nameInput = UIHelpers.CreateTMPInputField(row, "PackName", 0, 1, pack.Name, 16f);
            var nameLE = nameInput.gameObject.AddComponent<LayoutElement>();
            nameLE.flexibleWidth = 1;
            nameLE.preferredWidth = 160;
            nameInput.onEndEdit.AddListener(v => {
                var trimmed = v?.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed == pack.Name) return;
                pack.Name = trimmed;
                if (!PackRegistry.Save(pack)) {
                    setStatus(string.Format("status.save_failed".i18n(), "status.context.rename".i18n()),
                        new Color(1f, 0.5f, 0.4f));
                    return;
                }
                // Deferred by the host: rebuilding here would destroy this TMP_InputField
                // while its own onEndEdit is still on the stack.
                onChanged();
            });

            var (countObj, _c) = UIHelpers.Create("PackCount", row.transform);
            var countLE = countObj.AddComponent<LayoutElement>();
            countLE.preferredWidth = 90;
            countLE.flexibleWidth = 0;
            UIHelpers.AddLabel(countObj,
                string.Format("pack.member_count".i18n(), pack.PresetIds.Count), 13f,
                TextAlignmentOptions.Midline, Color.gray);

            bool expanded = expandedPackId == pack.Id;
            var (membersBtn, _m) = UIHelpers.Create("MembersBtn", row.transform);
            var membersLE = membersBtn.AddComponent<LayoutElement>();
            membersLE.preferredWidth = 80;
            membersLE.flexibleWidth = 0;
            UIHelpers.AddBackground(membersBtn,
                expanded ? new Color(0.4f, 0.35f, 0.2f) : new Color(0.25f, 0.3f, 0.35f));
            UIHelpers.AddLabel(membersBtn,
                (expanded ? "pack.button.close_members" : "pack.button.members").i18n(), 14f,
                TextAlignmentOptions.Midline);
            membersBtn.AddComponent<Button>().onClick.AddListener(() => {
                expandedPackId = expanded ? null : pack.Id;
                onChanged();
            });

            var (exportBtn, _x) = UIHelpers.Create("PackExportBtn", row.transform);
            var exportLE = exportBtn.AddComponent<LayoutElement>();
            exportLE.preferredWidth = 70;
            exportLE.flexibleWidth = 0;
            UIHelpers.AddBackground(exportBtn, new Color(0.3f, 0.3f, 0.5f, 1f));
            UIHelpers.AddLabel(exportBtn, "pack.button.export".i18n(), 14f, TextAlignmentOptions.Midline);
            exportBtn.AddComponent<Button>().onClick.AddListener(() => ExportPackToClipboard(pack, setStatus));

            var (delBtn, _d) = UIHelpers.Create("PackDelBtn", row.transform);
            var delLE = delBtn.AddComponent<LayoutElement>();
            delLE.preferredWidth = 70;
            delLE.flexibleWidth = 0;
            UIHelpers.AddBackground(delBtn, new Color(0.5f, 0.15f, 0.15f));
            UIHelpers.AddLabel(delBtn, "button.delete".i18n(), 14f, TextAlignmentOptions.Midline);
            delBtn.AddComponent<Button>().onClick.AddListener(() => {
                if (!PackRegistry.Delete(pack.Id))
                    setStatus(string.Format("status.save_failed".i18n(), "status.context.delete".i18n()),
                        new Color(1f, 0.5f, 0.4f));
                if (expandedPackId == pack.Id) expandedPackId = null;
                onChanged();
            });

            if (expanded) CreateMemberEditor(parent, pack, onChanged, setStatus);
        }

        /// <summary>
        /// Copies a self-contained bundle: the pack plus a full copy of every member preset.
        /// Exporting ids alone would resolve to nothing on the recipient's machine.
        /// </summary>
        static void ExportPackToClipboard(TacticsPack pack, Action<string, Color> setStatus) {
            var presets = new List<TacticsRule>();
            foreach (var id in pack.PresetIds) {
                var preset = PresetRegistry.Get(id);
                if (preset != null) presets.Add(preset);
            }
            if (presets.Count == 0) {
                setStatus(string.Format("status.pack_export_empty".i18n(), pack.Name),
                    new Color(1f, 0.5f, 0.4f));
                return;
            }

            var bundle = new PackBundle { Pack = pack, Presets = presets };
            GUIUtility.systemCopyBuffer =
                Newtonsoft.Json.JsonConvert.SerializeObject(bundle, Newtonsoft.Json.Formatting.Indented);
            setStatus(string.Format("status.pack_export_copied".i18n(), pack.Name, presets.Count),
                new Color(0.6f, 0.85f, 0.6f));
        }

        /// <summary>Clipboard wire format for a shared pack. Presets are inlined copies.</summary>
        public class PackBundle {
            public TacticsPack Pack;
            public List<TacticsRule> Presets;
        }

        static void CreateMemberEditor(Transform parent, TacticsPack pack, Action onChanged,
            Action<string, Color> setStatus) {

            var (title, _t) = UIHelpers.Create($"Members_{pack.Id}", parent);
            title.AddComponent<LayoutElement>().preferredHeight = 24;
            UIHelpers.AddLabel(title, "pack.members_title".i18n(), 13f,
                TextAlignmentOptions.MidlineLeft, new Color(0.8f, 0.8f, 0.8f));

            if (pack.PresetIds.Count == 0) {
                var (none, _no) = UIHelpers.Create($"MembersEmpty_{pack.Id}", parent);
                none.AddComponent<LayoutElement>().preferredHeight = 24;
                UIHelpers.AddLabel(none, "pack.members_empty".i18n(), 13f,
                    TextAlignmentOptions.MidlineLeft, Color.gray);
            }

            for (int i = 0; i < pack.PresetIds.Count; i++) {
                int idx = i;  // capture for the closures
                var preset = PresetRegistry.Get(pack.PresetIds[i]);
                var (memberRow, _mr) = UIHelpers.Create($"Member_{pack.Id}_{idx}", parent);
                memberRow.AddComponent<LayoutElement>().preferredHeight = 30;
                UIHelpers.AddBackground(memberRow, new Color(0.13f, 0.13f, 0.13f, 1f));

                var mhlg = memberRow.AddComponent<HorizontalLayoutGroup>();
                mhlg.spacing = 4;
                mhlg.childForceExpandWidth = false;
                mhlg.childForceExpandHeight = true;
                mhlg.childControlWidth = true;
                mhlg.childControlHeight = true;
                mhlg.padding = new RectOffset(16, 8, 2, 2);
                mhlg.childAlignment = TextAnchor.MiddleLeft;

                var (label, _l) = UIHelpers.Create("MemberLabel", memberRow.transform);
                var labelLE = label.AddComponent<LayoutElement>();
                labelLE.flexibleWidth = 1;
                labelLE.preferredWidth = 200;
                // A member whose preset was deleted outside the mod still renders, greyed —
                // silently dropping it would hide why the pack applies fewer rules than expected.
                UIHelpers.AddLabel(label, preset?.Name ?? pack.PresetIds[idx], 14f,
                    TextAlignmentOptions.MidlineLeft,
                    preset != null ? Color.white : new Color(0.7f, 0.4f, 0.4f));

                AddMemberButton(memberRow.transform, "MemberUp", "^", new Color(0.3f, 0.3f, 0.3f), () => {
                    if (idx == 0) return;
                    var tmp = pack.PresetIds[idx - 1];
                    pack.PresetIds[idx - 1] = pack.PresetIds[idx];
                    pack.PresetIds[idx] = tmp;
                    PersistPack(pack, onChanged, setStatus);
                });
                AddMemberButton(memberRow.transform, "MemberDown", "v", new Color(0.3f, 0.3f, 0.3f), () => {
                    if (idx >= pack.PresetIds.Count - 1) return;
                    var tmp = pack.PresetIds[idx + 1];
                    pack.PresetIds[idx + 1] = pack.PresetIds[idx];
                    pack.PresetIds[idx] = tmp;
                    PersistPack(pack, onChanged, setStatus);
                });
                AddMemberButton(memberRow.transform, "MemberRemove", "pack.member_remove".i18n(),
                    new Color(0.5f, 0.15f, 0.15f), () => {
                        pack.PresetIds.RemoveAt(idx);
                        PersistPack(pack, onChanged, setStatus);
                    });
            }

            var (availTitle, _at) = UIHelpers.Create($"Available_{pack.Id}", parent);
            availTitle.AddComponent<LayoutElement>().preferredHeight = 24;
            UIHelpers.AddLabel(availTitle, "pack.available_title".i18n(), 13f,
                TextAlignmentOptions.MidlineLeft, new Color(0.8f, 0.8f, 0.8f));

            foreach (var preset in PresetRegistry.All()) {
                // Duplicates inside one pack would insert the same rule twice — hide members.
                if (pack.PresetIds.Contains(preset.Id)) continue;
                var captured = preset;
                var (availRow, _ar) = UIHelpers.Create($"Avail_{pack.Id}_{preset.Id}", parent);
                availRow.AddComponent<LayoutElement>().preferredHeight = 28;

                var ahlg = availRow.AddComponent<HorizontalLayoutGroup>();
                ahlg.spacing = 4;
                ahlg.childForceExpandWidth = false;
                ahlg.childForceExpandHeight = true;
                ahlg.childControlWidth = true;
                ahlg.childControlHeight = true;
                ahlg.padding = new RectOffset(16, 8, 2, 2);
                ahlg.childAlignment = TextAnchor.MiddleLeft;

                var (label, _l) = UIHelpers.Create("AvailLabel", availRow.transform);
                var labelLE = label.AddComponent<LayoutElement>();
                labelLE.flexibleWidth = 1;
                labelLE.preferredWidth = 200;
                UIHelpers.AddLabel(label, captured.Name, 13f,
                    TextAlignmentOptions.MidlineLeft, new Color(0.75f, 0.75f, 0.75f));

                AddMemberButton(availRow.transform, "AvailAdd", "pack.member_add".i18n(),
                    new Color(0.2f, 0.45f, 0.2f), () => {
                        pack.PresetIds.Add(captured.Id);
                        PersistPack(pack, onChanged, setStatus);
                    });
            }
        }

        static void AddMemberButton(Transform parent, string name, string label, Color bg,
            UnityEngine.Events.UnityAction onClick) {
            var (obj, _) = UIHelpers.Create(name, parent);
            var le = obj.AddComponent<LayoutElement>();
            le.preferredWidth = 34;
            le.flexibleWidth = 0;
            UIHelpers.AddBackground(obj, bg);
            UIHelpers.AddLabel(obj, label, 15f, TextAlignmentOptions.Midline);
            obj.AddComponent<Button>().onClick.AddListener(onClick);
        }

        static void PersistPack(TacticsPack pack, Action onChanged, Action<string, Color> setStatus) {
            if (!PackRegistry.Save(pack))
                setStatus(string.Format("status.save_failed".i18n(), pack.Name), new Color(1f, 0.5f, 0.4f));
            onChanged();
        }
    }
}
