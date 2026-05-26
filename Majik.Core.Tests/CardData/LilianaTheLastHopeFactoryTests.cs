using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;
using MajikStack = Majik.Core.Stack.Stack;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Liliana, the Last Hope (Eldritch Moon, {1}{B}{B}).
///
/// Covers:
///   - Card identity (Legendary Planeswalker — Liliana, starting loyalty 3,
///     mana cost {1}{B}{B}).
///   - Loyalty ability shape (three abilities: +1, -2, -7).
///   - +1: target creature gets -2/-1 (modelled as EOT pump via
///     PumpUntilEndOfTurnEffect; "until your next turn" is the same
///     deferred surface Wrenn -1 has).
///   - +1 no-target / no-effects-service path is a legal no-op.
///   - -2: returns up to two creature cards from controller's graveyard
///     to controller's hand; skips non-creatures; honours the cap.
///   - -7 ultimate creates an emblem in controller's command zone (with
///     a registered end-step trigger when a TriggerManager is wired).
///   - NamedCardFactory dispatch.
/// </summary>
public class LilianaTheLastHopeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Liliana_IsLegendaryPlaneswalker_Liliana_3Loyalty_AtCost1BB()
    {
        var liliana = LilianaTheLastHopeFactory.Create(_alice);

        liliana.Name.Should().Be("Liliana, the Last Hope");
        liliana.ManaCost.Should().Be("{1}{B}{B}");
        liliana.HasType(CardType.Planeswalker).Should().BeTrue();
        liliana.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        liliana.HasSubtype(CardSubtype.Liliana).Should().BeTrue();
        liliana.Loyalty.Should().Be(3);
        liliana.StartingLoyalty.Should().Be(3);
        liliana.Owner.Should().BeSameAs(_alice);
        liliana.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Liliana_HasThreeLoyaltyAbilities_Plus1_Minus2_Minus7()
    {
        var liliana = LilianaTheLastHopeFactory.Create(_alice);
        var loyaltyAbilities = liliana.Abilities.OfType<LoyaltyAbility>().ToList();

        loyaltyAbilities.Should().HaveCount(3);
        loyaltyAbilities.Select(a => a.LoyaltyChange)
            .Should().BeEquivalentTo(new[] { +1, -2, -7 });
    }

    [Fact]
    public void Liliana_Plus1_AppliesMinus2Minus1ToTargetCreature()
    {
        var effects = new ContinuousEffectsService();
        var target = new Creature("Grizzly Bears", "{1}{G}", power: 2, toughness: 2,
            subtypes: new[] { CardSubtype.Bear });
        target.SetOwner(_bob);
        target.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(target);
        target.SetZone(ZoneType.Battlefield);

        var liliana = LilianaTheLastHopeFactory.Create(
            _alice,
            targetCreatureResolver: () => new[] { target },
            effects: effects,
            endStepTrigger: null);
        _alice.Zones.Battlefield.AddCard(liliana);
        liliana.SetZone(ZoneType.Battlefield);

        var plus1 = liliana.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == +1);
        plus1.Activate();

        liliana.Loyalty.Should().Be(4, "3 + 1 = 4");
        target.GetPower().Should().Be(0, "2 + (-2) = 0");
        target.GetToughness().Should().Be(1, "2 + (-1) = 1");
    }

    [Fact]
    public void Liliana_Plus1_NoResolver_IsLegalNoOp()
    {
        var liliana = LilianaTheLastHopeFactory.Create(_alice);

        var plus1 = liliana.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == +1);
        plus1.Activate();

        liliana.Loyalty.Should().Be(4, "loyalty +1 still applies");
    }

    [Fact]
    public void Liliana_Plus1_NoCandidates_IsLegalNoOp()
    {
        var effects = new ContinuousEffectsService();
        var liliana = LilianaTheLastHopeFactory.Create(
            _alice,
            targetCreatureResolver: () => Array.Empty<Creature>(),
            effects: effects,
            endStepTrigger: null);

        var plus1 = liliana.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == +1);
        plus1.Activate();

        liliana.Loyalty.Should().Be(4, "'up to one' — empty target list is legal");
    }

    [Fact]
    public void Liliana_Minus2_ReturnsUpToTwoCreatureCardsFromGraveyardToHand()
    {
        var c1 = new Creature("Grizzly Bears", "{1}{G}", power: 2, toughness: 2);
        c1.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(c1);
        c1.SetZone(ZoneType.Graveyard);

        var c2 = new Creature("Llanowar Elves", "{G}", power: 1, toughness: 1);
        c2.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(c2);
        c2.SetZone(ZoneType.Graveyard);

        var c3 = new Creature("Hill Giant", "{3}{R}", power: 3, toughness: 3);
        c3.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(c3);
        c3.SetZone(ZoneType.Graveyard);

        var liliana = LilianaTheLastHopeFactory.Create(_alice);
        liliana.AddLoyalty(1); // 3 → 4 so -2 is legal.

        var minus2 = liliana.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == -2);
        minus2.Activate();

        liliana.Loyalty.Should().Be(2, "4 - 2 = 2");
        _alice.Zones.Hand.GetCards().Should().Contain(new ICard[] { c1, c2 });
        _alice.Zones.Hand.GetCards().Should().NotContain(c3, "the cap is two");
        _alice.Zones.Graveyard.GetCards().Should().Contain(c3);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(c1);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(c2);
    }

    [Fact]
    public void Liliana_Minus2_SkipsNonCreatureCardsInGraveyard()
    {
        var bolt = new Instant("Lightning Bolt", "R") { Owner = _alice };
        _alice.Zones.Graveyard.AddCard(bolt);
        bolt.SetZone(ZoneType.Graveyard);

        var bear = new Creature("Grizzly Bears", "{1}{G}", power: 2, toughness: 2);
        bear.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(bear);
        bear.SetZone(ZoneType.Graveyard);

        var liliana = LilianaTheLastHopeFactory.Create(_alice);
        liliana.AddLoyalty(1); // 3 → 4 so -2 is legal.

        var minus2 = liliana.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == -2);
        minus2.Activate();

        _alice.Zones.Hand.GetCards().Should().Contain(bear);
        _alice.Zones.Hand.GetCards().Should().NotContain(bolt);
        _alice.Zones.Graveyard.GetCards().Should().Contain(bolt);
    }

    [Fact]
    public void Liliana_Minus2_EmptyGraveyardIsLegalNoOp()
    {
        var liliana = LilianaTheLastHopeFactory.Create(_alice);
        liliana.AddLoyalty(1); // 3 → 4 so -2 is legal.

        var minus2 = liliana.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == -2);
        minus2.Activate();

        liliana.Loyalty.Should().Be(2, "loyalty change still applies");
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Liliana_Minus7_AddsEmblemToControllerCommandZone_Structural()
    {
        var liliana = LilianaTheLastHopeFactory.Create(_alice);
        liliana.AddLoyalty(5); // 3 → 8 so -7 is legal.

        var ultimate = liliana.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == -7);
        ultimate.CanActivate().Should().BeTrue();
        ultimate.Activate();

        liliana.Loyalty.Should().Be(1, "8 - 7 = 1");
        _alice.Emblems.Should().HaveCount(1);
        _alice.Emblems[0].SourceName.Should().Contain("Liliana, the Last Hope");
        _alice.Emblems[0].Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Liliana_Minus7_EndStepTrigger_CreatesTwoZombieTokensOnControllerEndStep()
    {
        var bus = new EventBus();
        var stack = new MajikStack(bus);
        var triggers = new TriggerManager(stack, bus);

        var liliana = LilianaTheLastHopeFactory.Create(
            _alice,
            targetCreatureResolver: null,
            effects: null,
            endStepTrigger: triggers);
        liliana.AddLoyalty(5); // 3 → 8 so -7 is legal.

        var ultimate = liliana.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == -7);
        ultimate.Activate();

        _alice.Emblems.Should().HaveCount(1);

        // Fire Alice's end step — the emblem trigger should create two
        // 2/2 black Zombie creature tokens under Alice. EvaluateTriggers
        // fires via the bus subscription; then drain pending onto the
        // stack and resolve.
        bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));
        triggers.PutPendingTriggersOnStack(_alice);
        while (stack.Count > 0)
        {
            var top = stack.Pop()!;
            if (top is TriggeredAbility ta) ta.Resolve();
        }

        var aliceBattlefield = _alice.Zones.Battlefield.GetCards().ToList();
        var zombies = aliceBattlefield.OfType<Creature>()
            .Where(c => c.Name == "Zombie")
            .ToList();
        zombies.Should().HaveCount(2);
        zombies.Should().AllSatisfy(z =>
        {
            z.BasePower.Should().Be(2);
            z.BaseToughness.Should().Be(2);
            z.HasSubtype(CardSubtype.Zombie).Should().BeTrue();
            z.Controller.Should().BeSameAs(_alice);
        });
    }

    [Fact]
    public void Liliana_Minus7_EndStepTrigger_DoesNotFireOnOpponentEndStep()
    {
        var bus = new EventBus();
        var stack = new MajikStack(bus);
        var triggers = new TriggerManager(stack, bus);

        var liliana = LilianaTheLastHopeFactory.Create(
            _alice,
            targetCreatureResolver: null,
            effects: null,
            endStepTrigger: triggers);
        liliana.AddLoyalty(5);

        var ultimate = liliana.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == -7);
        ultimate.Activate();

        // Bob's end step should NOT trigger Alice's emblem.
        bus.Publish(new StepStartedEvent(PhaseStateType.End, _bob));
        triggers.PutPendingTriggersOnStack(_bob);
        while (stack.Count > 0)
        {
            var top = stack.Pop()!;
            if (top is TriggeredAbility ta) ta.Resolve();
        }

        var aliceZombies = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.Name == "Zombie")
            .ToList();
        aliceZombies.Should().BeEmpty(
            "the emblem reads 'your end step' — only the controller's end step triggers");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_LilianaTheLastHope()
    {
        var card = NamedCardFactory.Create("Liliana, the Last Hope", _alice);

        card.Should().BeOfType<Planeswalker>();
        card.Name.Should().Be("Liliana, the Last Hope");
        card.HasType(CardType.Planeswalker).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasSubtype(CardSubtype.Liliana).Should().BeTrue();
        ((Planeswalker)card).Loyalty.Should().Be(3);
        card.Owner.Should().Be(_alice);
        card.Abilities.OfType<LoyaltyAbility>().Should().HaveCount(3);
    }
}
