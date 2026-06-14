using FluentAssertions;
using Majik.Core.Abilities;
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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Archon of Emeria (Zendikar Rising, {2}{W}).
///
/// Oracle (verified against Scryfall):
///   "Flying
///    Each player can't cast more than one spell each turn.
///    Nonbasic lands your opponents control enter tapped."
///
/// Coverage:
///   * Identity: Creature — Archon {2}{W} 2/3 with Flying.
///   * Dispatch through <see cref="NamedCardFactory"/>.
///   * One-spell-per-turn cap: every player gets a cap of 1 while Archon is out
///     (CR 601.3); ActionValidator allows the first cast and blocks the second.
///   * Symmetry (CR 109.5): the cap applies to Archon's controller too.
///   * Per-turn reset (CR 514.2): a consumed cap is re-seeded to 1 at turn start.
///   * Cap lifts when Archon leaves the battlefield.
///   * Opponent's nonbasic lands enter tapped (CR 305.6); basic lands do not.
///   * Controller's own nonbasic lands are unaffected (one-sided, CR 109.5).
///   * Opponent's creatures are NOT tapped (Archon taps lands only, unlike
///     Thalia, Heretic Cathar).
///   * Enters-tapped replacement unregisters when Archon leaves the battlefield.
///   * Single-arg dispatch path registers neither static.
/// </summary>
[Trait("Color", "W")]
public class ArchonOfEmeriaTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();
    private readonly ReplacementBus _replacements = new();
    private readonly ZoneService _zones;
    private readonly ActionValidator _validator = new();

    public ArchonOfEmeriaTests()
    {
        _zones = new ZoneService(_bus, _replacements);
        CastingRestrictions.Clear();
    }

    public void Dispose() => CastingRestrictions.Clear();

    private IReadOnlyList<Player> AllPlayers() => new[] { _alice, _bob };

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasCorrectCardShape()
    {
        var archon = ArchonOfEmeriaFactory.Create(_alice);

        archon.Name.Should().Be("Archon of Emeria");
        archon.HasType(CardType.Creature).Should().BeTrue();
        archon.HasSubtype(CardSubtype.Archon).Should().BeTrue();
        archon.ManaCost.Should().Be("{2}{W}");
        archon.ManaCostValue.Generic.Should().Be(2);
        archon.ManaCostValue.White.Should().Be(1);
        archon.Power.Should().Be(2);
        archon.Toughness.Should().Be(3);
        archon.Owner.Should().BeSameAs(_alice);
        archon.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Create_HasFlyingKeyword()
    {
        var archon = ArchonOfEmeriaFactory.Create(_alice);

        archon.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k =>
                k.Keyword.Equals("Flying", StringComparison.OrdinalIgnoreCase),
                "CR 702.9 — Flying keyword marker must be attached");
    }

    [Fact]
    public void Dispatch_ByName_ProducesArchon()
    {
        var card = NamedCardFactory.Create("Archon of Emeria", _alice);

        card.Should().NotBeNull();
        card.Name.Should().Be("Archon of Emeria");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Archon).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Battlefield helper
    // -----------------------------------------------------------------------

    private Creature ArchonOnBattlefield()
    {
        var archon = ArchonOfEmeriaFactory.Create(
            _alice, _replacements, _bus, AllPlayers);
        _alice.Zones.Library.AddCard(archon);
        archon.SetZone(ZoneType.Library);
        _zones.MoveCard(archon, ZoneType.Library, ZoneType.Battlefield);
        return archon;
    }

    // -----------------------------------------------------------------------
    // One-spell-per-turn cast cap (CR 601.3 / 109.5 / 514.2)
    // -----------------------------------------------------------------------

    [Fact]
    public void CastCap_AppliesToBothPlayers_WhileArchonIsOut()
    {
        ArchonOnBattlefield();

        // Each player may still cast their first spell.
        CastingRestrictions.IsAtSpellsPerTurnCap(_alice).Should().BeFalse();
        CastingRestrictions.IsAtSpellsPerTurnCap(_bob).Should().BeFalse();

        // After one cast each, both are at the cap.
        CastingRestrictions.RecordSpellCast(_alice);
        CastingRestrictions.RecordSpellCast(_bob);

        CastingRestrictions.IsAtSpellsPerTurnCap(_alice).Should().BeTrue(
            "Each player can't cast more than one spell each turn (CR 601.3) — symmetric");
        CastingRestrictions.IsAtSpellsPerTurnCap(_bob).Should().BeTrue();
    }

    [Fact]
    public void ActionValidator_AllowsFirstSpell_BlocksSecond()
    {
        ArchonOnBattlefield();

        var spell = ArchonOfEmeriaFactory.Create(_bob);

        // First spell allowed.
        var first = new CastSpellAction(spell, _bob, sorcerySpeedAvailable: true);
        _validator.ValidateAction(first).IsValid.Should().BeTrue(
            "the first spell of the turn is allowed (cap = 1 remaining)");

        // Simulate the cast (SpellCastFlow's per-cast hook on the static rail).
        CastingRestrictions.RecordSpellCast(_bob);

        var second = new CastSpellAction(spell, _bob, sorcerySpeedAvailable: true);
        var result = _validator.ValidateAction(second);
        result.IsValid.Should().BeFalse(
            "a second spell is blocked while Archon of Emeria is out (CR 601.3)");
        result.Violation!.RuleNumber.Should().Be("601.3");
    }

    [Fact]
    public void CastCap_ResetsAtTurnStart()
    {
        ArchonOnBattlefield();

        // Both players cast their one allowed spell this turn.
        CastingRestrictions.RecordSpellCast(_alice);
        CastingRestrictions.RecordSpellCast(_bob);
        CastingRestrictions.IsAtSpellsPerTurnCap(_alice).Should().BeTrue();
        CastingRestrictions.IsAtSpellsPerTurnCap(_bob).Should().BeTrue();

        // A new turn begins — the "each turn" allowance refreshes (CR 514.2).
        _bus.Publish(new TurnStartedEvent(_bob, 2));

        CastingRestrictions.IsAtSpellsPerTurnCap(_alice).Should().BeFalse(
            "the per-turn cap is re-seeded to 1 at turn start");
        CastingRestrictions.IsAtSpellsPerTurnCap(_bob).Should().BeFalse();
    }

    [Fact]
    public void CastCap_LiftsWhenArchonLeavesBattlefield()
    {
        var archon = ArchonOnBattlefield();

        CastingRestrictions.IsAtSpellsPerTurnCap(_alice).Should().BeFalse();
        CastingRestrictions.RecordSpellCast(_alice);
        CastingRestrictions.IsAtSpellsPerTurnCap(_alice).Should().BeTrue();

        // Archon dies — the static stops applying.
        _zones.MoveCard(archon, ZoneType.Battlefield, ZoneType.Graveyard);

        CastingRestrictions.IsAtSpellsPerTurnCap(_alice).Should().BeFalse(
            "the cast cap lifts when Archon of Emeria leaves the battlefield");
    }

    // -----------------------------------------------------------------------
    // Nonbasic lands enter tapped (CR 614.1c / 305.6 / 109.5)
    // -----------------------------------------------------------------------

    [Fact]
    public void OpponentNonbasicLand_EntersTapped()
    {
        ArchonOnBattlefield();

        var nonbasic = new Land("Steam Vents");
        nonbasic.SetOwner(_bob);
        nonbasic.SetController(_bob);
        _bob.Zones.Hand.AddCard(nonbasic);
        nonbasic.SetZone(ZoneType.Hand);

        _zones.MoveCard(nonbasic, ZoneType.Hand, ZoneType.Battlefield, _bob);

        nonbasic.IsTapped.Should().BeTrue(
            "Archon makes opponents' nonbasic lands enter tapped (CR 305.6)");
    }

    [Fact]
    public void OpponentBasicLand_EntersUntapped()
    {
        ArchonOnBattlefield();

        var island = new Land("Island", supertypes: new[] { CardSupertype.Basic });
        island.SetOwner(_bob);
        island.SetController(_bob);
        _bob.Zones.Hand.AddCard(island);
        island.SetZone(ZoneType.Hand);

        _zones.MoveCard(island, ZoneType.Hand, ZoneType.Battlefield, _bob);

        island.IsTapped.Should().BeFalse(
            "basic lands are NOT affected — printed text says 'nonbasic lands' (CR 305.6)");
    }

    [Fact]
    public void ControllerOwnNonbasicLand_EntersUntapped_OneSided()
    {
        ArchonOnBattlefield();

        var ownNonbasic = new Land("Steam Vents");
        ownNonbasic.SetOwner(_alice);
        ownNonbasic.SetController(_alice);
        _alice.Zones.Hand.AddCard(ownNonbasic);
        ownNonbasic.SetZone(ZoneType.Hand);

        _zones.MoveCard(ownNonbasic, ZoneType.Hand, ZoneType.Battlefield, _alice);

        ownNonbasic.IsTapped.Should().BeFalse(
            "Archon is one-sided — only 'your opponents control' lands enter tapped (CR 109.5)");
    }

    [Fact]
    public void OpponentCreature_EntersUntapped_LandsOnly()
    {
        ArchonOnBattlefield();

        // Archon taps LANDS only (unlike Thalia, Heretic Cathar's creatures clause).
        var goblin = new Creature("Goblin Guide", "{R}", 2, 2);
        goblin.SetOwner(_bob);
        goblin.SetController(_bob);
        _bob.Zones.Hand.AddCard(goblin);
        goblin.SetZone(ZoneType.Hand);

        _zones.MoveCard(goblin, ZoneType.Hand, ZoneType.Battlefield, _bob);

        goblin.IsTapped.Should().BeFalse(
            "Archon of Emeria does NOT tap creatures — only nonbasic lands");
    }

    [Fact]
    public void ArchonLeavesBattlefield_EntersTappedReplacementUnregisters()
    {
        var archon = ArchonOnBattlefield();

        var landBefore = new Land("Steam Vents");
        landBefore.SetOwner(_bob);
        landBefore.SetController(_bob);
        _bob.Zones.Hand.AddCard(landBefore);
        landBefore.SetZone(ZoneType.Hand);
        _zones.MoveCard(landBefore, ZoneType.Hand, ZoneType.Battlefield, _bob);
        landBefore.IsTapped.Should().BeTrue();

        _zones.MoveCard(archon, ZoneType.Battlefield, ZoneType.Graveyard);

        var landAfter = new Land("Steam Vents");
        landAfter.SetOwner(_bob);
        landAfter.SetController(_bob);
        _bob.Zones.Hand.AddCard(landAfter);
        landAfter.SetZone(ZoneType.Hand);
        _zones.MoveCard(landAfter, ZoneType.Hand, ZoneType.Battlefield, _bob);

        landAfter.IsTapped.Should().BeFalse(
            "replacement must be removed when Archon leaves the battlefield");
    }

    // -----------------------------------------------------------------------
    // Dispatch / single-arg path
    // -----------------------------------------------------------------------

    [Fact]
    public void SingleArgPath_RegistersNoStatics()
    {
        // No replacement bus, no resolver — neither static activates.
        ArchonOfEmeriaFactory.Create(_alice);

        // Enters-tapped not registered.
        var land = new Land("Steam Vents");
        var intent = new ZoneMoveIntent(
            land, ZoneType.Hand, ZoneType.Battlefield, Controller: _bob);
        var emptyBus = new ReplacementBus();
        emptyBus.Apply(intent)!.EntersTapped.Should().BeFalse(
            "no enters-tapped replacement is registered on the single-arg path");

        // Cast cap not registered.
        CastingRestrictions.RecordSpellCast(_alice);
        CastingRestrictions.IsAtSpellsPerTurnCap(_alice).Should().BeFalse(
            "no cast cap is registered on the single-arg path");
    }
}
