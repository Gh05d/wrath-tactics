using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WrathTactics.Logging;
using WrathTactics.Models;

namespace WrathTactics.Persistence {
    /// <summary>
    /// Deserializes a Condition but returns null when the JSON references
    /// an enum value that no longer exists in the current code (e.g. an
    /// old MissingBuff / HasDebuff entry). Any other deserialization error
    /// is re-thrown so structural corruption is caught at the outer level.
    /// </summary>
    public class SafeConditionConverter : JsonConverter {
        public override bool CanWrite => false;

        public override bool CanConvert(Type objectType) {
            return objectType == typeof(Condition);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) {
            throw new NotImplementedException("CanWrite is false");
        }

        public override object ReadJson(JsonReader reader, Type objectType,
            object existingValue, JsonSerializer serializer) {
            JObject obj;
            try {
                obj = JObject.Load(reader);
            } catch (Exception ex) {
                Log.Persistence.Warn($"Skipping condition — could not parse JSON object: {ex.Message}");
                return null;
            }

            var condition = new Condition();
            try {
                // Populate via a nested serializer pass without this converter, so
                // enum-parse failures here throw instead of recursing.
                var inner = new JsonSerializer();
                foreach (var c in serializer.Converters) {
                    if (!(c is SafeConditionConverter)) inner.Converters.Add(c);
                }
                using (var sub = obj.CreateReader()) {
                    inner.Populate(sub, condition);
                }
                return condition;
            } catch (JsonSerializationException ex) {
                Log.Persistence.Warn($"Dropping condition due to unknown enum value. Raw JSON: {obj.ToString(Formatting.None)} — {ex.Message}");
                return null;
            }
        }
    }
}
