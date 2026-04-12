using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WrathTactics.Models;
using WrathTactics.Persistence;

namespace WrathTactics.UI {
    public class RuleEditorWidget : MonoBehaviour {
        TacticsRule rule;
        int index;
        List<TacticsRule> ruleList;
        Action onChanged;
        Text nameLabel;
        Text enabledLabel;
        Text summaryLabel;

        public void Init(TacticsRule rule, int index, List<TacticsRule> ruleList, Action onChanged) {
            this.rule = rule;
            this.index = index;
            this.ruleList = ruleList;
            this.onChanged = onChanged;
            BuildUI();
        }

        void BuildUI() {
            var root = gameObject;

            // Card background
            UIHelpers.AddBackground(root, new Color(0.18f, 0.18f, 0.18f, 1f));

            // Set preferred height via LayoutElement
            root.AddComponent<LayoutElement>().preferredHeight = 100;

            // Header row: name + buttons
            var (header, headerRect) = UIHelpers.Create("Header", root.transform);
            headerRect.SetAnchor(0.01, 0.99, 0.6, 0.95);
            headerRect.sizeDelta = Vector2.zero;

            // Rule name
            nameLabel = UIHelpers.AddLabel(header, $"{index + 1}. {rule.Name}", 14, TextAnchor.MiddleLeft);

            // Enable toggle button
            var (enableBtn, enableRect) = UIHelpers.Create("EnableBtn", header.transform);
            enableRect.SetAnchor(0.7, 0.78, 0, 1);
            enableRect.sizeDelta = Vector2.zero;
            enabledLabel = UIHelpers.AddLabel(enableBtn, rule.Enabled ? "[ON]" : "[OFF]", 12, TextAnchor.MiddleCenter,
                rule.Enabled ? Color.green : Color.gray);
            enableBtn.AddComponent<Button>().onClick.AddListener(() => {
                rule.Enabled = !rule.Enabled;
                enabledLabel.text = rule.Enabled ? "[ON]" : "[OFF]";
                enabledLabel.color = rule.Enabled ? Color.green : Color.gray;
                ConfigManager.Save();
            });

            // Move up button
            var (upBtn, upRect) = UIHelpers.Create("UpBtn", header.transform);
            upRect.SetAnchor(0.8, 0.86, 0, 1);
            upRect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(upBtn, new Color(0.3f, 0.3f, 0.3f, 1f));
            UIHelpers.AddLabel(upBtn, "^", 14, TextAnchor.MiddleCenter);
            upBtn.AddComponent<Button>().onClick.AddListener(() => MoveRule(-1));

            // Move down button
            var (downBtn, downRect) = UIHelpers.Create("DownBtn", header.transform);
            downRect.SetAnchor(0.87, 0.93, 0, 1);
            downRect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(downBtn, new Color(0.3f, 0.3f, 0.3f, 1f));
            UIHelpers.AddLabel(downBtn, "v", 14, TextAnchor.MiddleCenter);
            downBtn.AddComponent<Button>().onClick.AddListener(() => MoveRule(1));

            // Delete button
            var (delBtn, delRect) = UIHelpers.Create("DeleteBtn", header.transform);
            delRect.SetAnchor(0.94, 1, 0, 1);
            delRect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(delBtn, new Color(0.6f, 0.2f, 0.2f, 1f));
            UIHelpers.AddLabel(delBtn, "X", 14, TextAnchor.MiddleCenter);
            delBtn.AddComponent<Button>().onClick.AddListener(() => DeleteRule());

            // Summary row: conditions + action + target
            var (summary, summaryRect) = UIHelpers.Create("Summary", root.transform);
            summaryRect.SetAnchor(0.02, 0.98, 0.05, 0.55);
            summaryRect.sizeDelta = Vector2.zero;

            string condText = rule.ConditionGroups.Count > 0
                ? $"WENN: {rule.ConditionGroups.Count} Bedingungsgruppe(n)"
                : "WENN: (keine Bedingungen)";
            string actionText = $"DANN: {rule.Action.Type}";
            string targetText = $"AUF: {rule.Target.Type}";
            string cooldownText = $"Cooldown: {rule.CooldownRounds} Runde(n)";

            summaryLabel = UIHelpers.AddLabel(summary,
                $"{condText}  |  {actionText}  |  {targetText}  |  {cooldownText}",
                11, TextAnchor.MiddleLeft, new Color(0.7f, 0.7f, 0.7f));
        }

        void MoveRule(int direction) {
            int newIndex = index + direction;
            if (newIndex < 0 || newIndex >= ruleList.Count) return;
            ruleList.RemoveAt(index);
            ruleList.Insert(newIndex, rule);
            ConfigManager.Save();
            onChanged?.Invoke();
        }

        void DeleteRule() {
            ruleList.Remove(rule);
            ConfigManager.Save();
            onChanged?.Invoke();
        }
    }
}
