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
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SunscorchedDesertFactory"/> (Hour of
/// Devastation).
///
/// Covers:
/// - Identity (Land, no printed subtype, owner / controller).
/// - {T}: Add {C} mana ability present.
/// - Enters-tapped replacement (CR 614.1c) — present when wired through
///   <see cref="ReplacementBus"/>; absent on shape-only path (Desert
///   enters untapped).
/// - ETB triggered ability (CR 603.6a) — 1 damage to a chosen Player.
/// - ETB triggered ability (CR 603.6a) — 1 damage to a chosen Creature
///   AND 1 loyalty removed from a chosen Planeswalker (via
///   <see cref="SearingBlazeFactory.DealDamageWithPlaneswalker"/>).
/// - Fizzle (CR 608.2b) — no target chosen → clean no-op.
/// - <see cref="NamedCardFactory"/> dispatch.
/// </summary>
public class SunscorchedDesertFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SunscorchedDesert_Identity_LandWithNoSubtype()
    {
        var d = SunscorchedDesertFactory.Create(_alice);

        d.Name.Should().Be("Sunscorched Desert");
        d.HasType(CardType.Land).Should().BeTrue();
        d.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Sunscorched Desert is a non-basic Land");
        // Printed type on Hour of Devastation is just "Land" — no Desert
        // subtype on this card (later Desert cycles added the subtype
        // but never to Sunscorched Desert itself).
        d.Owner.Should().BeSameAs(_alice);
        d.Controller.Should().BeSameAs(_alice);

        // {T}: Add {C} + the ETB damage trigger.
        d.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        d.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void SunscorchedDesert_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Sunscorched Desert", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Sunscorched Desert");
        card.HasType(CardType.Land).Should().BeTrue();
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Enters-tapped (CR 614.1c)
    // -----------------------------------------------------------------------

    /// <summary>
    /// CR 614.1c — "Sunscorched Desert enters tapped." When the
    /// EntersTappedReplacement is registered on the ReplacementBus, the
    /// ZoneService.MoveCardTo path sets IsTapped on landing.
    /// </summary>
    [Fact]
    public void SunscorchedDesert_EntersTapped_WhenWiredThroughReplacementBus()
    {
        var (zones, _, triggers, rep) = BuildEngine();

        var desert = SunscorchedDesertFactory.Create(_alice, eventBus: null, triggers, rep);
        desert.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(desert);
        triggers.BindCard(desert);

        zones.MoveCardTo(desert, ZoneType.Battlefield, controller: _alice);

        desert.IsTapped.Should().BeTrue(
            "CR 614.1c — Sunscorched Desert enters tapped");
        desert.Zone.Should().Be(ZoneType.Battlefield);
    }

    /// <summary>
    /// Shape-only path (no ReplacementBus): the enters-tapped
    /// replacement is omitted, mirroring how Creeping Tar Pit / Valakut /
    /// Geralf's Messenger defer the restriction to the binder layer for
    /// shape construction. Desert enters untapped when moved through a
    /// ZoneService with no replacement-bus binding.
    /// </summary>
    [Fact]
    public void SunscorchedDesert_EntersUntapped_OnShapeOnlyPath()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);

        var desert = SunscorchedDesertFactory.Create(_alice);
        desert.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(desert);

        zones.MoveCardTo(desert, ZoneType.Battlefield, controller: _alice);

        desert.IsTapped.Should().BeFalse(
            "shape-only path omits the enters-tapped replacement");
    }

    // -----------------------------------------------------------------------
    // {T}: Add {C}
    // -----------------------------------------------------------------------

    [Fact]
    public void SunscorchedDesert_HasTapAddColorlessManaAbility()
    {
        var d = SunscorchedDesertFactory.Create(_alice);

        var mana = d.Abilities.OfType<ManaAbility>().Should().ContainSingle().Subject;
        // {C} folds into the generic bucket per ManaCost.Parse (the same
        // posture as Aether Hub's {T}: Add {C}). We assert non-zero
        // produced mana rather than colour identity here.
        mana.ManaGenerated.TotalValue.Should().Be(1,
            "{T}: Add {C} produces exactly one mana");
    }

    // -----------------------------------------------------------------------
    // ETB trigger — 1 damage to any target (CR 603.6a)
    // -----------------------------------------------------------------------

    /// <summary>
    /// CR 603.6a — "When this land enters, it deals 1 damage to any
    /// target." With a Player chosen as the target, the chosen Player
    /// loses 1 life on resolution via the SearingBlazeFactory dispatcher
    /// (Player → Player.LoseLife).
    /// </summary>
    [Fact]
    public void SunscorchedDesert_EntersBattlefield_OnePointToTargetPlayer()
    {
        var (zones, stack, triggers, rep) = BuildEngine();

        var desert = SunscorchedDesertFactory.Create(_alice, eventBus: null, triggers, rep);
        desert.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(desert);
        triggers.BindCard(desert);

        var bobLifeBefore = _bob.LifeTotal;

        zones.MoveCardTo(desert, ZoneType.Battlefield, controller: _alice);

        triggers.PendingCount.Should().Be(1,
            "ETB damage trigger must queue on entering battlefield");

        var etbTrigger = desert.Abilities.OfType<TriggeredAbility>().Single();
        etbTrigger.SetChosenTargets(new[]
        {
            (IReadOnlyList<object>)new object[] { _bob },
        });

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _bob.LifeTotal.Should().Be(bobLifeBefore - SunscorchedDesertFactory.DamageAmount,
            "target player should lose 1 life on Sunscorched Desert's ETB");
    }

    /// <summary>
    /// CR 603.6a — with a Creature chosen as the target, the chosen
    /// Creature takes 1 damage on resolution (Creature →
    /// Creature.TakeDamage path through
    /// SearingBlazeFactory.DealDamageWithPlaneswalker).
    /// </summary>
    [Fact]
    public void SunscorchedDesert_EntersBattlefield_OnePointToTargetCreature()
    {
        var (zones, stack, triggers, rep) = BuildEngine();

        // Seed a Creature on Bob's battlefield.
        var grizzly = new Creature(
            name: "Grizzly Bears",
            manaCost: "{1}{G}",
            power: 2,
            toughness: 2,
            subtypes: new[] { CardSubtype.Bear });
        grizzly.SetOwner(_bob);
        grizzly.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(grizzly);
        grizzly.SetZone(ZoneType.Battlefield);

        var desert = SunscorchedDesertFactory.Create(_alice, eventBus: null, triggers, rep);
        desert.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(desert);
        triggers.BindCard(desert);

        zones.MoveCardTo(desert, ZoneType.Battlefield, controller: _alice);

        var etbTrigger = desert.Abilities.OfType<TriggeredAbility>().Single();
        etbTrigger.SetChosenTargets(new[]
        {
            (IReadOnlyList<object>)new object[] { grizzly },
        });

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        grizzly.Damage.Should().Be(SunscorchedDesertFactory.DamageAmount,
            "target creature should take 1 damage on Sunscorched Desert's ETB");
    }

    /// <summary>
    /// CR 306.7 — damage to a planeswalker removes that much loyalty.
    /// Routed through SearingBlazeFactory.DealDamageWithPlaneswalker so
    /// the "any target" slot covers all three legal subtypes.
    /// </summary>
    [Fact]
    public void SunscorchedDesert_EntersBattlefield_OnePointToTargetPlaneswalker()
    {
        var (zones, stack, triggers, rep) = BuildEngine();

        var pw = new Planeswalker(
            name: "Test Walker",
            manaCost: "{2}{R}",
            startingLoyalty: 4,
            subtypes: new[] { CardSubtype.Chandra });
        pw.SetOwner(_bob);
        pw.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(pw);
        pw.SetZone(ZoneType.Battlefield);

        var desert = SunscorchedDesertFactory.Create(_alice, eventBus: null, triggers, rep);
        desert.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(desert);
        triggers.BindCard(desert);

        zones.MoveCardTo(desert, ZoneType.Battlefield, controller: _alice);

        var etbTrigger = desert.Abilities.OfType<TriggeredAbility>().Single();
        etbTrigger.SetChosenTargets(new[]
        {
            (IReadOnlyList<object>)new object[] { pw },
        });

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        pw.Loyalty.Should().Be(4 - SunscorchedDesertFactory.DamageAmount,
            "CR 306.7 — 1 damage to a planeswalker removes 1 loyalty counter");
    }

    /// <summary>
    /// CR 608.2b — when no target was chosen (or the slot is empty), the
    /// damage effect is a clean no-op. Mirrors Valakut / Earthshaker
    /// Khenra / Phlage's fizzle posture.
    /// </summary>
    [Fact]
    public void SunscorchedDesert_NoTargetChosen_DamageNoOps()
    {
        var (zones, stack, triggers, rep) = BuildEngine();

        var desert = SunscorchedDesertFactory.Create(_alice, eventBus: null, triggers, rep);
        desert.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(desert);
        triggers.BindCard(desert);

        var aliceLifeBefore = _alice.LifeTotal;
        var bobLifeBefore = _bob.LifeTotal;

        zones.MoveCardTo(desert, ZoneType.Battlefield, controller: _alice);

        // Leave ChosenTargets unset and put the trigger on the stack.
        triggers.PutPendingTriggersOnStack(_alice);
        if (!stack.IsEmpty)
        {
            stack.Pop()!.Resolve();
        }

        _alice.LifeTotal.Should().Be(aliceLifeBefore,
            "fizzle (no target) must not change any life total");
        _bob.LifeTotal.Should().Be(bobLifeBefore);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static (ZoneService zones, Majik.Core.Stack.Stack stack, TriggerManager triggers, ReplacementBus replacements) BuildEngine()
    {
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        return (zones, stack, triggers, rep);
    }
}
