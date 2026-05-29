using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="BleachboneVergeFactory"/>.
///
/// Bleachbone Verge — WB Verge cycle. Counterpart to Gloomlake Verge (UB),
/// Wastewood Verge (GB), Sunsplit Verge (RW), Gleamfield Verge (GW),
/// Floodfarm Verge (UR).
///
/// Oracle text:
///   "{T}: Add {B}.
///    {T}: Add {W}. Activate only if you control a Plains or a Swamp."
///
/// CR 605.1 — both abilities are mana abilities (do not use the stack).
/// CR 605.4 — "Activate only if …" is an activation restriction checked
/// before activation is legal; it is NOT a cost and does NOT use the stack.
///
/// Covers:
/// - Card identity: name "Bleachbone Verge", Land type, not Legendary.
/// - Owner / controller assignment.
/// - Exactly two ManaAbility objects; no TriggeredAbility / ActivatedAbility.
/// - {B} ability is always activable (untapped land, no restriction).
/// - {W} ability is legal only when controller's battlefield contains a
///   permanent with Plains or Swamp subtype.
/// - Tap-contention: activating either ability taps the land, rendering the
///   other ability unactivatable for the same untap cycle.
/// - NamedCardFactory dispatch round-trip.
/// </summary>
public class BleachboneVergeTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void BleachboneVerge_IsLand()
    {
        var land = BleachboneVergeFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void BleachboneVerge_NameIsCorrect()
    {
        var land = BleachboneVergeFactory.Create(_alice);

        land.Name.Should().Be("Bleachbone Verge");
    }

    [Fact]
    public void BleachboneVerge_IsNotLegendary()
    {
        var land = BleachboneVergeFactory.Create(_alice);

        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void BleachboneVerge_OwnerAndControllerAreSet()
    {
        var land = BleachboneVergeFactory.Create(_alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Mana abilities — structure
    // -----------------------------------------------------------------------

    [Fact]
    public void BleachboneVerge_HasExactlyTwoManaAbilities()
    {
        var land = BleachboneVergeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2,
            "one for {B} and one for {W}");
    }

    [Fact]
    public void BleachboneVerge_HasBlackManaAbility()
    {
        var land = BleachboneVergeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Black == 1 && m.ManaGenerated.White == 0,
                "must have exactly one {B} mana ability");
    }

    [Fact]
    public void BleachboneVerge_HasWhiteManaAbility()
    {
        var land = BleachboneVergeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.White == 1 && m.ManaGenerated.Black == 0,
                "must have exactly one {W} mana ability");
    }

    [Fact]
    public void BleachboneVerge_BlackManaAbility_ProducesOnlyBlack()
    {
        var land = BleachboneVergeFactory.Create(_alice);
        var black = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Black == 1);

        black.ManaGenerated.Generic.Should().Be(0);
        black.ManaGenerated.White.Should().Be(0);
        black.ManaGenerated.Blue.Should().Be(0);
        black.ManaGenerated.Red.Should().Be(0);
        black.ManaGenerated.Green.Should().Be(0);
    }

    [Fact]
    public void BleachboneVerge_WhiteManaAbility_ProducesOnlyWhite()
    {
        var land = BleachboneVergeFactory.Create(_alice);
        var white = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.White == 1);

        white.ManaGenerated.Generic.Should().Be(0);
        white.ManaGenerated.Black.Should().Be(0);
        white.ManaGenerated.Blue.Should().Be(0);
        white.ManaGenerated.Red.Should().Be(0);
        white.ManaGenerated.Green.Should().Be(0);
    }

    [Fact]
    public void BleachboneVerge_HasNoTriggeredAbilities()
    {
        var land = BleachboneVergeFactory.Create(_alice);

        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Bleachbone Verge has no triggered abilities");
    }

    [Fact]
    public void BleachboneVerge_HasNoNonManaActivatedAbilities()
    {
        var land = BleachboneVergeFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Bleachbone Verge has no non-mana activated abilities");
    }

    // -----------------------------------------------------------------------
    // {B} ability — activation legality (no restriction)
    // -----------------------------------------------------------------------

    [Fact]
    public void BleachboneVerge_BlackAbility_CanActivate_WhenUntapped_NoPlainsOrSwamp()
    {
        var land = BleachboneVergeFactory.Create(_alice);
        // No Plains or Swamp on battlefield — {B} is still legal.
        var black = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Black == 1);

        black.CanActivate().Should().BeTrue(
            "{T}: Add {B} has no 'activate only if' restriction");
    }

    [Fact]
    public void BleachboneVerge_BlackAbility_CanActivate_WhenUntapped_WithSwamp()
    {
        var land = BleachboneVergeFactory.Create(_alice);
        var swamp = new Land("Swamp", subtypes: new[] { CardSubtype.Swamp });
        swamp.SetOwner(_alice);
        swamp.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(swamp);

        var black = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Black == 1);

        black.CanActivate().Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // {W} ability — activation legality (conditional)
    // -----------------------------------------------------------------------

    [Fact]
    public void BleachboneVerge_WhiteAbility_CannotActivate_WhenNoPlainsOrSwamp()
    {
        var land = BleachboneVergeFactory.Create(_alice);
        // Controller controls no Plains or Swamp — {W} is blocked.
        var white = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.White == 1);

        white.CanActivate().Should().BeFalse(
            "'Activate only if you control a Plains or a Swamp' blocks activation");
    }

    [Fact]
    public void BleachboneVerge_WhiteAbility_CanActivate_WhenControllerHasPlains()
    {
        var land = BleachboneVergeFactory.Create(_alice);
        var plains = new Land("Plains", subtypes: new[] { CardSubtype.Plains });
        plains.SetOwner(_alice);
        plains.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(plains);

        var white = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.White == 1);

        white.CanActivate().Should().BeTrue(
            "controller has a Plains on the battlefield");
    }

    [Fact]
    public void BleachboneVerge_WhiteAbility_CanActivate_WhenControllerHasSwamp()
    {
        var land = BleachboneVergeFactory.Create(_alice);
        var swamp = new Land("Swamp", subtypes: new[] { CardSubtype.Swamp });
        swamp.SetOwner(_alice);
        swamp.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(swamp);

        var white = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.White == 1);

        white.CanActivate().Should().BeTrue(
            "controller has a Swamp on the battlefield");
    }

    [Fact]
    public void BleachboneVerge_WhiteAbility_CannotActivate_WhenForestOnBattlefield_NotPlainsOrSwamp()
    {
        var land = BleachboneVergeFactory.Create(_alice);
        var forest = new Land("Forest", subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        forest.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(forest);

        var white = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.White == 1);

        white.CanActivate().Should().BeFalse(
            "Forest does not satisfy the Plains-or-Swamp restriction");
    }

    // -----------------------------------------------------------------------
    // Tap contention — only one ability fires per untap cycle
    // -----------------------------------------------------------------------

    [Fact]
    public void BleachboneVerge_AfterActivatingBlackAbility_WhiteAbilityCannotActivate()
    {
        var land = BleachboneVergeFactory.Create(_alice);
        // Put a Swamp on the battlefield so {W} would otherwise be legal.
        var swamp = new Land("Swamp", subtypes: new[] { CardSubtype.Swamp });
        swamp.SetOwner(_alice);
        swamp.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(swamp);

        var black = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Black == 1);
        var white = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.White == 1);

        // Activate {B} — this taps the land.
        black.Activate();

        // {W} cannot fire because the source is now tapped.
        white.CanActivate().Should().BeFalse(
            "the land is already tapped; both abilities share the same {T} cost");
    }

    [Fact]
    public void BleachboneVerge_AfterActivatingWhiteAbility_BlackAbilityCannotActivate()
    {
        var land = BleachboneVergeFactory.Create(_alice);
        var plains = new Land("Plains", subtypes: new[] { CardSubtype.Plains });
        plains.SetOwner(_alice);
        plains.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(plains);

        var black = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Black == 1);
        var white = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.White == 1);

        // Activate {W} — this taps the land.
        white.Activate();

        // {B} cannot fire because the source is now tapped.
        black.CanActivate().Should().BeFalse(
            "the land is already tapped; both abilities share the same {T} cost");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void NamedCardFactory_Dispatches_BleachboneVerge()
    {
        var card = NamedCardFactory.Create("Bleachbone Verge", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Bleachbone Verge");
        card.HasType(CardType.Land).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
    }
}
