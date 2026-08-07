using System;
using System.Collections.Generic;
using Kingmaker;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WrathTactics.Localization;
using WrathTactics.Models;

namespace WrathTactics.UI {
    /// <summary>
    /// Modal for "Save List as Pack": a name field plus one checkbox per rule, all
    /// pre-checked. Replaces the previous one-click flow, which promoted the whole list
    /// under an auto-generated name with no preview of what would be saved.
    /// </summary>
    public class SaveAsPackOverlay : MonoBehaviour {
        readonly List<TacticsRule> selected = new List<TacticsRule>();
        TMP_InputField nameInput;
        TextMeshProUGUI errorLabel;
        Action<string, List<TacticsRule>> onConfirm;
        bool closed;

        public static GameObject Open(string suggestedName, List<TacticsRule> rules,
            Func<TacticsRule, string> describe, Action<string, List<TacticsRule>> onConfirm) {

            var canvas = Game.Instance.UI.Canvas.transform;
            var (overlay, overlayRect) = UIHelpers.Create("SaveAsPackOverlay", canvas);
            overlayRect.FillParent();
            UIHelpers.AddBackground(overlay, new Color(0, 0, 0, 0.4f));
            overlay.AddComponent<Button>().onClick.AddListener(() => Destroy(overlay));

            var (popup, popupRect) = UIHelpers.Create("Popup", overlay.transform);
            UIHelpers.AddBackground(popup, new Color(0.12f, 0.12f, 0.12f, 0.99f));
            popupRect.anchorMin = new Vector2(0.5f, 0.5f);
            popupRect.anchorMax = new Vector2(0.5f, 0.5f);
            popupRect.pivot = new Vector2(0.5f, 0.5f);
            popupRect.anchoredPosition = Vector2.zero;
            popupRect.sizeDelta = new Vector2(460f, 520f);

            // Swallow clicks so the popup itself does not close the overlay.
            var swallow = popup.AddComponent<Button>();
            swallow.targetGraphic = popup.GetComponent<Image>();

            var controller = popup.AddComponent<SaveAsPackOverlay>();
            controller.onConfirm = (name, chosen) => {
                if (controller.closed) return;
                controller.closed = true;
                onConfirm?.Invoke(name, chosen);
                Destroy(overlay);
            };
            controller.selected.AddRange(rules);   // everything pre-checked
            controller.BuildUI(popup, suggestedName, rules, describe);
            return overlay;
        }

        void BuildUI(GameObject popup, string suggestedName, List<TacticsRule> rules,
            Func<TacticsRule, string> describe) {

            var vlg = popup.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 6;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.padding = new RectOffset(12, 12, 12, 12);

            var (titleObj, _t) = UIHelpers.Create("Title", popup.transform);
            titleObj.AddComponent<LayoutElement>().preferredHeight = 30;
            UIHelpers.AddLabel(titleObj, "pack.save_dialog.title".i18n(), 18f,
                TextAlignmentOptions.MidlineLeft, Color.white);

            var (nameLabel, _nl) = UIHelpers.Create("NameLabel", popup.transform);
            nameLabel.AddComponent<LayoutElement>().preferredHeight = 22;
            UIHelpers.AddLabel(nameLabel, "pack.save_dialog.name_label".i18n(), 14f,
                TextAlignmentOptions.MidlineLeft, new Color(0.8f, 0.8f, 0.8f));

            var (nameHolder, _nh) = UIHelpers.Create("NameHolder", popup.transform);
            nameHolder.AddComponent<LayoutElement>().preferredHeight = 32;
            nameInput = UIHelpers.CreateTMPInputField(nameHolder, "NameInput", 0, 1,
                suggestedName, 16f);

            var (rulesLabel, _rl) = UIHelpers.Create("RulesLabel", popup.transform);
            rulesLabel.AddComponent<LayoutElement>().preferredHeight = 22;
            UIHelpers.AddLabel(rulesLabel, "pack.save_dialog.rules_label".i18n(), 14f,
                TextAlignmentOptions.MidlineLeft, new Color(0.8f, 0.8f, 0.8f));

            // A character can have more rules than fit in the fixed-height popup, so the
            // checklist itself scrolls while the name field and buttons stay pinned. Same
            // viewport/content/ScrollRect idiom as TacticsPanel.CreateRuleList and
            // BuffPickerOverlay.BuildUI. RuleScroll is sized via LayoutElement — it is a
            // direct child of popup's VerticalLayoutGroup (childControl* on), so anchors
            // would be ignored/fought by the layout group. Its own children (Viewport,
            // Content) are NOT under a layout group and use anchors as usual.
            var (ruleScroll, _rs) = UIHelpers.Create("RuleScroll", popup.transform);
            var ruleScrollLE = ruleScroll.AddComponent<LayoutElement>();
            ruleScrollLE.flexibleHeight = 1;
            ruleScrollLE.minHeight = 80;

            var (viewport, viewportRect) = UIHelpers.Create("Viewport", ruleScroll.transform);
            viewportRect.FillParent();
            viewport.AddComponent<RectMask2D>();

            var (rowsContent, rowsContentRect) = UIHelpers.Create("Content", viewport.transform);
            rowsContentRect.SetAnchor(0, 1, 1, 1);
            rowsContentRect.pivot = new Vector2(0.5f, 1f);
            rowsContentRect.sizeDelta = new Vector2(0, 0);

            var rowsVlg = rowsContent.AddComponent<VerticalLayoutGroup>();
            rowsVlg.spacing = 4;
            rowsVlg.childForceExpandWidth = true;
            rowsVlg.childForceExpandHeight = false;
            rowsVlg.childControlWidth = true;
            rowsVlg.childControlHeight = true;

            var rowsCsf = rowsContent.AddComponent<ContentSizeFitter>();
            rowsCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = ruleScroll.AddComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = rowsContentRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 30f;

            foreach (var rule in rules) {
                var captured = rule;
                var (row, _r) = UIHelpers.Create($"Rule_{rule.Id}", rowsContent.transform);
                row.AddComponent<LayoutElement>().preferredHeight = 28;
                UIHelpers.AddBackground(row, new Color(0.16f, 0.16f, 0.16f, 1f));

                var hlg = row.AddComponent<HorizontalLayoutGroup>();
                hlg.spacing = 6;
                hlg.childForceExpandWidth = false;
                hlg.childForceExpandHeight = true;
                hlg.childControlWidth = true;
                hlg.childControlHeight = true;
                hlg.padding = new RectOffset(6, 6, 2, 2);
                hlg.childAlignment = TextAnchor.MiddleLeft;

                var (box, _b) = UIHelpers.Create("Check", row.transform);
                var boxLE = box.AddComponent<LayoutElement>();
                boxLE.preferredWidth = 28;
                boxLE.minWidth = 28;
                boxLE.flexibleWidth = 0;
                UIHelpers.AddBackground(box, new Color(0.25f, 0.4f, 0.25f, 1f));
                var boxLabel = UIHelpers.AddLabel(box, "x", 15f, TextAlignmentOptions.Midline);
                box.AddComponent<Button>().onClick.AddListener(() => {
                    if (selected.Contains(captured)) {
                        selected.Remove(captured);
                        boxLabel.text = "";
                    } else {
                        selected.Add(captured);
                        boxLabel.text = "x";
                    }
                    if (errorLabel != null) errorLabel.text = "";
                });

                var (nameObj, _n) = UIHelpers.Create("RuleName", row.transform);
                var nameLE = nameObj.AddComponent<LayoutElement>();
                nameLE.flexibleWidth = 1;
                nameLE.preferredWidth = 300;
                UIHelpers.AddLabel(nameObj, describe(captured), 13f,
                    TextAlignmentOptions.MidlineLeft, Color.white);
            }

            var (errObj, _e) = UIHelpers.Create("Error", popup.transform);
            errObj.AddComponent<LayoutElement>().preferredHeight = 20;
            errorLabel = UIHelpers.AddLabel(errObj, "", 13f,
                TextAlignmentOptions.MidlineLeft, new Color(1f, 0.5f, 0.4f));

            var (buttons, _bt) = UIHelpers.Create("Buttons", popup.transform);
            buttons.AddComponent<LayoutElement>().preferredHeight = 36;
            var bhlg = buttons.AddComponent<HorizontalLayoutGroup>();
            bhlg.spacing = 8;
            bhlg.childForceExpandWidth = true;
            bhlg.childForceExpandHeight = true;
            bhlg.childControlWidth = true;
            bhlg.childControlHeight = true;

            var (cancel, _c) = UIHelpers.Create("Cancel", buttons.transform);
            UIHelpers.AddBackground(cancel, new Color(0.3f, 0.3f, 0.3f, 1f));
            UIHelpers.AddLabel(cancel, "pack.save_dialog.cancel".i18n(), 15f,
                TextAlignmentOptions.Midline);
            cancel.AddComponent<Button>().onClick.AddListener(() => Destroy(transform.parent.gameObject));

            var (confirm, _cf) = UIHelpers.Create("Confirm", buttons.transform);
            UIHelpers.AddBackground(confirm, new Color(0.25f, 0.45f, 0.3f, 1f));
            UIHelpers.AddLabel(confirm, "pack.save_dialog.confirm".i18n(), 15f,
                TextAlignmentOptions.Midline);
            confirm.AddComponent<Button>().onClick.AddListener(() => {
                if (selected.Count == 0) {
                    errorLabel.text = "pack.save_dialog.none_selected".i18n();
                    return;
                }
                // Preserve list order rather than click order — rule order is priority.
                var ordered = new List<TacticsRule>();
                foreach (var rule in rules) if (selected.Contains(rule)) ordered.Add(rule);
                onConfirm(nameInput.text?.Trim(), ordered);
            });

            UIHelpers.EnsureAllHoverable(popup);
        }

        // Without this, TacticsPanel's own ESC handler (Update() in TacticsPanel.cs) is the
        // only thing listening for Escape while this dialog is open: it closes the whole panel
        // instead, leaving this full-screen modal orphaned on the canvas with no owner, and a
        // later Confirm would commit against a ConfigManager.Current a savegame load may have
        // replaced by then. Mirrors BuffPickerOverlay.Update(), including the `closed` guard —
        // onConfirm can also set `closed` and destroy the overlay in the same frame.
        void Update() {
            if (closed) return;
            if (Input.GetKeyDown(KeyCode.Escape)) {
                closed = true;
                var overlay = transform.parent != null ? transform.parent.gameObject : gameObject;
                Destroy(overlay);
            }
        }
    }
}
