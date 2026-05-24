using System.Collections.Generic;
using WrathTactics.Engine;
using Xunit;

namespace WrathTactics.Tests {
    public class UnitClusterMetricsTests {
        [Fact]
        public void Empty_input_returns_minus_one() {
            Assert.Equal(-1, UnitClusterMetrics.FindMostClustered(new List<(float, float)>(), 5f));
            Assert.Equal(-1, UnitClusterMetrics.FindMostClustered(null, 5f));
        }

        [Fact]
        public void Single_unit_returns_index_zero() {
            var positions = new List<(float x, float z)> { (0f, 0f) };
            Assert.Equal(0, UnitClusterMetrics.FindMostClustered(positions, 5f));
        }

        [Fact]
        public void Picks_center_of_cluster() {
            // A is centre of a 3-unit huddle; B and C are within 5 m of A only.
            // D is a lone outlier 50 m away.
            var positions = new List<(float x, float z)> {
                /* A: */ ( 0f,  0f),
                /* B: */ ( 3f,  0f),
                /* C: */ (-3f,  0f),
                /* D: */ (50f, 50f),
            };
            Assert.Equal(0, UnitClusterMetrics.FindMostClustered(positions, 5f));
        }

        [Fact]
        public void Picks_densest_cluster_over_loose_pair() {
            // Three-unit cluster around the origin (A, B, C all within 4 m of each other)
            // vs. a two-unit pair 100 m away (D, E within 1 m of each other).
            var positions = new List<(float x, float z)> {
                /* A: */ (  0f, 0f),
                /* B: */ (  3f, 0f),
                /* C: */ (  0f, 3f),
                /* D: */ (100f, 0f),
                /* E: */ (101f, 0f),
            };
            // A, B, C each have 2 neighbours; D and E each have 1. Tie among A/B/C
            // is broken by first-best-wins → A (index 0).
            Assert.Equal(0, UnitClusterMetrics.FindMostClustered(positions, 5f));
        }

        [Fact]
        public void Radius_boundary_is_inclusive() {
            // Exactly at radius — should count as a neighbour (dx² + dz² <= r²).
            var positions = new List<(float x, float z)> {
                (0f, 0f),
                (5f, 0f),
            };
            Assert.Equal(0, UnitClusterMetrics.FindMostClustered(positions, 5f));
        }

        [Fact]
        public void Tie_broken_by_first_seen() {
            // Two isolated pairs, same density. First pair wins.
            var positions = new List<(float x, float z)> {
                /* A: */ (  0f, 0f),
                /* B: */ (  2f, 0f),
                /* C: */ (100f, 0f),
                /* D: */ (102f, 0f),
            };
            Assert.Equal(0, UnitClusterMetrics.FindMostClustered(positions, 5f));
        }

        [Fact]
        public void Picks_unit_with_three_neighbours_over_one_with_two() {
            // E sits at origin with 3 neighbours (A, B, C, D) within 5 m.
            // A is also at the edge but only has 2 neighbours (E + one other).
            var positions = new List<(float x, float z)> {
                /* A: */ ( 4f,  0f),  // sees E, B → 2 neighbours
                /* B: */ ( 0f,  4f),  // sees E, A? no, sqrt(32)>5 → only E → 1
                /* C: */ (-4f,  0f),  // sees E → 1
                /* D: */ ( 0f, -4f),  // sees E → 1
                /* E: */ ( 0f,  0f),  // sees A, B, C, D → 4
            };
            Assert.Equal(4, UnitClusterMetrics.FindMostClustered(positions, 5f));
        }
    }
}
