using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Anointer Priest (Amonkhet, {1}{W}, Creature — Human Cleric 1/3).
///
/// Covers:
///   - Card identity (name, type, subtypes, P/T, mana cost, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch returns the same shape.
///   - One triggered ability + Embalm keyword marker attached.
///   - Creature token ETB under controller → exactly one lifegain trigger
///     queued, resolving to +1 life.
///   - Multiple creature tokens entering → one trigger per token (additive).
///   - Non-token creature ETB → no trigger fires.
///   - Non-creature token ETB (Clue, Treasure) → no trigger fires.
///   - Opponent-controlled creature token → no trigger fires.
/// </summary>
public class AnointerPriestFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void AnointerPriest_Identity_HumanCleric13At1W()
    {
        var p = AnointerPriestFactory.Create(_alice);

        p.Name.Should().Be("Anointer Priest");
        p.ManaCost.Should().Be("{1}{W}");
        p.HasType(CardType.Creature).Should().BeTrue();
        p.HasSubtype(CardSubtype.Human).Should().BeTrue();
        p.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
        p.BasePower.Should().Be(1);
        p.BaseToughness.Should().Be(3);
        p.Owner.Should().BeSameAs(_alice);
        p.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_AnointerPriest()
    {
        var card = NamedCardFactory.Create("Anointer Priest", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Anointer Priest");
        card.ManaCost.Should().Be("{1}{W}");
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void AnointerPriest_HasEmbalmKeywordMarker()
    {
        var p = AnointerPriestFactory.Create(_alice);

        p.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Embalm");
    }

    // -----------------------------------------------------------------------
    // Lifegain trigger — creature-token ETB
    // -----------------------------------------------------------------------

    [Fact]
    public void CreatureTokenEntersUnderController_GainsOneLife()
    {
        var (zones, stack, triggers) = BuildEngine();

        var priest = AnointerPriestFactory.Create(_alice, triggers);
        priest.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(priest);

        var startingLife = _alice.LifeTotal;

        // Create a 1/1 Soldier creature token under Alice via TokenFactory
        // — ZoneService publishes the CardMovedEvent that drives the trigger.
        var spec = new TokenFactory.TokenSpec(
            Name: "Soldier",
            Power: 1,
            Toughness: 1,
            Subtypes: new[] { CardSubtype.Soldier },
            Colors: new[] { ManaColor.White });
        TokenFactory.CreateOnBattlefield(spec, _alice, zones);

        triggers.PendingCount.Should().Be(1, "exactly one lifegain trigger for one creature token ETB");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.LifeTotal.Should().Be(startingLife + 1, "Anointer Priest gains 1 life per creature token ETB");
    }

    [Fact]
    public void TwoCreatureTokensEnter_GainsTwoLife()
    {
        var (zones, stack, triggers) = BuildEngine();

        var priest = AnointerPriestFactory.Create(_alice, triggers);
        priest.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(priest);

        var startingLife = _alice.LifeTotal;

        for (int i = 0; i < 2; i++)
        {
            var spec = new TokenFactory.TokenSpec(
                Name: $"Soldier {i}",
                Power: 1,
                Toughness: 1,
                Subtypes: new[] { CardSubtype.Soldier },
                Colors: new[] { ManaColor.White });
            TokenFactory.CreateOnBattlefield(spec, _alice, zones);

            triggers.PutPendingTriggersOnStack(_alice);
            while (stack.Count > 0) stack.Pop()!.Resolve();
        }

        _alice.LifeTotal.Should().Be(startingLife + 2);
    }

    [Fact]
    public void NonTokenCreatureEnters_NoLifegainTrigger()
    {
        var (zones, _, triggers) = BuildEngine();

        var priest = AnointerPriestFactory.Create(_alice, triggers);
        priest.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(priest);

        var startingLife = _alice.LifeTotal;

        // A non-token creature ETB under Alice's control — should NOT
        // trigger (oracle: "a creature TOKEN you control").
        var bear = new Creature("Grizzly Bears", "1G", 2, 2);
        bear.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(bear);
        bear.SetZone(ZoneType.Hand);
        zones.MoveCardTo(bear, ZoneType.Battlefield, controller: _alice);

        triggers.PendingCount.Should().Be(0,
            "non-token creatures don't trigger Anointer Priest's lifegain");
        _alice.LifeTotal.Should().Be(startingLife);
    }

    [Fact]
    public void NonCreatureTokenEnters_NoLifegainTrigger()
    {
        var (zones, _, triggers) = BuildEngine();

        var priest = AnointerPriestFactory.Create(_alice, triggers);
        priest.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(priest);

        var startingLife = _alice.LifeTotal;

        // A Clue is an artifact token, NOT a creature token — should NOT
        // trigger. (Same predicate-check Anointer Priest does in print.)
        TokenFactory.CreateClue(_alice, zones);

        triggers.PendingCount.Should().Be(0,
            "Clue (non-creature artifact token) doesn't match \"creature token\"");
        _alice.LifeTotal.Should().Be(startingLife);
    }

    [Fact]
    public void OpponentCreatureTokenEnters_NoLifegainTrigger()
    {
        var (zones, _, triggers) = BuildEngine();

        var priest = AnointerPriestFactory.Create(_alice, triggers);
        priest.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(priest);

        var startingLife = _alice.LifeTotal;

        // Bob's creature token — should NOT trigger ("you control" gate).
        var spec = new TokenFactory.TokenSpec(
            Name: "Goblin",
            Power: 1,
            Toughness: 1,
            Subtypes: new[] { CardSubtype.Goblin },
            Colors: new[] { ManaColor.Red });
        TokenFactory.CreateOnBattlefield(spec, _bob, zones);

        triggers.PendingCount.Should().Be(0,
            "creature token under opponent's control doesn't trigger \"you control\"");
        _alice.LifeTotal.Should().Be(startingLife);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static (ZoneService zones, Majik.Core.Stack.Stack stack, TriggerManager triggers) BuildEngine()
    {
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        return (zones, stack, triggers);
    }
}
