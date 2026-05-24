using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="RelicOfProgenitusFactory"/> — Artifact {1} with two
/// activated abilities:
///   "{T}: Target player exiles a card from their graveyard."
///   "{1}, Exile Relic of Progenitus: Exile all cards from all graveyards.
///    Draw a card."
///
/// Covers:
/// - Card identity (Artifact, {1}, owner / controller).
/// - NamedCardFactory dispatch.
/// - Ability shape: two ActivatedAbilitys with the correct costs and targets.
/// - Tap-ability resolution: exiles one card from target player's graveyard.
/// - Sweep-ability resolution: exiles ALL graveyards (multi-player via
///   allPlayersResolver) and the caster draws a card.
/// - Sweep-ability single-arg path: exiles only the controller's graveyard.
/// </summary>
public class RelicOfProgenitusTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void RelicOfProgenitus_IsArtifact_WithOneManaCost()
    {
        var relic = RelicOfProgenitusFactory.Create(_alice);

        relic.HasType(CardType.Artifact).Should().BeTrue();
        relic.Name.Should().Be("Relic of Progenitus");
        relic.ManaCost.Should().Be("{1}");
        relic.Owner.Should().BeSameAs(_alice);
        relic.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_RelicOfProgenitus()
    {
        var card = NamedCardFactory.Create("Relic of Progenitus", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Relic of Progenitus");
        card.HasType(CardType.Artifact).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void RelicOfProgenitus_HasTwoActivatedAbilities()
    {
        var relic = RelicOfProgenitusFactory.Create(_alice);

        relic.Abilities.OfType<ActivatedAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void TapAbility_HasTapCost_AndOnePlayerTarget()
    {
        var relic = RelicOfProgenitusFactory.Create(_alice);

        var tapAbility = relic.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 1);

        tapAbility.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap,
                "the tap mode costs {T}");

        tapAbility.TargetRequests[0].MinTargets.Should().Be(1);
        tapAbility.TargetRequests[0].MaxTargets.Should().Be(1);
        tapAbility.TargetRequests[0].Description.Should().Contain("player");
    }

    [Fact]
    public void SweepAbility_Has1Generic_AndNoTargets()
    {
        var relic = RelicOfProgenitusFactory.Create(_alice);

        var sweepAbility = relic.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 0);

        sweepAbility.Costs.OfType<ManaCostCost>()
            .Should().ContainSingle(c => c.Description.Contains("1"),
                "the sweep mode costs {1}");
    }

    // -----------------------------------------------------------------------
    // {T}: target player exiles a card from their graveyard
    // -----------------------------------------------------------------------

    [Fact]
    public void TapAbility_ExilesOneCard_FromTargetPlayersGraveyard()
    {
        // Bob has two cards in graveyard. Alice activates {T} targeting Bob.
        var card1 = new Card("Dead Spell 1", "{1}");
        var card2 = new Card("Dead Spell 2", "{2}");
        _bob.Zones.Graveyard.AddCard(card1);
        card1.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(card2);
        card2.SetZone(ZoneType.Graveyard);

        var relic = RelicOfProgenitusFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(relic);
        relic.SetZone(ZoneType.Battlefield);

        var tapAbility = relic.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 1);

        tapAbility.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        tapAbility.Resolve();

        // Exactly one card was exiled from Bob's graveyard.
        _bob.Zones.Graveyard.GetCards().Should().HaveCount(1,
            "only the first graveyard card is exiled (v1 auto-pick)");
        _bob.Zones.Exile.GetCards().Should().HaveCount(1);
        _bob.Zones.Exile.GetCards().Should().Contain(card1);
        card1.Zone.Should().Be(ZoneType.Exile);
    }

    [Fact]
    public void TapAbility_EmptyGraveyard_IsNoOp()
    {
        // Target player's graveyard is empty — activation should be a no-op.
        var relic = RelicOfProgenitusFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(relic);
        relic.SetZone(ZoneType.Battlefield);

        var tapAbility = relic.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 1);

        tapAbility.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        tapAbility.Resolve(); // Should not throw

        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
        _bob.Zones.Exile.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // {1}, Exile Relic: exile all graveyards + draw
    // -----------------------------------------------------------------------

    [Fact]
    public void SweepAbility_ExilesAllGraveyards_AndCasterDraws()
    {
        // Alice has 1 card in graveyard, Bob has 2. Both should be exiled.
        var aliceCard = new Card("Alice's Spell", "{1}");
        _alice.Zones.Graveyard.AddCard(aliceCard);
        aliceCard.SetZone(ZoneType.Graveyard);

        var bobCard1 = new Card("Bob's Spell 1", "{1}");
        var bobCard2 = new Card("Bob's Spell 2", "{2}");
        _bob.Zones.Graveyard.AddCard(bobCard1);
        bobCard1.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bobCard2);
        bobCard2.SetZone(ZoneType.Graveyard);

        // Alice has a card in library to draw.
        var topCard = new Card("Top of Library", "");
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var players = new List<Player> { _alice, _bob };
        var relic = RelicOfProgenitusFactory.Create(_alice, () => players);
        _alice.Zones.Battlefield.AddCard(relic);
        relic.SetZone(ZoneType.Battlefield);

        var sweepAbility = relic.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 0);

        sweepAbility.Resolve();

        // All graveyard cards are now in Exile.
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty(
            "Alice's graveyard is swept clean by the second ability");
        _alice.Zones.Exile.GetCards().Should().Contain(aliceCard);
        aliceCard.Zone.Should().Be(ZoneType.Exile);

        _bob.Zones.Graveyard.GetCards().Should().BeEmpty(
            "Bob's graveyard is swept clean by the second ability");
        _bob.Zones.Exile.GetCards().Should().Contain(bobCard1);
        _bob.Zones.Exile.GetCards().Should().Contain(bobCard2);

        // Relic itself is exiled as cost.
        relic.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(relic);

        // Caster draws a card.
        _alice.Zones.Hand.GetCards().Should().Contain(topCard);
        topCard.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void SweepAbility_SingleArgPath_SweeepsOnlyControllerGraveyard()
    {
        // Without allPlayersResolver, only the controller's graveyard is swept.
        var aliceCard = new Card("Alice's Spell", "{1}");
        _alice.Zones.Graveyard.AddCard(aliceCard);
        aliceCard.SetZone(ZoneType.Graveyard);

        var bobCard = new Card("Bob's Spell", "{1}");
        _bob.Zones.Graveyard.AddCard(bobCard);
        bobCard.SetZone(ZoneType.Graveyard);

        var topCard = new Card("Top of Library", "");
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var relic = RelicOfProgenitusFactory.Create(_alice); // no allPlayersResolver
        _alice.Zones.Battlefield.AddCard(relic);
        relic.SetZone(ZoneType.Battlefield);

        var sweepAbility = relic.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 0);

        sweepAbility.Resolve();

        // Alice's graveyard is swept.
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        _alice.Zones.Exile.GetCards().Should().Contain(aliceCard);

        // Bob's graveyard is untouched (no resolver supplied).
        _bob.Zones.Graveyard.GetCards().Should().Contain(bobCard,
            "Bob's graveyard is not swept without an allPlayersResolver");

        // Caster still draws.
        _alice.Zones.Hand.GetCards().Should().Contain(topCard);
    }
}
