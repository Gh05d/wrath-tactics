using WrathTactics.Engine;
using Xunit;

namespace WrathTactics.Tests {
    public class IssuedCommandPolicyTests {
        [Fact]
        public void a_command_that_acted_keeps_its_cooldown() {
            Assert.Equal(IssuedCommandOutcome.Spent, IssuedCommandPolicy.Classify(started: true, finished: true, acted: true, resident: false));
        }

        [Theory]
        [InlineData(false)]  // cancelled before it started (player move order over a pending cast)
        [InlineData(true)]   // cancelled mid-animation before the act point
        public void a_command_that_finished_without_acting_is_refunded(bool started) {
            Assert.Equal(IssuedCommandOutcome.Refund, IssuedCommandPolicy.Classify(started, finished: true, acted: false, resident: false));
        }

        [Fact]
        public void a_queued_command_wiped_before_starting_is_refunded() {
            Assert.Equal(IssuedCommandOutcome.Refund, IssuedCommandPolicy.Classify(started: false, finished: false, acted: false, resident: false));
        }

        [Theory]
        [InlineData(false, true)]   // pending in slot or queue
        [InlineData(true, true)]    // running
        [InlineData(true, false)]   // running; residency is irrelevant once started
        public void unfinished_commands_are_kept(bool started, bool resident) {
            Assert.Equal(IssuedCommandOutcome.Keep, IssuedCommandPolicy.Classify(started, finished: false, acted: false, resident: resident));
        }

        [Fact]
        public void acted_but_unfinished_is_still_kept() {
            // Post-act animation tail: the outcome is decided, but wait for IsFinished so
            // the entry is dropped exactly once.
            Assert.Equal(IssuedCommandOutcome.Keep, IssuedCommandPolicy.Classify(started: true, finished: false, acted: true, resident: true));
        }
    }
}
