using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WrathTactics.Localization;
using WrathTactics.Models;

namespace WrathTactics.UI {
    public partial class RuleEditorWidget {
        void SetupTargetRow(Transform parent) {
            var (row, rowRect) = UIHelpers.Create("TargetRow", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 28;

            // "TARGET:" label
            var (lbl, lblRect) = UIHelpers.Create("TargetLabel", row.transform);
            lblRect.SetAnchor(0, 0.1, 0, 1);
            lblRect.sizeDelta = Vector2.zero;
            UIHelpers.AddLabel(lbl, "section.target".i18n(), 16f, TextAlignmentOptions.MidlineLeft,
                new Color(0.15f, 0.10f, 0.06f));

            // Target type popup selector — rebuilds body so filter shows/hides
            var targetNames = EnumLabels.NamesFor<TargetType>();
            PopupSelector.Create(row, "TargetType", 0.11f, 0.5f, targetNames,
                (int)rule.Target.Type, idx => {
                    rule.Target.Type = (TargetType)idx;
                    PersistEdit();
                    RebuildBody();
                });

            // Filter input — only show for target types that need it
            bool needsFilter = rule.Target.Type == TargetType.AllyWithCondition
                || rule.Target.Type == TargetType.AllyMissingBuff
                || rule.Target.Type == TargetType.EnemyCreatureType;
            bool needsAllyPicker = rule.Target.Type == TargetType.SpecificAlly;

            if (needsFilter) {
                string filterLabel = rule.Target.Type == TargetType.AllyWithCondition ? "target.filter.condition".i18n()
                    : rule.Target.Type == TargetType.AllyMissingBuff ? "target.filter.buff_guid".i18n()
                    : "target.filter.creature_type".i18n();

                var (filterLbl, filterLblRect) = UIHelpers.Create("FilterLabel", row.transform);
                filterLblRect.SetAnchor(0.51, 0.65, 0, 1);
                filterLblRect.sizeDelta = Vector2.zero;
                UIHelpers.AddLabel(filterLbl, filterLabel, 15f, TextAlignmentOptions.MidlineLeft,
                    new Color(0.7f, 0.7f, 0.7f));

                var filterInput = UIHelpers.CreateTMPInputField(row, "TargetFilter",
                    0.66, 1.0, rule.Target.Filter ?? "", 15f);
                filterInput.onEndEdit.AddListener(v => {
                    rule.Target.Filter = v;
                    PersistEdit();
                });
            } else if (needsAllyPicker) {
                // SpecificAlly: pick a concrete companion. Filter stores the UniqueId (per-save).
                var (allyLbl, allyLblRect) = UIHelpers.Create("AllyLabel", row.transform);
                allyLblRect.SetAnchor(0.51, 0.65, 0, 1);
                allyLblRect.sizeDelta = Vector2.zero;
                UIHelpers.AddLabel(allyLbl, "target.filter.ally".i18n(), 15f, TextAlignmentOptions.MidlineLeft,
                    new Color(0.7f, 0.7f, 0.7f));

                var entries = Engine.AllyProvider.GetAll();
                if (entries.Count == 0) {
                    var input = UIHelpers.CreateTMPInputField(row, "TargetFilter",
                        0.66, 1.0, rule.Target.Filter ?? "", 15f);
                    input.onEndEdit.AddListener(v => { rule.Target.Filter = v; PersistEdit(); });
                } else {
                    var labels = entries.Select(e => e.DisplayName).ToList();
                    int idx = -1;
                    for (int i = 0; i < entries.Count; i++) {
                        if (entries[i].UniqueId == rule.Target.Filter) { idx = i; break; }
                    }
                    if (idx < 0) {
                        idx = 0;
                        rule.Target.Filter = entries[0].UniqueId;
                        PersistEdit();
                    }
                    PopupSelector.Create(row, "TargetSpecificAlly", 0.66f, 1.0f, labels, idx, v => {
                        rule.Target.Filter = entries[v].UniqueId;
                        PersistEdit();
                    });
                }
            }
        }
    }
}
