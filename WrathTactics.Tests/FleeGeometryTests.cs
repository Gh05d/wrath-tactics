using System.Collections.Generic;
using WrathTactics.Engine;
using Xunit;

namespace WrathTactics.Tests {
    public class FleeGeometryTests {
        [Theory]
        [InlineData(1f, 10f, 10f)]    // 10 required, 1 away: walk 9 + 1 margin
        [InlineData(9.5f, 10f, 3f)]   // nearly there: never below the minimum step
        [InlineData(0f, 40f, 12f)]    // Long: capped, the rule re-issues next tick
        public void step_length_covers_the_gap_within_bounds(float current, float required, float expected) {
            Assert.Equal(expected, FleeGeometry.StepLength(current, required), 3);
        }

        [Fact]
        public void away_direction_points_from_anchor_to_unit() {
            var d = FleeGeometry.AwayDirection((3f, 0f), (0f, 0f), (0f, 1f));
            Assert.Equal(1f, d.x, 3);
            Assert.Equal(0f, d.z, 3);
        }

        [Fact]
        public void away_direction_uses_facing_when_anchor_is_the_unit() {
            var d = FleeGeometry.AwayDirection((5f, 5f), (5f, 5f), (0f, 2f));
            Assert.Equal(0f, d.x, 3);
            Assert.Equal(1f, d.z, 3);
        }

        [Fact]
        public void candidates_start_straight_away_and_fan_out() {
            var pts = FleeGeometry.CandidatePoints((0f, 0f), (1f, 0f), 10f);
            Assert.Equal(FleeGeometry.CandidateAnglesDeg.Length, pts.Count);
            Assert.Equal(10f, pts[0].x, 3);
            Assert.Equal(0f, pts[0].z, 3);
            // 90° candidates are sideways, same length
            Assert.Equal(0f, pts[5].x, 3);
            Assert.Equal(10f, System.Math.Abs(pts[5].z), 3);
        }

        [Fact]
        public void picks_the_reachable_point_farthest_from_the_anchor() {
            var anchor = (0f, 0f);
            // unit at (2,0); straight-away candidate blocked at 3 m, sideways one reaches 8 m
            var reachable = new List<(float x, float z)> { (3f, 0f), (2f, 8f), (2f, -1f) };
            Assert.Equal(1, FleeGeometry.PickFarthest(anchor, reachable, currentDistance: 2f));
        }

        [Fact]
        public void cornered_unit_gets_no_destination() {
            var anchor = (0f, 0f);
            var reachable = new List<(float x, float z)> { (2.5f, 0f), (2f, 0.5f) };
            Assert.Equal(-1, FleeGeometry.PickFarthest(anchor, reachable, currentDistance: 2f));
            Assert.Equal(-1, FleeGeometry.PickFarthest(anchor, new List<(float x, float z)>(), 2f));
        }

        [Fact]
        public void ties_go_to_the_straighter_candidate() {
            var reachable = new List<(float x, float z)> { (10f, 0f), (0f, 10f) };
            Assert.Equal(0, FleeGeometry.PickFarthest((0f, 0f), reachable, currentDistance: 1f));
        }

        [Fact]
        public void centroid_of_points() {
            Assert.Null(FleeGeometry.Centroid(new List<(float x, float z)>()));
            var c = FleeGeometry.Centroid(new List<(float x, float z)> { (0f, 0f), (4f, 2f) }).Value;
            Assert.Equal(2f, c.x, 3);
            Assert.Equal(1f, c.z, 3);
        }
    }
}
