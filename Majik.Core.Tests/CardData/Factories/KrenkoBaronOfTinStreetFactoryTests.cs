using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="KrenkoBaronOfTinStreetFactory"/>.
///
/// Krenko, Baron of Tin Street — {2}{R} Legendary Creature — Goblin, 3/3
/// (verified against Scryfall):
///   "Haste
///    {T}, Sacrifice an artifact: Put a +1/+1 counter on each Goblin you
///    control.
///    Whenever an artifact is put into a graveyard from the battlefield, you
///    may pay {R}. If you do, create a 1/1 red Goblin creature token. It gains
///    haste until end of turn."
///
/// Covers:
/// - Identity: {2}{R} 3/3 red Legendary Goblin with Haste, mana value 3.
/// - Activated ability: {T}, Sacrifice an artifact → +1/+1 counter on EACH
///   Goblin you control (including Krenko itself).
/// - Triggered ability: an artifact dying triggers a may-pay-{R} → create a
///   1/1 red Goblin token with haste (paid path mints the token; no-mana
///   path fizzles).
/// </summary>
[Trait("Color", "R")]
public class KrenkoBaronOfTinStreetFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Krenko_Identity()
    {
        var krenko = KrenkoBaronOfTinStreetFactory.Create(_alice);

        krenko.Should().BeOfType<Creature>();
        krenko.Name.Should().Be("Krenko, Baron of Tin Street");
        krenko.ManaCost.Should().Be("{2}{R}");
        krenko.ManaCostValue.TotalValue.Should().Be(3, "{2}{R} is mana value 3");
        krenko.HasType(CardType.Creature).Should().BeTrue();
        krenko.HasSupertype(CardSupertype.Legendary).Should().BeTrue("Krenko is legendary");
        krenko.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        krenko.BasePower.Should().Be(3);
        krenko.BaseToughness.Should().Be(3);
        krenko.HasEffectiveKeyword("Haste").Should().BeTrue("Krenko has Haste");
        CardColors.GetColors(krenko).Should().Contain(ManaColor.Red, "{R} in the cost makes Krenko red");
        krenko.Owner.Should().BeSameAs(_alice);
        krenko.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Krenko_HasActivatedAndTriggeredAbility()
    {
        var krenko = KrenkoBaronOfTinStreetFactory.Create(_alice);

        krenko.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the {T}, Sacrifice an artifact counter ability");
        krenko.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the artifact-into-graveyard token trigger");
    }

    // -----------------------------------------------------------------------
    // {T}, Sacrifice an artifact: +1/+1 counter on each Goblin you control.
    // -----------------------------------------------------------------------

    [Fact]
    public void TapSacArtifact_PutsCounterOnEachGoblinIncludingKrenko()
    {
        var krenko = KrenkoBaronOfTinStreetFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(krenko);
        krenko.SetZone(ZoneType.Battlefield);

        // Another Goblin you control.
        var otherGoblin = new Creature("Goblin Friend", "{R}", 1, 1,
            subtypes: new[] { CardSubtype.Goblin });
        otherGoblin.SetOwner(_alice);
        otherGoblin.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(otherGoblin);
        otherGoblin.SetZone(ZoneType.Battlefield);

        // A non-Goblin creature — must NOT get a counter.
        var elf = new Creature("Elf", "{G}", 1, 1, subtypes: new[] { CardSubtype.Elf });
        elf.SetOwner(_alice);
        elf.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(elf);
        elf.SetZone(ZoneType.Battlefield);

        var ability = krenko.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var eff in ability.Effects) eff.Execute();

        krenko.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "Krenko is a Goblin you control — no 'other' qualifier");
        otherGoblin.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
        elf.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "non-Goblins get no counter");
    }

    [Fact]
    public void TapSacArtifact_CostIsTapPlusSacrificeArtifact()
    {
        var krenko = KrenkoBaronOfTinStreetFactory.Create(_alice);
        var ability = krenko.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<SacrificeAnArtifactCost>().Should().HaveCount(1,
            "the cost includes sacrificing an artifact");
        // Tap cost is present (AdditionalCost.Tap) — at least two cost
        // components total.
        ability.Costs.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    // -----------------------------------------------------------------------
    // Artifact-into-graveyard trigger: may pay {R} → 1/1 red Goblin w/ haste.
    // -----------------------------------------------------------------------

    [Fact]
    public void ArtifactDies_PayingRed_CreatesHastyGoblinToken()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var krenko = KrenkoBaronOfTinStreetFactory.Create(
            _alice, triggers: triggers, zoneService: zones, eventBus: bus);
        _alice.Zones.Battlefield.AddCard(krenko);
        krenko.SetZone(ZoneType.Battlefield);

        // Give Alice {R} so the optional may-pay succeeds (agent-less => auto-pay).
        _alice.AddManaToPool(ManaCost.Parse("{R}"));

        // An artifact moves from the battlefield to the graveyard.
        var artifact = new Artifact("Trinket", "{1}");
        artifact.SetOwner(_alice);
        artifact.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(artifact);
        artifact.SetZone(ZoneType.Battlefield);

        bus.Publish(new CardMovedEvent(artifact, ZoneType.Battlefield, ZoneType.Graveyard, _alice));

        ResolveTriggers(triggers, stack);

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.HasSubtype(CardSubtype.Goblin))
            .ToList();

        tokens.Should().HaveCount(1, "paying {R} creates one 1/1 red Goblin token");
        var token = tokens.Single();
        token.BasePower.Should().Be(1);
        token.BaseToughness.Should().Be(1);
        token.HasEffectiveKeyword("Haste").Should().BeTrue("the token gains haste until end of turn");
        CardColors.GetColors(token).Should().Contain(ManaColor.Red, "1/1 red Goblin token");
    }

    [Fact]
    public void ArtifactDies_WithoutManaToPay_NoTokenCreated()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var krenko = KrenkoBaronOfTinStreetFactory.Create(
            _alice, triggers: triggers, zoneService: zones, eventBus: bus);
        _alice.Zones.Battlefield.AddCard(krenko);
        krenko.SetZone(ZoneType.Battlefield);

        // No mana in pool — the agent-less auto-pay attempt fails the {R}
        // PayMana, so the optional trigger fizzles (CR 117.5).
        var artifact = new Artifact("Trinket", "{1}");
        artifact.SetOwner(_alice);
        artifact.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(artifact);
        artifact.SetZone(ZoneType.Battlefield);

        bus.Publish(new CardMovedEvent(artifact, ZoneType.Battlefield, ZoneType.Graveyard, _alice));

        ResolveTriggers(triggers, stack);

        _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Count(c => c.IsToken && c.HasSubtype(CardSubtype.Goblin))
            .Should().Be(0, "no {R} available => no token");
    }

    [Fact]
    public void NonArtifactDies_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var krenko = KrenkoBaronOfTinStreetFactory.Create(
            _alice, triggers: triggers, zoneService: zones, eventBus: bus);
        _alice.Zones.Battlefield.AddCard(krenko);
        krenko.SetZone(ZoneType.Battlefield);
        _alice.AddManaToPool(ManaCost.Parse("{R}"));

        // A creature (non-artifact) dies — should NOT trigger.
        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        bus.Publish(new CardMovedEvent(bear, ZoneType.Battlefield, ZoneType.Graveyard, _alice));

        ResolveTriggers(triggers, stack);

        _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Count(c => c.IsToken && c.HasSubtype(CardSubtype.Goblin))
            .Should().Be(0, "a non-artifact death does not trigger the token maker");
    }

    private void ResolveTriggers(TriggerManager triggers, Majik.Core.Stack.Stack stack)
    {
        triggers.PutPendingTriggersOnStack(_alice);
        while (stack.Count > 0)
        {
            var item = stack.Pop();
            if (item is TriggeredAbility ta)
            {
                foreach (var eff in ta.Effects) eff.Execute();
            }
        }
    }
}
