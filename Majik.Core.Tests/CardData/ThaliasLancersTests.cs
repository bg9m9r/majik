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
/// Unit tests for <see cref="ThaliasLancersFactory"/>.
///
/// Covers:
/// - Identity (name, mana cost, P/T, Human + Knight subtypes,
///   owner/controller).
/// - NamedCardFactory dispatch.
/// - First strike keyword marker (read by combat sequencer via
///   CombatAbilities.HasFirstStrike).
/// - ETB tutor: pulls the first legendary card from library to hand;
///   library is shuffled afterwards.
/// - ETB tutor: leaves non-legendary cards in the library
///   (predicate-filtered).
/// - ETB tutor: no legendary in library → no-op + shuffle still fires
///   (CR 701.19a decline / CR 701.20a search-completes-shuffle).
/// </summary>
public class ThaliasLancersTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void ThaliasLancers_Identity()
    {
        var c = ThaliasLancersFactory.Create(_alice);

        c.Name.Should().Be("Thalia's Lancers");
        c.ManaCost.Should().Be("{3}{W}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.Power.Should().Be(4);
        c.Toughness.Should().Be(4);
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Knight).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ThaliasLancers_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Thalia's Lancers", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Thalia's Lancers");
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Knight).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // First strike
    // -----------------------------------------------------------------------

    [Fact]
    public void ThaliasLancers_HasFirstStrikeKeyword()
    {
        var c = ThaliasLancersFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "First strike",
                "combat damage sequencer reads first strike off this marker");
    }

    // -----------------------------------------------------------------------
    // ETB tutor — happy path
    // -----------------------------------------------------------------------

    [Fact]
    public void ThaliasLancers_EtbTrigger_PullsLegendaryCardFromLibraryToHand()
    {
        var alice = new Player("Alice", 20);

        // Seed library: a non-legendary card first (bait), then a
        // legendary, then more bait — verify the predicate filters
        // correctly + the deterministic first-legendary picker wins.
        var bait1 = new Card("Random Card A", "");
        bait1.SetOwner(alice);
        alice.Zones.Library.AddCard(bait1);
        bait1.SetZone(ZoneType.Library);

        var legendaryCreature = new Creature("Legend Bear", "1G", 2, 2,
            subtypes: new[] { CardSubtype.Bear },
            supertypes: new[] { CardSupertype.Legendary });
        legendaryCreature.SetOwner(alice);
        alice.Zones.Library.AddCard(legendaryCreature);
        legendaryCreature.SetZone(ZoneType.Library);

        var bait2 = new Card("Random Card B", "");
        bait2.SetOwner(alice);
        alice.Zones.Library.AddCard(bait2);
        bait2.SetZone(ZoneType.Library);

        var lancers = ThaliasLancersFactory.Create(alice);
        var etb = lancers.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        legendaryCreature.Zone.Should().Be(ZoneType.Hand,
            "ETB tutor pulled the first legendary card to hand (CR 701.19a)");
        alice.Zones.Hand.GetCards().Should().Contain(legendaryCreature);
        alice.Zones.Library.GetCards().Should().NotContain(legendaryCreature);
        bait1.Zone.Should().Be(ZoneType.Library,
            "non-legendary cards stay in the library");
        bait2.Zone.Should().Be(ZoneType.Library);
    }

    // -----------------------------------------------------------------------
    // ETB tutor — legendary non-creature card (e.g., legendary artifact)
    // also qualifies (printed text is "a legendary card", not "creature")
    // -----------------------------------------------------------------------

    [Fact]
    public void ThaliasLancers_EtbTrigger_PullsLegendaryArtifact()
    {
        var alice = new Player("Alice", 20);

        var legendaryArtifact = new Artifact(
            "Legend Relic", "3",
            subtypes: System.Array.Empty<CardSubtype>(),
            supertypes: new[] { CardSupertype.Legendary });
        legendaryArtifact.SetOwner(alice);
        alice.Zones.Library.AddCard(legendaryArtifact);
        legendaryArtifact.SetZone(ZoneType.Library);

        var lancers = ThaliasLancersFactory.Create(alice);
        var etb = lancers.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        legendaryArtifact.Zone.Should().Be(ZoneType.Hand,
            "ANY legendary card type qualifies — Lancers tutors legendary " +
            "artifacts / planeswalkers / lands per oracle text");
    }

    // -----------------------------------------------------------------------
    // ETB tutor — no legendary in library
    // -----------------------------------------------------------------------

    [Fact]
    public void ThaliasLancers_EtbTrigger_NoLegendaryInLibrary_IsNoOp()
    {
        var alice = new Player("Alice", 20);

        var unrelated = new Card("Random Card", "");
        unrelated.SetOwner(alice);
        alice.Zones.Library.AddCard(unrelated);
        unrelated.SetZone(ZoneType.Library);

        var lancers = ThaliasLancersFactory.Create(alice);
        var etb = lancers.Abilities.OfType<TriggeredAbility>().Single();

        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };

        act.Should().NotThrow(
            "no legendary card in library = CR 701.19a decline / no-op");
        unrelated.Zone.Should().Be(ZoneType.Library);
        alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // ETB trigger shape — active zone gates on Battlefield
    // -----------------------------------------------------------------------

    [Fact]
    public void ThaliasLancers_EtbTrigger_ActiveOnBattlefieldOnly()
    {
        var c = ThaliasLancersFactory.Create(_alice);
        var etb = c.Abilities.OfType<TriggeredAbility>().Single();

        etb.ActiveZones.Should().Contain(ZoneType.Battlefield,
            "the ETB trigger should be active while on the battlefield");
    }
}
