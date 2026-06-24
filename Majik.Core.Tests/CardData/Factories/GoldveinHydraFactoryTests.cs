using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Goldvein Hydra (Outlaws of Thunder Junction, {X}{G}, Creature —
/// Hydra 0/0). Oracle text (verified against Scryfall):
///   "Vigilance, trample, haste
///    This creature enters with X +1/+1 counters on it.
///    When this creature dies, create a number of tapped Treasure tokens
///    equal to its power."
///
/// Covers ONLY the card's unique behaviour (dispatch + well-formedness are
/// asserted automatically by CardFactoryContractTests):
///   - Identity (name, {X}{G} with HasX, 0/0, Creature — Hydra, the three
///     evergreen keywords).
///   - Exactly one factory-attached trigger (the dies trigger); the ETB-X
///     counters are owned by the EntersWithCountersBinder, not the factory.
///   - Dies trigger: with power 3 (three +1/+1 counters on the 0/0 base),
///     creates 3 TAPPED Treasure tokens (CR 603.6d / 603.10 / 111.10).
///   - Dies trigger: with power 0, creates no tokens.
/// </summary>
[Trait("Color", "G")]
public class GoldveinHydraFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void GoldveinHydra_Identity_HydraXG_0_0_WithEvergreenKeywords()
    {
        var card = GoldveinHydraFactory.Create(_alice);

        card.Name.Should().Be("Goldvein Hydra");
        card.ManaCost.Should().Be("{X}{G}");
        card.ManaCostValue.HasX.Should().BeTrue("printed cost has X (CR 202.3b)");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Hydra).Should().BeTrue();
        card.BasePower.Should().Be(0);
        card.BaseToughness.Should().Be(0);
        card.HasEffectiveKeyword("Vigilance").Should().BeTrue("CR 702.20");
        card.HasEffectiveKeyword("Trample").Should().BeTrue("CR 702.19");
        card.HasEffectiveKeyword("Haste").Should().BeTrue("CR 702.10");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void GoldveinHydra_AttachesDiesTriggerOnly_NoEtbCountersTrigger()
    {
        var card = GoldveinHydraFactory.Create(_alice);

        // CR 614.1d — the ETB-X counters are a binder-registered replacement,
        // NOT a factory-attached trigger. Only the dies-Treasures trigger is
        // factory-attached.
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "only the dies-Treasures trigger is factory-attached; the ETB-X " +
            "counters are owned by the EntersWithCountersBinder");
        card.Abilities.OfType<TriggeredAbility>().Single()
            .Effects.Should().Contain(e => e.Description.Contains("Treasure"),
                "the lone factory trigger is the dies-Treasures trigger");
    }

    [Fact]
    public void GoldveinHydra_DoesNotSelfManageEntersWithCounters()
    {
        // The factory must leave SelfManagesEntersWithCounters false so the
        // EntersWithCountersBinder registers the variable-X replacement on the
        // prod route (setting the flag suppresses the binder → 0 counters).
        var card = GoldveinHydraFactory.Create(_alice);

        card.SelfManagesEntersWithCounters.Should().BeFalse(
            "the binder owns the ETB-X replacement; self-managing suppresses it");
    }

    [Fact]
    public void GoldveinHydra_DiesTrigger_CreatesTappedTreasuresEqualToPower()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();
        var zones = new ZoneService(eventBus: bus, replacements: null);

        var card = GoldveinHydraFactory.Create(_alice, triggers, bus, zones, effects);
        card.ActiveEffects = effects;
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        // Hydra cast for X=3 → enters with 3 +1/+1 counters → power 3
        // (0 base + 3). ActiveEffects is wired so the counters raise power.
        card.Counters.Add(CounterType.PlusOnePlusOne, 3);
        card.Power.Should().Be(3, "0 base + three +1/+1 counters (CR 122 / 613)");

        // Hydra dies: battlefield -> graveyard.
        _alice.Zones.Battlefield.RemoveCard(card);
        _alice.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);

        var dies = card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Effects.Any(e => e.Description.Contains("Treasure")));
        foreach (var e in dies.Effects) e.Execute();

        var treasures = _alice.Zones.Battlefield.GetCards()
            .Where(c => c.Name == "Treasure")
            .ToList();
        treasures.Should().HaveCount(3,
            "one tapped Treasure per point of power (LKI power = 3 → 3 Treasures)");
        treasures.Should().AllSatisfy(t =>
        {
            t.HasSubtype(CardSubtype.Treasure).Should().BeTrue();
            ((Permanent)t).IsTapped.Should().BeTrue("the Treasures enter tapped (CR 701.21a)");
        });
    }

    [Fact]
    public void GoldveinHydra_DiesTrigger_ZeroPower_NoTreasures()
    {
        var card = GoldveinHydraFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        // No counters / pump — the 0/0 dies via SBA with power 0.

        _alice.Zones.Battlefield.RemoveCard(card);
        _alice.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);

        var dies = card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Effects.Any(e => e.Description.Contains("Treasure")));
        foreach (var e in dies.Effects) e.Execute();

        _alice.Zones.Battlefield.GetCards()
            .Where(c => c.Name == "Treasure").Should().BeEmpty(
                "power 0 → zero Treasure tokens");
    }
}
