using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="AdelineResplendentCatharFactory"/>.
///
/// Adeline, Resplendent Cathar — {1}{W}{W} Legendary Creature — Human Knight,
/// printed power "*" / toughness 4. Oracle text (verified against Scryfall):
///   "Vigilance
///    Adeline's power is equal to the number of creatures you control.
///    Whenever you attack, for each opponent, create a 1/1 white Human
///    creature token that's tapped and attacking that player or a
///    planeswalker they control."
///
/// Covers:
///   - Identity: {1}{W}{W} Legendary white Human Knight, mana value 3, dispatch.
///   - Vigilance keyword marker (CR 702.21).
///   - CDA power = number of creatures you control (CR 604.3 / 613.2 Layer 7a);
///     toughness stays 4.
///   - Attack trigger: "Whenever you attack" (controller is the attacker)
///     creates, for each opponent, a 1/1 white Human token tapped and
///     attacking that opponent (CR 508.3g).
///   - Attack trigger does NOT fire on an opponent's attack.
/// </summary>
public class AdelineResplendentCatharFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature NewCreature(Player controller, string name, int p = 2, int t = 2)
    {
        var creature = new Creature(name, "{G}", p, t);
        creature.SetOwner(controller);
        creature.SetController(controller);
        return creature;
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Adeline_Identity_LegendaryWhiteHumanKnight_AtCost1WW()
    {
        var card = AdelineResplendentCatharFactory.Create(_alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Adeline, Resplendent Cathar");
        card.ManaCost.Should().Be("{1}{W}{W}");
        card.ManaCostValue.TotalValue.Should().Be(3, "{1}{W}{W} is mana value 3");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Knight).Should().BeTrue();
        card.BaseToughness.Should().Be(4);
        CardColors.GetColors(card).Should().Contain(ManaColor.White);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Adeline()
    {
        var card = NamedCardFactory.Create("Adeline, Resplendent Cathar", _alice);

        card.Should().BeAssignableTo<Creature>();
        card.Name.Should().Be("Adeline, Resplendent Cathar");
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Knight).Should().BeTrue();
    }

    [Fact]
    public void Adeline_HasVigilanceKeywordMarker()
    {
        var card = AdelineResplendentCatharFactory.Create(_alice);

        card.Abilities.OfType<KeywordAbility>()
            .Any(k => string.Equals(k.Keyword, "Vigilance", StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue("the printed line includes Vigilance");
    }

    [Fact]
    public void Adeline_HasAttackTriggeredAbility()
    {
        var card = AdelineResplendentCatharFactory.Create(_alice);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // CDA power = number of creatures you control (CR 604.3 / 613.2 Layer 7a).
    // -----------------------------------------------------------------------

    [Fact]
    public void Adeline_Power_EqualsNumberOfCreaturesYouControl_ToughnessStays4()
    {
        var bus = new EventBus();
        // Wire the effects service to the bus so its CR-613 memoization cache
        // invalidates on game events (matches production GameDependencies and
        // the Tarmogoyf CDA test).
        var effects = new ContinuousEffectsService(bus);

        Func<IEnumerable<ICard>> mine = () => _alice.Zones.Battlefield.GetCards();

        var card = AdelineResplendentCatharFactory.Create(
            _alice, effects, bus, mine,
            opponentResolver: null, triggers: null, combat: null);
        card.ActiveEffects = effects;
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        // ETB fires the CDA lifecycle (register the Layer-7a CDA on the
        // battlefield) — same CardMovedEvent path real zone moves take.
        bus.Publish(new CardMovedEvent(card, ZoneType.Hand, ZoneType.Battlefield));

        // Only Adeline herself is a creature you control → power 1.
        card.Power.Should().Be(1, "Adeline counts herself among creatures you control");
        card.Toughness.Should().Be(4, "toughness is the printed 4");

        // Add two more creatures under Alice's control.
        var bear = NewCreature(_alice, "Bear");
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);
        var wolf = NewCreature(_alice, "Wolf");
        _alice.Zones.Battlefield.AddCard(wolf);
        wolf.SetZone(ZoneType.Battlefield);

        // An opponent's creature does NOT count.
        var bobBear = NewCreature(_bob, "BobBear");
        _bob.Zones.Battlefield.AddCard(bobBear);
        bobBear.SetZone(ZoneType.Battlefield);

        // The CDA reads creatures-you-control live, but the layer-pipeline
        // memoization cache is keyed by generation; raw AddCard moves don't
        // fire events, so nudge the cache the way real zone moves would
        // (CardMovedEvent → SubscribeAll → BumpGeneration).
        bus.Publish(new CardMovedEvent(bear, ZoneType.Hand, ZoneType.Battlefield));

        card.Power.Should().Be(3, "three creatures you control: Adeline, Bear, Wolf");
        card.Toughness.Should().Be(4, "toughness unaffected by the CDA");
    }

    // -----------------------------------------------------------------------
    // Attack trigger — "Whenever you attack, for each opponent, create a 1/1
    // white Human creature token that's tapped and attacking that player."
    // -----------------------------------------------------------------------

    [Fact]
    public void AttackTrigger_YouAttack_CreatesTappedAttackingWhiteHumanTokenPerOpponent()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var combat = new CombatManager(bus);
        var effects = new ContinuousEffectsService();

        Func<IEnumerable<ICard>> mine = () => _alice.Zones.Battlefield.GetCards();

        var card = AdelineResplendentCatharFactory.Create(
            _alice, effects, bus, mine,
            opponentResolver: () => new[] { _bob },
            triggers: triggers,
            combat: combat);
        card.ActiveEffects = effects;
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        card.ClearSummoningSickness();

        combat.StartCombat(_alice);
        // DeclareAttackers publishes AttackersDeclaredEvent itself.
        combat.DeclareAttackers(_alice, new[]
        {
            new AttackerDeclaration(card, targetPlayer: _bob),
        });

        triggers.PendingCount.Should().Be(1, "'Whenever you attack' fires when you attack");

        var attack = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in attack.Effects) e.Execute();

        var humans = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.HasSubtype(CardSubtype.Human))
            .ToList();

        humans.Should().HaveCount(1, "one token per opponent (a single opponent here)");
        var token = humans.Single();
        token.BasePower.Should().Be(1);
        token.BaseToughness.Should().Be(1);
        CardColors.GetColors(token).Should().Contain(ManaColor.White, "white Human token");
        token.IsTapped.Should().BeTrue("the token enters tapped");

        combat.CurrentCombat!.Attackers.Select(a => a.Creature).Should().Contain(token,
            "the token enters attacking");
        combat.CurrentCombat.Attackers
            .Single(a => ReferenceEquals(a.Creature, token))
            .TargetPlayer.Should().BeSameAs(_bob,
                "the token attacks that opponent (CR 508.4)");
    }

    [Fact]
    public void AttackTrigger_OpponentAttacks_DoesNotFire()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var combat = new CombatManager(bus);
        var effects = new ContinuousEffectsService();

        Func<IEnumerable<ICard>> mine = () => _alice.Zones.Battlefield.GetCards();

        var card = AdelineResplendentCatharFactory.Create(
            _alice, effects, bus, mine,
            opponentResolver: () => new[] { _bob },
            triggers: triggers,
            combat: combat);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var bobBear = NewCreature(_bob, "BobBear");
        _bob.Zones.Battlefield.AddCard(bobBear);
        bobBear.SetZone(ZoneType.Battlefield);
        bobBear.ClearSummoningSickness();

        combat.StartCombat(_bob);
        combat.DeclareAttackers(_bob, new[]
        {
            new AttackerDeclaration(bobBear, targetPlayer: _alice),
        });

        triggers.PendingCount.Should().Be(0,
            "'Whenever you attack' only fires when Adeline's controller is the attacker");
    }
}
