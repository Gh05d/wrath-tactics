using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WrathTactics.Localization;
using WrathTactics.Models;

namespace WrathTactics.UI {
    public partial class RuleEditorWidget {
        void CreateHeader(Transform parent, TacticsRule linkedPreset) {
            bool isLinked = linkedPreset != null;

            var (header, _) = UIHelpers.Create("Header", parent);
            header.AddComponent<LayoutElement>().preferredHeight = 44;
            // Pack origin wins over the generic linked tint so a character running several
            // packs can tell at a glance which rules belong together. A dangling PackId
            // (pack deleted) resolves to null and falls back to the normal colours.
            var pack = Engine.PackRegistry.Get(rule.PackId);
            var headerBg = pack != null
                ? PackPalette.HeaderTint(pack.ColorIndex)
                : isLinked
                    ? new Color(0.22f, 0.3f, 0.4f, 1f)   // blue-grey for linked
                    : new Color(0.25f, 0.22f, 0.18f, 1f); // default brown
            UIHelpers.AddBackground(header, headerBg);

            // HLG — childControlWidth=true so LayoutElement.preferredWidth/flexibleWidth work
            var hlg = header.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4;
            hlg.childForceExpandHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childControlHeight = true;
            hlg.childControlWidth = true;
            hlg.padding = new RectOffset(4, 4, 4, 4);
            hlg.childAlignment = TextAnchor.MiddleLeft;

            // Order: [ON] [^] [v] [X] [Name input (flexible)]

            // ON/OFF button
            var (enableBtnObj, _1) = UIHelpers.Create("EnableBtn", header.transform);
            var enableLE = enableBtnObj.AddComponent<LayoutElement>();
            enableLE.preferredWidth = 50;
            enableLE.flexibleWidth = 0;
            UIHelpers.AddBackground(enableBtnObj, new Color(0.25f, 0.25f, 0.25f, 1f));
            enabledLabel = UIHelpers.AddLabel(enableBtnObj, (rule.Enabled ? "button.on" : "button.off").i18n(), 16f,
                TextAlignmentOptions.Midline, rule.Enabled ? Color.green : Color.gray);
            enableBtnObj.AddComponent<Button>().onClick.AddListener(() => {
                rule.Enabled = !rule.Enabled;
                enabledLabel.text = (rule.Enabled ? "button.on" : "button.off").i18n();
                enabledLabel.color = rule.Enabled ? Color.green : Color.gray;
                PersistEdit();
            });

            // Move up
            var (upObj, _2) = UIHelpers.Create("Up", header.transform);
            var upLE = upObj.AddComponent<LayoutElement>();
            upLE.preferredWidth = 36;
            upLE.flexibleWidth = 0;
            UIHelpers.AddBackground(upObj, new Color(0.3f, 0.3f, 0.3f, 1f));
            UIHelpers.AddLabel(upObj, "^", 18f, TextAlignmentOptions.Midline);
            upObj.AddComponent<Button>().onClick.AddListener(() => MoveRule(-1));

            // Move down
            var (downObj, _3) = UIHelpers.Create("Down", header.transform);
            var downLE = downObj.AddComponent<LayoutElement>();
            downLE.preferredWidth = 36;
            downLE.flexibleWidth = 0;
            UIHelpers.AddBackground(downObj, new Color(0.3f, 0.3f, 0.3f, 1f));
            UIHelpers.AddLabel(downObj, "v", 18f, TextAlignmentOptions.Midline);
            downObj.AddComponent<Button>().onClick.AddListener(() => MoveRule(1));

            // Copy
            var (copyObj, _4c) = UIHelpers.Create("Copy", header.transform);
            var copyLE = copyObj.AddComponent<LayoutElement>();
            copyLE.preferredWidth = 48;
            copyLE.flexibleWidth = 0;
            UIHelpers.AddBackground(copyObj, new Color(0.2f, 0.35f, 0.5f, 1f));
            UIHelpers.AddLabel(copyObj, "button.copy".i18n(), 14f, TextAlignmentOptions.Midline);
            copyObj.AddComponent<Button>().onClick.AddListener(() => CloneRule());

            // Export (clipboard) — wraps the resolved rule in a 1-element JSON array
            var (exportObj, _4e) = UIHelpers.Create("Export", header.transform);
            var exportLE = exportObj.AddComponent<LayoutElement>();
            exportLE.preferredWidth = 56;
            exportLE.flexibleWidth = 0;
            UIHelpers.AddBackground(exportObj, new Color(0.3f, 0.3f, 0.5f, 1f));
            UIHelpers.AddLabel(exportObj, "button.export".i18n(), 13f, TextAlignmentOptions.Midline);
            exportObj.AddComponent<Button>().onClick.AddListener(() => ExportRuleToClipboard());

            // Promote to preset — only for unlinked character rules
            bool canPromote = !isLinked && !string.IsNullOrEmpty(unitId);
            if (canPromote) {
                var (promoteObj, _4p) = UIHelpers.Create("Promote", header.transform);
                var promoteLE = promoteObj.AddComponent<LayoutElement>();
                promoteLE.preferredWidth = 64;
                promoteLE.flexibleWidth = 0;
                UIHelpers.AddBackground(promoteObj, new Color(0.25f, 0.45f, 0.3f, 1f));
                UIHelpers.AddLabel(promoteObj, "button.promote_to_preset".i18n(), 13f, TextAlignmentOptions.Midline);
                promoteObj.AddComponent<Button>().onClick.AddListener(() => PromoteToPreset());
            }

            // Delete
            var (delObj, _4) = UIHelpers.Create("Del", header.transform);
            var delLE = delObj.AddComponent<LayoutElement>();
            delLE.preferredWidth = 36;
            delLE.flexibleWidth = 0;
            UIHelpers.AddBackground(delObj, new Color(0.6f, 0.2f, 0.2f, 1f));
            UIHelpers.AddLabel(delObj, "X", 18f, TextAlignmentOptions.Midline);
            delObj.AddComponent<Button>().onClick.AddListener(() => DeleteRule());

            // Name input — fills remaining space on the right
            string displayName = isLinked
                ? string.Format("linked.name_format".i18n(), linkedPreset.Name)
                : $"{index + 1}. {rule.Name}";

            var nameInput = UIHelpers.CreateTMPInputField(header, "NameInput",
                0, 1, displayName, 18f);
            var nameLE = nameInput.gameObject.GetComponent<LayoutElement>();
            if (nameLE == null) nameLE = nameInput.gameObject.AddComponent<LayoutElement>();
            nameLE.flexibleWidth = 1;
            nameLE.preferredWidth = 200;

            nameInput.interactable = !isLinked;  // linked: name comes from preset, not editable here
            if (!isLinked) {
                nameInput.onEndEdit.AddListener(v => {
                    string prefix = $"{index + 1}. ";
                    rule.Name = v.StartsWith(prefix) ? v.Substring(prefix.Length) : v;
                    PersistEdit();
                });
            }
        }

        void RenderLinkedSummary(Transform parent, TacticsRule preset) {
            // Badge
            var (badge, _) = UIHelpers.Create("LinkedBadge", parent);
            badge.AddComponent<LayoutElement>().preferredHeight = 26;
            UIHelpers.AddBackground(badge, new Color(0.22f, 0.3f, 0.4f, 1f));
            UIHelpers.AddLabel(badge, string.Format("linked.badge".i18n(), preset.Name), 14f,
                TextAlignmentOptions.MidlineLeft, new Color(0.85f, 0.9f, 1f));

            // Summary
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

            var (sumObj, _s) = UIHelpers.Create("Summary", parent);
            sumObj.AddComponent<LayoutElement>().preferredHeight = 22;
            UIHelpers.AddLabel(sumObj, summary, 13f, TextAlignmentOptions.MidlineLeft, Color.gray);

            // Unlink & Edit button
            var (unlinkBtn, _u) = UIHelpers.Create("UnlinkBtn", parent);
            unlinkBtn.AddComponent<LayoutElement>().preferredHeight = 28;
            UIHelpers.AddBackground(unlinkBtn, new Color(0.45f, 0.35f, 0.15f));
            UIHelpers.AddLabel(unlinkBtn, "button.unlink_edit".i18n(), 14f, TextAlignmentOptions.Midline);
            unlinkBtn.AddComponent<Button>().onClick.AddListener(() => {
                Engine.PresetRegistry.BreakLink(rule);
                PersistEdit();
                RebuildBody();
            });
        }
    }
}
