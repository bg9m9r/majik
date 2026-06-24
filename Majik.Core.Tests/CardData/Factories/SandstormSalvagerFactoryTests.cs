using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="SandstormSalvagerFactory"/> (The Brothers' War,
/// {2}{G}). Creature — Human Artificer 1/1. Oracle text (verified against
/// Scryfall):
///   "When this creature enters, create a 3/3 colorless Golem artifact
///    creature token.
///    {2}, {T}: Put a +1/+1 counter on each creature token you control. They
///    gain trample until end of turn."
///
/// Covers ONLY the card's unique behaviour (one non-vanilla Identity assert for
/// exact mana cost / P-T / subtypes; the ETB Golem token; the {2},{T}
/// token-pump activated ability). Dispatch + well-formedness are asserted for
/// every implemented card automatically by CardFactoryContractTests, so no
/// dispatch test is added here.
/// </summary>
[Trait("Color", "G")]
public class SandstormSalvagerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void SandstormSalvager_Identity()
    {
        var card = SandstormSalvagerFactory.Create(_alice);

        card.Name.Should().Be("Sandstorm Salvager");
        card.ManaCost.Should().Be("{2}{G}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Artificer).Should().BeTrue();
        card.BasePower.Should().Be(1);
        card.BaseToughness.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // ETB — create a 3/3 colourless Golem artifact creature token.
    // -----------------------------------------------------------------------

    [Fact]
    public void CreateGolemToken_Builds_3_3_Colourless_Artifact_Golem()
    {
        var golem = SandstormSalvagerFactory.CreateGolemToken(_alice);

        golem.Name.Should().Be("Golem");
        golem.Power.Should().Be(3);
        golem.Toughness.Should().Be(3);
        golem.IsToken.Should().BeTrue();
        golem.HasType(CardType.Creature).Should().BeTrue();
        golem.HasType(CardType.Artifact).Should().BeTrue(
            "the printed token is a 3/3 colorless Golem artifact creature token");
        golem.HasSubtype(CardSubtype.Golem).Should().BeTrue();
        golem.Owner.Should().BeSameAs(_alice);
        golem.Controller.Should().BeSameAs(_alice);
        golem.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void SandstormSalvager_EtbEffect_CreatesGolemUnderController()
    {
        var salvager = SandstormSalvagerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(salvager);
        salvager.SetZone(ZoneType.Battlefield);

        var etb = salvager.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in etb.Effects) e.Execute();

        var golems = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.Name == "Golem")
            .ToList();

        golems.Should().HaveCount(1, "the ETB effect creates one Golem token");
        golems[0].HasType(CardType.Artifact).Should().BeTrue();
        golems[0].IsToken.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // {2}, {T}: Put a +1/+1 counter on each creature token you control. They
    // gain trample until end of turn.
    // -----------------------------------------------------------------------

    [Fact]
    public void SandstormSalvager_HasManaPlusTapPumpAbility()
    {
        var card = SandstormSalvagerFactory.Create(_alice);

        var activated = card.Abilities.OfType<ActivatedAbility>().Single();
        activated.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the {2} mana portion of the cost is present");
        // {T} symbol cost is present alongside the mana cost — two costs total.
        activated.Costs.Should().HaveCount(2,
            "the ability costs {2} and {T}");
    }

    [Fact]
    public void PumpAbility_AddsCounterAndGrantsTrample_ToEachControlledToken()
    {
        var continuous = new ContinuousEffectsService();
        var salvager = SandstormSalvagerFactory.Create(
            _alice, zones: null, triggers: null, continuousEffects: continuous);
        _alice.Zones.Battlefield.AddCard(salvager);
        salvager.SetZone(ZoneType.Battlefield);

        // Two controlled creature tokens.
        var golemA = SandstormSalvagerFactory.CreateGolemToken(_alice);
        var golemB = SandstormSalvagerFactory.CreateGolemToken(_alice);

        // A non-token creature the controller controls must NOT be affected.
        var nonToken = BladeSplicerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(nonToken);
        nonToken.SetZone(ZoneType.Battlefield);

        var activated = salvager.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in activated.Effects) e.Execute();

        golemA.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "each creature token you control gets a +1/+1 counter");
        golemB.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
        nonToken.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "non-token creatures are not affected");

        continuous.Compute(golemA).Keywords.Should().Contain("Trample",
            "controlled tokens gain trample until end of turn");
        continuous.Compute(golemB).Keywords.Should().Contain("Trample");
        continuous.Compute(nonToken).Keywords.Should().NotContain("Trample");
    }
}
