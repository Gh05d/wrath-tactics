using System.Collections;
using Kingmaker;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WrathTactics.UI {
    /// <summary>
    /// Transient on-screen feedback when a tactics rule fires. Shows a fading toast
    /// near the top of the screen so the player can see what their companion just
    /// did and which rule drove it. Container persists across calls; individual
    /// toasts auto-destroy via FadeOut coroutine.
    /// </summary>
    public class RuleFireToast : MonoBehaviour {
        const float DurationSeconds = 3.5f;
        const float FadeStartFraction = 0.55f; // hold full alpha for the first 55% of duration

        static GameObject container;
        static int activeToastCount;

        public static void Show(string text) {
            if (string.IsNullOrEmpty(text)) return;
            if (Game.Instance?.UI?.Canvas == null) return;
            EnsureContainer();

            var (toast, _) = UIHelpers.Create("Toast", container.transform);
            toast.AddComponent<LayoutElement>().preferredHeight = 26;
            UIHelpers.AddBackground(toast, new Color(0.05f, 0.05f, 0.05f, 0.78f));
            var label = UIHelpers.AddLabel(toast, text, 14f, TextAlignmentOptions.Midline,
                new Color(0.95f, 0.95f, 0.82f));
            label.margin = new Vector4(10, 0, 10, 0);

            activeToastCount++;
            var driver = container.GetComponent<RuleFireToast>();
            driver.StartCoroutine(driver.FadeOut(toast));
        }

        static void EnsureContainer() {
            if (container != null) return;
            var canvas = Game.Instance.UI.Canvas.transform;
            (container, var rect) = UIHelpers.Create("WrathTacticsToasts", canvas);
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0, -90); // 90 px below screen top
            rect.sizeDelta = new Vector2(480, 0);

            var vlg = container.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 4;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;

            var fitter = container.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // CanvasGroup keeps the container click-through so it never steals input.
            var cg = container.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            cg.interactable = false;

            container.AddComponent<RuleFireToast>();
        }

        IEnumerator FadeOut(GameObject toast) {
            if (toast == null) yield break;

            var image = toast.GetComponent<Image>();
            var label = toast.GetComponentInChildren<TMP_Text>();

            // Cache base colours so the fade is relative to whatever the caller set.
            Color baseBg    = image != null ? image.color : Color.clear;
            Color baseLabel = label != null ? label.color : Color.white;
            float baseLabelA = baseLabel.a;
            float baseBgA = baseBg.a;

            float t = 0f;
            while (toast != null && t < DurationSeconds) {
                t += Time.unscaledDeltaTime;
                float alpha = t < DurationSeconds * FadeStartFraction
                    ? 1f
                    : Mathf.Clamp01(1f - (t - DurationSeconds * FadeStartFraction) / (DurationSeconds * (1f - FadeStartFraction)));
                if (image != null) image.color = new Color(baseBg.r, baseBg.g, baseBg.b, alpha * baseBgA);
                if (label != null) label.color = new Color(baseLabel.r, baseLabel.g, baseLabel.b, alpha * baseLabelA);
                yield return null;
            }

            if (toast != null) Destroy(toast);

            // Tear the container down once the last toast finishes — keeps the
            // VerticalLayoutGroup + ContentSizeFitter off the canvas-layout path
            // when no feedback is active. Without this, even with toasts disabled,
            // the container layout pass would run every frame the panel is visible.
            activeToastCount = System.Math.Max(0, activeToastCount - 1);
            if (activeToastCount == 0 && container != null) {
                Destroy(container);
                container = null;
            }
        }
    }
}
