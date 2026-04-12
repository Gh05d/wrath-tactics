using Kingmaker;
using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace WrathTactics.UI {
    static class UIHelpers {
        public static Transform StaticRoot => Game.Instance.UI.Canvas.transform;
        public static Transform ServiceWindow => StaticRoot.Find("ServiceWindowsPCView");

        public static void SetAnchor(this RectTransform transform, double xMin, double xMax, double yMin, double yMax) {
            transform.anchorMin = new Vector2((float)xMin, (float)yMin);
            transform.anchorMax = new Vector2((float)xMax, (float)yMax);
        }

        public static RectTransform Rect(this GameObject obj) => obj.transform as RectTransform;
        public static RectTransform Rect(this Transform obj) => obj as RectTransform;

        public static void FillParent(this RectTransform rect) {
            rect.SetAnchor(0, 1, 0, 1);
            rect.sizeDelta = Vector2.zero;
        }

        public static void FillParent(this GameObject obj) => obj.Rect().FillParent();

        public static (GameObject, RectTransform) Create(string name, Transform parent = null) {
            var obj = new GameObject(name, typeof(RectTransform));
            if (parent != null)
                obj.AddTo(parent);
            return (obj, obj.Rect());
        }

        public static void AddTo(this GameObject obj, Transform parent) {
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = Vector3.zero;
            obj.transform.localScale = Vector3.one;
            obj.transform.localRotation = Quaternion.identity;
        }

        public static Transform ChildTransform(this GameObject obj, string path) {
            return obj.transform.Find(path);
        }

        public static GameObject ChildObject(this GameObject obj, string path) {
            return obj.ChildTransform(path)?.gameObject;
        }

        public static T MakeComponent<T>(this GameObject obj, Action<T> build) where T : Component {
            var component = obj.AddComponent<T>();
            build(component);
            return component;
        }

        public static Text AddLabel(GameObject parent, string text, int fontSize = 14,
            TextAnchor alignment = TextAnchor.MiddleLeft, Color? color = null) {
            var (labelObj, _) = Create("Label", parent.transform);
            var txt = labelObj.AddComponent<Text>();
            txt.text = text;
            txt.fontSize = fontSize;
            txt.alignment = alignment;
            txt.color = color ?? Color.white;
            txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return txt;
        }

        public static Image AddBackground(GameObject obj, Color color) {
            var img = obj.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = true;
            return img;
        }
    }
}
