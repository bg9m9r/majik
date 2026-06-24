using FluentAssertions;
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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SureStrikeFactory"/>.
///
/// Card: Sure Strike — Instant {1}{R}.
///   Oracle text (verified against Scryfall):
///     "Target creature gets +3/+0 and gains first strike until end of turn.
///      (It deals combat damage before creatures without first strike.)"
///
/// Covers ONLY the card's unique behaviour (+ a single identity assert):
///   - Identity: name, Instant type, mana cost {1}{R} (mana value 2).
///   - SpellDefinition shape: single 1..1 "target creature" request, no
///     modes, no X.
///   - Resolve: target creature gets +3/+0 and gains First strike EOT.
///   - EOT cleanup: pump + first strike expire (CR 514.2).
///   - Illegal target (non-Creature resolver result) → no-op (CR 608.2b).
///
/// (Dispatch + well-formedness are asserted for every implemented card by
/// CardFactoryContractTests, so they are not re-tested here.)
/// </summary>
[Trait("Color", "R")]
public class SureStrikeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void SureStrike_Identity()
    {
        var c = SureStrikeFactory.Create(_alice);

        c.Name.Should().Be("Sure Strike");
        c.HasType(CardType.Instant).Should().BeTrue();
        c.ManaCostValue.TotalValue.Should().Be(2, because: "{1}{R} = mana value 2");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // SpellDefinition shape
    // -----------------------------------------------------------------------

    [Fact]
    public void SpellDefinition_DeclaresSingleTargetCreatureRequest()
    {
        var def = SureStrikeFactory.BuildSpellDefinition(t => t);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Contain("creature");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Resolve: pump + first strike
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_TargetBear_GainsPlus3Plus0AndFirstStrike()
    {
        var continuous = new ContinuousEffectsService();
        var bear = BuildBearWithEffects(continuous);

        // Pre-conditions: vanilla 2/2, no first strike.
        bear.Power.Should().Be(2);
        bear.Toughness.Should().Be(2);
        CombatAbilities.HasFirstStrike(bear).Should().BeFalse();

        ExecuteResolve(bear);

        // +3/+0 ⇒ 5/2; First strike granted (Layer 6 keyword grant).
        bear.Power.Should().Be(5);
        bear.Toughness.Should().Be(2);
        CombatAbilities.HasFirstStrike(bear).Should().BeTrue(
            "Sure Strike grants First strike until end of turn");
    }

    [Fact]
    public void Resolve_EndOfTurnCleanup_LiftsPumpAndFirstStrike()
    {
        var continuous = new ContinuousEffectsService();
        var bear = BuildBearWithEffects(continuous);

        ExecuteResolve(bear);

        bear.Power.Should().Be(5);
        CombatAbilities.HasFirstStrike(bear).Should().BeTrue();

        // CR 514.2 — both effects expire on cleanup.
        continuous.ExpireEndOfTurn();

        bear.Power.Should().Be(2);
        bear.Toughness.Should().Be(2);
        CombatAbilities.HasFirstStrike(bear).Should().BeFalse(
            "First strike grant expires at end of turn");
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

        var def = SureStrikeFactory.BuildSpellDefinition(_ => nonCreature);
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

    private Creature BuildBearWithEffects(ContinuousEffectsService continuous)
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = continuous,
        };
        _alice.Zones.Battlefield.AddCard(bear);
        return bear;
    }

    private void ExecuteResolve(Creature target)
    {
        var def = SureStrikeFactory.BuildSpellDefinition(t => t);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();
    }
}
