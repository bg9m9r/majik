using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using ManaColor = Majik.Core.ValueObjects.ManaColor;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="BushwhackFactory"/> — Bushwhack (Modern Horizons 2,
/// {G}).
///
/// Sorcery. CR 700.2d — modal "Choose one —":
///   Mode 0: Search your library for a basic land card, reveal it, put it into
///           your hand, then shuffle.
///   Mode 1: Target creature you control fights target creature you don't
///           control. (Each deals damage equal to its power to the other.)
///
/// Modal shape mirrors <see cref="WitherbloomCharmFactory"/>; the tutor mode
/// mirrors <see cref="BorderlandRangerFactory"/> (but mandatory, not "may"); the
/// fight mode mirrors <see cref="KhalniAmbushFactory"/> (CR 701.13a).
/// </summary>
[Trait("Color", "G")]
public class BushwhackFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // =========================================================================
    // Identity + dispatch
    // =========================================================================

    [Fact]
    public void Bushwhack_Identity_Green_Sorcery_ManaValueOne()
    {
        var card = BushwhackFactory.Create(_alice);

        card.Name.Should().Be("Bushwhack");
        card.ManaCost.Should().Be("{G}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.HasType(CardType.Instant).Should().BeFalse();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
        ManaCost.Parse(card.ManaCost).TotalValue.Should().Be(1,
            "Bushwhack costs {G} — a single green pip = MV 1 (CR 202.3)");
    }

    [Fact]
    public void Bushwhack_IsGreen()
    {
        var card = BushwhackFactory.Create(_alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Green);
        colors.Should().NotContain(ManaColor.White);
        colors.Should().NotContain(ManaColor.Blue);
        colors.Should().NotContain(ManaColor.Black);
        colors.Should().NotContain(ManaColor.Red);
    }

    [Fact]
    public void Bushwhack_NamedCardFactory_Dispatch()
    {
        var dispatched = NamedCardFactory.Create("Bushwhack", _alice);

        dispatched.Should().BeOfType<Sorcery>();
        dispatched.Name.Should().Be("Bushwhack");
        dispatched.HasType(CardType.Sorcery).Should().BeTrue();
    }

    // =========================================================================
    // SpellDefinition shape — CR 700.2d modal
    // =========================================================================

    [Fact]
    public void Bushwhack_BuildDefinition_TwoModes_TutorTargetlessFightTwoTargets()
    {
        var def = BushwhackFactory.BuildDefinition(_alice, o => o);

        def.Modes.Should().HaveCount(2, "Bushwhack is a Choose-one with two modes");
        def.HasVariableX.Should().BeFalse();

        // One target slot per mode (the fight mode owns two creature slots).
        def.TargetRequests.Should().HaveCount(3);
        // Mode 0 tutor — targetless.
        def.TargetRequests[0].MinTargets.Should().Be(0);
        def.TargetRequests[0].MaxTargets.Should().Be(0);
        // Mode 1 fight — your creature + their creature; MinTargets=0 so the
        // unchosen mode never gates the cast.
        def.TargetRequests[1].MinTargets.Should().Be(0);
        def.TargetRequests[1].MaxTargets.Should().Be(1);
        def.TargetRequests[2].MinTargets.Should().Be(0);
        def.TargetRequests[2].MaxTargets.Should().Be(1);
    }

    // =========================================================================
    // Mode 0 — tutor a basic land to hand (CR 701.19 / CR 701.20a)
    // =========================================================================

    [Fact]
    public void Bushwhack_ModeTutor_MovesBasicLandFromLibraryToHand()
    {
        var basic = new Land("Forest", supertypes: new[] { CardSupertype.Basic });
        basic.SetOwner(_alice);
        basic.SetController(_alice);
        basic.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(basic);

        // A non-basic land must NOT be tutored.
        var nonBasic = new Land("Wastes-but-nonbasic");
        nonBasic.SetOwner(_alice);
        nonBasic.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(nonBasic);

        ExecuteMode(BushwhackFactory.ModeTutorBasic);

        _alice.Zones.Hand.GetCards().Should().Contain(basic,
            "the mandatory search puts a basic land into the caster's hand (CR 701.19)");
        _alice.Zones.Library.GetCards().Should().NotContain(basic);
        _alice.Zones.Library.GetCards().Should().Contain(nonBasic,
            "a non-basic land is not a legal tutor target (CR 305.6)");
    }

    [Fact]
    public void Bushwhack_ModeTutor_NoBasicInLibrary_IsCleanNoOp()
    {
        // CR 701.19c — a search may legally find nothing; the spell still
        // resolves and the (empty-result) search is followed by a shuffle.
        var nonBasic = new Land("Some Nonbasic Land");
        nonBasic.SetOwner(_alice);
        nonBasic.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(nonBasic);

        Action act = () => ExecuteMode(BushwhackFactory.ModeTutorBasic);

        act.Should().NotThrow();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().Contain(nonBasic);
    }

    // =========================================================================
    // Mode 1 — fight (CR 701.13a)
    // =========================================================================

    [Fact]
    public void Bushwhack_ModeFight_BothCreaturesDealDamageEqualToPower()
    {
        var mine = MakeCreature("Mine", power: 3, toughness: 4, controller: _alice);
        var theirs = MakeCreature("Theirs", power: 2, toughness: 5, controller: _bob);

        ExecuteFight(mine, theirs);

        // CR 701.13a — each deals damage equal to its power to the other.
        theirs.Damage.Should().Be(3, "Mine has power 3");
        mine.Damage.Should().Be(2, "Theirs has power 2");
    }

    [Fact]
    public void Bushwhack_ModeFight_PowerReadBeforeAnyDamageApplies()
    {
        // CR 701.13a — both powers read simultaneously; neither creature's
        // incoming damage is influenced by the damage it deals.
        var mine = MakeCreature("Mine", power: 5, toughness: 5, controller: _alice);
        var theirs = MakeCreature("Theirs", power: 5, toughness: 5, controller: _bob);

        ExecuteFight(mine, theirs);

        mine.Damage.Should().Be(5);
        theirs.Damage.Should().Be(5);
    }

    [Fact]
    public void Bushwhack_ModeFight_NonCreatureTarget_IsCleanNoOp()
    {
        // CR 608.2b — an illegal target at resolution makes the fight do nothing.
        var mine = MakeCreature("Mine", power: 3, toughness: 3, controller: _alice);
        var notACreature = new Land("Forest");

        Action act = () => ExecuteFight(mine, notACreature);

        act.Should().NotThrow();
        mine.Damage.Should().Be(0);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private void ExecuteMode(int modeIndex)
    {
        var def = BushwhackFactory.BuildDefinition(_alice, o => o);
        var chosen = new ChosenSpellParams(
            ModeIndex: modeIndex,
            X: null,
            Targets: new IReadOnlyList<object>[]
            {
                Array.Empty<object>(),
                Array.Empty<object>(),
                Array.Empty<object>(),
            },
            Mana: ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();
    }

    private void ExecuteFight(object a, object b)
    {
        var def = BushwhackFactory.BuildDefinition(_alice, o => o);
        var chosen = new ChosenSpellParams(
            ModeIndex: BushwhackFactory.ModeFight,
            X: null,
            Targets: new IReadOnlyList<object>[]
            {
                Array.Empty<object>(),       // slot 0 — tutor mode (unused)
                new object[] { a },          // slot 1 — your creature
                new object[] { b },          // slot 2 — their creature
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
