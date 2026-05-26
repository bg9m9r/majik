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
/// Unit tests for <see cref="ImperialRecruiterFactory"/>.
///
/// Covers:
/// - Identity (name, type, P/T, subtypes, mana cost, owner/controller).
/// - NamedCardFactory dispatch.
/// - ETB tutor: pulls a power-2 creature from library → hand.
/// - ETB tutor: pulls a power-1 creature from library → hand.
/// - ETB tutor: only high-power creatures → no-op.
/// - ETB tutor: no creatures at all → no-op.
/// </summary>
public class ImperialRecruiterTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void ImperialRecruiter_Identity()
    {
        var c = ImperialRecruiterFactory.Create(_alice);

        c.Name.Should().Be("Imperial Recruiter");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(1);
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Advisor).Should().BeTrue();
        c.ManaCost.Should().Be("{2}{R}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ImperialRecruiter_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Imperial Recruiter", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Imperial Recruiter");
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Advisor).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // ETB tutor — happy paths
    // -----------------------------------------------------------------------

    [Fact]
    public void ImperialRecruiter_EtbTrigger_PullsPowerTwoCreature_FromLibraryToHand()
    {
        var alice = new Player("Alice", 20);

        var bear = new Creature("Grizzly Bears", "{1}{G}", power: 2, toughness: 2);
        bear.SetOwner(alice);
        alice.Zones.Library.AddCard(bear);
        bear.SetZone(ZoneType.Library);

        var recruiter = ImperialRecruiterFactory.Create(alice);
        var etb = recruiter.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        bear.Zone.Should().Be(ZoneType.Hand,
            "ETB tutor pulled the power-2 creature to hand");
        alice.Zones.Hand.GetCards().Should().Contain(bear);
    }

    [Fact]
    public void ImperialRecruiter_EtbTrigger_PullsPowerOneCreature_FromLibraryToHand()
    {
        var alice = new Player("Alice", 20);

        var weenie = new Creature("Savannah Lions", "{W}", power: 2, toughness: 1);
        weenie.SetOwner(alice);
        alice.Zones.Library.AddCard(weenie);
        weenie.SetZone(ZoneType.Library);

        var recruiter = ImperialRecruiterFactory.Create(alice);
        var etb = recruiter.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        weenie.Zone.Should().Be(ZoneType.Hand);
        alice.Zones.Hand.GetCards().Should().Contain(weenie);
    }

    // -----------------------------------------------------------------------
    // ETB tutor — no eligible target
    // -----------------------------------------------------------------------

    [Fact]
    public void ImperialRecruiter_EtbTrigger_OnlyHighPowerCreatures_IsNoOp()
    {
        var alice = new Player("Alice", 20);

        var fattie = new Creature("Hill Giant", "{3}{R}", power: 3, toughness: 3);
        fattie.SetOwner(alice);
        alice.Zones.Library.AddCard(fattie);
        fattie.SetZone(ZoneType.Library);

        var recruiter = ImperialRecruiterFactory.Create(alice);
        var etb = recruiter.Abilities.OfType<TriggeredAbility>().Single();

        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };

        act.Should().NotThrow();
        fattie.Zone.Should().Be(ZoneType.Library,
            "power-3 creature is filtered out by power ≤ 2 gate");
        alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void ImperialRecruiter_EtbTrigger_NoCreaturesInLibrary_IsNoOp()
    {
        var alice = new Player("Alice", 20);

        var sorcery = new Sorcery("Wrath of God", "{2}{W}{W}");
        sorcery.SetOwner(alice);
        alice.Zones.Library.AddCard(sorcery);
        sorcery.SetZone(ZoneType.Library);

        var recruiter = ImperialRecruiterFactory.Create(alice);
        var etb = recruiter.Abilities.OfType<TriggeredAbility>().Single();

        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };

        act.Should().NotThrow();
        sorcery.Zone.Should().Be(ZoneType.Library);
        alice.Zones.Hand.GetCards().Should().BeEmpty();
    }
}
