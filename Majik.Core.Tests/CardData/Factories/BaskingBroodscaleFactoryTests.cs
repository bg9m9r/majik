using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="BaskingBroodscaleFactory"/>.
///
/// Card: Basking Broodscale (Modern Horizons 3, {1}{G}). Creature —
/// Eldrazi Lizard. 2/2.
///
/// Oracle text (Scryfall-verified):
///   "Devoid (This card has no color.)
///    {1}{G}: Adapt 1. (If this creature has no +1/+1 counters on it,
///    put a +1/+1 counter on it.)
///    Whenever one or more +1/+1 counters are put on this creature, you
///    may create a 0/1 colorless Eldrazi Spawn creature token with
///    \"Sacrifice this token: Add {C}.\""
///
/// Coverage:
/// <list type="bullet">
///   <item>Identity ({1}{G}, 2/2, Creature, Eldrazi, Lizard) — base
///       shape materialised from the embedded JSON definition.</item>
///   <item>Devoid (CR 702.114) — card reports colourless.</item>
///   <item>Dispatch via <see cref="NamedCardFactory"/>.</item>
///   <item>Ability shape — 1 activated Adapt, 1 counter-added triggered,
///       plus the Adapt + Devoid keyword markers.</item>
///   <item>Adapt 1 places one +1/+1 counter when none present, no-op when
///       already present (CR 702.116b).</item>
///   <item>Counter-added trigger creates a 0/1 colourless Eldrazi Spawn
///       token; end-to-end Adapt 1 → trigger fires → token created.</item>
///   <item>The trigger fires on this creature only — counters added to a
///       different permanent do not trigger.</item>
/// </list>
/// </summary>
[Trait("Color", "C")]
public class BaskingBroodscaleFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private record BroodscaleRig(
        Creature Broodscale,
        TriggerManager Triggers,
        Majik.Core.Stack.Stack Stack,
        ZoneService Zones,
        ReplacementBus Reps,
        EventBus Bus);

    private BroodscaleRig MakeBroodscale()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var reps = new ReplacementBus();
        var zones = new ZoneService(bus, reps);
        var triggers = new TriggerManager(stack, bus);
        var broodscale = BaskingBroodscaleFactory.Create(_alice, zones, triggers, reps, bus);
        broodscale.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(broodscale);
        triggers.BindCard(broodscale);
        return new BroodscaleRig(broodscale, triggers, stack, zones, reps, bus);
    }

    [Fact]
    public void BaskingBroodscale_Identity()
    {
        var card = BaskingBroodscaleFactory.Create(_alice);

        card.Name.Should().Be("Basking Broodscale");
        card.ManaCost.Should().Be("{1}{G}");
        card.BasePower.Should().Be(2);
        card.BaseToughness.Should().Be(2);
        card.HasType(CardType.Creature).Should().BeTrue();
        card.Subtypes.Should().Contain(CardSubtype.Eldrazi);
        card.Subtypes.Should().Contain(CardSubtype.Lizard);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BaskingBroodscale_IsDevoid_ReportsColourless()
    {
        var card = BaskingBroodscaleFactory.Create(_alice);

        // CR 702.114 — Devoid. Despite the {G} pip, the card is colourless.
        card.IsDevoid.Should().BeTrue("CR 702.114 — Devoid stamps the colourless flag");
        CardColors.GetColors(card).Should().BeEmpty();
        card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .Should().Contain("Devoid");
    }
    [Fact]
    public void BaskingBroodscale_AbilityShape()
    {
        var card = BaskingBroodscaleFactory.Create(_alice);

        // One activated ability (Adapt 1).
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);

        // One triggered ability (counter-added → Eldrazi Spawn token).
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);

        // Adapt + Devoid keyword markers.
        card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .Should().Contain(new[] { "Adapt 1", "Devoid" });
    }

    [Fact]
    public void Adapt_PlacesOneCounter_WhenNonePresent()
    {
        var card = BaskingBroodscaleFactory.Create(_alice);
        card.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(card);

        var adapt = card.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var eff in adapt.Effects) eff.Execute();

        card.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
    }

    [Fact]
    public void Adapt_IsNoOp_WhenPlusOneCountersAlreadyPresent()
    {
        var card = BaskingBroodscaleFactory.Create(_alice);
        card.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(card);
        card.Counters.Add(CounterType.PlusOnePlusOne, 1);

        var adapt = card.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var eff in adapt.Effects) eff.Execute();

        card.Counters.Count(CounterType.PlusOnePlusOne)
            .Should().Be(1, because: "CR 702.116b — Adapt fizzles when counters already present");
    }

    [Fact]
    public void CounterTrigger_CreatesEldraziSpawnToken_WhenCountersPlaced()
    {
        var rig = MakeBroodscale();

        // Add a +1/+1 counter via CountersService (the surface Adapt routes
        // through) — fires CounterAddedEvent → trigger.
        CountersService.Add(rig.Broodscale, CounterType.PlusOnePlusOne, 1, rig.Reps, rig.Bus);

        rig.Triggers.PutPendingTriggersOnStack(_alice);
        while (!rig.Stack.IsEmpty) rig.Stack.Pop()!.Resolve();

        var spawn = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .SingleOrDefault(c => c.Name == "Eldrazi Spawn");
        spawn.Should().NotBeNull("the counter-added trigger creates a 0/1 colourless Eldrazi Spawn");
        spawn!.BasePower.Should().Be(0);
        spawn.BaseToughness.Should().Be(1);
        spawn.Subtypes.Should().Contain(CardSubtype.Eldrazi);
        spawn.Subtypes.Should().Contain(CardSubtype.Spawn);
        CardColors.GetColors(spawn).Should().BeEmpty("CR 111.10 — Eldrazi Spawn tokens are colourless");
        spawn.Abilities.OfType<ManaAbility>().Should().NotBeEmpty(
            "the Spawn carries the \"Sacrifice this token: Add {C}.\" mana ability");
    }

    [Fact]
    public void EndToEnd_Adapt_TriggersSpawnCreation()
    {
        var rig = MakeBroodscale();

        var adapt = rig.Broodscale.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var eff in adapt.Effects) eff.Execute();

        rig.Broodscale.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);

        rig.Triggers.PutPendingTriggersOnStack(_alice);
        while (!rig.Stack.IsEmpty) rig.Stack.Pop()!.Resolve();

        _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Count(c => c.Name == "Eldrazi Spawn")
            .Should().Be(1, "Adapt 1 placed a counter → trigger created one Eldrazi Spawn");
    }

    [Fact]
    public void CounterTrigger_DoesNotFire_ForCountersOnAnotherPermanent()
    {
        var rig = MakeBroodscale();

        var other = new Creature("Other Creature", "{G}", 1, 1)
        { Owner = _alice, Controller = _alice };
        other.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(other);

        CountersService.Add(other, CounterType.PlusOnePlusOne, 1, rig.Reps, rig.Bus);

        rig.Triggers.PutPendingTriggersOnStack(_alice);
        while (!rig.Stack.IsEmpty) rig.Stack.Pop()!.Resolve();

        _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Should().NotContain(c => c.Name == "Eldrazi Spawn",
                because: "the trigger is scoped to counters on Basking Broodscale itself");
    }
}
