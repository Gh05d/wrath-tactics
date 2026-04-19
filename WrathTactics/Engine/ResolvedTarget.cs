using Kingmaker.EntitySystem.Entities;
using UnityEngine;

namespace WrathTactics.Engine {
    public readonly struct ResolvedTarget {
        public readonly UnitEntityData Unit;
        public readonly Vector3? Point;

        public static readonly ResolvedTarget None = default;

        public ResolvedTarget(UnitEntityData unit) { Unit = unit; Point = null; }
        public ResolvedTarget(Vector3 point) { Unit = null; Point = point; }

        public bool IsValid => Unit != null || Point.HasValue;
        public bool IsPoint => Point.HasValue;
    }
}
