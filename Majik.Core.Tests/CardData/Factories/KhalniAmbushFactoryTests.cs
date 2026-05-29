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
/// Tests for <see cref="KhalniAmbushFactory"/> and
/// <see cref="KhalniTerritoryFactory"/> — the front + back faces of the
/// Zendikar Rising modal double-faced card Khalni Ambush // Khalni Territory.
///
/// Front face (Khalni Ambush, {2}{G}):
///   Instant. "Target creature you control fights target creature you don't
///   control. (Each deals damage equal to its power to the other.)"
///
/// Back face (Khalni Territory):
///   Land. "This land enters tapped." / "{T}: Add {G}."
///
/// Covers:
/// - Front identity (name, cost, type, colour, owner, MV).
/// - <see cref="NamedCardFactory"/> dispatches both printed names.
/// - MDFC face tracker (front starts front; back pre-flipped).
/// - Front — SpellDefinition shape (two 1..1 creature target requests).
/// - Front — resolve: CR 701.13a mutual simultaneous damage; current-power
///   read before any damage applies; non-creature / illegal targets no-op.
/// - Back — identity (Land, non-basic, no subtype).
/// - Back — single {T}: Add {G} mana ability.
/// - Back — enters tapped replacement fires when bus is wired; no bus → none.
/// </summary>
public class KhalniAmbushFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // =========================================================================
    // Front face — identity + dispatch
    // =========================================================================

    [Fact]
    public void KhalniAmbush_Identity_Green_Instant_ManaValueThree()
    {
        var card = KhalniAmbushFactory.Create(_alice);

        card.Name.Should().Be("Khalni Ambush");
        card.ManaCost.Should().Be("{2}{G}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
        ManaCost.Parse(card.ManaCost).TotalValue.Should().Be(3,
            "Khalni Ambush costs {2}{G} — generic 2 + 1 green = MV 3 (CR 202.3)");
    }

    [Fact]
    public void KhalniAmbush_IsGreen()
    {
        var card = KhalniAmbushFactory.Create(_alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Green);
        colors.Should().NotContain(ManaColor.White);
        colors.Should().NotContain(ManaColor.Blue);
        colors.Should().NotContain(ManaColor.Black);
        colors.Should().NotContain(ManaColor.Red);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_KhalniAmbush()
    {
        var card = NamedCardFactory.Create("Khalni Ambush", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Khalni Ambush");
        card.HasType(CardType.Instant).Should().BeTrue();
    }

    [Fact]
    public void KhalniAmbush_CarriesMdfcState_FrontFace()
    {
        var card = KhalniAmbushFactory.Create(_alice);

        card.MdfcState.Should().NotBeNull(
            "Khalni Ambush is the front face of an MDFC and must carry a face tracker");
        card.MdfcState!.FrontFaceName.Should().Be("Khalni Ambush");
        card.MdfcState!.BackFaceName.Should().Be("Khalni Territory");
        card.MdfcState!.IsBackFace.Should().BeFalse(
            "the front-face card starts on the front face");
        card.MdfcState!.ActiveFaceName.Should().Be("Khalni Ambush");
    }

    // =========================================================================
    // Front face — SpellDefinition shape
    // =========================================================================

    [Fact]
    public void KhalniAmbush_BuildDefinition_TwoCreatureTargetRequests()
    {
        var def = KhalniAmbushFactory.BuildDefinition(o => o);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(2,
            "fight needs a creature you control + a creature you don't (CR 701.13)");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[1].MinTargets.Should().Be(1);
        def.TargetRequests[1].MaxTargets.Should().Be(1);
    }

    // =========================================================================
    // Front face — resolution (CR 701.13a)
    // =========================================================================

    [Fact]
    public void KhalniAmbush_Resolve_BothCreaturesDealDamageEqualToPower()
    {
        var mine = MakeCreature("Mine", power: 3, toughness: 4, controller: _alice);
        var theirs = MakeCreature("Theirs", power: 2, toughness: 5, controller: _bob);

        ExecuteResolve(mine, theirs);

        // CR 701.13a — each deals damage equal to its power to the other.
        theirs.Damage.Should().Be(3, "Mine has power 3");
        mine.Damage.Should().Be(2, "Theirs has power 2");
    }

    [Fact]
    public void KhalniAmbush_Resolve_PowerReadBeforeAnyDamageApplies()
    {
        // CR 701.13a — both powers are read simultaneously; neither
        // creature's incoming damage is influenced by the damage it deals.
        var mine = MakeCreature("Mine", power: 5, toughness: 5, controller: _alice);
        var theirs = MakeCreature("Theirs", power: 5, toughness: 5, controller: _bob);

        ExecuteResolve(mine, theirs);

        mine.Damage.Should().Be(5);
        theirs.Damage.Should().Be(5);
    }

    [Fact]
    public void KhalniAmbush_Resolve_ZeroPowerDealsNoDamage()
    {
        var mine = MakeCreature("Mine", power: 0, toughness: 3, controller: _alice);
        var theirs = MakeCreature("Theirs", power: 4, toughness: 4, controller: _bob);

        ExecuteResolve(mine, theirs);

        // Mine has 0 power → deals no damage.
        theirs.Damage.Should().Be(0);
        mine.Damage.Should().Be(4);
    }

    [Fact]
    public void KhalniAmbush_Resolve_NonCreatureTarget_IsCleanNoOp()
    {
        // CR 608.2b — if a target is illegal at resolution the fight does
        // nothing. A non-creature object resolves to no fight.
        var mine = MakeCreature("Mine", power: 3, toughness: 3, controller: _alice);
        var notACreature = new Land("Forest");

        Action act = () => ExecuteResolve(mine, notACreature);

        act.Should().NotThrow();
        mine.Damage.Should().Be(0);
    }

    // =========================================================================
    // Back face — identity + dispatch
    // =========================================================================

    [Fact]
    public void KhalniTerritory_Identity()
    {
        var land = KhalniTerritoryFactory.Create(_alice);

        land.Should().BeOfType<Land>();
        land.Name.Should().Be("Khalni Territory");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Khalni Territory is a non-basic land");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_KhalniTerritory()
    {
        var card = NamedCardFactory.Create("Khalni Territory", _alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be("Khalni Territory");
        card.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void KhalniTerritory_CarriesMdfcState_PreFlippedToBackFace()
    {
        var land = KhalniTerritoryFactory.Create(_alice);

        land.MdfcState.Should().NotBeNull(
            "Khalni Territory is the back face of an MDFC and must carry a face tracker");
        land.MdfcState!.FrontFaceName.Should().Be("Khalni Ambush");
        land.MdfcState!.BackFaceName.Should().Be("Khalni Territory");
        land.MdfcState!.IsBackFace.Should().BeTrue(
            "the back-face card is constructed pre-flipped to the back face");
        land.MdfcState!.ActiveFaceName.Should().Be("Khalni Territory");
    }

    // =========================================================================
    // Back face — mana ability
    // =========================================================================

    [Fact]
    public void KhalniTerritory_HasSingleGreenManaAbility()
    {
        var land = KhalniTerritoryFactory.Create(_alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1, "Khalni Territory has {T}: Add {G}");

        var green = ManaCost.Parse("G");
        manaAbilities[0].ManaGenerated.Green.Should().Be(green.Green);
        manaAbilities[0].ManaGenerated.Green.Should().BeGreaterThan(0);
    }

    [Fact]
    public void KhalniTerritory_HasNoActivatedOrTriggeredAbilitiesBeyondMana()
    {
        var land = KhalniTerritoryFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>()
            .Should().BeEmpty("Khalni Territory has no non-mana activated abilities");
        land.Abilities.OfType<TriggeredAbility>()
            .Should().BeEmpty("ETB is a replacement, not a triggered ability (CR 614.1c)");
    }

    // =========================================================================
    // Back face — enters-tapped replacement
    // =========================================================================

    [Fact]
    public void KhalniTerritory_EntersTapped_WhenReplacementBusIsWired()
    {
        var bus = new ReplacementBus();
        var land = KhalniTerritoryFactory.Create(_alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "Khalni Territory always enters tapped (no optional life payment)");
        _alice.LifeTotal.Should().Be(20,
            "Khalni Territory does not require any life payment");
    }

    [Fact]
    public void KhalniTerritory_NoBus_ReplacementNotRegistered_ShapeOnly()
    {
        var land = KhalniTerritoryFactory.Create(_alice);

        land.Should().NotBeNull();
        land.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "the mana ability is always wired regardless of the bus");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private void ExecuteResolve(object a, object b)
    {
        var def = KhalniAmbushFactory.BuildDefinition(o => o);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[]
            {
                new object[] { a },
                new object[] { b },
            },
            Mana: ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();
    }

    private Creature MakeCreature(string name, int power, int toughness, Player controller)
    {
        var c = new Creature(name, "{G}", power: power, toughness: toughness);
        c.SetOwner(controller);
        c.SetController(controller);
        c.SetZone(ZoneType.Battlefield);
        controller.Zones.Battlefield.AddCard(c);
        return c;
    }
}
