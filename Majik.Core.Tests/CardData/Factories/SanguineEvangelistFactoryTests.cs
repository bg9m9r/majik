using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="SanguineEvangelistFactory"/>.
///
/// Sanguine Evangelist — {2}{W} Creature — Vampire Cleric, 2/1 (Scryfall):
///   "Battle cry (Whenever this creature attacks, each other attacking
///    creature gets +1/+0 until end of turn.)
///    When this creature enters or dies, create a 1/1 black Bat creature
///    token with flying."
///
/// Covers the card's UNIQUE behaviour:
/// - Identity: {2}{W} 2/1 white Vampire Cleric (one *_Identity assert).
/// - Battle cry: keyword marker present; on attack each OTHER attacking
///   creature gets +1/+0 EOT, the Evangelist itself is not pumped.
/// - Enters-or-dies rider: a 1/1 BLACK Bat token with flying is created on
///   ETB and again on death.
/// </summary>
[Trait("Color", "W")]
public class SanguineEvangelistFactoryTests
{
    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void SanguineEvangelist_IsWhiteVampireCleric_2_1_ManaValue3()
    {
        var alice = new Player("Alice", 20);
        var card = SanguineEvangelistFactory.Create(alice);

        card.Should().BeOfType<Creature>();
        card.HasType(CardType.Creature).Should().BeTrue();
        card.Name.Should().Be("Sanguine Evangelist");
        card.BasePower.Should().Be(2);
        card.BaseToughness.Should().Be(1);
        card.ManaCostValue.TotalValue.Should().Be(3, "{2}{W} is mana value 3");
        card.HasSubtype(CardSubtype.Vampire).Should().BeTrue();
        card.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.White, "the {W} pip is white");
        card.Owner.Should().BeSameAs(alice);
        card.Controller.Should().BeSameAs(alice);
    }

    [Fact]
    public void SanguineEvangelist_HasBattleCryKeywordMarker()
    {
        var alice = new Player("Alice", 20);
        var card = SanguineEvangelistFactory.Create(alice);

        card.Abilities.OfType<KeywordAbility>()
            .Any(k => string.Equals(k.Keyword, "Battle cry", StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue("the printed line includes Battle cry");
    }

    // -----------------------------------------------------------------------
    // Battle cry: each OTHER attacking creature gets +1/+0 EOT.
    // -----------------------------------------------------------------------

    [Fact]
    public void BattleCry_PumpsEachOtherAttackingCreature_NotItself()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var eventBus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(eventBus);
        var triggers = new TriggerManager(stack, eventBus);
        var combat = new CombatManager(eventBus);
        var effects = new ContinuousEffectsService();

        var evangelist = SanguineEvangelistFactory.Create(
            alice,
            triggers: triggers,
            zoneService: null,
            attackingCreaturesSource: () => combat.CurrentCombat?.Attackers
                .Select(a => a.Creature).OfType<Creature>().ToList() ?? new List<Creature>());
        evangelist.ActiveEffects = effects;
        alice.Zones.Battlefield.AddCard(evangelist);
        evangelist.SetZone(ZoneType.Battlefield);
        evangelist.ClearSummoningSickness();

        var ally = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        ally.SetOwner(alice);
        ally.SetController(alice);
        ally.ActiveEffects = effects;
        alice.Zones.Battlefield.AddCard(ally);
        ally.SetZone(ZoneType.Battlefield);
        ally.ClearSummoningSickness();

        combat.StartCombat(alice);
        combat.DeclareAttackers(alice, new[]
        {
            new AttackerDeclaration(evangelist, targetPlayer: bob),
            new AttackerDeclaration(ally, targetPlayer: bob),
        });
        eventBus.Publish(new CreatureAttacksEvent(evangelist, bob));

        ResolveTriggers(triggers, stack, alice);

        ally.GetPower().Should().Be(3, "battle cry gives each other attacker +1/+0");
        ally.GetToughness().Should().Be(2, "battle cry is +1/+0 — toughness unchanged");

        evangelist.GetPower().Should().Be(2, "Evangelist is not pumped by its own battle cry");
        evangelist.GetToughness().Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Enters-or-dies rider: 1/1 black Bat with flying.
    // -----------------------------------------------------------------------

    [Fact]
    public void OnEnter_CreatesOneBlackFlyingBatToken()
    {
        var alice = new Player("Alice", 20);

        var eventBus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(eventBus);
        var triggers = new TriggerManager(stack, eventBus);
        var zoneService = new ZoneService(eventBus);

        var evangelist = SanguineEvangelistFactory.Create(
            alice, triggers: triggers, zoneService: zoneService, attackingCreaturesSource: null);

        // Move the Evangelist onto the battlefield via ZoneService so the ETB
        // trigger (CR 603.6a) fires.
        evangelist.SetZone(ZoneType.Library);
        alice.Zones.Library.AddCard(evangelist);
        zoneService.MoveCardTo(evangelist, ZoneType.Battlefield, alice);

        ResolveTriggers(triggers, stack, alice);

        AssertSingleBlackFlyingBat(alice);
    }

    [Fact]
    public void OnDies_CreatesOneBlackFlyingBatToken()
    {
        var alice = new Player("Alice", 20);

        var eventBus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(eventBus);
        var triggers = new TriggerManager(stack, eventBus);
        var zoneService = new ZoneService(eventBus);

        var evangelist = SanguineEvangelistFactory.Create(
            alice, triggers: triggers, zoneService: zoneService, attackingCreaturesSource: null);
        evangelist.SetOwner(alice);
        evangelist.SetController(alice);
        alice.Zones.Battlefield.AddCard(evangelist);
        evangelist.SetZone(ZoneType.Battlefield);

        // CR 603.6c / 700.4 — death = battlefield → graveyard.
        zoneService.MoveCardTo(evangelist, ZoneType.Graveyard, alice);

        ResolveTriggers(triggers, stack, alice);

        AssertSingleBlackFlyingBat(alice);
    }

    private static void AssertSingleBlackFlyingBat(Player owner)
    {
        var bats = owner.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.HasSubtype(CardSubtype.Bat))
            .ToList();

        bats.Should().HaveCount(1, "the rider creates exactly one Bat token");
        var bat = bats[0];
        bat.BasePower.Should().Be(1);
        bat.BaseToughness.Should().Be(1);
        CardColors.GetColors(bat).Should().Contain(ManaColor.Black, "the Bat is black");
        CardColors.GetColors(bat).Should().NotContain(ManaColor.White);
        bat.Abilities.OfType<KeywordAbility>()
            .Any(k => string.Equals(k.Keyword, "Flying", StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue("the Bat has flying");
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
}
