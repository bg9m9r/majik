using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="RecruiterOfTheGuardFactory"/>.
///
/// Covers:
/// - Identity (name, type, P/T, subtypes, mana cost, owner/controller).
/// - NamedCardFactory dispatch.
/// - ETB tutor: pulls a toughness-2 creature from library → hand.
/// - ETB tutor: pulls a toughness-1 creature from library → hand.
/// - ETB tutor: only high-toughness creatures → no-op.
/// - ETB tutor: no creatures at all → no-op.
/// </summary>
public class RecruiterOfTheGuardTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void RecruiterOfTheGuard_Identity()
    {
        var c = RecruiterOfTheGuardFactory.Create(_alice);

        c.Name.Should().Be("Recruiter of the Guard");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(1);
        c.HasSubtype(CardSubtype.Human).Should().BeTrue("Recruiter is a Human");
        c.HasSubtype(CardSubtype.Soldier).Should().BeTrue("Recruiter is a Soldier");
        c.ManaCost.Should().Be("{2}{W}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void RecruiterOfTheGuard_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Recruiter of the Guard", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Recruiter of the Guard");
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // ETB tutor — happy paths
    // -----------------------------------------------------------------------

    [Fact]
    public void RecruiterOfTheGuard_EtbTrigger_PullsToughnessTwoCreature_FromLibraryToHand()
    {
        var alice = new Player("Alice", 20);

        var smallCreature = new Creature("Bear", "{1}{G}", power: 2, toughness: 2);
        smallCreature.SetOwner(alice);
        alice.Zones.Library.AddCard(smallCreature);
        smallCreature.SetZone(ZoneType.Library);

        var recruiter = RecruiterOfTheGuardFactory.Create(alice);
        var etb = recruiter.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        smallCreature.Zone.Should().Be(ZoneType.Hand,
            "ETB tutor pulled the toughness-2 creature to hand (CR 701.19a)");
        alice.Zones.Hand.GetCards().Should().Contain(smallCreature);
    }

    [Fact]
    public void RecruiterOfTheGuard_EtbTrigger_PullsToughnessOneCreature_FromLibraryToHand()
    {
        var alice = new Player("Alice", 20);

        var oneToughness = new Creature("Goblin", "{R}", power: 1, toughness: 1);
        oneToughness.SetOwner(alice);
        alice.Zones.Library.AddCard(oneToughness);
        oneToughness.SetZone(ZoneType.Library);

        var recruiter = RecruiterOfTheGuardFactory.Create(alice);
        var etb = recruiter.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        oneToughness.Zone.Should().Be(ZoneType.Hand);
        alice.Zones.Hand.GetCards().Should().Contain(oneToughness);
    }

    // -----------------------------------------------------------------------
    // ETB tutor — no eligible target
    // -----------------------------------------------------------------------

    [Fact]
    public void RecruiterOfTheGuard_EtbTrigger_OnlyHighToughnessCreatures_IsNoOp()
    {
        var alice = new Player("Alice", 20);

        var fattie = new Creature("Big Beast", "{4}{G}", power: 5, toughness: 5);
        fattie.SetOwner(alice);
        alice.Zones.Library.AddCard(fattie);
        fattie.SetZone(ZoneType.Library);

        var recruiter = RecruiterOfTheGuardFactory.Create(alice);
        var etb = recruiter.Abilities.OfType<TriggeredAbility>().Single();

        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };

        act.Should().NotThrow();
        fattie.Zone.Should().Be(ZoneType.Library,
            "toughness-5 creature is filtered out by toughness ≤ 2 gate");
        alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void RecruiterOfTheGuard_EtbTrigger_NoCreaturesInLibrary_IsNoOp()
    {
        var alice = new Player("Alice", 20);

        var instant = new Instant("Lightning Bolt", "{R}");
        instant.SetOwner(alice);
        alice.Zones.Library.AddCard(instant);
        instant.SetZone(ZoneType.Library);

        var recruiter = RecruiterOfTheGuardFactory.Create(alice);
        var etb = recruiter.Abilities.OfType<TriggeredAbility>().Single();

        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };

        act.Should().NotThrow();
        instant.Zone.Should().Be(ZoneType.Library);
        alice.Zones.Hand.GetCards().Should().BeEmpty();
    }
}
