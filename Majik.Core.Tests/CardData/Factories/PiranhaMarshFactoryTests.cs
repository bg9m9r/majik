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
/// Unit tests for <see cref="PiranhaMarshFactory"/> (Conflux mono-black
/// life-loss tapland).
///
/// Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    When this land enters, target player loses 1 life.
///    {T}: Add {B}."
///
/// Identity + the single {B} mana ability load from the embedded JSON
/// definition via
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/>; the
/// ETB life-loss trigger is wired in code (Abraded Bluffs pattern, with
/// "deals damage to target opponent" swapped for "target player loses 1
/// life" — a direct <see cref="Player.LoseLife"/> on the chosen player,
/// per CR 119.3; the target is any player, not just an opponent).
///
/// Covers:
/// - Identity (Land, no subtype, nonbasic, owner/controller).
/// - Single {B} mana ability (CR 605.1a).
/// - ETB triggered ability (CR 603.6a) — chosen player loses 1 life.
/// - Enters-tapped (CR 614.1c) — present when wired through a
///   <see cref="ReplacementBus"/>; absent on the shape-only path.
/// - Fizzle (CR 608.2b) — no target chosen → clean no-op.
/// - <see cref="NamedCardFactory"/> dispatch.
/// </summary>
[Trait("Color", "B")]
public class PiranhaMarshFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void PiranhaMarsh_Identity_NonbasicLandNoSubtype()
    {
        var land = (Land)NamedCardFactory.Create("Piranha Marsh", _alice);

        land.Name.Should().Be("Piranha Marsh");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Piranha Marsh is a nonbasic Land");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        land.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // {T}: Add {B}
    // -----------------------------------------------------------------------

    [Fact]
    public void PiranhaMarsh_HasManaAbility_ForBlack()
    {
        var land = (Land)NamedCardFactory.Create("Piranha Marsh", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Black == 1 && m.ManaGenerated.Red == 0);
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
    public void PiranhaMarsh_EntersTapped_WhenReplacementRegistered()
    {
        var (zones, _, triggers, rep) = BuildEngine();

        var land = PiranhaMarshFactory.Create(_alice, triggers);
        rep.Register(new EntersTappedReplacement(land));
        land.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(land);
        triggers.BindCard(land);

        zones.MoveCardTo(land, ZoneType.Battlefield, controller: _alice);

        land.IsTapped.Should().BeTrue("CR 614.1c — Piranha Marsh enters tapped");
        land.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void PiranhaMarsh_EntersUntapped_OnShapeOnlyPath()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);

        var land = PiranhaMarshFactory.Create(_alice);
        land.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(land);

        zones.MoveCardTo(land, ZoneType.Battlefield, controller: _alice);

        land.IsTapped.Should().BeFalse(
            "shape-only path omits the enters-tapped replacement");
    }

    // -----------------------------------------------------------------------
    // ETB trigger — target player loses 1 life (CR 603.6a)
    // -----------------------------------------------------------------------

    [Fact]
    public void PiranhaMarsh_EtbTrigger_IsBattlefieldActive()
    {
        var land = PiranhaMarshFactory.Create(_alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    /// <summary>
    /// CR 603.6a — "When this land enters, target player loses 1 life."
    /// With the opponent chosen as the target, the chosen player loses 1
    /// life on resolution (CR 119.3 — direct life loss).
    /// </summary>
    [Fact]
    public void PiranhaMarsh_EntersBattlefield_ChosenPlayerLosesOneLife()
    {
        var (zones, stack, triggers, _) = BuildEngine();

        var land = PiranhaMarshFactory.Create(_alice, triggers);
        land.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(land);
        triggers.BindCard(land);

        var bobLifeBefore = _bob.LifeTotal;

        zones.MoveCardTo(land, ZoneType.Battlefield, controller: _alice);

        triggers.PendingCount.Should().Be(1,
            "ETB life-loss trigger must queue on entering battlefield");

        var etbTrigger = land.Abilities.OfType<TriggeredAbility>().Single();
        etbTrigger.SetChosenTargets(new[]
        {
            (IReadOnlyList<object>)new object[] { _bob },
        });

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _bob.LifeTotal.Should().Be(bobLifeBefore - PiranhaMarshFactory.LifeLossAmount,
            "target player should lose 1 life on Piranha Marsh's ETB");
    }

    /// <summary>
    /// "Target player" is any player — the controller may target themselves.
    /// </summary>
    [Fact]
    public void PiranhaMarsh_TargetMayBeController()
    {
        var (zones, stack, triggers, _) = BuildEngine();

        var land = PiranhaMarshFactory.Create(_alice, triggers);
        land.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(land);
        triggers.BindCard(land);

        var aliceLifeBefore = _alice.LifeTotal;

        zones.MoveCardTo(land, ZoneType.Battlefield, controller: _alice);

        var etbTrigger = land.Abilities.OfType<TriggeredAbility>().Single();
        etbTrigger.SetChosenTargets(new[]
        {
            (IReadOnlyList<object>)new object[] { _alice },
        });

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.LifeTotal.Should().Be(aliceLifeBefore - PiranhaMarshFactory.LifeLossAmount,
            "'target player' may be the controller themselves");
    }

    /// <summary>
    /// CR 608.2b — when no target was chosen, the life-loss effect is a
    /// clean no-op. No life total changes.
    /// </summary>
    [Fact]
    public void PiranhaMarsh_NoTargetChosen_LifeLossNoOps()
    {
        var (zones, stack, triggers, _) = BuildEngine();

        var land = PiranhaMarshFactory.Create(_alice, triggers);
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
