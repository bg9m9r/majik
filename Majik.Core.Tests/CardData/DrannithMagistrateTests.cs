using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Drannith Magistrate — Creature — Human Wizard {1}{W} 1/3
/// (CR 113.6 printed static "Your opponents can't cast spells from
/// anywhere other than their hands.").
///
/// Covers:
/// - Card identity / subtype / P/T / dispatcher routing.
/// - The printed-static cast-from-hand-only restriction wired via
///   <see cref="CastFromHandOnlyRestrictionEffect"/> +
///   <see cref="CastingRestrictions"/> and observed through
///   <see cref="ActionValidator"/>.
/// - LTB releases the restriction; the controller is never restricted;
///   casts whose <see cref="CastSpellAction.FromZone"/> is the hand
///   remain legal.
///
/// Tests dispose-clean the static <see cref="CastingRestrictions"/>
/// registry to prevent cross-test leakage.
/// </summary>
public class DrannithMagistrateTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;

    public DrannithMagistrateTests()
    {
        _zones = new ZoneService(_bus);
        CastingRestrictions.Clear();
    }

    public void Dispose()
    {
        CastingRestrictions.Clear();
    }

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void DrannithMagistrate_HasCorrectIdentity_AndPT_AndSubtypes()
    {
        var magistrate = DrannithMagistrateFactory.Create(_alice);

        magistrate.Name.Should().Be("Drannith Magistrate");
        magistrate.ManaCost.Should().Be("{1}{W}");
        magistrate.HasType(CardType.Creature).Should().BeTrue();
        magistrate.HasSubtype(CardSubtype.Human).Should().BeTrue();
        magistrate.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        magistrate.Power.Should().Be(1);
        magistrate.Toughness.Should().Be(3);
        magistrate.Owner.Should().BeSameAs(_alice);
        magistrate.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_RoutesDrannithMagistrate_ToFactory()
    {
        var card = NamedCardFactory.Create("Drannith Magistrate", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Drannith Magistrate");
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        ((Creature)card).Power.Should().Be(1);
        ((Creature)card).Toughness.Should().Be(3);
    }

    // -----------------------------------------------------------------------
    // Printed static — CR 113.6 cast-from-hand-only restriction
    // -----------------------------------------------------------------------

    [Fact]
    public void MagistrateOnBattlefield_BlocksOpponentCast_FromExile()
    {
        var magistrate = DrannithMagistrateFactory.Create(
            _alice,
            opponentResolver: () => new[] { _bob },
            eventBus: _bus);

        // Move onto the battlefield so the lifecycle picks it up.
        _alice.Zones.Library.AddCard(magistrate);
        magistrate.SetZone(ZoneType.Library);
        _zones.MoveCard(magistrate, ZoneType.Library, ZoneType.Battlefield);

        // Bob tries to cast a card from exile (cascade / suspend /
        // foretell / etc.) — rejected.
        var bolt = new Instant("Lightning Bolt", "R") { Owner = _bob };
        var action = new CastSpellAction(bolt, _bob, sorcerySpeedAvailable: true, fromZone: ZoneType.Exile);
        var result = new ActionValidator().ValidateAction(action);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Exile");
        result.Violation!.RuleNumber.Should().Be("113.6");
    }

    [Fact]
    public void MagistrateOnBattlefield_BlocksOpponentCast_FromGraveyard()
    {
        // Flashback / disturb / aftermath / escape / jump-start — all
        // cast-from-graveyard flows should be rejected.
        var magistrate = DrannithMagistrateFactory.Create(
            _alice,
            opponentResolver: () => new[] { _bob },
            eventBus: _bus);
        _alice.Zones.Library.AddCard(magistrate);
        magistrate.SetZone(ZoneType.Library);
        _zones.MoveCard(magistrate, ZoneType.Library, ZoneType.Battlefield);

        var looting = new Sorcery("Faithless Looting", "R") { Owner = _bob };
        var action = new CastSpellAction(looting, _bob, sorcerySpeedAvailable: true, fromZone: ZoneType.Graveyard);
        var result = new ActionValidator().ValidateAction(action);

        result.IsValid.Should().BeFalse();
        result.Violation!.RuleNumber.Should().Be("113.6");
    }

    [Fact]
    public void MagistrateOnBattlefield_AllowsOpponentCast_FromHand()
    {
        // The whole point: hand casts are unaffected.
        var magistrate = DrannithMagistrateFactory.Create(
            _alice,
            opponentResolver: () => new[] { _bob },
            eventBus: _bus);
        _alice.Zones.Library.AddCard(magistrate);
        magistrate.SetZone(ZoneType.Library);
        _zones.MoveCard(magistrate, ZoneType.Library, ZoneType.Battlefield);

        var bolt = new Instant("Lightning Bolt", "R") { Owner = _bob };
        var action = new CastSpellAction(bolt, _bob, sorcerySpeedAvailable: true, fromZone: ZoneType.Hand);
        new ActionValidator().ValidateAction(action).IsValid.Should().BeTrue();
    }

    [Fact]
    public void MagistrateOnBattlefield_DoesNotRestrictMagistrateController()
    {
        // CR 113.6 — restriction targets each *opponent*, not Alice.
        var magistrate = DrannithMagistrateFactory.Create(
            _alice,
            opponentResolver: () => new[] { _bob },
            eventBus: _bus);
        _alice.Zones.Library.AddCard(magistrate);
        magistrate.SetZone(ZoneType.Library);
        _zones.MoveCard(magistrate, ZoneType.Library, ZoneType.Battlefield);

        // Alice flashes back her own spell from her graveyard — fine.
        var looting = new Sorcery("Faithless Looting", "R") { Owner = _alice };
        var action = new CastSpellAction(looting, _alice, sorcerySpeedAvailable: true, fromZone: ZoneType.Graveyard);
        new ActionValidator().ValidateAction(action).IsValid.Should().BeTrue();
    }

    [Fact]
    public void MagistrateLeavingBattlefield_ReleasesRestriction()
    {
        var magistrate = DrannithMagistrateFactory.Create(
            _alice,
            opponentResolver: () => new[] { _bob },
            eventBus: _bus);
        _alice.Zones.Library.AddCard(magistrate);
        magistrate.SetZone(ZoneType.Library);
        _zones.MoveCard(magistrate, ZoneType.Library, ZoneType.Battlefield);

        CastingRestrictions.MustCastFromHand(_bob).Should().BeTrue();

        // Magistrate dies → restriction lifts.
        _zones.MoveCard(magistrate, ZoneType.Battlefield, ZoneType.Graveyard);

        CastingRestrictions.MustCastFromHand(_bob).Should().BeFalse();

        // Now Bob can cast from exile again.
        var bolt = new Instant("Lightning Bolt", "R") { Owner = _bob };
        var action = new CastSpellAction(bolt, _bob, sorcerySpeedAvailable: true, fromZone: ZoneType.Exile);
        new ActionValidator().ValidateAction(action).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Magistrate_UnspecifiedFromZone_DoesNotBlock()
    {
        // Backward compatibility: callers that don't stamp a FromZone
        // get unrestricted on this axis (validator no-op).
        var magistrate = DrannithMagistrateFactory.Create(
            _alice,
            opponentResolver: () => new[] { _bob },
            eventBus: _bus);
        _alice.Zones.Library.AddCard(magistrate);
        magistrate.SetZone(ZoneType.Library);
        _zones.MoveCard(magistrate, ZoneType.Library, ZoneType.Battlefield);

        var bolt = new Instant("Lightning Bolt", "R") { Owner = _bob };
        var action = new CastSpellAction(bolt, _bob, sorcerySpeedAvailable: true); // FromZone null
        new ActionValidator().ValidateAction(action).IsValid.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // CastingRestrictions registry — direct unit-level coverage
    // -----------------------------------------------------------------------

    [Fact]
    public void CastingRestrictions_AddAndRemove_CastFromHandOnly_Toggles()
    {
        var token = new object();
        CastingRestrictions.MustCastFromHand(_bob).Should().BeFalse();

        CastingRestrictions.AddCastFromHandOnlyRestriction(token, _bob);
        CastingRestrictions.MustCastFromHand(_bob).Should().BeTrue();

        // Idempotent for the same (token, player).
        CastingRestrictions.AddCastFromHandOnlyRestriction(token, _bob);
        CastingRestrictions.MustCastFromHand(_bob).Should().BeTrue();

        CastingRestrictions.RemoveCastFromHandOnlyRestriction(token);
        CastingRestrictions.MustCastFromHand(_bob).Should().BeFalse();
    }
}
