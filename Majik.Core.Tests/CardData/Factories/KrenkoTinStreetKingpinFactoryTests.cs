using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="KrenkoTinStreetKingpinFactory"/>.
///
/// Krenko, Tin Street Kingpin — {2}{R} Legendary Creature — Goblin, 1/2
/// (verified against Scryfall):
///   "Whenever Krenko attacks, put a +1/+1 counter on it, then create a
///    number of 1/1 red Goblin creature tokens equal to Krenko's power."
///
/// Covers:
/// - Identity: {2}{R} 1/2 red Legendary Goblin, mana value 3, dispatch.
/// - Attack trigger: a +1/+1 counter is added FIRST, then a number of
///   1/1 red Goblin tokens equal to Krenko's CURRENT power (post-counter)
///   are minted (CR 608.2 — left-to-right resolution, so a fresh 1/2
///   becomes 2/3 and makes two tokens).
/// </summary>
[Trait("Color", "R")]
public class KrenkoTinStreetKingpinFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Krenko_Identity()
    {
        var krenko = KrenkoTinStreetKingpinFactory.Create(_alice);

        krenko.Should().BeOfType<Creature>();
        krenko.Name.Should().Be("Krenko, Tin Street Kingpin");
        krenko.ManaCost.Should().Be("{2}{R}");
        krenko.ManaCostValue.TotalValue.Should().Be(3, "{2}{R} is mana value 3");
        krenko.HasType(CardType.Creature).Should().BeTrue();
        krenko.HasSupertype(CardSupertype.Legendary).Should().BeTrue("Krenko is legendary");
        krenko.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        krenko.BasePower.Should().Be(1);
        krenko.BaseToughness.Should().Be(2);
        CardColors.GetColors(krenko).Should().Contain(ManaColor.Red, "{R} in the cost makes Krenko red");
        krenko.Owner.Should().BeSameAs(_alice);
        krenko.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Krenko_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Krenko, Tin Street Kingpin", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Krenko, Tin Street Kingpin");
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
    }

    [Fact]
    public void Krenko_HasOneAttackTrigger()
    {
        var krenko = KrenkoTinStreetKingpinFactory.Create(_alice);

        krenko.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "Krenko has a single 'whenever Krenko attacks' trigger");
    }

    // -----------------------------------------------------------------------
    // Attack trigger: counter first, then tokens = current power.
    // -----------------------------------------------------------------------

    [Fact]
    public void OnAttack_AddsCounterThenMintsTokensEqualToPower()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var krenko = KrenkoTinStreetKingpinFactory.Create(
            _alice, triggers: triggers, effects: effects, zoneService: null);
        krenko.ActiveEffects = effects;
        _alice.Zones.Battlefield.AddCard(krenko);
        krenko.SetZone(ZoneType.Battlefield);
        krenko.ClearSummoningSickness();

        bus.Publish(new CreatureAttacksEvent(krenko, _bob));

        ResolveTriggers(triggers, stack, _alice);

        // CR 608.2 — counter resolves first: a 1/2 becomes 2/3.
        krenko.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "a +1/+1 counter is put on Krenko first");
        krenko.Power.Should().Be(2, "the +1/+1 counter raises power before tokens are counted");

        // ...then tokens equal to Krenko's CURRENT power (2).
        var goblins = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.HasSubtype(CardSubtype.Goblin))
            .ToList();

        goblins.Should().HaveCount(2,
            "tokens equal Krenko's power AFTER the +1/+1 counter (1/2 -> 2/3 -> two tokens)");
        goblins.Should().AllSatisfy(g =>
        {
            g.BasePower.Should().Be(1);
            g.BaseToughness.Should().Be(1);
            CardColors.GetColors(g).Should().Contain(ManaColor.Red, "1/1 red Goblin tokens");
        });
    }

    [Fact]
    public void OnAttack_SecondAttack_MintsMoreTokensAsCounterStacks()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var krenko = KrenkoTinStreetKingpinFactory.Create(
            _alice, triggers: triggers, effects: effects, zoneService: null);
        krenko.ActiveEffects = effects;
        _alice.Zones.Battlefield.AddCard(krenko);
        krenko.SetZone(ZoneType.Battlefield);
        krenko.ClearSummoningSickness();

        // First attack: 1/2 -> 2/3, two tokens.
        bus.Publish(new CreatureAttacksEvent(krenko, _bob));
        ResolveTriggers(triggers, stack, _alice);

        // Second attack (e.g. extra combat): 2/3 -> 3/4, three more tokens.
        bus.Publish(new CreatureAttacksEvent(krenko, _bob));
        ResolveTriggers(triggers, stack, _alice);

        krenko.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2);
        krenko.Power.Should().Be(3);

        var goblins = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Count(c => c.IsToken && c.HasSubtype(CardSubtype.Goblin));
        goblins.Should().Be(5, "two tokens from the first attack + three from the second");
    }

    private static void ResolveTriggers(TriggerManager triggers, Majik.Core.Stack.Stack stack, Player active)
    {
        triggers.PutPendingTriggersOnStack(active);
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
