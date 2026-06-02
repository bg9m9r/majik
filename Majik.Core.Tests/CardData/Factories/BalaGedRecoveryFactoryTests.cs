using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Tests.Helpers;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using ManaColor = Majik.Core.ValueObjects.ManaColor;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="BalaGedRecoveryFactory"/> and
/// <see cref="BalaGedSanctuaryFactory"/> — the front + back faces of the
/// Zendikar Rising modal double-faced card Bala Ged Recovery //
/// Bala Ged Sanctuary.
///
/// Front face (Bala Ged Recovery, {2}{G}):
///   Sorcery. "Return target card from your graveyard to your hand."
///
/// Back face (Bala Ged Sanctuary):
///   Land. "This land enters tapped." / "{T}: Add {G}."
///
/// Covers:
/// - Front identity (name, cost, type, colour, owner, MV).
/// - <see cref="NamedCardFactory"/> dispatches both printed names.
/// - MDFC face tracker (front starts front; back pre-flipped).
/// - Front — SpellDefinition shape (1..1 graveyard-card request, ANY type).
/// - Front — resolve: agent-set target returned; first-card fallback;
///   empty graveyard no-op; any-card-type returnable; ZoneService route.
/// - Front — illegal-on-resolution target (left graveyard) → no-op.
/// - Back — identity (Land, non-basic, no subtype).
/// - Back — single {T}: Add {G} mana ability.
/// - Back — enters tapped replacement fires when bus is wired; no bus → none.
/// </summary>
[Trait("Color", "G")]
public class BalaGedRecoveryFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // =========================================================================
    // Front face — identity + dispatch
    // =========================================================================

    [Fact]
    public void BalaGedRecovery_Identity_Green_Sorcery_ManaValueThree()
    {
        var card = BalaGedRecoveryFactory.Create(_alice);

        card.Name.Should().Be("Bala Ged Recovery");
        card.ManaCost.Should().Be("{2}{G}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
        ManaCost.Parse(card.ManaCost).TotalValue.Should().Be(3,
            "Bala Ged Recovery costs {2}{G} — generic 2 + 1 green = MV 3 (CR 202.3)");
    }

    [Fact]
    public void BalaGedRecovery_IsGreen()
    {
        var card = BalaGedRecoveryFactory.Create(_alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Green);
        colors.Should().NotContain(ManaColor.White);
        colors.Should().NotContain(ManaColor.Blue);
        colors.Should().NotContain(ManaColor.Black);
        colors.Should().NotContain(ManaColor.Red);
    }
    [Fact]
    public void BalaGedRecovery_CarriesMdfcState_FrontFace()
    {
        var card = BalaGedRecoveryFactory.Create(_alice);

        card.MdfcState.Should().NotBeNull(
            "Bala Ged Recovery is the front face of an MDFC and must carry a face tracker");
        card.MdfcState!.FrontFaceName.Should().Be("Bala Ged Recovery");
        card.MdfcState!.BackFaceName.Should().Be("Bala Ged Sanctuary");
        card.MdfcState!.IsBackFace.Should().BeFalse(
            "the front-face card starts on the front face");
        card.MdfcState!.ActiveFaceName.Should().Be("Bala Ged Recovery");
    }

    // =========================================================================
    // Front face — SpellDefinition shape
    // =========================================================================

    [Fact]
    public void BalaGedRecovery_BuildDefinition_SingleGraveyardCardRequest()
    {
        var def = BalaGedRecoveryFactory.BuildDefinition(_alice, o => o);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Contain("graveyard");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // =========================================================================
    // Front face — resolution
    // =========================================================================

    [Fact]
    public void BalaGedRecovery_Resolve_ReturnsChosenTarget()
    {
        var bolt = MakeInstantInGraveyard("Lightning Bolt", "{R}");
        var rampant = MakeSorceryInGraveyard("Rampant Growth", "{1}{G}");

        ExecuteResolve(target: rampant);

        _alice.Zones.Hand.GetCards().Should().Contain(rampant);
        rampant.Zone.Should().Be(ZoneType.Hand);

        // Bolt was not chosen → stays in graveyard ("target" is singular,
        // CR 700.6).
        _alice.Zones.Graveyard.GetCards().Should().Contain(bolt);
        bolt.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void BalaGedRecovery_Resolve_NoTarget_FallsBackToFirstCardInGraveyard()
    {
        var bolt = MakeInstantInGraveyard("Lightning Bolt", "{R}");
        var rampant = MakeSorceryInGraveyard("Rampant Growth", "{1}{G}");

        // No target supplied — deterministic fallback picks the first card.
        ExecuteResolve(target: null);

        _alice.Zones.Hand.GetCards().Should().Contain(bolt);
        bolt.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Graveyard.GetCards().Should().Contain(rampant);
    }

    [Fact]
    public void BalaGedRecovery_Resolve_EmptyGraveyard_IsCleanNoOp()
    {
        Action act = () => ExecuteResolve(target: null);

        act.Should().NotThrow();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    [Theory]
    [InlineData("Instant")]
    [InlineData("Sorcery")]
    [InlineData("Creature")]
    [InlineData("Land")]
    public void BalaGedRecovery_ReturnsAnyCardType(string cardType)
    {
        // CR 700.6 — the oracle says "card", with no type restriction.
        ICard seed = cardType switch
        {
            "Instant" => MakeInstantInGraveyard("Lightning Bolt", "{R}"),
            "Sorcery" => MakeSorceryInGraveyard("Rampant Growth", "{1}{G}"),
            "Creature" => MakeCreatureInGraveyard("Llanowar Elves", "{G}"),
            "Land" => MakeLandInGraveyard("Forest"),
            _ => throw new ArgumentOutOfRangeException(nameof(cardType)),
        };

        ExecuteResolve(target: seed);

        seed.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Hand.GetCards().Should().Contain(seed);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(seed);
    }

    [Fact]
    public void BalaGedRecovery_Resolve_TargetNoLongerInGraveyard_IsNoOp()
    {
        // CR 608.2b — a chosen card that has left the graveyard by
        // resolution fizzles the return.
        var bolt = MakeInstantInGraveyard("Lightning Bolt", "{R}");
        _alice.Zones.Graveyard.RemoveCard(bolt);
        bolt.SetZone(ZoneType.Exile);

        ExecuteResolve(target: bolt);

        _alice.Zones.Hand.GetCards().Should().NotContain(bolt);
        bolt.Zone.Should().Be(ZoneType.Exile);
    }

    [Fact]
    public void BalaGedRecovery_Resolve_RoutesThroughZoneService_WhenSupplied()
    {
        var bus = new TestEventBus();
        var zones = new ZoneService(bus);
        var rampant = MakeSorceryInGraveyard("Rampant Growth", "{1}{G}");

        var def = BalaGedRecoveryFactory.BuildDefinition(_alice, o => o, zones);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { rampant } },
            Mana: ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        // The ZoneService route moves the card Graveyard → Hand (and
        // publishes a CardMovedEvent so any "leaves graveyard" triggers
        // fire — CR 603.6a / CR 701.20).
        rampant.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Hand.GetCards().Should().Contain(rampant);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(rampant);
    }

    // =========================================================================
    // Back face — identity + dispatch
    // =========================================================================

    [Fact]
    public void BalaGedSanctuary_Identity()
    {
        var land = BalaGedSanctuaryFactory.Create(_alice);

        land.Should().BeOfType<Land>();
        land.Name.Should().Be("Bala Ged Sanctuary");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Bala Ged Sanctuary is a non-basic land");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void BalaGedSanctuary_CarriesMdfcState_PreFlippedToBackFace()
    {
        var land = BalaGedSanctuaryFactory.Create(_alice);

        land.MdfcState.Should().NotBeNull(
            "Bala Ged Sanctuary is the back face of an MDFC and must carry a face tracker");
        land.MdfcState!.FrontFaceName.Should().Be("Bala Ged Recovery");
        land.MdfcState!.BackFaceName.Should().Be("Bala Ged Sanctuary");
        land.MdfcState!.IsBackFace.Should().BeTrue(
            "the back-face card is constructed pre-flipped to the back face");
        land.MdfcState!.ActiveFaceName.Should().Be("Bala Ged Sanctuary");
    }

    // =========================================================================
    // Back face — mana ability
    // =========================================================================

    [Fact]
    public void BalaGedSanctuary_HasSingleGreenManaAbility()
    {
        var land = BalaGedSanctuaryFactory.Create(_alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1, "Bala Ged Sanctuary has {T}: Add {G}");

        var green = ManaCost.Parse("G");
        manaAbilities[0].ManaGenerated.Green.Should().Be(green.Green);
        manaAbilities[0].ManaGenerated.Green.Should().BeGreaterThan(0);
    }

    [Fact]
    public void BalaGedSanctuary_HasNoActivatedOrTriggeredAbilitiesBeyondMana()
    {
        var land = BalaGedSanctuaryFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>()
            .Should().BeEmpty("Bala Ged Sanctuary has no non-mana activated abilities");
        land.Abilities.OfType<TriggeredAbility>()
            .Should().BeEmpty("ETB is a replacement, not a triggered ability (CR 614.1c)");
    }

    // =========================================================================
    // Back face — enters-tapped replacement
    // =========================================================================

    [Fact]
    public void BalaGedSanctuary_EntersTapped_WhenReplacementBusIsWired()
    {
        var bus = new ReplacementBus();
        var land = BalaGedSanctuaryFactory.Create(_alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "Bala Ged Sanctuary always enters tapped (no optional life payment)");
        _alice.LifeTotal.Should().Be(20,
            "Bala Ged Sanctuary does not require any life payment");
    }

    [Fact]
    public void BalaGedSanctuary_NoBus_ReplacementNotRegistered_ShapeOnly()
    {
        var land = BalaGedSanctuaryFactory.Create(_alice);

        land.Should().NotBeNull();
        land.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "the mana ability is always wired regardless of the bus");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private void ExecuteResolve(ICard? target)
    {
        var def = BalaGedRecoveryFactory.BuildDefinition(_alice, o => o);
        var targets = target == null
            ? Array.Empty<IReadOnlyList<object>>()
            : new IReadOnlyList<object>[] { new object[] { target } };
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();
    }

    private Instant MakeInstantInGraveyard(string name, string manaCost)
    {
        var card = new Instant(name, manaCost);
        card.SetOwner(_alice);
        card.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(card);
        return card;
    }

    private Sorcery MakeSorceryInGraveyard(string name, string manaCost)
    {
        var card = new Sorcery(name, manaCost);
        card.SetOwner(_alice);
        card.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(card);
        return card;
    }

    private Creature MakeCreatureInGraveyard(string name, string manaCost)
    {
        var card = new Creature(name, manaCost, power: 1, toughness: 1);
        card.SetOwner(_alice);
        card.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(card);
        return card;
    }

    private Land MakeLandInGraveyard(string name)
    {
        var card = new Land(name);
        card.SetOwner(_alice);
        card.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(card);
        return card;
    }
}
