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
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="TemurBattleRageFactory"/>.
///
/// Card: Temur Battle Rage — Instant {1}{R} (Khans of Tarkir).
///   "Target creature gains double strike until end of turn.
///    Ferocious — That creature also gains trample until end of turn if
///    you control a creature with power 4 or greater."
///
/// Covers:
///   - Identity + <see cref="NamedCardFactory"/> dispatch.
///   - SpellDefinition shape (1 target creature request, no modes, no X).
///   - Resolve: target creature gains Double strike EOT (CR 514.2).
///   - Ferocious active: target also gains Trample EOT.
///   - Ferocious inactive: target does NOT gain Trample.
///   - EOT cleanup: both effects expire (CR 514.2).
///   - Illegal target (non-Creature resolver result) → no-op (CR 608.2b).
/// </summary>
public class TemurBattleRageTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void TemurBattleRage_Identity()
    {
        var c = TemurBattleRageFactory.Create(_alice);

        c.Name.Should().Be("Temur Battle Rage");
        c.ManaCost.Should().Be("{1}{R}");
        c.HasType(CardType.Instant).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_TemurBattleRage()
    {
        var card = NamedCardFactory.Create("Temur Battle Rage", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Temur Battle Rage");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{R}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SpellDefinition_DeclaresSingleTargetCreatureRequest()
    {
        var def = TemurBattleRageFactory.BuildSpellDefinition(t => t);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Contain("creature");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Resolve: double strike granted
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_TargetCreature_GainsDoubleStrike()
    {
        var continuous = new ContinuousEffectsService();
        var target = BuildCreatureWithEffects(continuous, power: 2, toughness: 2);

        // No double strike before resolution.
        CombatAbilities.HasDoubleStrike(target).Should().BeFalse();

        ExecuteResolve(target, powerChecker: null);

        CombatAbilities.HasDoubleStrike(target).Should().BeTrue(
            "Temur Battle Rage grants Double strike until end of turn");
    }

    [Fact]
    public void Resolve_EndOfTurnCleanup_LiftDoubleStrike()
    {
        var continuous = new ContinuousEffectsService();
        var target = BuildCreatureWithEffects(continuous, power: 2, toughness: 2);

        ExecuteResolve(target, powerChecker: null);
        CombatAbilities.HasDoubleStrike(target).Should().BeTrue();

        // CR 514.2 — effects flagged ExpiresAtEndOfTurn expire on cleanup.
        continuous.ExpireEndOfTurn();

        CombatAbilities.HasDoubleStrike(target).Should().BeFalse(
            "Double strike grant expires at end of turn");
    }

    // -----------------------------------------------------------------------
    // Ferocious — Trample conditional
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_WithFerocious_TargetAlsoGainsTrample()
    {
        var continuous = new ContinuousEffectsService();
        var target = BuildCreatureWithEffects(continuous, power: 2, toughness: 2);

        // Ferocious is active: caster controls a 4-power creature.
        ExecuteResolve(target, powerChecker: () => true);

        CombatAbilities.HasDoubleStrike(target).Should().BeTrue(
            "Double strike is always granted");
        CombatAbilities.HasTrample(target).Should().BeTrue(
            "Ferocious active — Temur Battle Rage also grants Trample");
    }

    [Fact]
    public void Resolve_WithoutFerocious_TargetDoesNotGainTrample()
    {
        var continuous = new ContinuousEffectsService();
        var target = BuildCreatureWithEffects(continuous, power: 2, toughness: 2);

        // Ferocious is inactive: no power-4+ creature under caster's control.
        ExecuteResolve(target, powerChecker: () => false);

        CombatAbilities.HasDoubleStrike(target).Should().BeTrue(
            "Double strike is always granted regardless of ferocious");
        CombatAbilities.HasTrample(target).Should().BeFalse(
            "Ferocious not active — no Trample granted");
    }

    [Fact]
    public void Resolve_NullPowerChecker_TargetDoesNotGainTrample()
    {
        // When powerChecker is null (single-arg dispatcher path), ferocious
        // check is skipped and only double strike is granted.
        var continuous = new ContinuousEffectsService();
        var target = BuildCreatureWithEffects(continuous, power: 2, toughness: 2);

        ExecuteResolve(target, powerChecker: null);

        CombatAbilities.HasDoubleStrike(target).Should().BeTrue();
        CombatAbilities.HasTrample(target).Should().BeFalse(
            "No power checker supplied — ferocious not evaluated");
    }

    [Fact]
    public void Resolve_WithFerocious_EndOfTurnCleanup_LiftsDoubleStrikeAndTrample()
    {
        var continuous = new ContinuousEffectsService();
        var target = BuildCreatureWithEffects(continuous, power: 2, toughness: 2);

        ExecuteResolve(target, powerChecker: () => true);

        CombatAbilities.HasDoubleStrike(target).Should().BeTrue();
        CombatAbilities.HasTrample(target).Should().BeTrue();

        // CR 514.2 — both effects expire at cleanup.
        continuous.ExpireEndOfTurn();

        CombatAbilities.HasDoubleStrike(target).Should().BeFalse(
            "Double strike grant expires at end of turn");
        CombatAbilities.HasTrample(target).Should().BeFalse(
            "Trample grant expires at end of turn");
    }

    // -----------------------------------------------------------------------
    // Illegal target guard
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_IllegalTarget_NonCreature_IsNoOp()
    {
        // CR 608.2b — if the resolver returns a non-Creature object, the
        // effect does nothing (no throw, no registered effects).
        var nonCreature = new Card("Mountain Token", "");

        var def = TemurBattleRageFactory.BuildSpellDefinition(_ => nonCreature);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { nonCreature } },
            Mana: ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        // Clean no-op: nothing to assert — just must not throw.
        var continuous = new ContinuousEffectsService();
        continuous.ExpireEndOfTurn(); // must not throw
    }

    // -----------------------------------------------------------------------
    // BuildFerociousChecker integration
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildFerociousChecker_ReturnsFalse_WhenNoPower4Creature()
    {
        var checker = TemurBattleRageFactory.BuildFerociousChecker(_alice);

        // Alice controls a 2/2 — not ferocious.
        var small = BuildCreatureWithEffects(new ContinuousEffectsService(), power: 2, toughness: 2);

        checker().Should().BeFalse();
    }

    [Fact]
    public void BuildFerociousChecker_ReturnsTrue_WhenPower4CreaturePresent()
    {
        var checker = TemurBattleRageFactory.BuildFerociousChecker(_alice);

        // Add a 4/4 to Alice's battlefield.
        var big = new Creature("Beast", "{4}", 4, 4)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = new ContinuousEffectsService(),
        };
        _alice.Zones.Battlefield.AddCard(big);

        checker().Should().BeTrue(
            "Alice controls a creature with base power 4");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private Creature BuildCreatureWithEffects(
        ContinuousEffectsService continuous,
        int power,
        int toughness)
    {
        var creature = new Creature("Bear", "{G}", power, toughness)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = continuous,
        };
        _alice.Zones.Battlefield.AddCard(creature);
        return creature;
    }

    private static void ExecuteResolve(Creature target, Func<bool>? powerChecker)
    {
        var def = TemurBattleRageFactory.BuildSpellDefinition(t => t, powerChecker);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();
    }
}
