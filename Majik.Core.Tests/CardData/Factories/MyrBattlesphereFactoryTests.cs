using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="MyrBattlesphereFactory"/> (Magic 2011 / New Phyrexia,
/// {7}). Artifact Creature — Myr Construct, 4/7:
///   "When this creature enters, create four 1/1 colorless Myr artifact
///    creature tokens.
///    Whenever this creature attacks, you may tap X untapped Myr you control.
///    If you do, this creature gets +X/+0 until end of turn and deals X damage
///    to the player or planeswalker it's attacking."
///
/// Covers:
/// - Identity (Artifact + Creature, Myr + Construct subtypes, {7}, 4/7).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - ETB trigger (CR 603.6a): creates four 1/1 colorless Myr artifact-creature
///   tokens under the controller.
/// - Attack trigger (CR 508.1f): taps the controller's untapped Myr, pumps the
///   Battlesphere +X/+0 (CR 613.1g) and deals X damage to the attacked player
///   or planeswalker (CR 119 / CR 306.7).
/// </summary>
[Trait("Color", "C")]
public class MyrBattlesphereFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static TriggeredAbility GetEtbTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CardMovedEvent>);

    private static TriggeredAbility GetAttackTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CreatureAttacksEvent>);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void MyrBattlesphere_Identity()
    {
        var c = MyrBattlesphereFactory.Create(_alice);

        c.Name.Should().Be("Myr Battlesphere");
        c.ManaCost.Should().Be("{7}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Artifact).Should().BeTrue("CR 301.1 — Artifact Creature.");
        c.HasSubtype(CardSubtype.Myr).Should().BeTrue();
        c.HasSubtype(CardSubtype.Construct).Should().BeTrue();
        c.BasePower.Should().Be(4);
        c.BaseToughness.Should().Be(7);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void MyrBattlesphere_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Myr Battlesphere", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Myr Battlesphere");
        c.HasSubtype(CardSubtype.Myr).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // ETB trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void MyrBattlesphere_EtbTrigger_MatchesSelfOnly()
    {
        var c = MyrBattlesphereFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);
        var trigger = GetEtbTrigger(c);

        trigger.IsTriggered(new CardMovedEvent(c, ZoneType.Hand, ZoneType.Battlefield))
            .Should().BeTrue("CR 603.6a — this creature entering triggers its own ETB.");

        var other = new Creature("Bear", "{1}{G}", 2, 2);
        other.SetOwner(_alice);
        other.SetController(_alice);
        trigger.IsTriggered(new CardMovedEvent(other, ZoneType.Hand, ZoneType.Battlefield))
            .Should().BeFalse("only Myr Battlesphere's own ETB fires this trigger.");
    }

    [Fact]
    public void MyrBattlesphere_EtbResolution_CreatesFourColorlessMyrArtifactTokens()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);

        var sphere = MyrBattlesphereFactory.Create(_alice, triggers: null, zoneService: zones);
        _alice.Zones.Battlefield.AddCard(sphere);
        sphere.SetZone(ZoneType.Battlefield);

        var trigger = GetEtbTrigger(sphere);
        foreach (var e in trigger.Effects) e.Execute();

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(t => t.IsToken && t.HasSubtype(CardSubtype.Myr))
            .ToList();

        tokens.Should().HaveCount(4, "the ETB creates four 1/1 Myr tokens (CR 111).");
        foreach (var token in tokens)
        {
            token.BasePower.Should().Be(1);
            token.BaseToughness.Should().Be(1);
            token.HasType(CardType.Artifact).Should().BeTrue(
                "CR 111.4 — '1/1 colorless Myr artifact creature token'.");
            token.HasType(CardType.Creature).Should().BeTrue();
            token.Controller.Should().BeSameAs(_alice);
            CardColors.GetColors(token).Should().BeEmpty(
                "CR 111.4 — the Myr tokens are colorless.");
        }
    }

    // -----------------------------------------------------------------------
    // Attack trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void MyrBattlesphere_AttackTrigger_MatchesSelfOnly()
    {
        var c = MyrBattlesphereFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);
        var trigger = GetAttackTrigger(c);

        trigger.IsTriggered(new CreatureAttacksEvent(c, _bob)).Should().BeTrue(
            "CR 508.1f — 'whenever this creature attacks' self-match.");

        var other = new Creature("Bear", "{1}{G}", 2, 2);
        other.SetOwner(_alice);
        other.SetController(_alice);
        trigger.IsTriggered(new CreatureAttacksEvent(other, _bob)).Should().BeFalse(
            "the attack trigger only fires for Myr Battlesphere itself.");
    }

    [Fact]
    public void MyrBattlesphere_AttackTrigger_TapsMyr_PumpsAndDamagesDefendingPlayer()
    {
        var sphere = MyrBattlesphereFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sphere);
        sphere.SetZone(ZoneType.Battlefield);
        sphere.ActiveEffects = new ContinuousEffectsService();

        // Two untapped Myr tokens the controller controls (X should be 2).
        var myr1 = MakeMyr();
        var myr2 = MakeMyr();
        _alice.Zones.Battlefield.AddCard(myr1);
        _alice.Zones.Battlefield.AddCard(myr2);

        // Trigger fires for the Battlesphere attacking Bob.
        var trigger = GetAttackTrigger(sphere);
        trigger.IsTriggered(new CreatureAttacksEvent(sphere, _bob)).Should().BeTrue();
        foreach (var e in trigger.Effects) e.Execute();

        myr1.IsTapped.Should().BeTrue("X untapped Myr are tapped as the cost.");
        myr2.IsTapped.Should().BeTrue();

        sphere.GetPower().Should().Be(6, "CR 613.1g — +X/+0 with X=2 (4 -> 6).");
        sphere.GetToughness().Should().Be(7, "+X/+0 leaves toughness unchanged.");

        _bob.LifeTotal.Should().Be(18, "CR 119 — X=2 damage to the attacked player.");
    }

    [Fact]
    public void MyrBattlesphere_AttackTrigger_NoUntappedMyr_IsNoOp()
    {
        var sphere = MyrBattlesphereFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sphere);
        sphere.SetZone(ZoneType.Battlefield);

        var trigger = GetAttackTrigger(sphere);
        trigger.IsTriggered(new CreatureAttacksEvent(sphere, _bob)).Should().BeTrue();

        var act = () => { foreach (var e in trigger.Effects) e.Execute(); };
        act.Should().NotThrow();
        _bob.LifeTotal.Should().Be(20, "X=0 with no untapped Myr — no damage.");
    }

    private Creature MakeMyr()
    {
        var m = new Creature("Myr", "", 1, 1, subtypes: new[] { CardSubtype.Myr });
        m.AddCardType(CardType.Artifact);
        m.SetOwner(_alice);
        m.SetController(_alice);
        m.SetZone(ZoneType.Battlefield);
        return m;
    }
}
