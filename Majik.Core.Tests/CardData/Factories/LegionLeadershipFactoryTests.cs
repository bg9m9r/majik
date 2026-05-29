using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
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
/// Tests for <see cref="LegionLeadershipFactory"/> and
/// <see cref="LegionStrongholdFactory"/> — the front + back faces of the
/// Modern Horizons 3 modal double-faced card Legion Leadership //
/// Legion Stronghold.
///
/// Front face (Legion Leadership, {1}{R/W}):
///   Instant. "Until end of turn, double target creature's power and it
///   gains first strike."
///
/// Back face (Legion Stronghold):
///   Land. "This land enters tapped." / "{T}: Add {R} or {W}."
///
/// Covers:
/// - Identity for both faces (name, cost, type, colour, owner, MV).
/// - <see cref="NamedCardFactory"/> dispatches both printed names.
/// - MDFC face-tracker (front starts on front; back pre-flipped).
/// - Front face — SpellDefinition shape (1 target creature request).
/// - Front face — resolve: 3/3 target → effective 6/3 + first strike.
/// - Front face — resolve: first strike keyword persists until EOT.
/// - Front face — EOT cleanup removes pump AND first strike (CR 514.2).
/// - Front face — target with 0 power: no pump, but first strike granted.
/// - Front face — non-Creature target at resolution → no-op (CR 608.2b).
/// - Front face — target off the battlefield at resolution → no-op.
/// - Front face — hybrid cost is both red AND white (CR 107.4e).
/// - Back face — identity (Land, non-basic, no subtype).
/// - Back face — <see cref="NamedCardFactory"/> dispatch.
/// - Back face — MDFC face-tracker pre-flipped to back face.
/// - Back face — two mana abilities: {T}: Add {R} and {T}: Add {W}.
/// - Back face — enters tapped replacement fires when bus is wired.
/// - Back face — no bus → no replacement (shape-only path).
/// </summary>
public class LegionLeadershipFactoryTests : IDisposable
{
    public LegionLeadershipFactoryTests()
    {
        AgentRegistry.Clear();
    }

    public void Dispose()
    {
        AgentRegistry.Clear();
    }

    // =========================================================================
    // Front face — identity + dispatch
    // =========================================================================

    [Fact]
    public void LegionLeadership_Identity_HybridRW_Instant_ManaValueTwo()
    {
        var alice = new Player("Alice", 20);
        var card = LegionLeadershipFactory.Create(alice);

        card.Name.Should().Be("Legion Leadership");
        card.ManaCost.Should().Be("{1}{R/W}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.Owner.Should().BeSameAs(alice);
        card.Controller.Should().BeSameAs(alice);
        ManaCost.Parse(card.ManaCost).TotalValue.Should().Be(2,
            "Legion Leadership costs {1}{R/W} — generic 1 + hybrid pip MV 1 = MV 2 (CR 202.3)");
    }

    [Fact]
    public void LegionLeadership_IsRedAndWhite_FromHybridPip()
    {
        // CR 107.4e — hybrid pip contributes BOTH listed colours to the
        // card's colour identity. Same assertion pattern as Boros Reckoner.
        var alice = new Player("Alice", 20);
        var card = LegionLeadershipFactory.Create(alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Red,
            "hybrid {R/W} pip contributes red to the colour identity");
        colors.Should().Contain(ManaColor.White,
            "hybrid {R/W} pip contributes white to the colour identity");
        colors.Should().NotContain(ManaColor.Blue);
        colors.Should().NotContain(ManaColor.Black);
        colors.Should().NotContain(ManaColor.Green);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_LegionLeadership()
    {
        var alice = new Player("Alice", 20);
        var card = NamedCardFactory.Create("Legion Leadership", alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Legion Leadership");
        card.HasType(CardType.Instant).Should().BeTrue();
    }

    // =========================================================================
    // MDFC face tracker — front face
    // =========================================================================

    [Fact]
    public void LegionLeadership_CarriesMdfcState_FrontFace()
    {
        var alice = new Player("Alice", 20);
        var card = LegionLeadershipFactory.Create(alice);

        card.MdfcState.Should().NotBeNull(
            "Legion Leadership is the front face of an MDFC and must carry a face tracker");
        card.MdfcState!.FrontFaceName.Should().Be("Legion Leadership");
        card.MdfcState!.BackFaceName.Should().Be("Legion Stronghold");
        card.MdfcState!.IsBackFace.Should().BeFalse(
            "the front-face card starts on the front face");
        card.MdfcState!.ActiveFaceName.Should().Be("Legion Leadership");
    }

    // =========================================================================
    // Front face — SpellDefinition shape
    // =========================================================================

    [Fact]
    public void LegionLeadership_BuildDefinition_SingleTargetCreatureRequest()
    {
        var alice = new Player("Alice", 20);
        var def = LegionLeadershipFactory.BuildDefinition(o => o);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Contain("creature");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // =========================================================================
    // Front face — resolve: double power + first strike
    // =========================================================================

    [Fact]
    public void LegionLeadership_Resolve_Doubles3PlusToPower_And_GrantsFirstStrike()
    {
        // A 3/3 → should become effective 6/3 with first strike.
        var alice = new Player("Alice", 20);
        var continuous = new ContinuousEffectsService();
        var target = BuildCreature(continuous, alice, power: 3, toughness: 3);

        CombatAbilities.HasFirstStrike(target).Should().BeFalse(
            "creature does not have first strike before Legion Leadership resolves");

        ExecuteResolve(target);

        target.GetPower().Should().Be(6,
            "CR 613.4d — power was 3 at resolution; +3/+0 added ≡ ×2 (6/3 effective)");
        target.GetToughness().Should().Be(3,
            "Legion Leadership's pump is power-only (+X/+0)");
        CombatAbilities.HasFirstStrike(target).Should().BeTrue(
            "CR 702.7 — Legion Leadership grants first strike until end of turn");
    }

    [Fact]
    public void LegionLeadership_Resolve_EndOfTurnCleanup_RemovesPumpAndFirstStrike()
    {
        // CR 514.2 — both the pump and the first-strike keyword expire at cleanup.
        var alice = new Player("Alice", 20);
        var continuous = new ContinuousEffectsService();
        var target = BuildCreature(continuous, alice, power: 3, toughness: 3);

        ExecuteResolve(target);
        target.GetPower().Should().Be(6);
        CombatAbilities.HasFirstStrike(target).Should().BeTrue();

        continuous.ExpireEndOfTurn();

        target.GetPower().Should().Be(3,
            "pump (+X/+0) expires at cleanup (CR 514.2)");
        target.GetToughness().Should().Be(3);
        CombatAbilities.HasFirstStrike(target).Should().BeFalse(
            "first strike grant expires at cleanup (CR 514.2)");
    }

    [Fact]
    public void LegionLeadership_Resolve_ZeroPowerCreature_GrantsFirstStrikeOnly()
    {
        // A 0/4 wall — power is 0 so no pump is applied, but first strike
        // is still granted (the "+X/+0 where X=0" pump is intentionally
        // skipped to avoid registering a no-op effect).
        var alice = new Player("Alice", 20);
        var continuous = new ContinuousEffectsService();
        var wall = BuildCreature(continuous, alice, power: 0, toughness: 4);

        ExecuteResolve(wall);

        wall.GetPower().Should().Be(0,
            "power is already 0; +0/+0 pump not registered");
        CombatAbilities.HasFirstStrike(wall).Should().BeTrue(
            "first strike is still granted regardless of power");
    }

    [Fact]
    public void LegionLeadership_Resolve_NonCreatureTarget_IsNoOp()
    {
        var alice = new Player("Alice", 20);
        var nonCreature = new Card("Swamp Token", "");

        var def = LegionLeadershipFactory.BuildDefinition(_ => nonCreature);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { nonCreature } },
            Mana: ManaPayment.Empty);

        // CR 608.2b — non-Creature resolver result → effect resolves as no-op.
        var act = () => { foreach (var e in def.EffectFactory(chosen)) e.Execute(); };
        act.Should().NotThrow(
            "non-Creature target at resolution is a no-op (CR 608.2b)");
    }

    [Fact]
    public void LegionLeadership_Resolve_TargetNotOnBattlefield_IsNoOp()
    {
        var alice = new Player("Alice", 20);
        var continuous = new ContinuousEffectsService();

        var target = new Creature("Bears", "{1}{G}", 2, 2)
        {
            Owner = alice,
            Controller = alice,
            Zone = ZoneType.Graveyard,
            ActiveEffects = continuous,
        };
        alice.Zones.Graveyard.AddCard(target);

        ExecuteResolve(target);

        target.GetPower().Should().Be(2,
            "CR 608.2b — target not on battlefield → no-op");
        CombatAbilities.HasFirstStrike(target).Should().BeFalse();
    }

    // =========================================================================
    // Back face — identity + dispatch
    // =========================================================================

    [Fact]
    public void LegionStronghold_Identity()
    {
        var alice = new Player("Alice", 20);
        var land = LegionStrongholdFactory.Create(alice);

        land.Should().BeOfType<Land>();
        land.Name.Should().Be("Legion Stronghold");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Legion Stronghold is a non-basic land");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.Owner.Should().BeSameAs(alice);
        land.Controller.Should().BeSameAs(alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_LegionStronghold()
    {
        var alice = new Player("Alice", 20);
        var card = NamedCardFactory.Create("Legion Stronghold", alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be("Legion Stronghold");
        card.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void LegionStronghold_CarriesMdfcState_PreFlippedToBackFace()
    {
        var alice = new Player("Alice", 20);
        var land = LegionStrongholdFactory.Create(alice);

        land.MdfcState.Should().NotBeNull(
            "Legion Stronghold is the back face of an MDFC and must carry a face tracker");
        land.MdfcState!.FrontFaceName.Should().Be("Legion Leadership");
        land.MdfcState!.BackFaceName.Should().Be("Legion Stronghold");
        land.MdfcState!.IsBackFace.Should().BeTrue(
            "the back-face card is constructed pre-flipped to the back face");
        land.MdfcState!.ActiveFaceName.Should().Be("Legion Stronghold");
    }

    // =========================================================================
    // Back face — mana abilities
    // =========================================================================

    [Fact]
    public void LegionStronghold_HasTwoManaAbilities_RedAndWhite()
    {
        var alice = new Player("Alice", 20);
        var land = LegionStrongholdFactory.Create(alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(2,
            "Legion Stronghold has {T}: Add {R} and {T}: Add {W} — two separate mana abilities");

        var red   = ManaCost.Parse("R");
        var white = ManaCost.Parse("W");

        manaAbilities.Should().Contain(ma =>
            ma.ManaGenerated.Red == red.Red && ma.ManaGenerated.Red > 0,
            "one mana ability produces {R}");
        manaAbilities.Should().Contain(ma =>
            ma.ManaGenerated.White == white.White && ma.ManaGenerated.White > 0,
            "one mana ability produces {W}");
    }

    [Fact]
    public void LegionStronghold_HasNoActivatedOrTriggeredAbilitiesBeyondMana()
    {
        var alice = new Player("Alice", 20);
        var land = LegionStrongholdFactory.Create(alice);

        land.Abilities.OfType<ActivatedAbility>()
            .Should().BeEmpty("Legion Stronghold has no non-mana activated abilities");
        land.Abilities.OfType<TriggeredAbility>()
            .Should().BeEmpty("ETB is a replacement, not a triggered ability (CR 614.1c)");
    }

    // =========================================================================
    // Back face — enters-tapped replacement
    // =========================================================================

    [Fact]
    public void LegionStronghold_EntersTapped_WhenReplacementBusIsWired()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var land = LegionStrongholdFactory.Create(alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "Legion Stronghold always enters tapped (no optional life payment)");
        alice.LifeTotal.Should().Be(20,
            "Legion Stronghold does not require any life payment");
    }

    [Fact]
    public void LegionStronghold_NoBus_ReplacementNotRegistered_ShapeOnly()
    {
        // Single-arg path — no bus supplied → no replacement registered.
        // The land is still created with correct mana abilities.
        var alice = new Player("Alice", 20);
        var land = LegionStrongholdFactory.Create(alice);

        land.Should().NotBeNull();
        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2,
            "mana abilities are always wired regardless of the bus");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static void ExecuteResolve(Creature target)
    {
        var def = LegionLeadershipFactory.BuildDefinition(t => t);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();
    }

    private static Creature BuildCreature(
        ContinuousEffectsService continuous,
        Player controller,
        int power,
        int toughness)
    {
        var c = new Creature($"{power}/{toughness} Creature", "{R}", power, toughness)
        {
            Owner = controller,
            Controller = controller,
            Zone = ZoneType.Battlefield,
            ActiveEffects = continuous,
        };
        controller.Zones.Battlefield.AddCard(c);
        return c;
    }
}
