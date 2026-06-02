using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for The Wandering Emperor (Kamigawa: Neon Dynasty, {2}{W}{W}).
///
/// Covers:
///   - Card identity (Legendary Planeswalker, starting loyalty 3, mana cost
///     {2}{W}{W}, Flash keyword marker), materialised from the embedded JSON.
///   - +1: put a +1/+1 counter on up to one target creature; it gains first
///     strike (and the "up to one"/no-target no-op).
///   - −1: create a 2/2 white Samurai token with vigilance.
///   - −2: exile target tapped creature, gain 2 life (and the filter that
///     skips untapped creatures / non-creatures).
///   - NamedCardFactory dispatch.
/// </summary>
[Trait("Color", "W")]
public class TheWanderingEmperorFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Emperor_IsLegendaryPlaneswalker_3Loyalty_AtCost2WW_WithFlash()
    {
        var emperor = TheWanderingEmperorFactory.Create(_alice);

        emperor.Name.Should().Be("The Wandering Emperor");
        emperor.ManaCost.Should().Be("{2}{W}{W}");
        emperor.HasType(CardType.Planeswalker).Should().BeTrue();
        emperor.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        emperor.Loyalty.Should().Be(3);
        emperor.StartingLoyalty.Should().Be(3);
        emperor.Owner.Should().BeSameAs(_alice);
        emperor.Controller.Should().BeSameAs(_alice);
        emperor.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Flash", "The Wandering Emperor has Flash (CR 702.8)");
    }

    [Fact]
    public void Emperor_HasThreeLoyaltyAbilities_Plus1_Minus1_Minus2()
    {
        var emperor = TheWanderingEmperorFactory.Create(_alice);

        var loyalty = emperor.Abilities.OfType<LoyaltyAbility>().ToList();
        loyalty.Should().HaveCount(3);
        loyalty.Select(a => a.LoyaltyChange)
            .Should().BeEquivalentTo(new[] { +1, -1, -2 });
    }
    // -----------------------------------------------------------------------
    // +1: +1/+1 counter + first strike on up to one target creature
    // -----------------------------------------------------------------------

    [Fact]
    public void Plus1_PutsCounterAndGrantsFirstStrike_OnTargetCreature_AndAddsLoyalty()
    {
        var creature = new Creature("Loyal Retainer", "{1}{W}", 1, 1);
        creature.SetOwner(_alice); creature.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(creature);
        creature.SetZone(ZoneType.Battlefield);

        var emperor = TheWanderingEmperorFactory.Create(
            _alice,
            targetResolver: () => new Permanent[] { creature },
            zoneService: null);

        emperor.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == +1).Activate();

        emperor.Loyalty.Should().Be(4, "3 + 1 = 4");
        creature.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
        creature.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "First Strike", "It gains first strike (CR 702.7)");
    }

    [Fact]
    public void Plus1_NoCreatureTarget_NoOps_ButStillAddsLoyalty()
    {
        // "up to one target creature" — zero is a legal choice; with no
        // resolver the clause no-ops but the loyalty change still applies.
        var emperor = TheWanderingEmperorFactory.Create(_alice);

        emperor.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == +1).Activate();

        emperor.Loyalty.Should().Be(4, "loyalty change still applies (CR 606.3)");
    }

    // -----------------------------------------------------------------------
    // −1: create a 2/2 white Samurai token with vigilance
    // -----------------------------------------------------------------------

    [Fact]
    public void Minus1_CreatesSamuraiToken_2_2_White_Vigilance()
    {
        var emperor = TheWanderingEmperorFactory.Create(_alice);

        emperor.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -1).Activate();

        emperor.Loyalty.Should().Be(2, "3 - 1 = 2");

        var token = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .SingleOrDefault(c => c.IsToken);
        token.Should().NotBeNull("the −1 mints a Samurai token");
        token!.Name.Should().Be("Samurai");
        token.GetPower().Should().Be(2);
        token.GetToughness().Should().Be(2);
        token.HasSubtype(CardSubtype.Samurai).Should().BeTrue();
        token.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Vigilance", "the token has vigilance (CR 702.20)");
    }

    // -----------------------------------------------------------------------
    // −2: exile target tapped creature, gain 2 life
    // -----------------------------------------------------------------------

    [Fact]
    public void Minus2_ExilesTappedCreature_AndGains2Life()
    {
        var tapped = new Creature("Tapped Ronin", "{1}{R}", 2, 2);
        tapped.SetOwner(_bob); tapped.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(tapped);
        tapped.SetZone(ZoneType.Battlefield);
        Fx.Tap(tapped);

        var emperor = TheWanderingEmperorFactory.Create(
            _alice,
            targetResolver: () => new Permanent[] { tapped },
            zoneService: null);

        emperor.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -2).Activate();

        emperor.Loyalty.Should().Be(1, "3 - 2 = 1");
        _bob.Zones.Exile.GetCards().Should().Contain(tapped);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(tapped);
        tapped.Zone.Should().Be(ZoneType.Exile);
        _alice.LifeTotal.Should().Be(22, "gained 2 life (CR 119.3)");
    }

    [Fact]
    public void Minus2_SkipsUntappedCreature_AndNonCreature_ButStillGainsLife()
    {
        var untapped = new Creature("Untapped Bear", "{1}{G}", 2, 2);
        untapped.SetOwner(_bob); untapped.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(untapped);
        untapped.SetZone(ZoneType.Battlefield);

        var artifact = new Artifact("Idle Relic", "{2}");
        artifact.SetOwner(_bob); artifact.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(artifact);
        artifact.SetZone(ZoneType.Battlefield);
        Fx.Tap(artifact); // tapped, but not a creature → not a legal target

        var emperor = TheWanderingEmperorFactory.Create(
            _alice,
            targetResolver: () => new Permanent[] { untapped, artifact },
            zoneService: null);

        emperor.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -2).Activate();

        _bob.Zones.Exile.GetCards().Should().BeEmpty(
            "an untapped creature and a tapped non-creature are not legal −2 targets");
        _bob.Zones.Battlefield.GetCards().Should().Contain(untapped);
        _bob.Zones.Battlefield.GetCards().Should().Contain(artifact);
        _alice.LifeTotal.Should().Be(22, "the life-gain clause is not gated on the exile");
    }
}
