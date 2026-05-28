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
/// Unit tests for <see cref="ViolentUrgeFactory"/>.
///
/// Card: Violent Urge — Instant {R} (Duskmourn: House of Horror).
///   "Target creature gets +1/+0 and gains first strike until end of turn.
///    Delirium — If there are four or more card types among cards in your
///    graveyard, that creature gains double strike until end of turn."
///
/// Covers:
///   - Identity + <see cref="NamedCardFactory"/> dispatch.
///   - SpellDefinition shape (1 target creature request, no modes, no X).
///   - Resolve: target creature gets +1/+0 and gains First strike EOT.
///   - Delirium active (≥4 distinct card types in graveyard): target also
///     gains Double strike EOT.
///   - Delirium inactive: target does NOT gain Double strike.
///   - EOT cleanup: all effects expire (CR 514.2).
///   - Illegal target (non-Creature resolver result) → no-op (CR 608.2b).
/// </summary>
public class ViolentUrgeTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void ViolentUrge_Identity()
    {
        var c = ViolentUrgeFactory.Create(_alice);

        c.Name.Should().Be("Violent Urge");
        c.ManaCost.Should().Be("{R}");
        c.HasType(CardType.Instant).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_ViolentUrge()
    {
        var card = NamedCardFactory.Create("Violent Urge", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Violent Urge");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{R}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SpellDefinition_DeclaresSingleTargetCreatureRequest()
    {
        var def = ViolentUrgeFactory.BuildSpellDefinition(_alice, t => t);

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
    public void Resolve_TargetBear_GainsPlus1Plus0AndFirstStrike()
    {
        var continuous = new ContinuousEffectsService();
        var bear = BuildBearWithEffects(continuous);

        // Pre-conditions: vanilla 2/2, no first strike.
        bear.Power.Should().Be(2);
        bear.Toughness.Should().Be(2);
        CombatAbilities.HasFirstStrike(bear).Should().BeFalse();

        ExecuteResolve(bear);

        // +1/+0 ⇒ 3/2; First strike granted (Layer 6 keyword grant).
        bear.Power.Should().Be(3);
        bear.Toughness.Should().Be(2);
        CombatAbilities.HasFirstStrike(bear).Should().BeTrue(
            "Violent Urge grants First strike until end of turn");
        CombatAbilities.HasDoubleStrike(bear).Should().BeFalse(
            "delirium inactive — no Double strike");
    }

    [Fact]
    public void Resolve_EndOfTurnCleanup_LiftsPumpAndFirstStrike()
    {
        var continuous = new ContinuousEffectsService();
        var bear = BuildBearWithEffects(continuous);

        ExecuteResolve(bear);

        bear.Power.Should().Be(3);
        CombatAbilities.HasFirstStrike(bear).Should().BeTrue();

        // CR 514.2 — both effects expire on cleanup.
        continuous.ExpireEndOfTurn();

        bear.Power.Should().Be(2);
        bear.Toughness.Should().Be(2);
        CombatAbilities.HasFirstStrike(bear).Should().BeFalse(
            "First strike grant expires at end of turn");
    }

    // -----------------------------------------------------------------------
    // Delirium — double strike rider
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_WithDelirium_TargetAlsoGainsDoubleStrike()
    {
        var continuous = new ContinuousEffectsService();
        var bear = BuildBearWithEffects(continuous);

        // Stock Alice's graveyard with 4 distinct card types: Creature,
        // Instant, Sorcery, Artifact.
        SeedAliceGraveyard(
            new[] { CardType.Creature },
            new[] { CardType.Instant },
            new[] { CardType.Sorcery },
            new[] { CardType.Artifact });

        ViolentUrgeFactory.IsDeliriumActive(_alice).Should().BeTrue(
            "graveyard has 4 distinct card types (Creature, Instant, Sorcery, Artifact)");

        ExecuteResolve(bear);

        bear.Power.Should().Be(3);
        CombatAbilities.HasFirstStrike(bear).Should().BeTrue(
            "First strike is always granted");
        CombatAbilities.HasDoubleStrike(bear).Should().BeTrue(
            "Delirium active — Violent Urge also grants Double strike");
    }

    [Fact]
    public void Resolve_WithoutDelirium_TargetDoesNotGainDoubleStrike()
    {
        var continuous = new ContinuousEffectsService();
        var bear = BuildBearWithEffects(continuous);

        // Only 3 distinct card types in graveyard — delirium inactive.
        SeedAliceGraveyard(
            new[] { CardType.Creature },
            new[] { CardType.Instant },
            new[] { CardType.Sorcery });

        ViolentUrgeFactory.IsDeliriumActive(_alice).Should().BeFalse(
            "only 3 distinct card types — below delirium threshold");

        ExecuteResolve(bear);

        CombatAbilities.HasFirstStrike(bear).Should().BeTrue();
        CombatAbilities.HasDoubleStrike(bear).Should().BeFalse(
            "delirium inactive — no Double strike granted");
    }

    [Fact]
    public void Resolve_WithDelirium_EndOfTurnCleanup_LiftsAllGrants()
    {
        var continuous = new ContinuousEffectsService();
        var bear = BuildBearWithEffects(continuous);

        // Stock 4 distinct card types so delirium fires.
        SeedAliceGraveyard(
            new[] { CardType.Creature },
            new[] { CardType.Instant },
            new[] { CardType.Sorcery },
            new[] { CardType.Artifact });

        ExecuteResolve(bear);

        bear.Power.Should().Be(3);
        CombatAbilities.HasFirstStrike(bear).Should().BeTrue();
        CombatAbilities.HasDoubleStrike(bear).Should().BeTrue();

        // CR 514.2 — every until-end-of-turn effect expires on cleanup.
        continuous.ExpireEndOfTurn();

        bear.Power.Should().Be(2);
        bear.Toughness.Should().Be(2);
        CombatAbilities.HasFirstStrike(bear).Should().BeFalse(
            "First strike grant expires at end of turn");
        CombatAbilities.HasDoubleStrike(bear).Should().BeFalse(
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

        var def = ViolentUrgeFactory.BuildSpellDefinition(_alice, _ => nonCreature);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { nonCreature } },
            Mana: ManaPayment.Empty);

        // Clean no-op: contract is "must not throw" for an illegal non-creature
        // target (CR 608.2b).
        var act = () =>
        {
            foreach (var e in def.EffectFactory(chosen)) e.Execute();
            var continuous = new ContinuousEffectsService();
            continuous.ExpireEndOfTurn();
        };
        act.Should().NotThrow();
    }

    // -----------------------------------------------------------------------
    // IsDeliriumActive helper
    // -----------------------------------------------------------------------

    [Fact]
    public void IsDeliriumActive_FalseWhenEmpty()
    {
        ViolentUrgeFactory.IsDeliriumActive(_alice).Should().BeFalse(
            "empty graveyard cannot satisfy delirium");
    }

    [Fact]
    public void IsDeliriumActive_TrueAtThreshold()
    {
        // 4 distinct card types — exactly the threshold.
        SeedAliceGraveyard(
            new[] { CardType.Creature },
            new[] { CardType.Instant },
            new[] { CardType.Sorcery },
            new[] { CardType.Artifact });

        ViolentUrgeFactory.IsDeliriumActive(_alice).Should().BeTrue();
    }

    [Fact]
    public void IsDeliriumActive_FalseBelowThreshold()
    {
        // 3 distinct card types — below threshold.
        SeedAliceGraveyard(
            new[] { CardType.Creature },
            new[] { CardType.Instant },
            new[] { CardType.Sorcery });

        ViolentUrgeFactory.IsDeliriumActive(_alice).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Build a vanilla 2/2 Bear under Alice's control with a live
    /// <see cref="ContinuousEffectsService"/> wired so registered EOT
    /// effects can be observed via <see cref="Creature.Power"/> +
    /// <see cref="CombatAbilities.HasFirstStrike"/> /
    /// <see cref="CombatAbilities.HasDoubleStrike"/>.
    /// </summary>
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

    /// <summary>
    /// Drop one card per supplied type-set into Alice's graveyard. Each
    /// inner array becomes one card's <see cref="CardType"/> set —
    /// matches the helper Unholy Heat's tests use so the delirium counter
    /// stays observationally identical (CR 702.105).
    /// </summary>
    private void SeedAliceGraveyard(params CardType[][] typeBundles)
    {
        var i = 0;
        foreach (var types in typeBundles)
        {
            var card = new Card($"Seed{i++}", "0", types);
            card.SetOwner(_alice);
            _alice.Zones.Graveyard.AddCard(card);
        }
    }

    /// <summary>
    /// Resolve Violent Urge against <paramref name="target"/> by invoking
    /// the <see cref="SpellDefinition.EffectFactory"/> directly with a
    /// synthetic <see cref="ChosenSpellParams"/> — the same shape
    /// <see cref="SpellCastFlow"/> would pass at resolution.
    /// </summary>
    private void ExecuteResolve(Creature target)
    {
        var def = ViolentUrgeFactory.BuildSpellDefinition(_alice, t => t);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();
    }
}
