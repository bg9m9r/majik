using FluentAssertions;
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
/// Unit tests for <see cref="AssaultStrobeFactory"/>.
///
/// Card: Assault Strobe — Sorcery {R} (Mirrodin Besieged).
///   "Target creature gains double strike until end of turn."
///
/// Covers:
///   - Identity + <see cref="NamedCardFactory"/> dispatch.
///   - SpellDefinition shape (1 target creature request, no modes, no X).
///   - Resolve: target creature gains Double strike EOT (CR 514.2).
///   - EOT cleanup: effect expires (CR 514.2).
///   - Illegal target (non-Creature resolver result) → no-op (CR 608.2b).
/// </summary>
public class AssaultStrobeTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void AssaultStrobe_Identity()
    {
        var c = AssaultStrobeFactory.Create(_alice);

        c.Name.Should().Be("Assault Strobe");
        c.ManaCost.Should().Be("{R}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_AssaultStrobe()
    {
        var card = NamedCardFactory.Create("Assault Strobe", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Assault Strobe");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{R}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SpellDefinition_DeclaresSingleTargetCreatureRequest()
    {
        var def = AssaultStrobeFactory.BuildSpellDefinition(t => t);

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

        ExecuteResolve(target);

        CombatAbilities.HasDoubleStrike(target).Should().BeTrue(
            "Assault Strobe grants Double strike until end of turn");
    }

    [Fact]
    public void Resolve_EndOfTurnCleanup_LiftsDoubleStrike()
    {
        var continuous = new ContinuousEffectsService();
        var target = BuildCreatureWithEffects(continuous, power: 2, toughness: 2);

        ExecuteResolve(target);
        CombatAbilities.HasDoubleStrike(target).Should().BeTrue();

        // CR 514.2 — effects flagged ExpiresAtEndOfTurn expire on cleanup.
        continuous.ExpireEndOfTurn();

        CombatAbilities.HasDoubleStrike(target).Should().BeFalse(
            "Double strike grant expires at end of turn");
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

        var def = AssaultStrobeFactory.BuildSpellDefinition(_ => nonCreature);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { nonCreature } },
            Mana: ManaPayment.Empty);

        var act = () =>
        {
            foreach (var e in def.EffectFactory(chosen)) e.Execute();
            var continuous = new ContinuousEffectsService();
            continuous.ExpireEndOfTurn();
        };
        act.Should().NotThrow();
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

    private static void ExecuteResolve(Creature target)
    {
        var def = AssaultStrobeFactory.BuildSpellDefinition(t => t);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();
    }
}
