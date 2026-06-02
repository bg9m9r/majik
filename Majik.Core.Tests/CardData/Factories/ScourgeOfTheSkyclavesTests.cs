using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// End-to-end tests for Scourge of the Skyclaves — Creature — Demon {1}{B},
/// power/toughness "*/*".
///
/// Oracle text (Scryfall verified):
///   "Kicker {4}{B}
///    When you cast this spell, if it was kicked, each player loses half
///    their life, rounded up.
///    Scourge of the Skyclaves's power and toughness are each equal to 20
///    minus the highest life total among players."
///
/// Two behaviours, both built on existing engine primitives:
///   * CDA P/T (CR 604.3 / 613.2 Layer 7a) — clamp(20 - highest life among
///     players, 0, 20). Death's Shadow shape, but the life lookup spans all
///     players via an allPlayersResolver (CR 613.2; "among players").
///   * Cast trigger (CR 603.3 / 702.33b) — "when you cast this spell, if it
///     was kicked, each player loses half their life, rounded up." Keyed on
///     SpellCastEvent for this card with an intervening-if on Card.WasKicked
///     (CR 603.4), active on the Stack (same shape as Cascade).
///
/// Validates identity + dispatch, kicker rider, CDA endpoints, the
/// half-life-rounded-up math, and the kicked / not-kicked cast-trigger
/// branches.
/// </summary>
[Trait("Color", "B")]
public class ScourgeOfTheSkyclavesTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private IReadOnlyList<Player> AllPlayers() => new[] { _alice, _bob };

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Scourge_IsDemonCreature_AtCost1B()
    {
        var scourge = ScourgeOfTheSkyclavesFactory.Create(_alice);

        scourge.Name.Should().Be("Scourge of the Skyclaves");
        scourge.HasType(CardType.Creature).Should().BeTrue();
        scourge.HasSubtype(CardSubtype.Demon).Should().BeTrue();
        scourge.ManaCost.Should().Be("{1}{B}");
        scourge.Owner.Should().BeSameAs(_alice);
        scourge.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // Kicker rider
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildAdditionalCost_ReturnsKickerCostAt4B()
    {
        var scourge = ScourgeOfTheSkyclavesFactory.Create(_alice);
        var cost = ScourgeOfTheSkyclavesFactory.BuildAdditionalCost(scourge);

        cost.Should().BeOfType<KickerAdditionalCost>();
        ((KickerAdditionalCost)cost).KickerCost.Should().Be(ManaCost.Parse("{4}{B}"));
    }

    [Fact]
    public void KickerAltCostProbe_Recognises_Scourge()
    {
        var scourge = ScourgeOfTheSkyclavesFactory.Create(_alice);
        scourge.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(scourge);

        var probe = new KickerAltCostProbe();
        probe.KickerCostFor(scourge, _alice).Should().Be(ManaCost.Parse("{4}{B}"));
    }

    // -----------------------------------------------------------------------
    // CDA P/T — clamp(20 - highest life among players, 0, 20)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(20, 20, 0)]   // highest 20 → 20-20 = 0
    [InlineData(20, 13, 0)]   // highest is Alice's 20 → 0
    [InlineData(12, 8, 8)]    // highest 12 → 20-12 = 8
    [InlineData(5, 1, 15)]    // highest 5 → 15
    [InlineData(0, 0, 20)]    // highest 0 → 20 (printed cap)
    [InlineData(-3, 2, 18)]   // highest 2 → 18 (negative life ignored)
    [InlineData(40, 5, 0)]    // highest 40 → 20-40 = -20, clamped to 0
    public void ComputePT_UsesHighestLifeAmongPlayers_ClampedZeroToTwenty(
        int aliceLife, int bobLife, int expected)
    {
        ScourgeOfTheSkyclavesFactory
            .ComputePT(new[] { aliceLife, bobLife })
            .Should().Be(expected);
    }

    [Fact]
    public void Scourge_CdaTracksHighestLifeAcrossPlayers_Live()
    {
        var bus = new EventBus();
        var effects = new ContinuousEffectsService();
        var zones = new ZoneService(bus);

        _alice.LifeTotal = 20;
        _bob.LifeTotal = 20;

        var scourge = ScourgeOfTheSkyclavesFactory.Create(
            _alice, AllPlayers, effects, bus, triggers: null);
        scourge.ActiveEffects = effects;
        scourge.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(scourge);

        zones.MoveCard(scourge, ZoneType.Library, ZoneType.Battlefield, _alice);

        // Highest is 20 → 0/0.
        scourge.Power.Should().Be(0);
        scourge.Toughness.Should().Be(0);

        // Alice drops to 5, Bob still 20 → highest 20 → still 0/0. Life loss
        // here pokes Player.LoseLife directly (no PlayerService/event), so
        // invalidate the layer-system cache explicitly via Clear() —
        // production's LifeChangedEvent would do this.
        _alice.LoseLife(15);
        effects.Clear();
        scourge.Power.Should().Be(0);

        // Bob drops to 12 → highest now 12 → 20-12 = 8/8.
        _bob.LoseLife(8);
        effects.Clear();
        scourge.Power.Should().Be(8);
        scourge.Toughness.Should().Be(8);
    }

    // -----------------------------------------------------------------------
    // Cast trigger — "if it was kicked, each player loses half their life,
    // rounded up." CR 603.3 / 702.33b.
    // -----------------------------------------------------------------------

    [Fact]
    public void Scourge_HasCastTrigger_InterveningIfKicked_ActiveOnStack()
    {
        var scourge = ScourgeOfTheSkyclavesFactory.Create(_alice);

        var castTrigger = scourge.Abilities
            .OfType<TriggeredAbility>()
            .Single(t => t.ActiveZones.Contains(ZoneType.Stack));

        castTrigger.InterveningIf.Should().NotBeNull(
            "the cast trigger is intervening-if-kicked — CR 603.4 / 702.33b");
    }

    [Fact]
    public void CastTrigger_Kicked_EachPlayerLosesHalfLifeRoundedUp()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        _alice.LifeTotal = 20; // half = 10
        _bob.LifeTotal = 15;   // half = 7.5 → rounded up = 8

        var scourge = ScourgeOfTheSkyclavesFactory.Create(
            _alice, AllPlayers, effects: null, eventBus: bus, triggers: triggers);
        scourge.SetWasKicked(true); // simulate kicker-paid cast (CR 702.33).
        scourge.SetZone(ZoneType.Stack);

        // Fire the cast trigger by publishing the SpellCastEvent.
        var spell = new Majik.Core.Spells.Spell(scourge, _alice);
        bus.Publish(new SpellCastEvent(spell));

        triggers.PendingCount.Should().BeGreaterThanOrEqualTo(1,
            "kicked → intervening-if true → cast trigger queues");
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.LifeTotal.Should().Be(10, "20 - ceil(20/2) = 10");
        _bob.LifeTotal.Should().Be(7, "15 - ceil(15/2) = 15 - 8 = 7");
    }

    [Fact]
    public void CastTrigger_NotKicked_NoLifeLoss()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        _alice.LifeTotal = 20;
        _bob.LifeTotal = 15;

        var scourge = ScourgeOfTheSkyclavesFactory.Create(
            _alice, AllPlayers, effects: null, eventBus: bus, triggers: triggers);
        // WasKicked stays false — no kicker paid.
        scourge.SetZone(ZoneType.Stack);

        var spell = new Majik.Core.Spells.Spell(scourge, _alice);
        bus.Publish(new SpellCastEvent(spell));

        // CR 603.4 — intervening-if false → trigger never queues.
        if (triggers.PendingCount > 0)
        {
            triggers.PutPendingTriggersOnStack(_alice);
            while (stack.Top != null) stack.Pop()!.Resolve();
        }

        _alice.LifeTotal.Should().Be(20, "not kicked → no life loss");
        _bob.LifeTotal.Should().Be(15);
    }

    // -----------------------------------------------------------------------
    // Pure helper sanity — half rounded up (CR 120 / "rounded up").
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(20, 10)]
    [InlineData(15, 8)]
    [InlineData(1, 1)]
    [InlineData(7, 4)]
    [InlineData(0, 0)]
    public void HalfRoundedUp_Matches(int life, int expected)
    {
        ScourgeOfTheSkyclavesFactory.HalfRoundedUp(life).Should().Be(expected);
    }
}
