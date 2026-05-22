using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Effects;

public class CombatRestrictionEffectTests
{
    private static (Player owner, Creature c) MakeCreature(string name, int pow = 2, int tou = 2)
    {
        var owner = new Player(name + "Owner", 20);
        var card = new Creature(name, "", pow, tou, subtypes: new[] { CardSubtype.Bear });
        card.SetOwner(owner);
        card.SetController(owner);
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        // No summoning sickness so it can attack/block.
        card.ClearSummoningSickness();
        return (owner, card);
    }

    [Fact]
    public void HasRestriction_TargetedEffect_AppliesOnlyToTarget()
    {
        var svc = new ContinuousEffectsService();
        var (_, a) = MakeCreature("A");
        var (_, b) = MakeCreature("B");

        svc.Register(new CombatRestrictionEffect(CombatRestriction.CannotBlock, a));

        svc.HasRestriction(a, CombatRestriction.CannotBlock).Should().BeTrue();
        svc.HasRestriction(b, CombatRestriction.CannotBlock).Should().BeFalse();
    }

    [Fact]
    public void HasRestriction_MassEffect_AppliesToEveryCreature()
    {
        var svc = new ContinuousEffectsService();
        var (_, a) = MakeCreature("A");
        var (_, b) = MakeCreature("B");

        svc.Register(new CombatRestrictionEffect(CombatRestriction.CannotBlock, target: null));

        svc.HasRestriction(a, CombatRestriction.CannotBlock).Should().BeTrue();
        svc.HasRestriction(b, CombatRestriction.CannotBlock).Should().BeTrue();
    }

    [Fact]
    public void HasRestriction_DistinctRestrictions_DoNotCrossContaminate()
    {
        var svc = new ContinuousEffectsService();
        var (_, a) = MakeCreature("A");

        svc.Register(new CombatRestrictionEffect(CombatRestriction.CannotBlock, a));

        svc.HasRestriction(a, CombatRestriction.CannotBlock).Should().BeTrue();
        svc.HasRestriction(a, CombatRestriction.CannotAttack).Should().BeFalse();
        svc.HasRestriction(a, CombatRestriction.CannotBeBlocked).Should().BeFalse();
    }

    [Fact]
    public void HasRestriction_ExpiresAtEndOfTurn()
    {
        var svc = new ContinuousEffectsService();
        var (_, a) = MakeCreature("A");

        svc.Register(new CombatRestrictionEffect(CombatRestriction.CannotBlock, a));
        svc.HasRestriction(a, CombatRestriction.CannotBlock).Should().BeTrue();

        svc.ExpireEndOfTurn();
        svc.HasRestriction(a, CombatRestriction.CannotBlock).Should().BeFalse();
    }

    [Fact]
    public void CombatValidator_CanBlock_FalseWhenCannotBlockEffectActive()
    {
        var svc = new ContinuousEffectsService();
        var (defender, blocker) = MakeCreature("Blocker");
        var (attackerOwner, attackerCreature) = MakeCreature("Attacker");
        var attacker = new Attacker(attackerCreature, defender);

        var v = new CombatValidator(svc);
        v.CanBlock(blocker, attacker, defender).Should().BeTrue(); // baseline

        svc.Register(new CombatRestrictionEffect(CombatRestriction.CannotBlock, blocker));
        v.CanBlock(blocker, attacker, defender).Should().BeFalse();
    }

    [Fact]
    public void CombatValidator_CanBlock_FalseWhenAttackerHasCannotBeBlocked()
    {
        var svc = new ContinuousEffectsService();
        var (defender, blocker) = MakeCreature("Blocker");
        var (_, attackerCreature) = MakeCreature("Attacker");
        var attacker = new Attacker(attackerCreature, defender);

        var v = new CombatValidator(svc);
        v.CanBlock(blocker, attacker, defender).Should().BeTrue();

        svc.Register(new CombatRestrictionEffect(CombatRestriction.CannotBeBlocked, attackerCreature));
        v.CanBlock(blocker, attacker, defender).Should().BeFalse();
    }

    [Fact]
    public void CombatValidator_CanAttack_FalseWhenCannotAttackEffectActive()
    {
        var svc = new ContinuousEffectsService();
        var (active, attacker) = MakeCreature("Attacker");

        var v = new CombatValidator(svc);
        v.CanAttack(attacker, active).Should().BeTrue();

        svc.Register(new CombatRestrictionEffect(CombatRestriction.CannotAttack, attacker));
        v.CanAttack(attacker, active).Should().BeFalse();
    }
}
