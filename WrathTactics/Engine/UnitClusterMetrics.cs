using System.Collections.Generic;

namespace WrathTactics.Engine {
    public static class UnitClusterMetrics {
        // Roughly matches RangeBracket.Cone (5 m). Tuned for a Fireball-style ~20 ft
        // burst — units within this distance of the centre are likely to land in the
        // same blast.
        public const float DefaultNeighborRadiusMeters = 5f;

        // Returns the index of the position with the most OTHER positions within
        // `radius` (planar XZ distance — Y is intentionally omitted so cliff /
        // staircase elevation doesn't suppress hits). Empty input → -1. Ties broken
        // by first-best-wins so callers can rely on a stable pick for a given input
        // order.
        public static int FindMostClustered(IList<(float x, float z)> positions, float radius) {
            if (positions == null || positions.Count == 0) return -1;

            int bestIdx = 0;
            int bestCount = -1;
            float r2 = radius * radius;

            for (int i = 0; i < positions.Count; i++) {
                var self = positions[i];
                int count = 0;
                for (int j = 0; j < positions.Count; j++) {
                    if (j == i) continue;
                    float dx = positions[j].x - self.x;
                    float dz = positions[j].z - self.z;
                    if (dx * dx + dz * dz <= r2) count++;
                }
                if (count > bestCount) {
                    bestCount = count;
                    bestIdx = i;
                }
            }
            return bestIdx;
        }
    }
}
