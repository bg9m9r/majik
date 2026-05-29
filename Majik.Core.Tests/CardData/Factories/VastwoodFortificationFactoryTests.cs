using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using ManaColor = Majik.Core.ValueObjects.ManaColor;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="VastwoodFortificationFactory"/> and
/// <see cref="VastwoodThicketFactory"/> — the front + back faces of the
/// Modern Horizons 3 modal double-faced card Vastwood Fortification //
/// Vastwood Thicket.
///
/// Front face (Vastwood Fortification, {G}):
///   Instant. "Put a +1/+1 counter on target creature."
///
/// Back face (Vastwood Thicket):
///   Land. "This land enters tapped." / "{T}: Add {G}."
///
/// Covers:
/// - Front identity (name, cost, type, colour, owner, MV).
/// - <see cref="NamedCardFactory"/> dispatches both printed names.
/// - MDFC face tracker (front starts front; back pre-flipped).
/// - Front — SpellDefinition shape (1..1 target-creature request).
/// - Front — resolve: places one +1/+1 counter on the chosen creature.
/// - Front — illegal-on-resolution target (off battlefield / non-creature)
///   → clean no-op (CR 608.2b).
/// - Back — identity (Land, non-basic, no subtype).
/// - Back — single {T}: Add {G} mana ability.
/// - Back — enters tapped replacement fires when bus is wired; no bus → none.
/// </summary>
public class VastwoodFortificationFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // =========================================================================
    // Front face — identity + dispatch
    // =========================================================================

    [Fact]
    public void VastwoodFortification_Identity_Green_Instant_ManaValueOne()
    {
        var card = VastwoodFortificationFactory.Create(_alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Vastwood Fortification");
        card.ManaCost.Should().Be("{G}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
        ManaCost.Parse(card.ManaCost).TotalValue.Should().Be(1,
            "Vastwood Fortification costs {G} — MV 1 (CR 202.3)");
    }

    [Fact]
    public void VastwoodFortification_IsGreen()
    {
        var card = VastwoodFortificationFactory.Create(_alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Green);
        colors.Should().NotContain(ManaColor.White);
        colors.Should().NotContain(ManaColor.Blue);
        colors.Should().NotContain(ManaColor.Black);
        colors.Should().NotContain(ManaColor.Red);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_VastwoodFortification()
    {
        var card = NamedCardFactory.Create("Vastwood Fortification", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Vastwood Fortification");
        card.HasType(CardType.Instant).Should().BeTrue();
    }

    [Fact]
    public void VastwoodFortification_CarriesMdfcState_FrontFace()
    {
        var card = VastwoodFortificationFactory.Create(_alice);

        card.MdfcState.Should().NotBeNull(
            "Vastwood Fortification is the front face of an MDFC and must carry a face tracker");
        card.MdfcState!.FrontFaceName.Should().Be("Vastwood Fortification");
        card.MdfcState!.BackFaceName.Should().Be("Vastwood Thicket");
        card.MdfcState!.IsBackFace.Should().BeFalse(
            "the front-face card starts on the front face");
        card.MdfcState!.ActiveFaceName.Should().Be("Vastwood Fortification");
    }

    // =========================================================================
    // Front face — SpellDefinition shape
    // =========================================================================

    [Fact]
    public void VastwoodFortification_BuildDefinition_SingleTargetCreatureRequest()
    {
        var def = VastwoodFortificationFactory.BuildDefinition(o => o);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Contain("creature");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // =========================================================================
    // Front face — resolution
    // =========================================================================

    [Fact]
    public void VastwoodFortification_Resolve_PlacesPlusOnePlusOneCounter()
    {
        var bear = MakeCreatureOnBattlefield("Grizzly Bears", "{1}{G}", 2, 2);

        ExecuteResolve(target: bear);

        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "Vastwood Fortification puts a +1/+1 counter on the target creature");
    }

    [Fact]
    public void VastwoodFortification_Resolve_TargetNotOnBattlefield_IsNoOp()
    {
        // CR 608.2b — a target that has left the battlefield by resolution
        // fizzles. The counter is not placed.
        var bear = MakeCreatureOnBattlefield("Grizzly Bears", "{1}{G}", 2, 2);
        _alice.Zones.Battlefield.RemoveCard(bear);
        bear.SetZone(ZoneType.Graveyard);

        ExecuteResolve(target: bear);

        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "an off-battlefield target is illegal at resolution → no counter (CR 608.2b)");
    }

    [Fact]
    public void VastwoodFortification_Resolve_NonCreatureTarget_IsNoOp()
    {
        // CR 608.2b — a non-creature target is illegal at resolution.
        var def = VastwoodFortificationFactory.BuildDefinition(o => o);
        Action act = () => ExecuteResolveWith(def, target: _alice);

        act.Should().NotThrow("a non-creature target is a clean no-op (CR 608.2b)");
    }

    // =========================================================================
    // Back face — identity + dispatch
    // =========================================================================

    [Fact]
    public void VastwoodThicket_Identity()
    {
        var land = VastwoodThicketFactory.Create(_alice);

        land.Should().BeOfType<Land>();
        land.Name.Should().Be("Vastwood Thicket");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Vastwood Thicket is a non-basic land");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_VastwoodThicket()
    {
        var card = NamedCardFactory.Create("Vastwood Thicket", _alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be("Vastwood Thicket");
        card.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void VastwoodThicket_CarriesMdfcState_PreFlippedToBackFace()
    {
        var land = VastwoodThicketFactory.Create(_alice);

        land.MdfcState.Should().NotBeNull(
            "Vastwood Thicket is the back face of an MDFC and must carry a face tracker");
        land.MdfcState!.FrontFaceName.Should().Be("Vastwood Fortification");
        land.MdfcState!.BackFaceName.Should().Be("Vastwood Thicket");
        land.MdfcState!.IsBackFace.Should().BeTrue(
            "the back-face card is constructed pre-flipped to the back face");
        land.MdfcState!.ActiveFaceName.Should().Be("Vastwood Thicket");
    }

    // =========================================================================
    // Back face — mana ability
    // =========================================================================

    [Fact]
    public void VastwoodThicket_HasSingleGreenManaAbility()
    {
        var land = VastwoodThicketFactory.Create(_alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1, "Vastwood Thicket has {T}: Add {G}");

        var green = ManaCost.Parse("G");
        manaAbilities[0].ManaGenerated.Green.Should().Be(green.Green);
        manaAbilities[0].ManaGenerated.Green.Should().BeGreaterThan(0);
        manaAbilities[0].ManaGenerated.Red.Should().Be(0);
        manaAbilities[0].ManaGenerated.Blue.Should().Be(0);
        manaAbilities[0].ManaGenerated.Black.Should().Be(0);
        manaAbilities[0].ManaGenerated.White.Should().Be(0);
    }

    [Fact]
    public void VastwoodThicket_HasNoActivatedOrTriggeredAbilitiesBeyondMana()
    {
        var land = VastwoodThicketFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>()
            .Should().BeEmpty("Vastwood Thicket has no non-mana activated abilities");
        land.Abilities.OfType<TriggeredAbility>()
            .Should().BeEmpty("ETB is a replacement, not a triggered ability (CR 614.1c)");
    }

    // =========================================================================
    // Back face — enters-tapped replacement
    // =========================================================================

    [Fact]
    public void VastwoodThicket_EntersTapped_WhenReplacementBusIsWired()
    {
        var bus = new ReplacementBus();
        var land = VastwoodThicketFactory.Create(_alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "Vastwood Thicket always enters tapped (no optional life payment)");
        _alice.LifeTotal.Should().Be(20,
            "Vastwood Thicket does not require any life payment");
    }

    [Fact]
    public void VastwoodThicket_NoBus_ReplacementNotRegistered_ShapeOnly()
    {
        var land = VastwoodThicketFactory.Create(_alice);

        land.Should().NotBeNull();
        land.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "the mana ability is always wired regardless of the bus");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private void ExecuteResolve(ICard? target)
    {
        var def = VastwoodFortificationFactory.BuildDefinition(o => o);
        ExecuteResolveWith(def, target);
    }

    private static void ExecuteResolveWith(SpellDefinition def, object? target)
    {
        var targets = target == null
            ? Array.Empty<IReadOnlyList<object>>()
            : new IReadOnlyList<object>[] { new[] { target } };
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();
    }

    private Creature MakeCreatureOnBattlefield(string name, string manaCost, int power, int toughness)
    {
        var card = new Creature(name, manaCost, power, toughness);
        card.SetOwner(_alice);
        card.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        return card;
    }
}
