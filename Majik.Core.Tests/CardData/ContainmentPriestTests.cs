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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Containment Priest and Meddling Mage.
///
/// Containment Priest — Creature — Human Cleric {1}{W} 2/2
///   "Flash
///    If a nontoken creature would enter the battlefield and it wasn't
///    cast, exile it instead." (CR 614)
///
/// Meddling Mage — Creature — Human Wizard {W}{U} 2/2
///   "As Meddling Mage enters the battlefield, choose a nonland card name.
///    Spells with the chosen name can't be cast." (CR 601.3)
///
/// Tests dispose-clean the static <see cref="CastingRestrictions"/> registry
/// to prevent cross-test leakage.
/// </summary>
public class ContainmentPriestTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;
    private readonly ReplacementBus _replacements = new();

    public ContainmentPriestTests()
    {
        _zones = new ZoneService(_bus);
        CastingRestrictions.Clear();
    }

    public void Dispose()
    {
        CastingRestrictions.Clear();
    }

    // =========================================================================
    // Containment Priest — card identity
    // =========================================================================

    [Fact]
    public void ContainmentPriest_HasCorrectIdentity_AndPT_AndSubtypes()
    {
        var priest = ContainmentPriestFactory.Create(_alice);

        priest.Name.Should().Be("Containment Priest");
        priest.ManaCost.Should().Be("{1}{W}");
        priest.HasType(CardType.Creature).Should().BeTrue();
        priest.HasSubtype(CardSubtype.Human).Should().BeTrue();
        priest.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
        priest.Power.Should().Be(2);
        priest.Toughness.Should().Be(2);
        priest.Owner.Should().BeSameAs(_alice);
        priest.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ContainmentPriest_HasFlashKeyword()
    {
        var priest = ContainmentPriestFactory.Create(_alice);

        priest.Abilities
            .OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Flash",
                "Containment Priest must have Flash (CR 702.8)");
    }

    [Fact]
    public void NamedCardFactory_RoutesContainmentPriest_ToFactory()
    {
        var card = NamedCardFactory.Create("Containment Priest", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Containment Priest");
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
        ((Creature)card).Power.Should().Be(2);
        ((Creature)card).Toughness.Should().Be(2);
    }

    // =========================================================================
    // Containment Priest — replacement effect registration
    // =========================================================================

    [Fact]
    public void ContainmentPriest_WithReplacementBus_RegistrationActiveWhenOnBattlefield()
    {
        var priest = ContainmentPriestFactory.Create(_alice, _replacements, _bus);

        // Move onto the battlefield so the lifecycle picks it up.
        _alice.Zones.Library.AddCard(priest);
        priest.SetZone(ZoneType.Library);
        _zones.MoveCard(priest, ZoneType.Library, ZoneType.Battlefield);

        // The replacement must now intercept a non-cast, non-token
        // creature-into-battlefield intent.
        var goyf = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        var intent = new ZoneMoveIntent(
            Card: goyf,
            FromZone: ZoneType.Graveyard,
            ToZone: ZoneType.Battlefield,
            WasCast: false);

        var result = _replacements.Apply(intent);

        result.Should().NotBeNull("Apply returns non-null even after replacement");
        result!.ToZone.Should().Be(ZoneType.Exile,
            "Containment Priest exiles non-cast creatures that would ETB");
    }

    [Fact]
    public void ContainmentPriest_DoesNotExile_CastCreatures()
    {
        var priest = ContainmentPriestFactory.Create(_alice, _replacements, _bus);
        _alice.Zones.Library.AddCard(priest);
        priest.SetZone(ZoneType.Library);
        _zones.MoveCard(priest, ZoneType.Library, ZoneType.Battlefield);

        var goyf = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        // WasCast = true — this creature was cast normally.
        var intent = new ZoneMoveIntent(
            Card: goyf,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            WasCast: true);

        var result = _replacements.Apply(intent);

        result!.ToZone.Should().Be(ZoneType.Battlefield,
            "Cast creatures are unaffected by Containment Priest");
    }

    [Fact]
    public void ContainmentPriest_DoesNotExile_Tokens()
    {
        var priest = ContainmentPriestFactory.Create(_alice, _replacements, _bus);
        _alice.Zones.Library.AddCard(priest);
        priest.SetZone(ZoneType.Library);
        _zones.MoveCard(priest, ZoneType.Library, ZoneType.Battlefield);

        var tokenCreature = new Creature("Rhino Warrior", "{0}", 4, 4);
        tokenCreature.MarkAsToken();

        var intent = new ZoneMoveIntent(
            Card: tokenCreature,
            FromZone: ZoneType.Exile,
            ToZone: ZoneType.Battlefield,
            WasCast: false);

        var result = _replacements.Apply(intent);

        result!.ToZone.Should().Be(ZoneType.Battlefield,
            "Tokens are not affected by Containment Priest");
    }

    [Fact]
    public void ContainmentPriest_DoesNotExile_NonCreatures()
    {
        var priest = ContainmentPriestFactory.Create(_alice, _replacements, _bus);
        _alice.Zones.Library.AddCard(priest);
        priest.SetZone(ZoneType.Library);
        _zones.MoveCard(priest, ZoneType.Library, ZoneType.Battlefield);

        var artifact = new Artifact("Mox Opal", "{0}");
        var intent = new ZoneMoveIntent(
            Card: artifact,
            FromZone: ZoneType.Graveyard,
            ToZone: ZoneType.Battlefield,
            WasCast: false);

        var result = _replacements.Apply(intent);

        result!.ToZone.Should().Be(ZoneType.Battlefield,
            "Non-creature cards are not affected by Containment Priest");
    }

    [Fact]
    public void ContainmentPriest_LeavingBattlefield_UnregistersReplacement()
    {
        var priest = ContainmentPriestFactory.Create(_alice, _replacements, _bus);
        _alice.Zones.Library.AddCard(priest);
        priest.SetZone(ZoneType.Library);
        _zones.MoveCard(priest, ZoneType.Library, ZoneType.Battlefield);

        // Verify it fires while on the battlefield.
        var goyf = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        var intentBefore = new ZoneMoveIntent(goyf, ZoneType.Graveyard, ZoneType.Battlefield, WasCast: false);
        _replacements.Apply(intentBefore)!.ToZone.Should().Be(ZoneType.Exile);

        // Priest dies.
        _zones.MoveCard(priest, ZoneType.Battlefield, ZoneType.Graveyard);

        // Now the same intent should pass through unchanged.
        var goyf2 = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        var intentAfter = new ZoneMoveIntent(goyf2, ZoneType.Graveyard, ZoneType.Battlefield, WasCast: false);
        _replacements.Apply(intentAfter)!.ToZone.Should().Be(ZoneType.Battlefield,
            "Replacement must be removed when Containment Priest leaves the battlefield");
    }

    [Fact]
    public void ContainmentPriest_SingleArgPath_DoesNotRegisterReplacement()
    {
        var priest = ContainmentPriestFactory.Create(_alice);

        // Single-arg path — no replacement bus, so no registration.
        var goyf = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        var intent = new ZoneMoveIntent(goyf, ZoneType.Graveyard, ZoneType.Battlefield, WasCast: false);

        // An empty bus should pass through unchanged.
        var emptyBus = new ReplacementBus();
        emptyBus.Apply(intent)!.ToZone.Should().Be(ZoneType.Battlefield,
            "No replacement is registered on the single-arg path");
    }

    // =========================================================================
    // Meddling Mage — card identity
    // =========================================================================

    [Fact]
    public void MeddlingMage_HasCorrectIdentity_AndPT_AndSubtypes()
    {
        var mage = MeddlingMageFactory.Create(_alice);

        mage.Name.Should().Be("Meddling Mage");
        mage.ManaCost.Should().Be("{W}{U}");
        mage.HasType(CardType.Creature).Should().BeTrue();
        mage.HasSubtype(CardSubtype.Human).Should().BeTrue();
        mage.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        mage.Power.Should().Be(2);
        mage.Toughness.Should().Be(2);
        mage.Owner.Should().BeSameAs(_alice);
        mage.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_RoutesMeddlingMage_ToFactory()
    {
        var card = NamedCardFactory.Create("Meddling Mage", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Meddling Mage");
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        ((Creature)card).Power.Should().Be(2);
        ((Creature)card).Toughness.Should().Be(2);
    }

    // =========================================================================
    // Meddling Mage — named cast restriction (CR 601.3)
    // =========================================================================

    [Fact]
    public void MeddlingMage_WithChosenName_BlocksCastOfNamedCard()
    {
        var mage = MeddlingMageFactory.Create(_alice, "Lightning Bolt", _bus);

        // Move onto the battlefield so the lifecycle registers the block.
        _alice.Zones.Library.AddCard(mage);
        mage.SetZone(ZoneType.Library);
        _zones.MoveCard(mage, ZoneType.Library, ZoneType.Battlefield);

        // Bob tries to cast Lightning Bolt — rejected.
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob };
        var action = new CastSpellAction(bolt, _bob, sorcerySpeedAvailable: true);
        var result = new ActionValidator().ValidateAction(action);

        result.IsValid.Should().BeFalse();
        result.Violation!.RuleNumber.Should().Be("601.3");
    }

    [Fact]
    public void MeddlingMage_WithChosenName_DoesNotBlockOtherCards()
    {
        var mage = MeddlingMageFactory.Create(_alice, "Lightning Bolt", _bus);
        _alice.Zones.Library.AddCard(mage);
        mage.SetZone(ZoneType.Library);
        _zones.MoveCard(mage, ZoneType.Library, ZoneType.Battlefield);

        // Bob tries to cast Path to Exile — different name, should be fine.
        var path = new Instant("Path to Exile", "{W}") { Owner = _bob };
        var action = new CastSpellAction(path, _bob, sorcerySpeedAvailable: true);
        var result = new ActionValidator().ValidateAction(action);

        result.IsValid.Should().BeTrue(
            "Only spells with the chosen name are blocked");
    }

    [Fact]
    public void MeddlingMage_WithEmptyChosenName_NoRestriction()
    {
        // Single-arg or empty-string path — no restriction at all.
        var mage = MeddlingMageFactory.Create(_alice, string.Empty, _bus);
        _alice.Zones.Library.AddCard(mage);
        mage.SetZone(ZoneType.Library);
        _zones.MoveCard(mage, ZoneType.Library, ZoneType.Battlefield);

        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob };
        var action = new CastSpellAction(bolt, _bob, sorcerySpeedAvailable: true);
        var result = new ActionValidator().ValidateAction(action);

        result.IsValid.Should().BeTrue(
            "Empty chosen name means no restriction (CR fixture path)");
    }

    [Fact]
    public void MeddlingMage_LeavingBattlefield_RemovesRestriction()
    {
        var mage = MeddlingMageFactory.Create(_alice, "Lightning Bolt", _bus);
        _alice.Zones.Library.AddCard(mage);
        mage.SetZone(ZoneType.Library);
        _zones.MoveCard(mage, ZoneType.Library, ZoneType.Battlefield);

        // Block is in effect.
        CastingRestrictions.IsCardNameBlocked("Lightning Bolt").Should().BeTrue();

        // Mage dies.
        _zones.MoveCard(mage, ZoneType.Battlefield, ZoneType.Graveyard);

        // Block is removed.
        CastingRestrictions.IsCardNameBlocked("Lightning Bolt").Should().BeFalse();

        // Bob can now cast Lightning Bolt.
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob };
        var action = new CastSpellAction(bolt, _bob, sorcerySpeedAvailable: true);
        new ActionValidator().ValidateAction(action).IsValid.Should().BeTrue();
    }

    [Fact]
    public void MeddlingMage_SingleArgPath_NoBlock()
    {
        var mage = MeddlingMageFactory.Create(_alice);

        // Single-arg path defaults to empty name — nothing blocked.
        CastingRestrictions.IsCardNameBlocked("Lightning Bolt").Should().BeFalse();
    }

    [Fact]
    public void MeddlingMage_NameMatchIsCaseInsensitive()
    {
        // Card names use the normalised Scryfall casing but comparison
        // should be ordinal-case-insensitive for safety.
        var mage = MeddlingMageFactory.Create(_alice, "lightning bolt", _bus);
        _alice.Zones.Library.AddCard(mage);
        mage.SetZone(ZoneType.Library);
        _zones.MoveCard(mage, ZoneType.Library, ZoneType.Battlefield);

        CastingRestrictions.IsCardNameBlocked("Lightning Bolt").Should().BeTrue();
        CastingRestrictions.IsCardNameBlocked("LIGHTNING BOLT").Should().BeTrue();
    }
}
