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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SentinelTotemFactory"/> (Hour of Devastation /
/// reprints).
///
/// Artifact {1}. Oracle text (Scryfall-confirmed):
///   "When this artifact enters, scry 1. (Look at the top card of your library.
///    You may put that card on the bottom.)
///    {T}, Exile this artifact: Exile all graveyards."
///
/// Hybrid card identity:
/// - Name / Artifact type / {1} cost and the ETB "scry 1" trigger come from the
///   embedded JSON definition (<c>sentinel-totem.json</c>) via the standard
///   <c>scry_self</c> path (CR 701.20) — same posture as
///   <see cref="TempleOfDeceitFactory"/>.
/// - The activated graveyard-hate ability
///   <b>{T}, Exile this artifact: Exile all graveyards</b> is hand-built in the
///   factory (no JSON verb exists for "exile all graveyards") — mirrors
///   <see cref="RelicOfProgenitusFactory"/>'s sweep ability (CR 605, exile is
///   not a mana ability; the self-exile zone move is performed by the effect
///   closure since the generic additional-cost pay path is a stub).
///
/// Covers:
/// - Card identity (Artifact, {1}, owner / controller).
/// - NamedCardFactory dispatch.
/// - One ETB triggered ability (scry 1) sourced from JSON.
/// - One activated ability with tap + self-exile cost and NO targets.
/// - Scry-1 ETB fall-back (no agent) puts the peeked card on the bottom.
/// - Sweep resolution exiles ALL graveyards (multi-player) and self-exiles.
/// - Sweep single-arg path sweeps only the controller's graveyard.
/// </summary>
public class SentinelTotemTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void SentinelTotem_IsArtifact_WithOneManaCost()
    {
        var totem = SentinelTotemFactory.Create(_alice);

        totem.HasType(CardType.Artifact).Should().BeTrue();
        totem.HasType(CardType.Creature).Should().BeFalse();
        totem.Name.Should().Be("Sentinel Totem");
        totem.ManaCost.Should().Be("{1}");
        totem.Owner.Should().BeSameAs(_alice);
        totem.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SentinelTotem()
    {
        var card = NamedCardFactory.Create("Sentinel Totem", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Sentinel Totem");
        card.HasType(CardType.Artifact).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void SentinelTotem_HasEtbScryTrigger_AndActivatedSweep()
    {
        var totem = SentinelTotemFactory.Create(_alice);

        totem.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the ETB scry-1 trigger comes from JSON");
        totem.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the graveyard-hate sweep is the one activated ability");
    }

    [Fact]
    public void SentinelTotem_EtbTrigger_IsBattlefieldActive()
    {
        var totem = SentinelTotemFactory.Create(_alice);
        var trigger = totem.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void SweepAbility_HasTapCost_AndNoTargets()
    {
        var totem = SentinelTotemFactory.Create(_alice);

        var sweep = totem.Abilities.OfType<ActivatedAbility>().Single();

        sweep.TargetRequests.Should().BeEmpty(
            "exile all graveyards is untargeted");
        sweep.Costs.OfType<AdditionalCost>()
            .Should().Contain(c => c.CostType == AdditionalCostType.Tap,
                "the ability costs {T}");
    }

    // -----------------------------------------------------------------------
    // ETB: scry 1
    // -----------------------------------------------------------------------

    [Fact]
    public void EtbEffect_ScriesOne_DefaultsTopCardToBottom()
    {
        var alice = new Player("Alice", 20);
        var top = new Card("Top", ""); top.SetOwner(alice);
        var second = new Card("Second", ""); second.SetOwner(alice);
        foreach (var c in new[] { top, second })
        {
            alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var totem = SentinelTotemFactory.Create(alice);
        var etb = totem.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        // No agent registered → fall-back puts the single peeked card (Top)
        // on the bottom; the previously-second card is now on top.
        alice.Zones.Library.GetCards().Should().Equal(new[] { second, top });
        alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void EtbEffect_EmptyLibrary_NoOp()
    {
        var alice = new Player("Alice", 20);

        var totem = SentinelTotemFactory.Create(alice);
        var etb = totem.Abilities.OfType<TriggeredAbility>().Single();
        Action act = () =>
        {
            foreach (var effect in etb.Effects) effect.Execute();
        };

        act.Should().NotThrow();
        alice.Zones.Library.GetCards().Should().BeEmpty();
        alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // {T}, Exile this artifact: Exile all graveyards
    // -----------------------------------------------------------------------

    [Fact]
    public void SweepAbility_ExilesAllGraveyards_AndSelfExiles()
    {
        // Alice has 1 card in graveyard, Bob has 2. All should be exiled.
        var aliceCard = new Card("Alice's Spell", "{1}");
        _alice.Zones.Graveyard.AddCard(aliceCard);
        aliceCard.SetZone(ZoneType.Graveyard);

        var bobCard1 = new Card("Bob's Spell 1", "{1}");
        var bobCard2 = new Card("Bob's Spell 2", "{2}");
        _bob.Zones.Graveyard.AddCard(bobCard1);
        bobCard1.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bobCard2);
        bobCard2.SetZone(ZoneType.Graveyard);

        var players = new List<Player> { _alice, _bob };
        var totem = SentinelTotemFactory.Create(_alice, () => players);
        _alice.Zones.Battlefield.AddCard(totem);
        totem.SetZone(ZoneType.Battlefield);

        var sweep = totem.Abilities.OfType<ActivatedAbility>().Single();
        sweep.Resolve();

        // All graveyard cards are now in Exile.
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        _alice.Zones.Exile.GetCards().Should().Contain(aliceCard);
        aliceCard.Zone.Should().Be(ZoneType.Exile);

        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
        _bob.Zones.Exile.GetCards().Should().Contain(bobCard1);
        _bob.Zones.Exile.GetCards().Should().Contain(bobCard2);

        // Totem itself is exiled as cost.
        totem.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(totem);

        // No draw (unlike Relic of Progenitus).
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void SweepAbility_SingleArgPath_SweepsOnlyControllerGraveyard()
    {
        // Without allPlayersResolver, only the controller's graveyard is swept.
        var aliceCard = new Card("Alice's Spell", "{1}");
        _alice.Zones.Graveyard.AddCard(aliceCard);
        aliceCard.SetZone(ZoneType.Graveyard);

        var bobCard = new Card("Bob's Spell", "{1}");
        _bob.Zones.Graveyard.AddCard(bobCard);
        bobCard.SetZone(ZoneType.Graveyard);

        var totem = SentinelTotemFactory.Create(_alice); // no allPlayersResolver
        _alice.Zones.Battlefield.AddCard(totem);
        totem.SetZone(ZoneType.Battlefield);

        var sweep = totem.Abilities.OfType<ActivatedAbility>().Single();
        sweep.Resolve();

        // Alice's graveyard is swept.
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        _alice.Zones.Exile.GetCards().Should().Contain(aliceCard);

        // Bob's graveyard is untouched (no resolver supplied).
        _bob.Zones.Graveyard.GetCards().Should().Contain(bobCard,
            "Bob's graveyard is not swept without an allPlayersResolver");

        // Totem self-exiled.
        totem.Zone.Should().Be(ZoneType.Exile);
    }
}
