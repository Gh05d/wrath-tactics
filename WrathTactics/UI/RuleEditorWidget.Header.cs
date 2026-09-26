using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WrathTactics.Localization;
using WrathTactics.Models;

namespace WrathTactics.UI {
    public partial class RuleEditorWidget {
        void CreateHeader(Transform parent, TacticsRule linkedPreset) {
            bool isLinked = linkedPreset != null;
            // Pack origin wins over the generic linked tint so a character running several
            // packs can tell at a glance which rules belong together. A dangling PackId
            // (pack deleted) resolves to null and falls back to the normal band.
            var pack = Engine.PackRegistry.Get(rule.PackId);
            var style = isLinked ? BandStyle.Blue : BandStyle.Mauve;
            Color? tint = pack != null ? Theme.PackBandTint(pack.ColorIndex) : (Color?)null;
            var header = Widgets.BandRow(parent, "Header", style, Theme.HeaderHeight, tint);

            // [dot] [N. name (flex)] [origin] [copy] [export] [preset?] [^] [v] [delete]
            enabledDot = Widgets.ToggleDot(header.transform, "EnableBtn", rule.Enabled, () => {
                rule.Enabled = !rule.Enabled;
                Widgets.SetToggleDot(enabledDot, rule.Enabled);
                PersistEdit();
            });

            string displayName = isLinked
                ? string.Format("linked.name_format".i18n(), linkedPreset.Name)
                : $"{index + 1}. {rule.Name}";
            var nameInput = UIHelpers.CreateTMPInputFieldInRow(header, "NameInput", 200f, 1f, displayName, 18f);
            nameInput.interactable = !isLinked;  // linked: name comes from preset, not editable here
            if (!isLinked) {
                nameInput.onEndEdit.AddListener(v => {
                    string prefix = $"{index + 1}. ";
                    rule.Name = v.StartsWith(prefix) ? v.Substring(prefix.Length) : v;
                    PersistEdit();
                });
            }

            // No new locale keys: pack origin reuses the "Packs:" row label, linked origin the badge text.
            string origin = pack != null ? $"{"pack.row_label".i18n().TrimEnd(':', ' ')}: {pack.Name}"
                : isLinked ? string.Format("linked.badge".i18n(), linkedPreset.Name)
                : null;
            if (origin != null) {
                var (o, _) = UIHelpers.Create("Origin", header.transform);
                Widgets.InRow(o, 160f, 0f);
                Widgets.BandLabel(o, origin, 13f, TextAlignmentOptions.MidlineRight, italic: true);
            }

            Widgets.InlineLink(header.transform, "Copy", "button.copy".i18n(), () => CloneRule(), onBand: true, fontSize: 13f);
            // Export (clipboard) — wraps the resolved rule in a 1-element JSON array
            Widgets.InlineLink(header.transform, "Export", "button.export".i18n(), () => ExportRuleToClipboard(), onBand: true, fontSize: 13f);
            // Promote to preset — only for unlinked character rules
            bool canPromote = !isLinked && !string.IsNullOrEmpty(unitId);
            if (canPromote)
                Widgets.InlineLink(header.transform, "Promote", "button.promote_to_preset".i18n(), () => PromoteToPreset(), onBand: true, fontSize: 13f);

            Widgets.IconButton(header.transform, "Up", Icon.ArrowUp, Theme.IconSmall, () => MoveRule(-1), Theme.Ink);
            Widgets.IconButton(header.transform, "Down", Icon.ArrowDown, Theme.IconSmall, () => MoveRule(1), Theme.Ink);
            Widgets.IconButton(header.transform, "Del", Icon.Delete, Theme.IconMedium, () => DeleteRule());
        }

        void RenderLinkedSummary(Transform parent, TacticsRule preset) {
            int condCount = 0;
            if (preset.ConditionGroups != null) {
                foreach (var g in preset.ConditionGroups)
                    if (g?.Conditions != null) condCount += g.Conditions.Count;
            }
            string abilityInfo = string.IsNullOrEmpty(preset.Action.AbilityId)
                ? ""
                : $" ({preset.Action.AbilityId.Substring(0, System.Math.Min(8, preset.Action.AbilityId.Length))}…)";
            string condCountText = string.Format("linked.summary.condition_count".i18n(), condCount);
            string summary = string.Format("linked.summary".i18n(),
                condCountText, preset.Action.Type, abilityInfo, preset.Target.Type);

            // One ink line: summary on the left, "Unlink & edit" link on the right.
            var row = Widgets.Row(parent, "LinkedSummary", Theme.CollapsedBodyHeight);
            row.GetComponent<HorizontalLayoutGroup>().padding =
                new RectOffset((int)Theme.BandPaddingX, (int)Theme.BandPaddingX, 2, 2);
            var (sumObj, _s) = UIHelpers.Create("Summary", row.transform);
            Widgets.InRow(sumObj, 200f, 1f);
            Widgets.InkLabel(sumObj, summary, 13f, TextAlignmentOptions.MidlineLeft, Theme.InkLabel);
            Widgets.InlineLink(row.transform, "UnlinkBtn", "button.unlink_edit".i18n(), () => {
                Engine.PresetRegistry.BreakLink(rule);
                PersistEdit();
                RebuildBody();
            });
        }
    }
}
