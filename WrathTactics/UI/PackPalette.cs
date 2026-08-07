using UnityEngine;

namespace WrathTactics.UI {
    /// <summary>
    /// Fixed colour set for packs. TacticsPack.ColorIndex is a persisted index into this
    /// array, so entries may only ever be APPENDED — reordering repaints every existing
    /// pack. Colours are chosen to stay readable on the book-page background and to be
    /// distinguishable from the default rule header (brown) and the linked header (blue-grey).
    /// </summary>
    public static class PackPalette {
        static readonly Color[] Colors = {
            new Color(0.45f, 0.25f, 0.25f, 1f),  // 0 rust
            new Color(0.25f, 0.40f, 0.25f, 1f),  // 1 moss
            new Color(0.25f, 0.32f, 0.48f, 1f),  // 2 steel blue
            new Color(0.42f, 0.34f, 0.18f, 1f),  // 3 amber
            new Color(0.38f, 0.25f, 0.45f, 1f),  // 4 plum
            new Color(0.20f, 0.38f, 0.40f, 1f),  // 5 teal
        };

        public static int Count => Colors.Length;

        /// <summary>Index-safe lookup — out-of-range (corrupt/hand-edited JSON) falls back to 0.</summary>
        public static Color ColorAt(int index) {
            if (index < 0 || index >= Colors.Length) return Colors[0];
            return Colors[index];
        }

        public static int Next(int index) {
            if (index < 0 || index >= Colors.Length - 1) return 0;
            return index + 1;
        }

        /// <summary>Muted variant for rule-card headers, so the chip stays the louder element.</summary>
        public static Color HeaderTint(int index) {
            var c = ColorAt(index);
            return new Color(c.r * 0.75f, c.g * 0.75f, c.b * 0.75f, 1f);
        }
    }
}
