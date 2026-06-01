using FluentAssertions;
using Majik.Core.Combat;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.Keywords;

/// <summary>
/// CR 702.49 — Ninjutsu. Return an unblocked attacker you control to hand,
/// then put the Ninja onto the battlefield from hand tapped and attacking the
/// same defender.
/// </summary>
public class NinjutsuActionTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private CombatManager StartCombatWithUnblockedAttacker(Creature attacker)
    {
        attacker.SetOwner(_alice);
        attacker.SetController(_alice);
        attacker.SetZone(ZoneType.Battlefield);
        attacker.HasSummoningSickness = false;
        _alice.Zones.Battlefield.AddCard(attacker);

        var combat = new CombatManager();
        combat.StartCombat(_alice);
        combat.DeclareAttackers(_alice, new[]
        {
            new AttackerDeclaration(attacker, _bob),
        });
        // After DeclareAttackers the combat is in DeclaringBlockers state; no
        // blockers declared yet → the attacker is unblocked.
        return combat;
    }

    [Fact]
    public void Ninjutsu_ReturnsUnblockedAttackerToHand_PutsNinjaTappedAndAttacking()
    {
        var attacker = new Creature("Ornithopter", "{0}", 0, 2);
        var combat = StartCombatWithUnblockedAttacker(attacker);

        var ninja = new Creature("Ninja of the Deep Hours", "{1}{U}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
        };
        ninja.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(ninja);

        NinjutsuAction.CanExecute(ninja, _alice, combat).Should().BeTrue();

        var entered = NinjutsuAction.Execute(ninja, _alice, combat);

        entered.Should().NotBeNull("the ninja joined combat as an attacker");

        // The returned attacker went back to its owner's hand and left combat.
        attacker.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Hand.GetCards().Should().Contain(attacker);
        combat.CurrentCombat!.Attackers.Should().NotContain(a => ReferenceEquals(a.Creature, attacker),
            "the bounced attacker left combat (CR 506.4)");

        // The ninja entered the battlefield tapped and attacking the same
        // defender (CR 702.49b/d).
        ninja.Zone.Should().Be(ZoneType.Battlefield);
        ninja.IsTapped.Should().BeTrue("the ninja enters tapped (CR 508.3)");
        _alice.Zones.Hand.GetCards().Should().NotContain(ninja);
        combat.CurrentCombat!.Attackers.Should().Contain(a => ReferenceEquals(a.Creature, ninja));
        entered!.TargetPlayer.Should().BeSameAs(_bob,
            "attacking the same defender as the combat it joined (CR 508.4)");
    }

    [Fact]
    public void Ninjutsu_NoUnblockedAttacker_CannotExecute()
    {
        // No combat declared — nothing to bounce.
        var combat = new CombatManager();
        combat.StartCombat(_alice);

        var ninja = new Creature("Ninja", "{1}{U}", 2, 2) { Owner = _alice, Controller = _alice };
        ninja.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(ninja);

        NinjutsuAction.CanExecute(ninja, _alice, combat).Should().BeFalse();
        NinjutsuAction.Execute(ninja, _alice, combat).Should().BeNull();
        ninja.Zone.Should().Be(ZoneType.Hand, "no swap happened");
    }

    [Fact]
    public void Ninjutsu_NinjaNotInHand_CannotExecute()
    {
        var attacker = new Creature("Ornithopter", "{0}", 0, 2);
        var combat = StartCombatWithUnblockedAttacker(attacker);

        var ninja = new Creature("Ninja", "{1}{U}", 2, 2) { Owner = _alice, Controller = _alice };
        ninja.SetZone(ZoneType.Battlefield); // already on the battlefield, not in hand
        _alice.Zones.Battlefield.AddCard(ninja);

        NinjutsuAction.CanExecute(ninja, _alice, combat).Should().BeFalse();
    }

    [Fact]
    public void Ninjutsu_BlockedAttacker_IsNotEligible()
    {
        var attacker = new Creature("Ornithopter", "{0}", 0, 2);
        var combat = StartCombatWithUnblockedAttacker(attacker);

        // Block the attacker — it is no longer "unblocked".
        var blocker = new Creature("Wall", "{0}", 0, 4) { Owner = _bob, Controller = _bob };
        blocker.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(blocker);
        var attackerEntry = combat.CurrentCombat!.Attackers
            .Single(a => ReferenceEquals(a.Creature, attacker));
        combat.DeclareBlockers(_bob, new[]
        {
            new BlockerDeclaration(blocker, attackerEntry),
        });

        NinjutsuAction.FindUnblockedAttacker(_alice, combat).Should().BeNull(
            "the only attacker is now blocked");

        var ninja = new Creature("Ninja", "{1}{U}", 2, 2) { Owner = _alice, Controller = _alice };
        ninja.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(ninja);

        NinjutsuAction.CanExecute(ninja, _alice, combat).Should().BeFalse();
    }
}
