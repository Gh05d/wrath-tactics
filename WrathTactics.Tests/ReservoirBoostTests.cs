using Newtonsoft.Json;
using WrathTactics.Models;
using Xunit;

namespace WrathTactics.Tests {
    // ActionDef.Reservoir persists as an enum index: rules saved before the field existed must
    // load as None, and the indices must never shift (enums are append-only).
    public class ReservoirBoostTests {
        [Fact]
        public void Legacy_action_without_Reservoir_loads_as_None() {
            var action = JsonConvert.DeserializeObject<ActionDef>("{\"Type\":0,\"AbilityId\":\"x\"}");
            Assert.Equal(ReservoirBoost.None, action.Reservoir);
        }

        [Fact]
        public void Reservoir_round_trips() {
            var json = JsonConvert.SerializeObject(new ActionDef { Reservoir = ReservoirBoost.DC });
            Assert.Equal(ReservoirBoost.DC, JsonConvert.DeserializeObject<ActionDef>(json).Reservoir);
        }

        [Fact]
        public void Reservoir_indices_are_pinned() {
            Assert.Equal(0, (int)ReservoirBoost.None);
            Assert.Equal(1, (int)ReservoirBoost.CasterLevel);
            Assert.Equal(2, (int)ReservoirBoost.DC);
        }
    }
}
