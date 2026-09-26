using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WrathTactics.Engine;
using WrathTactics.Localization;
using WrathTactics.Models;

namespace WrathTactics.UI {
    /// <summary>
    /// The Packs section rendered on its own Packs tab. Stateless apart from which pack is
    /// expanded — the host (PackPanelHost) owns the rebuild, so every mutation ends in
    /// onChanged() rather than touching the hierarchy directly.
    /// </summary>
    public static class PackPanel {
        // Which pack's member editor is open. Static so it survives the host's Rebuild,
        // matching PresetPanel.expandedIds semantics.
        static string expandedPackId;

        public static void Build(Transform parent, Action onChanged, Action<string, Color> setStatus) {
            Widgets.SectionLabelRow(parent, "PackTitle", "pack.section_title".i18n());

            // No hint card here: the only caller is PackPanelHost.BuildUI, which already drew
            // pack.tab_hint immediately above — the pre-tab-move pack.hint stacked a second,
            // near-identical paragraph under it. Key dropped from the locales with it.

            var actions = Widgets.Row(parent, "PackActions", Theme.ActionRowHeight);
            Widgets.ActionButton(actions.transform, "NewPackBtn", "pack.button.new".i18n(), 15f, () => {
                var pack = new TacticsPack { Name = "pack.default_name".i18n() };
                if (!PackRegistry.Save(pack)) {
                    setStatus(string.Format("status.save_failed".i18n(), "pack.section_title".i18n()),
                        Theme.StatusError);
                    return;
                }
                expandedPackId = pack.Id;
                onChanged();
            }, 220f);

            var packs = PackRegistry.All();
            if (packs.Count == 0) {
                Widgets.StatusLabel(parent, "PackEmpty", "pack.empty".i18n(), Theme.InkMuted, Theme.StatusHeight, 14f);
            }

            foreach (var pack in packs) CreatePackRow(parent, pack, onChanged, setStatus);

            Widgets.FlourishDivider(parent);
        }

        static void CreatePackRow(Transform parent, TacticsPack pack, Action onChanged,
            Action<string, Color> setStatus) {

            var row = Widgets.BandRow(parent, $"Pack_{pack.Id}", BandStyle.Mauve, Theme.HeaderHeight,
                Theme.PackBandTint(pack.ColorIndex));

            // Colour swatch — click cycles to the next palette entry.
            var (swatch, _sw) = UIHelpers.Create("Swatch", row.transform);
            float d = Theme.IconMedium;
            Widgets.InRow(swatch, d, 0f).preferredHeight = d;
            var swImg = swatch.AddComponent<Image>();
            swImg.color = PackPalette.ColorAt(pack.ColorIndex);
            swImg.preserveAspect = true;
            if (ThemeProvider.ToggleOn != null) swImg.sprite = ThemeProvider.ToggleOn;
            var swBtn = swatch.AddComponent<Button>();
            swBtn.targetGraphic = swImg;
            Widgets.ApplyColorTint(swBtn);
            swBtn.onClick.AddListener(() => {
                pack.ColorIndex = PackPalette.Next(pack.ColorIndex);
                if (!PackRegistry.Save(pack))
                    setStatus(string.Format("status.save_failed".i18n(), pack.Name), Theme.StatusError);
                onChanged();
            });

            // Name — inline rename on end-edit.
            var nameInput = UIHelpers.CreateTMPInputFieldInRow(row, "PackName", 160f, 1f, pack.Name, 16f);
            nameInput.onEndEdit.AddListener(v => {
                var trimmed = v?.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed == pack.Name) return;
                pack.Name = trimmed;
                if (!PackRegistry.Save(pack)) {
                    setStatus(string.Format("status.save_failed".i18n(), "status.context.rename".i18n()),
                        Theme.StatusError);
                    return;
                }
                // Deferred by the host: rebuilding here would destroy this TMP_InputField
                // while its own onEndEdit is still on the stack.
                onChanged();
            });

            var (countObj, _c) = UIHelpers.Create("PackCount", row.transform);
            Widgets.InRow(countObj, 90f, 0f);
            Widgets.BandLabel(countObj, string.Format("pack.member_count".i18n(), pack.PresetIds.Count), 13f,
                TextAlignmentOptions.Midline, italic: true);

            bool expanded = expandedPackId == pack.Id;
            Widgets.InlineLink(row.transform, "MembersBtn",
                (expanded ? "pack.button.close_members" : "pack.button.members").i18n(), () => {
                    expandedPackId = expanded ? null : pack.Id;
                    onChanged();
                }, onBand: true);

            Widgets.InlineLink(row.transform, "PackExportBtn", "pack.button.export".i18n(),
                () => ExportPackToClipboard(pack, setStatus), onBand: true);

            Widgets.IconButton(row.transform, "PackDelBtn", Icon.Delete, Theme.IconMedium, () => {
                if (!PackRegistry.Delete(pack.Id))
                    setStatus(string.Format("status.save_failed".i18n(), "status.context.delete".i18n()),
                        Theme.StatusError);
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
                    Theme.StatusError);
                return;
            }

            var bundle = new PackBundle { Pack = pack, Presets = presets };
            GUIUtility.systemCopyBuffer =
                Newtonsoft.Json.JsonConvert.SerializeObject(bundle, Newtonsoft.Json.Formatting.Indented);
            setStatus(string.Format("status.pack_export_copied".i18n(), pack.Name, presets.Count),
                Theme.StatusOk);
        }

        /// <summary>Clipboard wire format for a shared pack. Presets are inlined copies.</summary>
        public class PackBundle {
            public TacticsPack Pack;
            public List<TacticsRule> Presets;
        }

        static void CreateMemberEditor(Transform parent, TacticsPack pack, Action onChanged,
            Action<string, Color> setStatus) {

            var available = new List<TacticsRule>();
            foreach (var preset in PresetRegistry.All()) {
                // Duplicates inside one pack would insert the same rule twice — hide members.
                if (!pack.PresetIds.Contains(preset.Id)) available.Add(preset);
            }

            // The whole editor sits on ONE inset surface so it reads as an extension of the pack
            // row above it, instead of loose strips floating on the parchment (play-test finding).
            // Height is computed rather than fitted: a ContentSizeFitter inside the host's
            // VerticalLayoutGroup fights it for control of the rect.
            float headerH = Theme.SectionLabelHeight, memberH = Theme.InlineRowHeight + 4f,
                  availH = Theme.InlineRowHeight, gap = 2f;
            float boxHeight = 12f
                + headerH + gap
                + (pack.PresetIds.Count > 0 ? pack.PresetIds.Count * (memberH + gap) : headerH + gap)
                + headerH + gap
                + available.Count * (availH + gap);

            var (box, _bx) = UIHelpers.Create($"MemberBox_{pack.Id}", parent);
            box.AddComponent<LayoutElement>().preferredHeight = boxHeight;
            Widgets.AddInset(box);
            var bvlg = box.AddComponent<VerticalLayoutGroup>();
            bvlg.spacing = gap;
            bvlg.childForceExpandWidth = true;
            bvlg.childForceExpandHeight = false;
            bvlg.childControlWidth = true;
            bvlg.childControlHeight = true;
            bvlg.padding = new RectOffset(6, 6, 6, 6);

            Widgets.SectionLabelRow(box.transform, $"Members_{pack.Id}", "pack.members_title".i18n());

            if (pack.PresetIds.Count == 0) {
                Widgets.StatusLabel(box.transform, $"MembersEmpty_{pack.Id}",
                    "pack.members_empty".i18n(), Theme.InkMuted, headerH);
            }

            for (int i = 0; i < pack.PresetIds.Count; i++) {
                int idx = i;  // capture for the closures
                var preset = PresetRegistry.Get(pack.PresetIds[i]);
                var memberRow = Widgets.Row(box.transform, $"Member_{pack.Id}_{idx}", memberH);
                memberRow.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(16, 8, 2, 2);

                var (label, _l) = UIHelpers.Create("MemberLabel", memberRow.transform);
                Widgets.InRow(label, 200f, 1f);
                // A member whose preset was deleted outside the mod still renders, in the error
                // colour — silently dropping it would hide why the pack applies fewer rules.
                Widgets.InkLabel(label, preset?.Name ?? pack.PresetIds[idx], 14f,
                    TextAlignmentOptions.MidlineLeft, preset != null ? Theme.Ink : Theme.StatusError);

                Widgets.IconButton(memberRow.transform, "MemberUp", Icon.ArrowUp, Theme.IconSmall, () => {
                    if (idx == 0) return;
                    var tmp = pack.PresetIds[idx - 1];
                    pack.PresetIds[idx - 1] = pack.PresetIds[idx];
                    pack.PresetIds[idx] = tmp;
                    PersistPack(pack, onChanged, setStatus);
                });
                Widgets.IconButton(memberRow.transform, "MemberDown", Icon.ArrowDown, Theme.IconSmall, () => {
                    if (idx >= pack.PresetIds.Count - 1) return;
                    var tmp = pack.PresetIds[idx + 1];
                    pack.PresetIds[idx + 1] = pack.PresetIds[idx];
                    pack.PresetIds[idx] = tmp;
                    PersistPack(pack, onChanged, setStatus);
                });
                Widgets.IconButton(memberRow.transform, "MemberRemove", Icon.X, Theme.IconSmall, () => {
                        pack.PresetIds.RemoveAt(idx);
                        PersistPack(pack, onChanged, setStatus);
                    });
            }

            Widgets.SectionLabelRow(box.transform, $"Available_{pack.Id}", "pack.available_title".i18n());

            foreach (var preset in available) {
                var captured = preset;
                var availRow = Widgets.Row(box.transform, $"Avail_{pack.Id}_{preset.Id}", availH);
                availRow.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(16, 8, 2, 2);
                // The whole row adds the preset (a 12 px "+" was the only hit target before, play-test).
                var rowImg = UIHelpers.AddBackground(availRow, Theme.ListRowFill);
                var rowBtn = availRow.AddComponent<Button>();
                rowBtn.targetGraphic = rowImg;
                Widgets.ApplyColorTint(rowBtn);
                rowBtn.onClick.AddListener(() => {
                    pack.PresetIds.Add(captured.Id);
                    PersistPack(pack, onChanged, setStatus);
                });

                var (label, _l) = UIHelpers.Create("AvailLabel", availRow.transform);
                Widgets.InRow(label, 200f, 1f);
                Widgets.InkLabel(label, captured.Name, 13f);

                Widgets.IconImage(availRow.transform, "AvailAdd", Icon.Add, Theme.IconSmall);
            }
        }

        static void PersistPack(TacticsPack pack, Action onChanged, Action<string, Color> setStatus) {
            // Packs persist on every edit — say so, or the missing "Save" button reads as data loss.
            if (PackRegistry.Save(pack))
                setStatus(string.Format("status.pack_saved".i18n(), pack.Name), Theme.StatusOk);
            else
                setStatus(string.Format("status.save_failed".i18n(), pack.Name), Theme.StatusError);
            onChanged();
        }
    }

    /// <summary>
    /// Owns the Packs tab: root layout, status line, and the deferred rebuild that keeps
    /// a rename's onEndEdit callback off the stack while its input field is destroyed.
    /// </summary>
    public class PackPanelHost : MonoBehaviour {
        static string lastStatus;
        static Color lastStatusColor = Theme.StatusMuted;

        /// <summary>
        /// Clears the static status line. Being static, it survives PackPanelHost's own
        /// deferred rebuild by design (see DeferredRebuild) — but that means it also survives
        /// a tab switch and even a savegame load unless cleared here, on tab ENTRY specifically
        /// (mirrors PresetPanel.ClearIOStatus, called from TacticsPanel.SelectTab).
        /// </summary>
        public static void ClearStatus() {
            lastStatus = null;
            lastStatusColor = Theme.StatusMuted;
        }

        public void Init() {
            BuildUI();
        }

        void BuildUI() {
            var vlg = gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 6;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.padding = new RectOffset(6, 6, 6, 6);
            var csf = gameObject.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            Widgets.HintCard(transform, "pack.tab_hint".i18n(), Theme.HintHeightShort);

            // Rendered unconditionally, even when empty: some setStatus callers (the New Pack
            // save-failure path) return without triggering a rebuild, so the strip has to
            // already exist for the message to land somewhere visible.
            Widgets.StatusLabel(transform, "PackTabStatus", lastStatus ?? "", lastStatusColor, Theme.StatusHeight);

            PackPanel.Build(transform, () => StartCoroutine(DeferredRebuild()),
                (text, color) => { lastStatus = text; lastStatusColor = color; });
        }

        IEnumerator DeferredRebuild() {
            yield return null;
            for (int i = transform.childCount - 1; i >= 0; i--)
                Destroy(transform.GetChild(i).gameObject);
            var vlg = GetComponent<VerticalLayoutGroup>();
            if (vlg != null) DestroyImmediate(vlg);
            var csf = GetComponent<ContentSizeFitter>();
            if (csf != null) DestroyImmediate(csf);
            BuildUI();
        }
    }
}
