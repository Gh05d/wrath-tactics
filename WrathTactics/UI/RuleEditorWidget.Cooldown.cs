using TMPro;
using UnityEngine;
using WrathTactics.Localization;

namespace WrathTactics.UI {
    public partial class RuleEditorWidget {
        void SetupCooldownRow(Transform parent) {
            var row = Widgets.Row(parent, "CooldownRow", Theme.RowHeight);

            var (lbl, _) = UIHelpers.Create("CdLabel", row.transform);
            Widgets.InRow(lbl, 0f, 0.25f);
            Widgets.SectionLabel(lbl, "cooldown.label".i18n());

            var cdInput = UIHelpers.CreateTMPInputFieldInRow(row, "CdInput", 90f, 0f,
                rule.CooldownRounds.ToString(), 16f, TMP_InputField.ContentType.IntegerNumber);
            cdInput.onEndEdit.AddListener(v => {
                if (int.TryParse(v, out int rounds))
                    rule.CooldownRounds = Mathf.Max(0, rounds);
                PersistEdit();
            });

            // Trailing flexible spacer keeps the input at its preferred width.
            var (spacer, _s) = UIHelpers.Create("Spacer", row.transform);
            Widgets.InRow(spacer, 0f, 0.55f);
        }
    }
}
