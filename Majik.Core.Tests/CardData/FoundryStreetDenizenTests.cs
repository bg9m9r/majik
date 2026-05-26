using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="FoundryStreetDenizenFactory"/>.
///
/// Foundry Street Denizen (Magic 2014, {R}):
///   Creature — Goblin Warrior 1/1.
///   Whenever another red creature enters under your control, Foundry
///   Street Denizen gets +1/+0 until end of turn.
///
/// Covers:
///   - Identity (Goblin Warrior 1/1, {R}, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - ETB-trigger predicate: matches another red creature entering under
///     the Denizen's controller; rejects self-ETB, non-red creatures,
///     non-creature reds, and opponent-controlled reds.
///   - Pump-effect resolution registers a +1/+0 EOT effect on the
///     <see cref="ContinuousEffectsService"/> and bumps power read-through.
///   - Shape-only path (no continuous-effects service) is a silent no-op.
/// </summary>
public class FoundryStreetDenizenTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void FoundryStreetDenizen_Identity()
    {
        var d = FoundryStreetDenizenFactory.Create(_alice);

        d.Name.Should().Be("Foundry Street Denizen");
        d.ManaCost.Should().Be("{R}");
        d.HasType(CardType.Creature).Should().BeTrue();
        d.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        d.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        d.BasePower.Should().Be(1);
        d.BaseToughness.Should().Be(1);
        d.Owner.Should().BeSameAs(_alice);
        d.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void FoundryStreetDenizen_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Foundry Street Denizen", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Foundry Street Denizen");
        card.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        card.HasSubtype(CardSubtype.Warrior).Should().BeTrue();

        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "single ETB trigger for another red creature entering");
    }

    [Fact]
    public void FoundryStreetDenizen_Trigger_HasBattlefieldActiveZone()
    {
        var d = FoundryStreetDenizenFactory.Create(_alice);
        var t = d.Abilities.OfType<TriggeredAbility>().Single();

        t.ActiveZones.Should().Contain(ZoneType.Battlefield,
            "the printed triggered ability functions only from the battlefield (CR 603.6c)");
    }

    // -----------------------------------------------------------------------
    // Trigger predicate
    // -----------------------------------------------------------------------

    [Fact]
    public void Trigger_AnotherRedCreatureEntersUnderControl_Matches()
    {
        var d = FoundryStreetDenizenFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(d);
        d.SetZone(ZoneType.Battlefield);

        var goblin = new Creature("Mogg Fanatic", "{R}", 1, 1,
            subtypes: new[] { CardSubtype.Goblin });
        goblin.SetOwner(_alice);
        goblin.SetController(_alice);

        var trigger = d.Abilities.OfType<TriggeredAbility>().Single();
        var evt = new CardMovedEvent(
            card: goblin,
            fromZone: ZoneType.Hand,
            toZone: ZoneType.Battlefield);

        trigger.Condition.Matches(evt, trigger).Should().BeTrue();
    }

    [Fact]
    public void Trigger_SelfEnter_DoesNotMatch()
    {
        // "Another" — Denizen's own ETB doesn't fire its own trigger
        // (CR 109.5).
        var d = FoundryStreetDenizenFactory.Create(_alice);

        var trigger = d.Abilities.OfType<TriggeredAbility>().Single();
        var evt = new CardMovedEvent(
            card: d,
            fromZone: ZoneType.Hand,
            toZone: ZoneType.Battlefield);

        trigger.Condition.Matches(evt, trigger).Should().BeFalse(
            "Denizen's own ETB does NOT fire its own trigger (\"another\")");
    }

    [Fact]
    public void Trigger_NonRedCreatureEnters_DoesNotMatch()
    {
        var d = FoundryStreetDenizenFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(d);
        d.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);

        var trigger = d.Abilities.OfType<TriggeredAbility>().Single();
        var evt = new CardMovedEvent(
            card: bear,
            fromZone: ZoneType.Hand,
            toZone: ZoneType.Battlefield);

        trigger.Condition.Matches(evt, trigger).Should().BeFalse(
            "non-red creature does NOT fire Denizen's trigger");
    }

    [Fact]
    public void Trigger_NonCreatureRedSpellEnters_DoesNotMatch()
    {
        // A non-creature red permanent (e.g. red artifact / enchantment in
        // theory) shouldn't fire the trigger — it requires the entering
        // card to be a creature.
        var d = FoundryStreetDenizenFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(d);
        d.SetZone(ZoneType.Battlefield);

        var redArtifact = new Artifact("Red Trinket", "{R}");
        redArtifact.SetOwner(_alice);
        redArtifact.SetController(_alice);

        var trigger = d.Abilities.OfType<TriggeredAbility>().Single();
        var evt = new CardMovedEvent(
            card: redArtifact,
            fromZone: ZoneType.Hand,
            toZone: ZoneType.Battlefield);

        trigger.Condition.Matches(evt, trigger).Should().BeFalse(
            "non-creature reds don't fire (predicate requires CardType.Creature)");
    }

    [Fact]
    public void Trigger_OpponentRedCreatureEnters_DoesNotMatch()
    {
        var d = FoundryStreetDenizenFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(d);
        d.SetZone(ZoneType.Battlefield);

        var oppGoblin = new Creature("Mogg Fanatic", "{R}", 1, 1,
            subtypes: new[] { CardSubtype.Goblin });
        oppGoblin.SetOwner(_bob);
        oppGoblin.SetController(_bob);

        var trigger = d.Abilities.OfType<TriggeredAbility>().Single();
        var evt = new CardMovedEvent(
            card: oppGoblin,
            fromZone: ZoneType.Hand,
            toZone: ZoneType.Battlefield);

        trigger.Condition.Matches(evt, trigger).Should().BeFalse(
            "opponent's red creature does NOT fire (CR 109.5 — 'under your control')");
    }

    // -----------------------------------------------------------------------
    // Trigger resolution — pump effect
    // -----------------------------------------------------------------------

    [Fact]
    public void TriggerEffect_RegistersPumpUntilEndOfTurn_BumpsPower()
    {
        var effects = new ContinuousEffectsService();
        var d = FoundryStreetDenizenFactory.Create(
            _alice, eventBus: null, triggers: null, effects: effects);
        _alice.Zones.Battlefield.AddCard(d);
        d.SetZone(ZoneType.Battlefield);

        // Base 1/1 before the pump resolves — Power read goes through
        // ActiveEffects which has no pumps yet.
        d.Power.Should().Be(1);
        d.Toughness.Should().Be(1);

        var trigger = d.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        // CR 613.1f Layer 7c — +1/+0 layered onto base power.
        d.Power.Should().Be(2, "Denizen is 2/1 after one red-creature ETB pump");
        d.Toughness.Should().Be(1, "+1/+0 leaves toughness alone");
    }

    [Fact]
    public void TriggerEffect_NoServiceSupplied_SilentNoOp()
    {
        // Shape-only path — pump silently skipped, no exception, no
        // mutation. Power read falls back to BasePower (no ActiveEffects).
        var d = FoundryStreetDenizenFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(d);
        d.SetZone(ZoneType.Battlefield);

        var trigger = d.Abilities.OfType<TriggeredAbility>().Single();
        var act = () => { foreach (var e in trigger.Effects) e.Execute(); };

        act.Should().NotThrow(
            "shape-only path silently skips the pump when no service is wired");
        d.Power.Should().Be(1, "no pump applied without continuous-effects service");
        d.Toughness.Should().Be(1);
    }
}
