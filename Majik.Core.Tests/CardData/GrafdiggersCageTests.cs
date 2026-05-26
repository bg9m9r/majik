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
/// Tests for <see cref="GrafdiggersCageFactory"/> — Dark Ascension
/// Artifact {1}:
///   "Creature cards in graveyards and libraries can't enter the
///    battlefield. Players can't cast spells from graveyards or
///    libraries."
///
/// Covers:
/// - Card identity / dispatch.
/// - The creature-ETB cancel-replacement registered on
///   <see cref="ReplacementBus"/> (CR 614) — cancels Graveyard→Battlefield
///   and Library→Battlefield creature moves, leaves non-creatures and
///   moves from other zones alone.
/// - The global cast-from-zone block (CR 601.3) wired through
///   <see cref="CastingRestrictions.IsCastFromZoneGloballyBlocked"/> and
///   observed via <see cref="ActionValidator"/> — rejects casts from
///   Graveyard and Library for every player; permits Hand / Exile casts.
/// - Lifecycle: both halves activate on ETB, detach on LTB.
///
/// Tests dispose-clean the static <see cref="CastingRestrictions"/>
/// registry to prevent cross-test leakage.
/// </summary>
public class GrafdiggersCageTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;
    private readonly ReplacementBus _replacements = new();
    private readonly ActionValidator _validator;

    public GrafdiggersCageTests()
    {
        _zones = new ZoneService(_bus);
        _validator = new ActionValidator(eventBus: _bus);
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
    public void GrafdiggersCage_HasCorrectIdentity()
    {
        var cage = GrafdiggersCageFactory.Create(_alice);

        cage.Name.Should().Be("Grafdigger's Cage");
        cage.ManaCost.Should().Be("{1}");
        cage.HasType(CardType.Artifact).Should().BeTrue();
        cage.Owner.Should().BeSameAs(_alice);
        cage.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_GrafdiggersCage()
    {
        var card = NamedCardFactory.Create("Grafdigger's Cage", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Grafdigger's Cage");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.ManaCost.Should().Be("{1}");
    }

    [Fact]
    public void GrafdiggersCage_ShapeOnly_NoReplacementBus_DoesNotRegister()
    {
        var cage = GrafdiggersCageFactory.Create(_alice);
        _alice.Zones.Library.AddCard(cage);
        cage.SetZone(ZoneType.Library);
        _zones.MoveCard(cage, ZoneType.Library, ZoneType.Battlefield);

        // The cast-from-zone block must not be registered when no
        // replacement-bus was supplied — the lifecycle never attaches.
        CastingRestrictions.IsCastFromZoneGloballyBlocked(ZoneType.Graveyard)
            .Should().BeFalse();
        CastingRestrictions.IsCastFromZoneGloballyBlocked(ZoneType.Library)
            .Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Replacement effect — creature ETB cancel from Graveyard / Library
    // -----------------------------------------------------------------------

    [Fact]
    public void GrafdiggersCage_OnBattlefield_CancelsCreatureETB_FromGraveyard()
    {
        var cage = GrafdiggersCageFactory.Create(_alice, _replacements, _bus);
        _alice.Zones.Library.AddCard(cage);
        cage.SetZone(ZoneType.Library);
        _zones.MoveCard(cage, ZoneType.Library, ZoneType.Battlefield);

        var goyf = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        var intent = new ZoneMoveIntent(
            Card: goyf,
            FromZone: ZoneType.Graveyard,
            ToZone: ZoneType.Battlefield,
            WasCast: false);

        var result = _replacements.Apply(intent);

        result.Should().BeNull(
            "Cage cancels (returns null) creature reanimation from a graveyard");
    }

    [Fact]
    public void GrafdiggersCage_OnBattlefield_CancelsCreatureETB_FromLibrary()
    {
        var cage = GrafdiggersCageFactory.Create(_alice, _replacements, _bus);
        _alice.Zones.Library.AddCard(cage);
        cage.SetZone(ZoneType.Library);
        _zones.MoveCard(cage, ZoneType.Library, ZoneType.Battlefield);

        var elf = new Creature("Llanowar Elves", "{G}", 1, 1);
        var intent = new ZoneMoveIntent(
            Card: elf,
            FromZone: ZoneType.Library,
            ToZone: ZoneType.Battlefield,
            WasCast: false);

        var result = _replacements.Apply(intent);

        result.Should().BeNull(
            "Cage cancels creature ETBs from a library (Bolas's Citadel-style cheats)");
    }

    [Fact]
    public void GrafdiggersCage_DoesNotCancel_NonCreatureETB_FromGraveyard()
    {
        var cage = GrafdiggersCageFactory.Create(_alice, _replacements, _bus);
        _alice.Zones.Library.AddCard(cage);
        cage.SetZone(ZoneType.Library);
        _zones.MoveCard(cage, ZoneType.Library, ZoneType.Battlefield);

        // Artifact reanimation — Cage's predicate is creature-typed only.
        var mox = new Artifact("Mox Opal", "{0}");
        var intent = new ZoneMoveIntent(
            Card: mox,
            FromZone: ZoneType.Graveyard,
            ToZone: ZoneType.Battlefield,
            WasCast: false);

        var result = _replacements.Apply(intent);

        result.Should().NotBeNull();
        result!.ToZone.Should().Be(ZoneType.Battlefield,
            "Non-creature reanimation passes through Cage");
    }

    [Fact]
    public void GrafdiggersCage_DoesNotCancel_CreatureETB_FromHand()
    {
        var cage = GrafdiggersCageFactory.Create(_alice, _replacements, _bus);
        _alice.Zones.Library.AddCard(cage);
        cage.SetZone(ZoneType.Library);
        _zones.MoveCard(cage, ZoneType.Library, ZoneType.Battlefield);

        // Regular cast-from-hand creature ETB — Cage only constrains
        // Graveyard / Library sources.
        var goyf = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        var intent = new ZoneMoveIntent(
            Card: goyf,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            WasCast: true);

        var result = _replacements.Apply(intent);

        result.Should().NotBeNull();
        result!.ToZone.Should().Be(ZoneType.Battlefield,
            "Hand → Battlefield casts pass through Cage");
    }

    [Fact]
    public void GrafdiggersCage_DoesNotCancel_CreatureETB_FromExile()
    {
        var cage = GrafdiggersCageFactory.Create(_alice, _replacements, _bus);
        _alice.Zones.Library.AddCard(cage);
        cage.SetZone(ZoneType.Library);
        _zones.MoveCard(cage, ZoneType.Library, ZoneType.Battlefield);

        // Suspend / cascade resolutions land creatures from Exile —
        // Cage doesn't gate Exile.
        var creature = new Creature("Restoration Angel", "{3}{W}", 3, 4);
        var intent = new ZoneMoveIntent(
            Card: creature,
            FromZone: ZoneType.Exile,
            ToZone: ZoneType.Battlefield,
            WasCast: true);

        var result = _replacements.Apply(intent);

        result.Should().NotBeNull();
        result!.ToZone.Should().Be(ZoneType.Battlefield,
            "Exile → Battlefield casts pass through Cage");
    }

    [Fact]
    public void GrafdiggersCage_DoesNotCancel_NonBattlefieldDestination()
    {
        var cage = GrafdiggersCageFactory.Create(_alice, _replacements, _bus);
        _alice.Zones.Library.AddCard(cage);
        cage.SetZone(ZoneType.Library);
        _zones.MoveCard(cage, ZoneType.Library, ZoneType.Battlefield);

        // Graveyard → Exile (Cling to Dust, Faerie Macabre) is not gated.
        var bolt = new Instant("Lightning Bolt", "{R}");
        var intent = new ZoneMoveIntent(
            Card: bolt,
            FromZone: ZoneType.Graveyard,
            ToZone: ZoneType.Exile,
            WasCast: false);

        var result = _replacements.Apply(intent);

        result.Should().NotBeNull();
        result!.ToZone.Should().Be(ZoneType.Exile,
            "Cage only gates moves whose destination is the battlefield");
    }

    // -----------------------------------------------------------------------
    // Cast-from-zone block — CR 601.3
    // -----------------------------------------------------------------------

    [Fact]
    public void GrafdiggersCage_OnBattlefield_RegistersCastFromZoneBlocks()
    {
        var cage = GrafdiggersCageFactory.Create(_alice, _replacements, _bus);
        _alice.Zones.Library.AddCard(cage);
        cage.SetZone(ZoneType.Library);
        _zones.MoveCard(cage, ZoneType.Library, ZoneType.Battlefield);

        CastingRestrictions.IsCastFromZoneGloballyBlocked(ZoneType.Graveyard)
            .Should().BeTrue("Cage blocks casts from graveyards");
        CastingRestrictions.IsCastFromZoneGloballyBlocked(ZoneType.Library)
            .Should().BeTrue("Cage blocks casts from libraries");
        CastingRestrictions.IsCastFromZoneGloballyBlocked(ZoneType.Hand)
            .Should().BeFalse("Hand casts remain legal");
        CastingRestrictions.IsCastFromZoneGloballyBlocked(ZoneType.Exile)
            .Should().BeFalse("Exile casts (cascade / suspend / foretell) remain legal");
    }

    [Fact]
    public void GrafdiggersCage_ActionValidator_RejectsCastFromGraveyard()
    {
        var cage = GrafdiggersCageFactory.Create(_alice, _replacements, _bus);
        _alice.Zones.Library.AddCard(cage);
        cage.SetZone(ZoneType.Library);
        _zones.MoveCard(cage, ZoneType.Library, ZoneType.Battlefield);

        // Bob tries to flashback a Lightning Bolt from his graveyard.
        var flashbackBolt = new Instant("Lightning Bolt", "{R}");
        var action = new CastSpellAction(
            card: flashbackBolt,
            player: _bob,
            sorcerySpeedAvailable: true,
            fromZone: ZoneType.Graveyard);

        var result = _validator.ValidateAction(action);

        result.IsValid.Should().BeFalse();
        result.Violation!.RuleNumber.Should().Be("601.3");
        result.ErrorMessage.Should().Contain("Graveyard");
    }

    [Fact]
    public void GrafdiggersCage_ActionValidator_RejectsCastFromLibrary()
    {
        var cage = GrafdiggersCageFactory.Create(_alice, _replacements, _bus);
        _alice.Zones.Library.AddCard(cage);
        cage.SetZone(ZoneType.Library);
        _zones.MoveCard(cage, ZoneType.Library, ZoneType.Battlefield);

        // Bolas's Citadel-style "cast from top of library" path.
        var spell = new Sorcery("Foo", "{1}");
        var action = new CastSpellAction(
            card: spell,
            player: _alice,
            sorcerySpeedAvailable: true,
            fromZone: ZoneType.Library);

        var result = _validator.ValidateAction(action);

        result.IsValid.Should().BeFalse();
        result.Violation!.RuleNumber.Should().Be("601.3");
    }

    [Fact]
    public void GrafdiggersCage_ActionValidator_AllowsCastFromHand()
    {
        var cage = GrafdiggersCageFactory.Create(_alice, _replacements, _bus);
        _alice.Zones.Library.AddCard(cage);
        cage.SetZone(ZoneType.Library);
        _zones.MoveCard(cage, ZoneType.Library, ZoneType.Battlefield);

        var bolt = new Instant("Lightning Bolt", "{R}");
        var action = new CastSpellAction(
            card: bolt,
            player: _bob,
            sorcerySpeedAvailable: true,
            fromZone: ZoneType.Hand);

        var result = _validator.ValidateAction(action);

        result.IsValid.Should().BeTrue(
            "Cage does not gate casts from the hand");
    }

    [Fact]
    public void GrafdiggersCage_ActionValidator_BlockIsSymmetric()
    {
        // Cage's controller (Alice) is also restricted — printed text is
        // global, not opponent-only.
        var cage = GrafdiggersCageFactory.Create(_alice, _replacements, _bus);
        _alice.Zones.Library.AddCard(cage);
        cage.SetZone(ZoneType.Library);
        _zones.MoveCard(cage, ZoneType.Library, ZoneType.Battlefield);

        var spell = new Instant("Snapcaster Bolt", "{R}");
        var action = new CastSpellAction(
            card: spell,
            player: _alice,
            sorcerySpeedAvailable: true,
            fromZone: ZoneType.Graveyard);

        var result = _validator.ValidateAction(action);

        result.IsValid.Should().BeFalse(
            "Cage affects its own controller too (printed symmetric)");
    }

    // -----------------------------------------------------------------------
    // Lifecycle — attach / detach
    // -----------------------------------------------------------------------

    [Fact]
    public void GrafdiggersCage_LeavingBattlefield_UnregistersBothHalves()
    {
        var cage = GrafdiggersCageFactory.Create(_alice, _replacements, _bus);
        _alice.Zones.Library.AddCard(cage);
        cage.SetZone(ZoneType.Library);
        _zones.MoveCard(cage, ZoneType.Library, ZoneType.Battlefield);

        // Sanity — both halves are active.
        CastingRestrictions.IsCastFromZoneGloballyBlocked(ZoneType.Graveyard)
            .Should().BeTrue();

        // Now move Cage to the graveyard.
        _zones.MoveCard(cage, ZoneType.Battlefield, ZoneType.Graveyard);

        // The cast-from-zone block must be gone.
        CastingRestrictions.IsCastFromZoneGloballyBlocked(ZoneType.Graveyard)
            .Should().BeFalse("Cage's LTB tears down the cast-from-zone block");
        CastingRestrictions.IsCastFromZoneGloballyBlocked(ZoneType.Library)
            .Should().BeFalse("Cage's LTB tears down the cast-from-zone block");

        // The replacement must also be gone — a creature ETB from a
        // graveyard now passes through.
        var goyf = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        var intent = new ZoneMoveIntent(
            Card: goyf,
            FromZone: ZoneType.Graveyard,
            ToZone: ZoneType.Battlefield,
            WasCast: false);

        var result = _replacements.Apply(intent);
        result.Should().NotBeNull();
        result!.ToZone.Should().Be(ZoneType.Battlefield,
            "Cage's LTB tears down the creature-ETB cancel replacement");
    }

    [Fact]
    public void GrafdiggersCage_InNonBattlefieldZone_DoesNotRestrict()
    {
        // Cage sits in the library — neither half is active yet.
        var cage = GrafdiggersCageFactory.Create(_alice, _replacements, _bus);
        _alice.Zones.Library.AddCard(cage);
        cage.SetZone(ZoneType.Library);

        CastingRestrictions.IsCastFromZoneGloballyBlocked(ZoneType.Graveyard)
            .Should().BeFalse();
        CastingRestrictions.IsCastFromZoneGloballyBlocked(ZoneType.Library)
            .Should().BeFalse();

        var goyf = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        var intent = new ZoneMoveIntent(
            Card: goyf,
            FromZone: ZoneType.Graveyard,
            ToZone: ZoneType.Battlefield,
            WasCast: false);

        var result = _replacements.Apply(intent);
        result.Should().NotBeNull();
        result!.ToZone.Should().Be(ZoneType.Battlefield);
    }
}
