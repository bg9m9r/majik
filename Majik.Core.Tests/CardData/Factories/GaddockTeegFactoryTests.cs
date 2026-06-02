using FluentAssertions;
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
/// Tests for Gaddock Teeg — Legendary Creature — Kithkin Advisor {G}{W} 2/2
/// (Lorwyn). Oracle text (verified against Scryfall):
///   "Noncreature spells with mana value 4 or greater can't be cast.
///    Noncreature spells with {X} in their mana costs can't be cast."
///
/// Covers:
/// - Card identity (P/T, supertype, subtypes, mana cost) + dispatcher routing.
/// - Printed static A (CR 601.3): while on the battlefield, the validator
///   rejects noncreature casts whose mana value is 4 or greater; lower-MV
///   noncreature casts and creature casts of any MV pass.
/// - Printed static B (CR 601.3): the validator rejects noncreature casts
///   whose printed cost has {X}; creature {X} casts pass.
/// - Symmetric: blocks every player's noncreature spells (including the
///   controller's — the printed text is not player-scoped).
/// - Lifecycle: both blocks lift when Gaddock Teeg leaves the battlefield
///   (CR 603.6 / zone change), and the shape-only path imposes no restriction.
/// </summary>
[Trait("Color", "GW")]
public class GaddockTeegFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;

    public GaddockTeegFactoryTests()
    {
        _zones = new ZoneService(_bus);
        CastingRestrictions.Clear();
    }

    public void Dispose()
    {
        CastingRestrictions.Clear();
    }

    private void PutOnBattlefield(Creature card, Player controller)
    {
        controller.Zones.Library.AddCard(card);
        card.SetZone(ZoneType.Library);
        _zones.MoveCard(card, ZoneType.Library, ZoneType.Battlefield);
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void GaddockTeeg_HasCorrectIdentity_AndPT_AndSubtypes()
    {
        var teeg = GaddockTeegFactory.Create(_alice);

        teeg.Name.Should().Be("Gaddock Teeg");
        teeg.ManaCost.Should().Be("{G}{W}");
        teeg.Power.Should().Be(2);
        teeg.Toughness.Should().Be(2);
        teeg.HasType(CardType.Creature).Should().BeTrue();
        teeg.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        teeg.HasSubtype(CardSubtype.Kithkin).Should().BeTrue();
        teeg.HasSubtype(CardSubtype.Advisor).Should().BeTrue();
        teeg.Owner.Should().BeSameAs(_alice);
        teeg.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_RoutesGaddockTeeg_ToFactory()
    {
        var card = NamedCardFactory.Create("Gaddock Teeg", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Gaddock Teeg");
        card.HasSubtype(CardSubtype.Kithkin).Should().BeTrue();
        ((Creature)card).Power.Should().Be(2);
        ((Creature)card).Toughness.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Static A — noncreature mana value 4 or greater (CR 601.3)
    // -----------------------------------------------------------------------

    [Fact]
    public void TeegOnBattlefield_BlocksNoncreatureCast_OfManaValue4()
    {
        var teeg = GaddockTeegFactory.Create(_alice, eventBus: _bus);
        PutOnBattlefield(teeg, _alice);

        // {3}{R} = mana value 4 — rejected.
        var spell = new Sorcery("Fireball Sorcery", "{3}{R}") { Owner = _bob };
        var action = new CastSpellAction(spell, _bob, sorcerySpeedAvailable: true);
        var result = new ActionValidator().ValidateAction(action);

        result.IsValid.Should().BeFalse();
        result.Violation!.RuleNumber.Should().Be("601.3");
    }

    [Fact]
    public void TeegOnBattlefield_BlocksNoncreatureCast_OfManaValueGreaterThan4()
    {
        var teeg = GaddockTeegFactory.Create(_alice, eventBus: _bus);
        PutOnBattlefield(teeg, _alice);

        // {6} = mana value 6 — rejected ("or greater").
        var spell = new Instant("Big Instant", "{6}") { Owner = _bob };
        var action = new CastSpellAction(spell, _bob, sorcerySpeedAvailable: false);
        new ActionValidator().ValidateAction(action).IsValid.Should().BeFalse();
    }

    [Fact]
    public void TeegOnBattlefield_AllowsNoncreatureCast_OfManaValueBelow4()
    {
        var teeg = GaddockTeegFactory.Create(_alice, eventBus: _bus);
        PutOnBattlefield(teeg, _alice);

        // {1}{R} = mana value 2 — allowed.
        var spell = new Instant("Lightning Helix", "{1}{R}") { Owner = _bob };
        var action = new CastSpellAction(spell, _bob, sorcerySpeedAvailable: false);
        new ActionValidator().ValidateAction(action).IsValid.Should().BeTrue();
    }

    [Fact]
    public void TeegOnBattlefield_AllowsCreatureCast_OfManaValue4OrGreater()
    {
        // The restriction is noncreature-only — a 4-MV creature spell is fine.
        var teeg = GaddockTeegFactory.Create(_alice, eventBus: _bus);
        PutOnBattlefield(teeg, _alice);

        var bigCreature = new Creature("Big Dude", "{2}{G}{G}", 5, 5) { Owner = _bob };
        var action = new CastSpellAction(bigCreature, _bob, sorcerySpeedAvailable: true);
        new ActionValidator().ValidateAction(action).IsValid.Should().BeTrue();
    }

    [Fact]
    public void TeegOnBattlefield_AlsoRestrictsControllersNoncreatureSpells()
    {
        // CR 601.3 — Gaddock Teeg's printed text is NOT player-scoped, so it
        // restricts EVERY player's noncreature spells, including its own
        // controller's (unlike Void Winnower's "your opponents" rail).
        var teeg = GaddockTeegFactory.Create(_alice, eventBus: _bus);
        PutOnBattlefield(teeg, _alice);

        var spell = new Sorcery("Big Sorcery", "{4}") { Owner = _alice };
        var action = new CastSpellAction(spell, _alice, sorcerySpeedAvailable: true);
        new ActionValidator().ValidateAction(action).IsValid.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Static B — noncreature {X} in mana cost (CR 601.3)
    // -----------------------------------------------------------------------

    [Fact]
    public void TeegOnBattlefield_BlocksNoncreatureCast_WithXInCost()
    {
        var teeg = GaddockTeegFactory.Create(_alice, eventBus: _bus);
        PutOnBattlefield(teeg, _alice);

        // {X}{R} — has {X}; printed MV is 1 (below 4) so the X-cost band is the
        // one that fires, proving it's independent of the MV-4 band.
        var spell = new Instant("X Burn", "{X}{R}") { Owner = _bob };
        var action = new CastSpellAction(spell, _bob, sorcerySpeedAvailable: false);
        var result = new ActionValidator().ValidateAction(action);

        result.IsValid.Should().BeFalse();
        result.Violation!.RuleNumber.Should().Be("601.3");
    }

    [Fact]
    public void TeegOnBattlefield_AllowsCreatureCast_WithXInCost()
    {
        // The {X} restriction is noncreature-only — Hydra-style {X}{G}{G}
        // creature spells are fine.
        var teeg = GaddockTeegFactory.Create(_alice, eventBus: _bus);
        PutOnBattlefield(teeg, _alice);

        var hydra = new Creature("X Hydra", "{X}{G}{G}", 0, 0) { Owner = _bob };
        var action = new CastSpellAction(hydra, _bob, sorcerySpeedAvailable: true);
        new ActionValidator().ValidateAction(action).IsValid.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Lifecycle
    // -----------------------------------------------------------------------

    [Fact]
    public void TeegLeavingBattlefield_ReleasesBothRestrictions()
    {
        var teeg = GaddockTeegFactory.Create(_alice, eventBus: _bus);
        PutOnBattlefield(teeg, _alice);

        CastingRestrictions.IsNoncreatureManaValueAtLeastBlocked(4).Should().BeTrue();
        CastingRestrictions.IsNoncreatureXCostBlocked().Should().BeTrue();

        _zones.MoveCard(teeg, ZoneType.Battlefield, ZoneType.Graveyard);

        CastingRestrictions.IsNoncreatureManaValueAtLeastBlocked(4).Should().BeFalse();
        CastingRestrictions.IsNoncreatureXCostBlocked().Should().BeFalse();

        var spell = new Sorcery("Big Sorcery", "{5}") { Owner = _bob };
        var action = new CastSpellAction(spell, _bob, sorcerySpeedAvailable: true);
        new ActionValidator().ValidateAction(action).IsValid.Should().BeTrue();
    }

    [Fact]
    public void ShapeOnly_DoesNotRegisterAnyRestriction()
    {
        var teeg = GaddockTeegFactory.Create(_alice);
        PutOnBattlefield(teeg, _alice);

        CastingRestrictions.IsNoncreatureManaValueAtLeastBlocked(4).Should().BeFalse();
        CastingRestrictions.IsNoncreatureXCostBlocked().Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Registry — direct unit-level coverage
    // -----------------------------------------------------------------------

    [Fact]
    public void NoncreatureManaValueAtLeastBlock_AddAndRemove_Toggles()
    {
        var token = new object();
        CastingRestrictions.IsNoncreatureManaValueAtLeastBlocked(4).Should().BeFalse();

        CastingRestrictions.AddNoncreatureManaValueAtLeastBlock(token, 4);
        CastingRestrictions.IsNoncreatureManaValueAtLeastBlocked(3).Should().BeFalse();
        CastingRestrictions.IsNoncreatureManaValueAtLeastBlocked(4).Should().BeTrue();
        CastingRestrictions.IsNoncreatureManaValueAtLeastBlocked(10).Should().BeTrue();

        CastingRestrictions.RemoveNoncreatureManaValueAtLeastBlock(token);
        CastingRestrictions.IsNoncreatureManaValueAtLeastBlocked(4).Should().BeFalse();
    }

    [Fact]
    public void NoncreatureXCostBlock_AddAndRemove_Toggles()
    {
        var token = new object();
        CastingRestrictions.IsNoncreatureXCostBlocked().Should().BeFalse();

        CastingRestrictions.AddNoncreatureXCostBlock(token);
        CastingRestrictions.IsNoncreatureXCostBlocked().Should().BeTrue();

        // Idempotent for the same token.
        CastingRestrictions.AddNoncreatureXCostBlock(token);
        CastingRestrictions.IsNoncreatureXCostBlocked().Should().BeTrue();

        CastingRestrictions.RemoveNoncreatureXCostBlock(token);
        CastingRestrictions.IsNoncreatureXCostBlocked().Should().BeFalse();
    }
}
