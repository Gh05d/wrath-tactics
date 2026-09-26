using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WrathTactics.Localization;
using WrathTactics.Models;

namespace WrathTactics.UI {
    public partial class RuleEditorWidget {
        void SetupTargetRow(Transform parent) {
            var row = Widgets.Row(parent, "TargetRow", Theme.RowHeight);

            // "TARGET" label
            var (lbl, _) = UIHelpers.Create("TargetLabel", row.transform);
            Widgets.InRow(lbl, 0f, 0.10f);
            Widgets.SectionLabel(lbl, "section.target".i18n());

            // Target type popup selector — rebuilds body so filter shows/hides
            var targetNames = EnumLabels.NamesFor<TargetType>();
            PopupSelector.CreateInRow(row, "TargetType", 0.39f, targetNames,
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

                var (filterLbl, _f) = UIHelpers.Create("FilterLabel", row.transform);
                Widgets.InRow(filterLbl, 0f, 0.14f);
                Widgets.InkLabel(filterLbl, filterLabel, 14f, TextAlignmentOptions.MidlineRight, Theme.InkMuted, italic: true);

                var filterInput = UIHelpers.CreateTMPInputFieldInRow(row, "TargetFilter",
                    80f, 0.34f, rule.Target.Filter ?? "", 15f);
                filterInput.onEndEdit.AddListener(v => {
                    rule.Target.Filter = v;
                    PersistEdit();
                });
            } else if (needsAllyPicker) {
                // SpecificAlly: pick a concrete companion. Filter stores the UniqueId (per-save).
                var (allyLbl, _a) = UIHelpers.Create("AllyLabel", row.transform);
                Widgets.InRow(allyLbl, 0f, 0.14f);
                Widgets.InkLabel(allyLbl, "target.filter.ally".i18n(), 14f, TextAlignmentOptions.MidlineRight, Theme.InkMuted, italic: true);

                var entries = Engine.AllyProvider.GetAll();
                if (entries.Count == 0) {
                    var input = UIHelpers.CreateTMPInputFieldInRow(row, "TargetFilter",
                        80f, 0.34f, rule.Target.Filter ?? "", 15f);
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
                    PopupSelector.CreateInRow(row, "TargetSpecificAlly", 0.34f, labels, idx, v => {
                        rule.Target.Filter = entries[v].UniqueId;
                        PersistEdit();
                    });
                }
            } else {
                var (spacer, _sp) = UIHelpers.Create("Spacer", row.transform);
                Widgets.InRow(spacer, 0f, 0.48f);
            }
        }
    }
}
