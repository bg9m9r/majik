using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="WitchbaneOrbFactory"/> (Witchbane Orb, {4}
/// Artifact — Innistrad).
///
/// Oracle text (verified against Scryfall 2026-06-01):
///   "When this artifact enters, destroy all Curses attached to you.
///    You have hexproof. (...)"
///
/// Covers:
/// - Identity ({4} Artifact, owner / controller wiring, MV 4).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - "You have hexproof" static (CR 702.11) — ETB registers the grant on
///   the controller, LTB drops it; opponent Bolt naming the controller is
///   rejected; self-target stays legal (CR 113.5b); two Orbs stack
///   idempotently.
/// - ETB Curse-destroy trigger shape: exactly one battlefield-active
///   <see cref="TriggeredAbility"/> firing on enter-self.
/// - ETB body is a no-op-safe destroy — it never destroys a non-Curse and
///   leaves player-enchant Curses untouched (no player-attachment registry
///   yet; see the factory's engine-model note).
///
/// Disposes the <see cref="PlayerStaticAbilities"/> registry to avoid
/// cross-test leakage of the hexproof static.
/// </summary>
public class WitchbaneOrbFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;

    public WitchbaneOrbFactoryTests()
    {
        _zones = new ZoneService(_bus);
        PlayerStaticAbilities.Clear();
    }

    public void Dispose() => PlayerStaticAbilities.Clear();

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void WitchbaneOrb_Identity()
    {
        var orb = WitchbaneOrbFactory.Create(_alice);

        orb.Name.Should().Be("Witchbane Orb");
        orb.ManaCost.Should().Be("{4}");
        orb.HasType(CardType.Artifact).Should().BeTrue();
        orb.Owner.Should().BeSameAs(_alice);
        orb.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void WitchbaneOrb_ManaValue_IsFour()
    {
        var orb = WitchbaneOrbFactory.Create(_alice);
        // {4} = MV 4 (CR 202.3).
        orb.ManaCostValue.TotalValue.Should().Be(4, "CR 202.3 — {4} has mana value 4");
    }

    [Fact]
    public void WitchbaneOrb_DispatchesViaNamedCardFactory()
    {
        var orb = NamedCardFactory.Create("Witchbane Orb", _alice);

        orb.Should().BeOfType<Artifact>("Witchbane Orb is an Artifact");
        orb.Name.Should().Be("Witchbane Orb");
        orb.ManaCost.Should().Be("{4}");
    }

    // -----------------------------------------------------------------------
    // "You have hexproof" static (CR 702.11)
    // -----------------------------------------------------------------------

    [Fact]
    public void WitchbaneOrb_OnBattlefield_GrantsControllerHexproof()
    {
        PlaceOrb(_alice);

        _alice.HasHexproof.Should().BeTrue();
        _bob.HasHexproof.Should().BeFalse();
    }

    [Fact]
    public void WitchbaneOrb_OnBattlefield_BlocksOpponentBolt_TargetingController()
    {
        PlaceOrb(_alice);

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
    public void WitchbaneOrb_OnBattlefield_AllowsControllerToTargetThemselves()
    {
        // CR 113.5b — hexproof only blocks spells/abilities controlled by
        // opponents; the controller may still target themselves.
        PlaceOrb(_alice);

        var salve = new Instant("Healing Salve", "{W}") { Owner = _alice };
        var action = new CastSpellAction(
            salve, _alice,
            sorcerySpeedAvailable: true,
            fromZone: ZoneType.Hand,
            targets: new object[] { _alice });

        new ActionValidator().ValidateAction(action).IsValid.Should().BeTrue();
    }

    [Fact]
    public void WitchbaneOrb_LeavingBattlefield_DropsHexproof()
    {
        var orb = PlaceOrb(_alice);
        _alice.HasHexproof.Should().BeTrue();

        _zones.MoveCard(orb, ZoneType.Battlefield, ZoneType.Graveyard);

        _alice.HasHexproof.Should().BeFalse();
    }

    [Fact]
    public void TwoWitchbaneOrbs_StackIdempotently_RemovingOne_PreservesHexproof()
    {
        var first = PlaceOrb(_alice);
        var second = PlaceOrb(_alice);

        _alice.HasHexproof.Should().BeTrue();

        _zones.MoveCard(first, ZoneType.Battlefield, ZoneType.Graveyard);
        _alice.HasHexproof.Should().BeTrue("the second Orb still grants hexproof");

        _zones.MoveCard(second, ZoneType.Battlefield, ZoneType.Graveyard);
        _alice.HasHexproof.Should().BeFalse("both Orbs are gone");
    }

    // -----------------------------------------------------------------------
    // ETB Curse-destroy trigger shape (CR 603.6a)
    // -----------------------------------------------------------------------

    [Fact]
    public void WitchbaneOrb_HasExactlyOneTriggeredAbility_BattlefieldActive()
    {
        var orb = WitchbaneOrbFactory.Create(_alice);

        var triggers = orb.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "the only triggered ability is the ETB Curse-destroy");
        triggers[0].ActiveZones.Should().Contain(ZoneType.Battlefield,
            "ETB triggers are battlefield-active per CR 603.6a");
    }

    [Fact]
    public void WitchbaneOrb_EtbTrigger_FiresOnEnterSelf()
    {
        var orb = WitchbaneOrbFactory.Create(_alice);
        orb.SetOwner(_alice);
        orb.SetController(_alice);
        orb.SetZone(ZoneType.Battlefield);

        var trigger = orb.Abilities.OfType<TriggeredAbility>().Single();

        var enterSelf = new CardMovedEvent(orb, ZoneType.Stack, ZoneType.Battlefield);
        trigger.IsTriggered(enterSelf).Should().BeTrue(
            "the ETB fires when the Orb itself enters the battlefield");
    }

    // -----------------------------------------------------------------------
    // ETB body — no-op-safe destroy
    // -----------------------------------------------------------------------

    [Fact]
    public void WitchbaneOrb_EtbResolution_DoesNotDestroyNonCurses()
    {
        // A vanilla artifact controlled by Alice must survive the ETB — the
        // Curse-destroy only ever targets Curses (CR 701.7), and there are
        // none attached to a player to hit.
        var bystander = new Artifact("Bystander", "{1}") { Owner = _alice };
        _alice.Zones.Battlefield.AddCard(bystander);
        bystander.SetZone(ZoneType.Battlefield);

        var orb = WitchbaneOrbFactory.Create(_alice);
        orb.SetOwner(_alice);
        orb.SetController(_alice);
        orb.SetZone(ZoneType.Battlefield);

        var trigger = orb.Abilities.OfType<TriggeredAbility>().Single();
        var effect = trigger.Effects.Single();

        effect.Execute();

        bystander.Zone.Should().Be(ZoneType.Battlefield,
            "the ETB destroys only Curses attached to you, never a bystander artifact");
        _alice.Zones.Graveyard.GetCards().Should().NotContain(bystander);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private Artifact PlaceOrb(Player controller)
    {
        var orb = WitchbaneOrbFactory.Create(controller, _bus, triggers: null);
        controller.Zones.Library.AddCard(orb);
        orb.SetZone(ZoneType.Library);
        _zones.MoveCard(orb, ZoneType.Library, ZoneType.Battlefield);
        return orb;
    }
}
