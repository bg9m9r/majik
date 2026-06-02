using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using ManaColor = Majik.Core.ValueObjects.ManaColor;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="SejiriShelterFactory"/> and
/// <see cref="SejiriGlacierFactory"/> — the front + back faces of the Zendikar
/// Rising modal double-faced card Sejiri Shelter // Sejiri Glacier.
///
/// Front face (Sejiri Shelter, {1}{W}):
///   Instant. "Target creature you control gains protection from the color of
///   your choice until end of turn."
///
/// Back face (Sejiri Glacier):
///   Land. "This land enters tapped." "{T}: Add {W}."
///
/// Covers:
/// - Identity for both faces (name, cost, type, colour, owner).
/// - MDFC face-tracker (front starts on front; back pre-flipped).
/// - Front: single 1..1 "creature you control" target request.
/// - Front: resolution grants protection from the chosen colour EOT.
/// - Front: default colour pick; injected colour pick honoured.
/// - Front: grant expires at end of turn (ExpireEndOfTurn revokes it).
/// - Front: illegal target / not-controlled / non-creature → clean no-op.
/// - Back: Land type, non-basic, {T}: Add {W} mana ability.
/// </summary>
[Trait("Color", "W")]
public class SejiriShelterFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeBear(string name, Player owner)
    {
        var c = new Creature(name, "{1}{G}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        return c;
    }

    private static void PutOnBattlefield(Player owner, Permanent perm)
    {
        owner.Zones.Battlefield.AddCard(perm);
        perm.SetZone(ZoneType.Battlefield);
        perm.SetController(owner);
    }

    // =========================================================================
    // Front face — identity + MDFC
    // =========================================================================

    [Fact]
    public void SejiriShelter_Identity_W_Instant()
    {
        var card = SejiriShelterFactory.Create(_alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Sejiri Shelter");
        card.ManaCost.Should().Be("{1}{W}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SejiriShelter_IsWhite()
    {
        var card = SejiriShelterFactory.Create(_alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.White, "the {W} pip makes it white");
        colors.Should().NotContain(ManaColor.Blue);
        colors.Should().NotContain(ManaColor.Black);
        colors.Should().NotContain(ManaColor.Red);
        colors.Should().NotContain(ManaColor.Green);
    }

    [Fact]
    public void SejiriShelter_CarriesMdfcState_FrontFace()
    {
        var card = SejiriShelterFactory.Create(_alice);

        card.MdfcState.Should().NotBeNull("Sejiri Shelter is the front face of an MDFC");
        card.MdfcState!.FrontFaceName.Should().Be("Sejiri Shelter");
        card.MdfcState!.BackFaceName.Should().Be("Sejiri Glacier");
        card.MdfcState!.IsBackFace.Should().BeFalse("front-face card starts on the front face");
        card.MdfcState!.ActiveFaceName.Should().Be("Sejiri Shelter");
    }

    // =========================================================================
    // Front face — SpellDefinition shape
    // =========================================================================

    [Fact]
    public void BuildSpellDefinition_SingleTargetCreature_NoVariableX()
    {
        var def = SejiriShelterFactory.BuildSpellDefinition(_alice, o => o!);

        def.HasVariableX.Should().BeFalse("Sejiri Shelter is not an X-spell");
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1, "Target creature you control — exactly one");
        def.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    [Fact]
    public void BuildSpellDefinition_CandidateGather_OnlyCasterControlledCreatures()
    {
        var mine = MakeBear("My Bear", _alice);
        PutOnBattlefield(_alice, mine);
        var theirs = MakeBear("Their Bear", _bob);
        PutOnBattlefield(_bob, theirs);

        var def = SejiriShelterFactory.BuildSpellDefinition(_alice, o => o!);
        var candidates = def.TargetRequests[0].CandidateGatherer!(null!);

        candidates.Should().Contain(mine);
        candidates.Should().NotContain(theirs, "printed 'creature you control' — opponent creatures aren't candidates");
    }

    // =========================================================================
    // Front face — resolution: grant protection from chosen colour
    // =========================================================================

    [Fact]
    public void Resolve_GrantsProtection_DefaultColour_White()
    {
        var bear = MakeBear("Grizzly Bears", _alice);
        bear.ActiveEffects = new ContinuousEffectsService();
        PutOnBattlefield(_alice, bear);

        var granted = SejiriShelterFactory.Resolve(_alice, bear, o => o!);

        granted.Should().BeSameAs(bear);
        var prot = bear.Abilities.OfType<ProtectionAbility>().Should().ContainSingle(
            "protection from the chosen colour is granted").Subject;
        prot.Quality.Should().Be("white", "default colour pick when none supplied");
    }

    [Fact]
    public void Resolve_HonoursInjectedColourPick()
    {
        var bear = MakeBear("Grizzly Bears", _alice);
        bear.ActiveEffects = new ContinuousEffectsService();
        PutOnBattlefield(_alice, bear);

        SejiriShelterFactory.Resolve(
            _alice, bear, o => o!,
            colorPicker: (_, _) => SejiriShelterFactory.QualityRed);

        var prot = bear.Abilities.OfType<ProtectionAbility>().Single();
        prot.Quality.Should().Be("red", "the injected colour pick is honoured");
    }

    [Fact]
    public void Resolve_Grant_ExpiresAtEndOfTurn()
    {
        var bear = MakeBear("Grizzly Bears", _alice);
        var effects = new ContinuousEffectsService();
        bear.ActiveEffects = effects;
        PutOnBattlefield(_alice, bear);

        SejiriShelterFactory.Resolve(_alice, bear, o => o!);
        bear.Abilities.OfType<ProtectionAbility>().Should().ContainSingle("grant is live before cleanup");

        // CR 514.2 — cleanup step expires "until end of turn" grants.
        effects.ExpireEndOfTurn();

        bear.Abilities.OfType<ProtectionAbility>().Should().BeEmpty(
            "the until-end-of-turn grant is revoked at cleanup (CR 514.2 / CR 613.6e)");
    }

    // =========================================================================
    // Front face — illegal targets (CR 608.2b/608.2c)
    // =========================================================================

    [Fact]
    public void Resolve_TargetNotOnBattlefield_NoGrant()
    {
        var bear = MakeBear("Grizzly Bears", _alice);
        bear.ActiveEffects = new ContinuousEffectsService();
        bear.SetZone(ZoneType.Hand); // not on battlefield

        var granted = SejiriShelterFactory.Resolve(_alice, bear, o => o!);

        granted.Should().BeNull("illegal target at resolution → spell does nothing (CR 608.2c)");
        bear.Abilities.OfType<ProtectionAbility>().Should().BeEmpty("no grant on an illegal target");
    }

    [Fact]
    public void Resolve_TargetNotControlledByCaster_NoGrant()
    {
        var bear = MakeBear("Grizzly Bears", _bob);
        bear.ActiveEffects = new ContinuousEffectsService();
        PutOnBattlefield(_bob, bear);

        var granted = SejiriShelterFactory.Resolve(_alice, bear, o => o!);

        granted.Should().BeNull("Sejiri Shelter only targets a creature YOU control");
        bear.Abilities.OfType<ProtectionAbility>().Should().BeEmpty();
    }

    [Fact]
    public void Resolve_TargetNotACreature_NoGrant()
    {
        var granted = SejiriShelterFactory.Resolve(
            _alice,
            rawTarget: "not-a-creature",
            resolver: _ => "not-a-creature");

        granted.Should().BeNull("non-creature target → spell does nothing");
    }

    // =========================================================================
    // Back face — identity + mana
    // =========================================================================

    [Fact]
    public void SejiriGlacier_Identity_Land()
    {
        var land = SejiriGlacierFactory.Create(_alice);

        land.Should().BeOfType<Land>();
        land.Name.Should().Be("Sejiri Glacier");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("Sejiri Glacier is non-basic");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SejiriGlacier_CarriesMdfcState_PreFlippedToBackFace()
    {
        var land = SejiriGlacierFactory.Create(_alice);

        land.MdfcState.Should().NotBeNull("Sejiri Glacier is the back face of an MDFC");
        land.MdfcState!.FrontFaceName.Should().Be("Sejiri Shelter");
        land.MdfcState!.BackFaceName.Should().Be("Sejiri Glacier");
        land.MdfcState!.IsBackFace.Should().BeTrue(
            "the back-face card is constructed pre-flipped to the back face");
        land.MdfcState!.ActiveFaceName.Should().Be("Sejiri Glacier");
    }

    [Fact]
    public void SejiriGlacier_HasSingleManaAbility_AddingWhite()
    {
        var land = SejiriGlacierFactory.Create(_alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1, "exactly one {T}: Add {W} ability");
        manaAbilities[0].ManaGenerated.White.Should().BeGreaterThan(0, "produces white mana");
        manaAbilities[0].ManaGenerated.Blue.Should().Be(0);
        manaAbilities[0].ManaGenerated.Black.Should().Be(0);
        manaAbilities[0].ManaGenerated.Red.Should().Be(0);
        manaAbilities[0].ManaGenerated.Green.Should().Be(0);
    }
}
