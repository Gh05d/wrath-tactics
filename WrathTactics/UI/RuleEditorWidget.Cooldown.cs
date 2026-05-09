using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WrathTactics.Localization;

namespace WrathTactics.UI {
    public partial class RuleEditorWidget {
        void SetupCooldownRow(Transform parent) {
            var (row, rowRect) = UIHelpers.Create("CooldownRow", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 28;

            var (lbl, lblRect) = UIHelpers.Create("CdLabel", row.transform);
            lblRect.SetAnchor(0, 0.25, 0, 1);
            lblRect.sizeDelta = Vector2.zero;
            UIHelpers.AddLabel(lbl, "cooldown.label".i18n(), 15f, TextAlignmentOptions.MidlineLeft,
                new Color(0.15f, 0.10f, 0.06f));

            var cdInput = UIHelpers.CreateTMPInputField(row, "CdInput",
                0.26, 0.45, rule.CooldownRounds.ToString(), 16f,
                TMP_InputField.ContentType.IntegerNumber);
            // Adjust vertical anchors for padding
            var cdRect = cdInput.GetComponent<RectTransform>();
            cdRect.SetAnchor(0.26, 0.45, 0.1, 0.9);
            cdInput.onEndEdit.AddListener(v => {
                if (int.TryParse(v, out int rounds))
                    rule.CooldownRounds = Mathf.Max(0, rounds);
                PersistEdit();
            });
        }
    }
}
