using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="RestlessApparitionFactory"/>.
///
/// Restless Apparition — Creature — Spirit 2/2, {W/B}{W/B}{W/B}:
///   "{W/B}{W/B}{W/B}: This creature gets +3/+3 until end of turn.
///    Persist (When this creature dies, if it had no -1/-1 counters on it,
///    return it to the battlefield under its owner's control with a -1/-1
///    counter on it.)"
///
/// Mirrors <see cref="Majik.Core.Tests.Cards.KitchenFinksTests"/> for the
/// Persist half and <see cref="Majik.Core.Tests.Cards.KitchenFinksTests"/>'s
/// JSON-base identity shape, plus the activated self-pump.
/// </summary>
public class RestlessApparitionFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Player _alice = new("Alice", 20);

    // ------------------------------------------------------------------
    // Identity (from JSON definition)
    // ------------------------------------------------------------------

    [Fact]
    public void RestlessApparition_Identity()
    {
        var c = RestlessApparitionFactory.Create(_alice);

        c.Name.Should().Be("Restless Apparition");
        c.Should().BeOfType<Creature>();
        c.ManaCost.Should().Be("{W/B}{W/B}{W/B}");
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(2);
        c.Subtypes.Should().Contain(CardSubtype.Spirit);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void RestlessApparition_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Restless Apparition", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Restless Apparition");
        c.ManaCost.Should().Be("{W/B}{W/B}{W/B}");
    }

    // ------------------------------------------------------------------
    // Activated pump — {W/B}{W/B}{W/B}: +3/+3 until end of turn
    // ------------------------------------------------------------------

    /// <summary>
    /// CR 602 — the factory wires a single activated ability whose cost is the
    /// three hybrid {W/B} pips and which has no targets (self-pump).
    /// </summary>
    [Fact]
    public void RestlessApparition_HasPumpActivatedAbility_WithHybridCost()
    {
        var c = RestlessApparitionFactory.Create(_alice);

        var activated = c.Abilities.OfType<ActivatedAbility>().Should().ContainSingle().Subject;
        activated.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the pump's activation cost is a single mana cost — the three hybrid {W/B} pips (CR 107.4e)");
        activated.TargetRequests.Should().BeEmpty("the pump targets nothing — it pumps this creature");
    }

    /// <summary>
    /// CR 613.7c — on resolution the pump registers a +3/+3
    /// <see cref="PumpUntilEndOfTurnEffect"/> on this creature, which expires in
    /// the cleanup step (CR 514.2).
    /// </summary>
    [Fact]
    public void RestlessApparition_PumpResolution_GrantsPlus3Plus3_ExpiringEndOfTurn()
    {
        var effects = new ContinuousEffectsService();
        var c = RestlessApparitionFactory.Create(_alice, effects);
        c.ActiveEffects = effects;
        c.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(c);

        var activated = c.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var fx in activated.Effects)
        {
            fx.Execute();
        }

        var chars = effects.Compute(c);
        chars.Power.Should().Be(5, "2 base +3 pump");
        chars.Toughness.Should().Be(5, "2 base +3 pump");

        // Cleanup-step expiry (CR 514.2).
        effects.ExpireEndOfTurn();
        var after = effects.Compute(c);
        after.Power.Should().Be(2, "pump expires at end of turn");
        after.Toughness.Should().Be(2);
    }

    /// <summary>
    /// Shape-only path: with no continuous-effects service wired the activated
    /// ability still resolves cleanly (the +3/+3 simply isn't tracked).
    /// </summary>
    [Fact]
    public void RestlessApparition_PumpResolution_NoEffectsService_IsCleanNoOp()
    {
        var c = RestlessApparitionFactory.Create(_alice);

        var activated = c.Abilities.OfType<ActivatedAbility>().Single();
        var act = () =>
        {
            foreach (var fx in activated.Effects)
            {
                fx.Execute();
            }
        };

        act.Should().NotThrow("no continuous-effects service → documented no-op pump");
    }

    // ------------------------------------------------------------------
    // Persist (CR 702.79)
    // ------------------------------------------------------------------

    private Creature MakeApparition()
    {
        var c = RestlessApparitionFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(c);
        return c;
    }

    /// <summary>
    /// CR 702.79 — when it dies with no -1/-1 counters, it returns to the
    /// battlefield under its owner's control with one -1/-1 counter.
    /// </summary>
    [Fact]
    public void RestlessApparition_DiesWithNoMinusCounters_ReturnsWithCounter()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var zones = new ZoneService(_bus);

        var c = MakeApparition();
        triggers.BindCard(c);

        zones.MoveCardTo(c, ZoneType.Graveyard);

        triggers.PendingCount.Should().Be(1, "Persist trigger queues on death without a -1/-1 counter");
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        c.Zone.Should().Be(ZoneType.Battlefield);
        _alice.Zones.Battlefield.GetCards().Should().Contain(c);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(c);
        c.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(1);
    }

    /// <summary>
    /// CR 702.79 + CR 603.4 — a creature that already had a -1/-1 counter when
    /// it died does NOT return (interveningIf gate fails).
    /// </summary>
    [Fact]
    public void RestlessApparition_DiesWithMinusCounter_StaysInGraveyard()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var zones = new ZoneService(_bus);

        var c = MakeApparition();
        triggers.BindCard(c);

        c.Counters.Add(CounterType.MinusOneMinusOne, 1);
        zones.MoveCardTo(c, ZoneType.Graveyard);

        triggers.PendingCount.Should().Be(0,
            "Persist must not trigger when a -1/-1 counter was present at death");
        c.Zone.Should().Be(ZoneType.Graveyard);
    }

    /// <summary>CR 702.79 keyword marker is attached for inspectors/tooltips.</summary>
    [Fact]
    public void RestlessApparition_HasPersistKeywordMarker()
    {
        var c = RestlessApparitionFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Persist");
    }
}
