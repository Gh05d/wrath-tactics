using WrathTactics.Models;
using Xunit;

namespace WrathTactics.Tests {
    // Fresh rules start on Self; offensive actions flip that to a threat-based selector.
    // An explicit non-Self choice is never touched.
    public class TargetDefaultsTests {
        [Theory]
        [InlineData(ActionType.AttackTarget, TargetType.EnemyHighestThreat)]
        [InlineData(ActionType.ThrowSplash,  TargetType.EnemyHighestThreat)]
        [InlineData(ActionType.MoveToTarget, TargetType.EnemyNearest)]
        public void ForAction_replaces_Self_for_offensive_actions(ActionType action, TargetType expected) {
            Assert.Equal(expected, TargetDefaults.ForAction(action, TargetType.Self));
        }

        [Theory]
        [InlineData(ActionType.CastSpell)]
        [InlineData(ActionType.CastAbility)]
        [InlineData(ActionType.UseItem)]
        [InlineData(ActionType.ToggleActivatable)]
        [InlineData(ActionType.Heal)]
        [InlineData(ActionType.DoNothing)]
        [InlineData(ActionType.SwitchWeaponSet)]
        public void ForAction_keeps_Self_for_non_offensive_actions(ActionType action) {
            Assert.Null(TargetDefaults.ForAction(action, TargetType.Self));
        }

        [Theory]
        [InlineData(TargetType.EnemyNearest)]
        [InlineData(TargetType.AllyLowestHp)]
        [InlineData(TargetType.ConditionTarget)]
        public void ForAction_never_overrides_an_explicit_choice(TargetType current) {
            Assert.Null(TargetDefaults.ForAction(ActionType.AttackTarget, current));
        }

        [Fact]
        public void ForAbility_enemy_only_ability_gets_threat_selector() {
            Assert.Equal(TargetType.EnemyHighestThreat,
                TargetDefaults.ForAbility(TargetType.Self, canTargetEnemies: true, canTargetFriends: false, canTargetSelf: false));
        }

        [Theory]
        [InlineData(true,  true,  false)] // hits both sides (e.g. Fireball) — ambiguous
        [InlineData(true,  false, true)]  // enemies or self
        [InlineData(false, true,  true)]  // buff
        [InlineData(false, false, true)]  // self-only
        public void ForAbility_keeps_Self_unless_enemy_only(bool enemies, bool friends, bool self) {
            Assert.Null(TargetDefaults.ForAbility(TargetType.Self, enemies, friends, self));
        }

        [Fact]
        public void ForAbility_never_overrides_an_explicit_choice() {
            Assert.Null(TargetDefaults.ForAbility(TargetType.AllyLowestHp, canTargetEnemies: true, canTargetFriends: false, canTargetSelf: false));
        }
    }
}
