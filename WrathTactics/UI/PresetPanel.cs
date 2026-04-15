using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WrathTactics.UI {
    public class PresetPanel : MonoBehaviour {
        public void Init(string _unusedCharacterId, Transform _unusedParent, Action _unusedCallback) {
            var (lbl, _) = UIHelpers.Create("Stub", gameObject.transform);
            lbl.AddComponent<LayoutElement>().preferredHeight = 40;
            UIHelpers.AddLabel(lbl, "Preset panel rebuilding…", 16f,
                TextAlignmentOptions.MidlineLeft, Color.yellow);
        }
    }
}
