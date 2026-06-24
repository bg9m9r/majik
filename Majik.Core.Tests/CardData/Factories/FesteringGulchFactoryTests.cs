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
/// Unit tests for <see cref="FesteringGulchFactory"/> (Outlaws of Thunder
/// Junction "painland Desert" cycle — the B/G member).
///
/// B/G damage-dealing tapland. Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    When this land enters, it deals 1 damage to target opponent.
///    {T}: Add {B} or {G}."
///
/// Type line is <c>Land — Desert</c>. Identity + dual mana load from the
/// embedded JSON definition via
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/>; the
/// ETB damage trigger is wired in code (Abraded Bluffs pattern, swapped to
/// the B/G colour pair).
///
/// Covers:
/// - Identity (Land, Desert subtype, nonbasic, owner/controller).
/// - Two single-colour mana abilities — {B} and {G} (CR 605.1a).
/// - ETB triggered ability (CR 603.6a) — 1 damage to the chosen opponent.
/// - Enters-tapped (CR 614.1c) — present when wired through a
///   <see cref="ReplacementBus"/>; absent on the shape-only path.
/// - Fizzle (CR 608.2b) — no target chosen → clean no-op.
/// </summary>
[Trait("Color", "C")]
public class FesteringGulchFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dual mana
    // -----------------------------------------------------------------------

    [Fact]
    public void FesteringGulch_Identity_LandWithDesertSubtype()
    {
        var land = FesteringGulchFactory.Create(_alice);

        land.Name.Should().Be("Festering Gulch");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSubtype(CardSubtype.Desert).Should().BeTrue(
            "Festering Gulch's printed type line is 'Land — Desert'");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Festering Gulch is a nonbasic Land");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
        land.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // {T}: Add {B} or {G}
    // -----------------------------------------------------------------------

    [Fact]
    public void FesteringGulch_HasManaAbility_ForBlack()
    {
        var land = FesteringGulchFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Black == 1 && m.ManaGenerated.Green == 0);
    }

    [Fact]
    public void FesteringGulch_HasManaAbility_ForGreen()
    {
        var land = FesteringGulchFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Green == 1 && m.ManaGenerated.Black == 0);
    }

    // -----------------------------------------------------------------------
    // Enters-tapped (CR 614.1c)
    // -----------------------------------------------------------------------

    /// <summary>
    /// CR 614.1c — "This land enters tapped." When the
    /// EntersTappedReplacement is registered on the ReplacementBus, the
    /// ZoneService.MoveCardTo path sets IsTapped on landing. (Production
    /// load wires this from the oracle text via EntersTappedBinder; the
    /// test registers it directly to exercise the tapped landing.)
    /// </summary>
    [Fact]
    public void FesteringGulch_EntersTapped_WhenReplacementRegistered()
    {
        var (zones, _, triggers, rep) = BuildEngine();

        var land = FesteringGulchFactory.Create(_alice, triggers);
        rep.Register(new EntersTappedReplacement(land));
        land.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(land);
        triggers.BindCard(land);

        zones.MoveCardTo(land, ZoneType.Battlefield, controller: _alice);

        land.IsTapped.Should().BeTrue("CR 614.1c — Festering Gulch enters tapped");
        land.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void FesteringGulch_EntersUntapped_OnShapeOnlyPath()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);

        var land = FesteringGulchFactory.Create(_alice);
        land.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(land);

        zones.MoveCardTo(land, ZoneType.Battlefield, controller: _alice);

        land.IsTapped.Should().BeFalse(
            "shape-only path omits the enters-tapped replacement");
    }

    // -----------------------------------------------------------------------
    // ETB trigger — 1 damage to target opponent (CR 603.6a)
    // -----------------------------------------------------------------------

    [Fact]
    public void FesteringGulch_EtbTrigger_IsBattlefieldActive()
    {
        var land = FesteringGulchFactory.Create(_alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    /// <summary>
    /// CR 603.6a — "When this land enters, it deals 1 damage to target
    /// opponent." With the opponent chosen as the target, the chosen
    /// player loses 1 life on resolution (Player → Player.LoseLife via
    /// Fx.DealDamageAny).
    /// </summary>
    [Fact]
    public void FesteringGulch_EntersBattlefield_OnePointToTargetOpponent()
    {
        var (zones, stack, triggers, _) = BuildEngine();

        var land = FesteringGulchFactory.Create(_alice, triggers);
        land.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(land);
        triggers.BindCard(land);

        var bobLifeBefore = _bob.LifeTotal;

        zones.MoveCardTo(land, ZoneType.Battlefield, controller: _alice);

        triggers.PendingCount.Should().Be(1,
            "ETB damage trigger must queue on entering battlefield");

        var etbTrigger = land.Abilities.OfType<TriggeredAbility>().Single();
        etbTrigger.SetChosenTargets(new[]
        {
            (IReadOnlyList<object>)new object[] { _bob },
        });

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _bob.LifeTotal.Should().Be(bobLifeBefore - FesteringGulchFactory.DamageAmount,
            "target opponent should lose 1 life on Festering Gulch's ETB");
    }

    /// <summary>
    /// CR 608.2b — when no target was chosen, the damage effect is a clean
    /// no-op. No life total changes.
    /// </summary>
    [Fact]
    public void FesteringGulch_NoTargetChosen_DamageNoOps()
    {
        var (zones, stack, triggers, _) = BuildEngine();

        var land = FesteringGulchFactory.Create(_alice, triggers);
        land.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(land);
        triggers.BindCard(land);

        var aliceLifeBefore = _alice.LifeTotal;
        var bobLifeBefore = _bob.LifeTotal;

        zones.MoveCardTo(land, ZoneType.Battlefield, controller: _alice);

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
