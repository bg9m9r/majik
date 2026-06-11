using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// CR 800.4a / 104.3a — a player who has already left the game (lost) is no
/// longer in the game, so damage to them is a no-op. Regression for the
/// fuzz-found crash: in a Burn mirror two burn spells can be on the stack and
/// the first is lethal; the SBA sweep marks the target lost, then the second
/// burn spell resolves and calls <see cref="Player.LoseLife"/> on the
/// already-lost player, which throws "Cannot lose life after losing the game".
/// The combat-damage path (CombatFlow) already guarded this; these tests pin
/// the spell-damage equivalent in <see cref="OracleSpellBinder.DealDamage"/>.
/// </summary>
public class DealDamageToLostPlayerTests
{
    [Fact]
    public void DealDamage_ToLostPlayer_DoesNotThrow_AndIsNoOp()
    {
        var victim = new Player("Victim", 0);
        victim.MarkLost();
        victim.HasLost.Should().BeTrue();

        var act = () => OracleSpellBinder.DealDamage(victim, 3);

        act.Should().NotThrow("a player who has left the game can't be dealt damage (CR 800.4a)");
        victim.LifeTotal.Should().Be(0, "no further life loss is applied to a lost player");
    }

    [Fact]
    public void DealDamage_ToLivePlayer_StillAppliesLifeLoss()
    {
        var target = new Player("Target", 20);

        OracleSpellBinder.DealDamage(target, 3);

        target.LifeTotal.Should().Be(17, "a live player still takes spell damage normally");
    }
}
