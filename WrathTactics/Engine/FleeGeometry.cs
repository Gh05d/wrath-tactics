using System.Collections.Generic;

namespace WrathTactics.Engine {
    /// <summary>
    /// Pure planar (XZ) geometry for the "Move away from target" action. The validator
    /// turns the candidate points into navmesh-clamped positions (ObstacleAnalyzer);
    /// everything here is engine-free so it can be unit-tested.
    /// </summary>
    public static class FleeGeometry {
        /// <summary>A flee point must gain at least this much distance from the anchor,
        /// otherwise the walk is pointless (cornered unit).</summary>
        public const float MinGainMeters = 1f;
        /// <summary>Shortest walk worth issuing — re-issuing 20 cm hops every tick would
        /// stutter (see ActionValidator.WalkRetargetMeters for the same problem on approach).</summary>
        public const float MinStepMeters = 3f;
        /// <summary>Longest single walk: the rule re-evaluates every tick, so a long flight
        /// is a chain of steps that keeps re-reading where the enemy is.</summary>
        public const float MaxStepMeters = 12f;
        /// <summary>Overshoot past the required distance so the next tick reads "beyond".</summary>
        public const float MarginMeters = 1f;

        /// <summary>Directions tried, as rotations of the straight-away vector. Straight
        /// away first; the sideways and backwards-diagonal options are for units with a
        /// wall or a cliff behind them.</summary>
        public static readonly float[] CandidateAnglesDeg = { 0f, 30f, -30f, 60f, -60f, 90f, -90f, 135f, -135f };

        public static float StepLength(float currentDistance, float requiredDistance) {
            float step = requiredDistance - currentDistance + MarginMeters;
            if (step < MinStepMeters) return MinStepMeters;
            if (step > MaxStepMeters) return MaxStepMeters;
            return step;
        }

        /// <summary>Unit vector from anchor to unit. When both coincide (the anchor is the
        /// unit itself) the fallback — the unit's facing — is used instead.</summary>
        public static (float x, float z) AwayDirection((float x, float z) unit, (float x, float z) anchor, (float x, float z) fallback) {
            float dx = unit.x - anchor.x;
            float dz = unit.z - anchor.z;
            float len2 = dx * dx + dz * dz;
            if (len2 < 0.01f) return Normalize(fallback);
            float len = (float)System.Math.Sqrt(len2);
            return (dx / len, dz / len);
        }

        public static (float x, float z) Rotate((float x, float z) dir, float degrees) {
            double rad = degrees * System.Math.PI / 180.0;
            float c = (float)System.Math.Cos(rad);
            float s = (float)System.Math.Sin(rad);
            return (dir.x * c - dir.z * s, dir.x * s + dir.z * c);
        }

        /// <summary>One candidate per entry of CandidateAnglesDeg, in that order.</summary>
        public static List<(float x, float z)> CandidatePoints((float x, float z) unit, (float x, float z) away, float step) {
            var points = new List<(float x, float z)>(CandidateAnglesDeg.Length);
            foreach (var angle in CandidateAnglesDeg) {
                var d = Rotate(away, angle);
                points.Add((unit.x + d.x * step, unit.z + d.z * step));
            }
            return points;
        }

        /// <summary>Index of the reachable point farthest from the anchor, or -1 when even
        /// the best one gains less than MinGainMeters over the current distance. Ties go to
        /// the earlier (straighter) candidate.</summary>
        public static int PickFarthest((float x, float z) anchor, IList<(float x, float z)> reachable, float currentDistance) {
            if (reachable == null || reachable.Count == 0) return -1;
            int best = -1;
            float bestDist = currentDistance + MinGainMeters;
            for (int i = 0; i < reachable.Count; i++) {
                float d = Distance(anchor, reachable[i]);
                if (d > bestDist || (best < 0 && d >= bestDist)) {
                    best = i;
                    bestDist = d;
                }
            }
            return best;
        }

        public static (float x, float z)? Centroid(IList<(float x, float z)> points) {
            if (points == null || points.Count == 0) return null;
            float sx = 0f, sz = 0f;
            foreach (var p in points) { sx += p.x; sz += p.z; }
            return (sx / points.Count, sz / points.Count);
        }

        public static float Distance((float x, float z) a, (float x, float z) b) {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return (float)System.Math.Sqrt(dx * dx + dz * dz);
        }

        static (float x, float z) Normalize((float x, float z) v) {
            float len = (float)System.Math.Sqrt(v.x * v.x + v.z * v.z);
            if (len < 0.0001f) return (0f, 1f);
            return (v.x / len, v.z / len);
        }
    }
}
