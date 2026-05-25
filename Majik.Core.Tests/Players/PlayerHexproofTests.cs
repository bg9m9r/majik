using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Services;
using TargetLegality = Majik.Core.Targeting.TargetLegality;
using TargetSpec = Majik.Core.Targeting.TargetSpec;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Players;

/// <summary>
/// Tests for the player-hexproof primitive (CR 702.11) plus the
/// Leyline of Sanctity end-to-end retrofit.
///
/// Covers:
/// - <see cref="Player.HasHexproof"/> is false by default and tracks
///   the <see cref="PlayerStaticAbilities"/> registry.
/// - <see cref="PlayerHexproofEffect"/> lifecycle: ETB registers the
///   grant, LTB drops it.
/// - Cast / ability gates in <see cref="ActionValidator"/> reject
///   opponent-controlled spells / abilities naming a hexproof player
///   with <see cref="RuleViolation"/> 702.11.
/// - <see cref="TargetLegality"/> resolution-time recheck (CR 608.2b)
///   honours player-hexproof.
/// - Self-targeting (CR 113.5b — "controlled by opponents" only)
///   bypasses hexproof.
/// - Multiple Leylines stack idempotently — hexproof is binary
///   (CR 702.11b); removing one keeps the other's grant live.
///
/// Tests dispose-clean <see cref="PlayerStaticAbilities"/> to prevent
/// cross-test leakage of the static registry.
/// </summary>
public class PlayerHexproofTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;

    public PlayerHexproofTests()
    {
        _zones = new ZoneService(_bus);
        PlayerStaticAbilities.Clear();
    }

    public void Dispose()
    {
        PlayerStaticAbilities.Clear();
    }

    // -----------------------------------------------------------------------
    // Player.HasHexproof primitive
    // -----------------------------------------------------------------------

    [Fact]
    public void Player_NoHexproof_ByDefault()
    {
        _alice.HasHexproof.Should().BeFalse();
        _bob.HasHexproof.Should().BeFalse();
    }

    [Fact]
    public void PlayerStaticAbilities_AddHexproof_LightsUpQuery()
    {
        var token = new object();
        PlayerStaticAbilities.AddHexproof(token, _alice);

        _alice.HasHexproof.Should().BeTrue();
        _bob.HasHexproof.Should().BeFalse();

        PlayerStaticAbilities.RemoveHexproof(token);
        _alice.HasHexproof.Should().BeFalse();
    }

    [Fact]
    public void PlayerStaticAbilities_DuplicateAdd_Idempotent()
    {
        var token = new object();
        PlayerStaticAbilities.AddHexproof(token, _alice);
        PlayerStaticAbilities.AddHexproof(token, _alice); // idempotent

        _alice.HasHexproof.Should().BeTrue();

        PlayerStaticAbilities.RemoveHexproof(token);
        _alice.HasHexproof.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Validator gate — CR 702.11
    // -----------------------------------------------------------------------

    [Fact]
    public void NoHexproof_OpponentBolt_TargetingPlayer_IsLegal()
    {
        // Baseline: no hexproof anywhere. Bob's Lightning Bolt naming
        // Alice as its target validates cleanly.
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob };
        var action = new CastSpellAction(
            bolt, _bob,
            sorcerySpeedAvailable: true,
            fromZone: ZoneType.Hand,
            targets: new object[] { _alice });

        var result = new ActionValidator().ValidateAction(action);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void LeylineOfSanctity_OnBattlefield_BlocksOpponentBolt_TargetingController()
    {
        // Alice has Leyline of Sanctity on the battlefield (printed
        // hexproof rider). Bob's Lightning Bolt naming Alice is rejected
        // with RuleViolation 702.11.
        PlaceSanctity(_alice);

        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob };
        var action = new CastSpellAction(
            bolt, _bob,
            sorcerySpeedAvailable: true,
            fromZone: ZoneType.Hand,
            targets: new object[] { _alice });

        var result = new ActionValidator().ValidateAction(action);

        result.IsValid.Should().BeFalse();
        result.Violation!.RuleNumber.Should().Be("702.11");
        result.ErrorMessage.Should().Contain("hexproof");
    }

    [Fact]
    public void LeylineOfSanctity_OnBattlefield_AllowsControllerToTargetThemselves()
    {
        // CR 113.5b — hexproof only blocks spells/abilities "controlled
        // by opponents". The controller is free to target themselves
        // (e.g. their own draw-3-pay-3-life spell, their own Healing
        // Salve).
        PlaceSanctity(_alice);

        var healingSalve = new Instant("Healing Salve", "{W}") { Owner = _alice };
        var action = new CastSpellAction(
            healingSalve, _alice,
            sorcerySpeedAvailable: true,
            fromZone: ZoneType.Hand,
            targets: new object[] { _alice });

        var result = new ActionValidator().ValidateAction(action);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void LeylineOfSanctity_LeavingBattlefield_DropsHexproof()
    {
        // ETB registers, LTB unregisters — Bob's Bolt becomes legal again.
        var sanctity = PlaceSanctity(_alice);

        _alice.HasHexproof.Should().BeTrue();

        _zones.MoveCard(sanctity, ZoneType.Battlefield, ZoneType.Graveyard);

        _alice.HasHexproof.Should().BeFalse();

        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob };
        var action = new CastSpellAction(
            bolt, _bob,
            sorcerySpeedAvailable: true,
            fromZone: ZoneType.Hand,
            targets: new object[] { _alice });

        new ActionValidator().ValidateAction(action).IsValid.Should().BeTrue();
    }

    [Fact]
    public void TwoLeylinesOfSanctity_StackIdempotently_RemovingOne_PreservesHexproof()
    {
        // Two Sanctities → still hexproof (CR 702.11b — having a
        // keyword twice has no extra effect). Drop one to graveyard;
        // the other's grant survives so hexproof stays on.
        var first = PlaceSanctity(_alice);
        var second = PlaceSanctity(_alice);

        _alice.HasHexproof.Should().BeTrue();

        _zones.MoveCard(first, ZoneType.Battlefield, ZoneType.Graveyard);

        _alice.HasHexproof.Should().BeTrue("the second Leyline still grants hexproof");

        _zones.MoveCard(second, ZoneType.Battlefield, ZoneType.Graveyard);

        _alice.HasHexproof.Should().BeFalse("both Leylines are gone");
    }

    // -----------------------------------------------------------------------
    // Activated-ability gate
    // -----------------------------------------------------------------------

    [Fact]
    public void LeylineOfSanctity_OnBattlefield_BlocksOpponentActivatedAbility_TargetingController()
    {
        PlaceSanctity(_alice);

        // Build a minimal activated-ability action. The exact ability
        // shape doesn't matter for the gate — only the (player, target)
        // pair is consulted.
        var ability = new Majik.Core.Abilities.ActivatedAbility(
            source: new object(),
            controller: _bob);
        var action = new Majik.Core.Rules.ActivateAbilityAction(
            ability, _bob,
            sorcerySpeedAvailable: true,
            targets: new object[] { _alice });

        var result = new ActionValidator().ValidateAction(action);

        result.IsValid.Should().BeFalse();
        result.Violation!.RuleNumber.Should().Be("702.11");
    }

    // -----------------------------------------------------------------------
    // Resolution-time recheck via TargetLegality (CR 608.2b)
    // -----------------------------------------------------------------------

    [Fact]
    public void TargetLegality_PlayerHexproof_RejectsOpponentSpec_AllowsSelfSpec()
    {
        PlaceSanctity(_alice);

        var spec = new TargetSpec("any").AnyCreatureOrPlayer();

        TargetLegality.IsLegal(spec, _alice, _bob).Should().BeFalse();   // opponent → blocked
        TargetLegality.IsLegal(spec, _alice, _alice).Should().BeTrue();  // self → allowed
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private Enchantment PlaceSanctity(Player controller)
    {
        var sanctity = LeylineOfSanctityFactory.Create(controller, _bus);
        controller.Zones.Library.AddCard(sanctity);
        sanctity.SetZone(ZoneType.Library);
        _zones.MoveCard(sanctity, ZoneType.Library, ZoneType.Battlefield);
        return sanctity;
    }

}
