using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="EnduringTenacityFactory"/>.
///
/// Enduring Tenacity (Duskmourn, {2}{B}{B}). Enchantment Creature — Snake
/// Glimmer 4/3. Oracle text (verified against Scryfall):
///   "Whenever you gain life, target opponent loses that much life.
///    When Enduring Tenacity dies, if it was a creature, return it to the
///    battlefield under its owner's control. It's an enchantment. (It's not a
///    creature.)"
///
/// Covers (UNIQUE behaviour only — dispatch + well-formedness are covered by
/// CardFactoryContractTests for every implemented card):
/// - Identity ({2}{B}{B} Enchantment Creature — Snake Glimmer, 4/3, mono-B).
/// - Lifegain-drain trigger condition (CR 119.3 / 603.6a): controller's
///   strictly-positive deltas only.
/// - Resolution drains "that much" life from the opponent — amount captured via
///   the SetPendingGainAmount test hook and via an EventBus subscription.
/// - Dies → return-to-battlefield + Layer-4 type-strip (CR 603.6c / 701.20 /
///   613.1d): after the return it's an enchantment but no longer a creature;
///   a subsequent death does not re-return it.
/// </summary>
[Trait("Color", "B")]
public class EnduringTenacityFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public void Dispose() => AgentRegistry.Clear();

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void EnduringTenacity_Identity()
    {
        var c = EnduringTenacityFactory.Create(_alice);

        c.Name.Should().Be("Enduring Tenacity");
        c.ManaCost.Should().Be("{2}{B}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.HasSubtype(CardSubtype.Snake).Should().BeTrue();
        c.HasSubtype(CardSubtype.Glimmer).Should().BeTrue();
        c.BasePower.Should().Be(4);
        c.BaseToughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Black);
        colors.Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Trigger shape
    // -----------------------------------------------------------------------

    [Fact]
    public void EnduringTenacity_HasTwoTriggers_BattlefieldActive()
    {
        var c = EnduringTenacityFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(2, "the lifegain-drain trigger + the dies-return trigger");
        triggers.Should().OnlyContain(t => t.ActiveZones.Contains(ZoneType.Battlefield));
    }

    // -----------------------------------------------------------------------
    // Lifegain-drain trigger condition (CR 119.3 / 603.6a)
    // -----------------------------------------------------------------------

    [Fact]
    public void LifegainTrigger_FiresForController_NotOpponent_OnlyOnGain()
    {
        var c = EnduringTenacityFactory.Create(_alice);
        var trigger = LifegainTrigger(c);

        trigger.Condition.Matches(new LifeChangedEvent(_alice, 20, 23), trigger).Should().BeTrue();
        trigger.Condition.Matches(new LifeChangedEvent(_bob, 20, 23), trigger).Should().BeFalse();
        trigger.Condition.Matches(new LifeChangedEvent(_alice, 20, 17), trigger).Should().BeFalse();
        trigger.Condition.Matches(new LifeChangedEvent(_alice, 20, 20), trigger).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Drain resolution — "that much"
    // -----------------------------------------------------------------------

    [Fact]
    public void ControllerGainsThree_OpponentLosesThree()
    {
        var c = EnduringTenacityFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);

        EnduringTenacityFactory.SetPendingGainAmount(c, 3);

        ResolveWithGame(LifegainTrigger(c), _alice, _alice, _bob);

        _bob.LifeTotal.Should().Be(17, "target opponent loses 3 — 'that much'");
    }

    [Fact]
    public void DrainsOpponent_OnProdBuild()
    {
        var c = (Creature)NamedCardFactory.Create("Enduring Tenacity", _alice);
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);

        EnduringTenacityFactory.SetPendingGainAmount(c, 4);

        ResolveWithGame(LifegainTrigger(c), _alice, _alice, _bob);

        _bob.LifeTotal.Should().Be(16,
            "the prod-built lifegain trigger reads opponents from the live context (not inert)");
    }

    [Fact]
    public void BusWiring_StampsAmountAutomatically()
    {
        var bus = new EventBus();
        var c = EnduringTenacityFactory.Create(
            _alice, eventBus: bus, triggers: null, continuousEffects: null, zoneService: null);
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);

        // Fire a LifeChangedEvent on the bus — the subscription should stamp the
        // "that much" amount slot (NewLife - PreviousLife = 5).
        bus.Publish(new LifeChangedEvent(_alice, 20, 25));

        ResolveWithGame(LifegainTrigger(c), _alice, _alice, _bob);

        _bob.LifeTotal.Should().Be(15, "target opponent loses 5 — controller gained 5 life");
    }

    [Fact]
    public void NoAmountStamp_DrainNoOps()
    {
        var c = EnduringTenacityFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);

        ResolveWithGame(LifegainTrigger(c), _alice, _alice, _bob);

        _bob.LifeTotal.Should().Be(20, "no life was gained — the drain clause no-ops");
    }

    // -----------------------------------------------------------------------
    // Dies → return as a (non-creature) enchantment
    // -----------------------------------------------------------------------

    [Fact]
    public void DiesTrigger_ReturnsToBattlefield_UnderOwnersControl()
    {
        var service = new ContinuousEffectsService();
        var c = EnduringTenacityFactory.Create(
            _alice, eventBus: null, triggers: null, continuousEffects: service, zoneService: null);

        c.SetOwner(_alice);
        c.SetController(_alice);
        _alice.Zones.Graveyard.AddCard(c);
        c.SetZone(ZoneType.Graveyard);

        var trig = DiesTrigger(c);
        foreach (var effect in trig.Effects) effect.Execute();

        c.Zone.Should().Be(ZoneType.Battlefield, "it returns to the battlefield (CR 701.20)");
        _alice.Zones.Battlefield.GetCards().Should().Contain(c);
        c.Controller.Should().BeSameAs(_alice, "under its owner's control");
    }

    [Fact]
    public void AfterReturn_ItsAnEnchantmentNotACreature()
    {
        var service = new ContinuousEffectsService();
        var c = EnduringTenacityFactory.Create(
            _alice, eventBus: null, triggers: null, continuousEffects: service, zoneService: null);

        c.SetOwner(_alice);
        c.SetController(_alice);
        _alice.Zones.Graveyard.AddCard(c);
        c.SetZone(ZoneType.Graveyard);

        foreach (var effect in DiesTrigger(c).Effects) effect.Execute();

        var chars = service.Compute((Permanent)c);
        chars.Types.Should().NotContain(CardType.Creature,
            "after returning, it's an enchantment, not a creature (CR 613.1d)");
        chars.Types.Should().Contain(CardType.Enchantment,
            "the printed Enchantment type is preserved (the strip is creature-only)");
    }

    [Fact]
    public void DiesTrigger_OnlyReturnsOnce_SecondDeathDoesNotReturn()
    {
        var service = new ContinuousEffectsService();
        var c = EnduringTenacityFactory.Create(
            _alice, eventBus: null, triggers: null, continuousEffects: service, zoneService: null);

        c.SetOwner(_alice);
        c.SetController(_alice);

        var diesTrigger = DiesTrigger(c);

        // First death → return.
        _alice.Zones.Graveyard.AddCard(c);
        c.SetZone(ZoneType.Graveyard);
        foreach (var effect in diesTrigger.Effects) effect.Execute();
        c.Zone.Should().Be(ZoneType.Battlefield);

        // Second death (now a non-creature enchantment) → intervening-if fails.
        _alice.Zones.Battlefield.RemoveCard(c);
        _alice.Zones.Graveyard.AddCard(c);
        c.SetZone(ZoneType.Graveyard);
        foreach (var effect in diesTrigger.Effects) effect.Execute();

        c.Zone.Should().Be(ZoneType.Graveyard,
            "once it has returned as a non-creature enchantment, dying again does not re-return it");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static TriggeredAbility LifegainTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<LifeChangedEvent>);

    private static TriggeredAbility DiesTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CardMovedEvent>);

    private static void ResolveWithGame(
        TriggeredAbility trigger, Player controller, params Player[] players)
    {
        var game = new GameContext(
            self: controller,
            allPlayers: players,
            activePlayer: controller,
            turnNumber: 1,
            currentPhase: null,
            stack: new Majik.Core.Stack.Stack(new EventBus()));

        trigger.ResolveAsync(agent: null, game: game).AsTask().GetAwaiter().GetResult();
    }
}
