using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using WrathTactics.Logging;

namespace WrathTactics.Engine {
    public static class AssetLoader {
        static readonly Dictionary<string, Sprite> SpriteCache = new();
        static bool initialized;

        public static void Init() {
            if (initialized) return;
            initialized = true;
            Log.UI.Info($"AssetLoader.Init — mod path: {Main.ModPath}");
        }

        /// <summary>
        /// Loads a PNG from `<ModPath>/Assets/<folder>/<file>` as a Sprite.
        /// Returns null on missing file or load failure (logged at warn level).
        /// Cached by <c>folder/file</c> key — subsequent calls return the same Sprite instance.
        /// </summary>
        public static Sprite Load(string folder, string file, Vector2Int size) {
            var cacheKey = $"{folder}/{file}";
            if (SpriteCache.TryGetValue(cacheKey, out var cached)) return cached;
            Texture2D texture = null;
            try {
                var path = Path.Combine(Main.ModPath, "Assets", folder, file);
                if (!File.Exists(path)) {
                    Log.UI.Warn($"Sprite '{cacheKey}' missing at {path} — falling back to Unity default.");
                    SpriteCache[cacheKey] = null;
                    return null;
                }
                var bytes = File.ReadAllBytes(path);
                texture = new Texture2D(size.x, size.y, TextureFormat.DXT5, false);
                texture.mipMapBias = 15.0f;
                if (!texture.LoadImage(bytes)) {
                    Log.UI.Warn($"Sprite '{cacheKey}' failed Texture2D.LoadImage at {path}.");
                    UnityEngine.Object.Destroy(texture);
                    SpriteCache[cacheKey] = null;
                    return null;
                }
                var sprite = Sprite.Create(texture, new Rect(0, 0, size.x, size.y), new Vector2(0.5f, 0.5f));
                SpriteCache[cacheKey] = sprite;
                return sprite;
            } catch (Exception ex) {
                Log.UI.Error(ex, $"AssetLoader.Load failed for {cacheKey}");
                if (texture != null) UnityEngine.Object.Destroy(texture);
                SpriteCache[cacheKey] = null;
                return null;
            }
        }
    }
}
