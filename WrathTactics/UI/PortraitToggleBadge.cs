using Kingmaker.EntitySystem.Entities;
using Kingmaker.UI.MVVM._VM.Party;
using Owlcat.Runtime.UI.MVVM;
using TMPro;
using UnityEngine;
using WrathTactics.Persistence;

namespace WrathTactics.UI {
    // One badge per portrait cell. The cell is pooled and gets re-bound to a
    // different unit on paging/party changes, so the unit is re-read from the
    // cell's ViewModel on EVERY refresh and click — never cached.
    public class PortraitToggleBadge : MonoBehaviour {
        ViewBase<PartyCharacterVM> cell;
        TextMeshProUGUI label;

        static readonly Color OnColor = new Color(0.35f, 0.9f, 0.35f);

        public void Init(ViewBase<PartyCharacterVM> boundCell, TextMeshProUGUI stateLabel) {
            cell = boundCell;
            label = stateLabel;
        }

        UnitEntityData CurrentUnit() {
            if (cell == null) return null;           // Unity destroyed-equality
            return cell.ViewModel?.UnitEntityData;   // null on unbound pooled cells
        }

        public void Refresh(bool show) {
            var unit = show ? CurrentUnit() : null;
            if (unit == null) {
                if (gameObject.activeSelf) gameObject.SetActive(false);
                return;
            }
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            bool enabled = ConfigManager.Current.IsEnabled(unit.UniqueId);
            label.color = enabled ? OnColor : Color.gray;
            label.fontStyle = enabled ? FontStyles.Normal : FontStyles.Strikethrough;
        }

        public void OnClick() {
            var unit = CurrentUnit();
            if (unit == null) return;
            var config = ConfigManager.Current;
            config.TacticsEnabled[unit.UniqueId] = !config.IsEnabled(unit.UniqueId);
            ConfigManager.Save();
            TacticsPanel.NotifyExternalConfigChange();
            Refresh(config.ShowPortraitToggles);
        }
    }
}
