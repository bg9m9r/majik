using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="RuneScarredDemonFactory"/> ({5}{B}{B}, 6/6
/// Creature — Demon).
///
/// "Flying
///  When this creature enters, search your library for a card, put it into
///  your hand, then shuffle." (CR 603.1 / 701.19a / 701.20a / 702.9)
///
/// Coverage (UNIQUE behaviour only — dispatch + well-formedness are covered
/// for every implemented card by CardFactoryContractTests):
///  - Identity: exact P/T, mana cost, subtype.
///  - Flying keyword marker (CR 702.9).
///  - ETB tutor pulls ANY card (unfiltered) from library → hand.
///  - ETB tutor: non-creature card is eligible (proves the pick is
///    unfiltered, unlike the creature-only recruiters).
///  - ETB tutor: empty library → no-op (CR 701.19a).
/// </summary>
[Trait("Color", "B")]
public class RuneScarredDemonTests
{
    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void RuneScarredDemon_Identity()
    {
        var alice = new Player("Alice", 20);
        var c = RuneScarredDemonFactory.Create(alice);

        c.HasType(CardType.Creature).Should().BeTrue();
        c.Power.Should().Be(6);
        c.Toughness.Should().Be(6);
        c.HasSubtype(CardSubtype.Demon).Should().BeTrue();
        c.ManaCost.Should().Be("{5}{B}{B}");
    }

    [Fact]
    public void RuneScarredDemon_HasFlying()
    {
        var alice = new Player("Alice", 20);
        var c = RuneScarredDemonFactory.Create(alice);

        // CR 702.9 — Flying printed as a KeywordAbility marker via the JSON
        // keyword line.
        c.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Flying");
    }

    // -----------------------------------------------------------------------
    // ETB tutor — unfiltered: any card is eligible
    // -----------------------------------------------------------------------

    [Fact]
    public void RuneScarredDemon_EtbTrigger_PullsAnyCard_FromLibraryToHand()
    {
        var alice = new Player("Alice", 20);

        var bear = new Creature("Grizzly Bears", "{1}{G}", power: 2, toughness: 2);
        bear.SetOwner(alice);
        alice.Zones.Library.AddCard(bear);
        bear.SetZone(ZoneType.Library);

        var demon = RuneScarredDemonFactory.Create(alice);
        var etb = demon.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        bear.Zone.Should().Be(ZoneType.Hand,
            "ETB tutor pulled the (only) library card to hand");
        alice.Zones.Hand.GetCards().Should().Contain(bear);
        alice.Zones.Library.GetCards().Should().NotContain(bear);
    }

    [Fact]
    public void RuneScarredDemon_EtbTrigger_PullsNonCreatureCard_FromLibraryToHand()
    {
        // Unlike the creature-only recruiters, Rune-Scarred Demon's search is
        // unfiltered — a Sorcery is a perfectly legal pick.
        var alice = new Player("Alice", 20);

        var wrath = new Sorcery("Wrath of God", "{2}{W}{W}");
        wrath.SetOwner(alice);
        alice.Zones.Library.AddCard(wrath);
        wrath.SetZone(ZoneType.Library);

        var demon = RuneScarredDemonFactory.Create(alice);
        var etb = demon.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        wrath.Zone.Should().Be(ZoneType.Hand,
            "the search is unfiltered — any card type is eligible");
        alice.Zones.Hand.GetCards().Should().Contain(wrath);
    }

    // -----------------------------------------------------------------------
    // ETB tutor — empty library (CR 701.19a failure to find = no-op)
    // -----------------------------------------------------------------------

    [Fact]
    public void RuneScarredDemon_EtbTrigger_EmptyLibrary_IsNoOp()
    {
        var alice = new Player("Alice", 20);

        var demon = RuneScarredDemonFactory.Create(alice);
        var etb = demon.Abilities.OfType<TriggeredAbility>().Single();

        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };

        act.Should().NotThrow();
        alice.Zones.Hand.GetCards().Should().BeEmpty();
        alice.Zones.Library.GetCards().Should().BeEmpty();
    }
}
