using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="FearOfMissingOutFactory"/>.
///
/// Fear of Missing Out — {1}{R} Enchantment Creature — Nightmare 2/3:
///   "When this creature enters, discard a card, then draw a card.
///    Delirium — Whenever this creature attacks for the first time each turn,
///    if there are four or more card types among cards in your graveyard,
///    untap target creature. After this phase, there is an additional combat
///    phase."
/// </summary>
[Trait("Color", "R")]
public class FearOfMissingOutFactoryTests
{
    [Fact]
    public void FearOfMissingOut_IsRedNightmareEnchantmentCreature_2_3()
    {
        var alice = new Player("Alice", 20);
        var card = FearOfMissingOutFactory.Create(alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Fear of Missing Out");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasType(CardType.Enchantment).Should().BeTrue("Enchantment Creature (CR 301.1)");
        card.HasSubtype(CardSubtype.Nightmare).Should().BeTrue();
        card.BasePower.Should().Be(2);
        card.BaseToughness.Should().Be(3);
        card.ManaCostValue.TotalValue.Should().Be(2, "{1}{R} is mana value 2");
        CardColors.GetColors(card).Should().Contain(ManaColor.Red);
    }

    [Fact]
    public void Etb_DiscardsThenDraws()
    {
        var alice = new Player("Alice", 20);

        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = FearOfMissingOutFactory.Create(alice, triggers, bus);

        // Seed hand (1 card) + library (1 card to draw).
        var handCard = new Creature("Hand Bear", "{1}{G}", 2, 2) { Owner = alice, Controller = alice };
        alice.Zones.Hand.AddCard(handCard); handCard.SetZone(ZoneType.Hand);
        var libCard = new Creature("Lib Bear", "{1}{G}", 2, 2) { Owner = alice, Controller = alice };
        alice.Zones.Library.AddCard(libCard); libCard.SetZone(ZoneType.Library);

        // ETB.
        alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(card, ZoneType.Stack, ZoneType.Battlefield));

        ResolveTriggers(triggers, stack, alice);

        alice.Zones.Graveyard.GetCards().Should().Contain(handCard, "discarded the hand card");
        alice.Zones.Hand.GetCards().Should().Contain(libCard, "drew the library card");
    }

    [Fact]
    public void DeliriumAttack_WithFourCardTypes_EnqueuesAdditionalCombatAndUntaps()
    {
        using var scope = AdditionalCombatRegistryProvider.PushScope();
        AdditionalCombatRegistryProvider.Current.Pending.Should().Be(0);

        var alice = new Player("Alice", 20);

        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = FearOfMissingOutFactory.Create(alice, triggers, bus);
        alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        card.ClearSummoningSickness();

        // Delirium ON — 4 distinct card types in the graveyard.
        SeedGraveyardForDelirium(alice);

        // A tapped creature to untap (the chosen target).
        var tappedBear = new Creature("Bear", "{1}{G}", 2, 2) { Owner = alice, Controller = alice };
        alice.Zones.Battlefield.AddCard(tappedBear);
        tappedBear.SetZone(ZoneType.Battlefield);
        tappedBear.Tap();

        bus.Publish(new CreatureAttacksEvent(card, new Player("Bob", 20)));
        triggers.PendingCount.Should().Be(1, "delirium-active first-attack trigger fired");

        ResolveTriggersWithTargets(triggers, stack, alice);

        AdditionalCombatRegistryProvider.Current.Pending.Should().Be(1,
            "an additional combat phase was enqueued (CR 506.4)");
        tappedBear.IsTapped.Should().BeFalse("target creature was untapped (CR 701.20a)");
    }

    [Fact]
    public void DeliriumAttack_WithoutDelirium_DoesNotFire()
    {
        using var scope = AdditionalCombatRegistryProvider.PushScope();

        var alice = new Player("Alice", 20);

        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = FearOfMissingOutFactory.Create(alice, triggers, bus);
        alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        card.ClearSummoningSickness();

        // No delirium (empty graveyard).
        bus.Publish(new CreatureAttacksEvent(card, new Player("Bob", 20)));

        // The intervening-if (delirium) is false → no trigger lands.
        triggers.PendingCount.Should().Be(0, "delirium intervening-if fails (CR 603.4)");
        AdditionalCombatRegistryProvider.Current.Pending.Should().Be(0);
    }

    [Fact]
    public void FearOfMissingOut_DispatchesThroughNamedFactory()
    {
        var alice = new Player("Alice", 20);
        var card = NamedCardFactory.Create("Fear of Missing Out", alice);

        card.Should().NotBeNull();
        card!.Name.Should().Be("Fear of Missing Out");
    }

    private static void SeedGraveyardForDelirium(Player p)
    {
        // 4 distinct card types: Creature, Instant, Sorcery, Enchantment.
        void Add(Card c) { p.Zones.Graveyard.AddCard(c); c.SetZone(ZoneType.Graveyard); }
        Add(new Creature("C", "{G}", 1, 1) { Owner = p });
        Add(new Instant("I", "{R}") { Owner = p });
        Add(new Sorcery("S", "{U}") { Owner = p });
        Add(new Enchantment("E", "{W}") { Owner = p });
    }

    private static void ResolveTriggers(
        TriggerManager triggers, Majik.Core.Stack.Stack stack, Player active)
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

    // Resolve triggers, auto-choosing the first legal target for any
    // target-requesting ability (so the "untap target creature" half lands).
    private static void ResolveTriggersWithTargets(
        TriggerManager triggers, Majik.Core.Stack.Stack stack, Player active)
    {
        triggers.PutPendingTriggersOnStack(active);
        while (stack.Count > 0)
        {
            var item = stack.Pop();
            if (item is TriggeredAbility ta)
            {
                ChooseFirstTargetIfAny(ta, active);
                foreach (var eff in ta.Effects) eff.Execute();
            }
        }
    }

    private static void ChooseFirstTargetIfAny(TriggeredAbility ta, Player active)
    {
        if (ta.TargetRequests == null || ta.TargetRequests.Count == 0) return;
        var ctx = new GameContext(active, new[] { active }, active, 1,
            StepStateType.DeclareAttackers, new Majik.Core.Stack.Stack(new EventBus()));
        var chosen = ta.TargetRequests
            .Select(req =>
            {
                var cands = req.CandidateGatherer?.Invoke(ctx) ?? req.LegalCandidates;
                // Prefer a tapped creature (so "untap target creature" is
                // observable); fall back to the first candidate otherwise.
                var pick = cands.FirstOrDefault(o => o is Creature { IsTapped: true })
                    ?? cands.FirstOrDefault();
                return pick == null
                    ? new System.Collections.Generic.List<object>()
                    : new System.Collections.Generic.List<object> { pick };
            })
            .ToList();
        ta.SetChosenTargets(chosen);
    }
}
