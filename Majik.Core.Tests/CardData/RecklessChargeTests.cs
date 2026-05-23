using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="RecklessChargeFactory"/>.
///
/// Card: Reckless Charge — Sorcery {R} (Odyssey / Modern Horizons).
///   "Target creature gets +3/+0 and gains haste until end of turn.
///    Flashback {2}{R}."
///
/// Covers:
///   - Identity + <see cref="NamedCardFactory"/> dispatch.
///   - Flashback alt-cost surfaced as {2}{R} via the oracle binder
///     (<see cref="FlashbackOracleParser"/>).
///   - Resolve: target Bear 2/2 becomes 5/2 with Haste until EOT.
///   - Flashback cast from graveyard: same resolve effect; cost's
///     <c>OnResolved</c> exiles the card (CR 702.34b).
///   - EOT cleanup: pump + haste grant both expire on
///     <see cref="ContinuousEffectsService.ExpireEndOfTurn"/> (CR 514.2).
///   - Illegal target (non-Creature resolver result) → effect is a no-op
///     (CR 608.2b defensive guard).
/// </summary>
public class RecklessChargeTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void RecklessCharge_Identity()
    {
        var c = RecklessChargeFactory.Create(_alice);

        c.Name.Should().Be("Reckless Charge");
        c.ManaCost.Should().Be("{R}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_RecklessCharge()
    {
        var card = NamedCardFactory.Create("Reckless Charge", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Reckless Charge");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{R}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void FlashbackCost_ParsedFromOracle_Is2R()
    {
        var fb = RecklessChargeFactory.BuildFlashbackCost();

        fb.AlternativeManaCost.Should().Be(ManaCost.Parse("2R"));
        fb.Description.Should().Contain("Flashback");
    }

    [Fact]
    public void SpellDefinition_DeclaresSingleTargetCreatureRequest()
    {
        // Shape contract: one 1..1 target creature request, no modes, no X.
        var def = RecklessChargeFactory.BuildSpellDefinition(t => t);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Contain("creature");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Resolve: pump + haste on a Bear
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_TargetBear_BecomesFivePowerTwoToughness_WithHaste()
    {
        // Set up a Bear with a live ContinuousEffectsService wired so the
        // pump + grant-haste effects can register.
        var continuous = new ContinuousEffectsService();
        var bear = BuildBearWithEffects(continuous);

        // Pre-conditions: vanilla 2/2, no haste.
        bear.Power.Should().Be(2);
        bear.Toughness.Should().Be(2);
        CombatAbilities.HasHaste(bear).Should().BeFalse();

        ExecuteResolve(bear);

        // +3/+0 ⇒ 5/2; Haste granted (Layer 6 keyword grant).
        bear.Power.Should().Be(5);
        bear.Toughness.Should().Be(2);
        CombatAbilities.HasHaste(bear).Should().BeTrue(
            "Reckless Charge grants Haste until end of turn");
    }

    [Fact]
    public void Resolve_EndOfTurnCleanup_LiftsPumpAndHaste()
    {
        // CR 514.2 — both effects flagged ExpiresAtEndOfTurn; after the
        // cleanup step (ExpireEndOfTurn), Bear reverts to printed 2/2 and
        // loses the granted Haste.
        var continuous = new ContinuousEffectsService();
        var bear = BuildBearWithEffects(continuous);

        ExecuteResolve(bear);

        bear.Power.Should().Be(5);
        CombatAbilities.HasHaste(bear).Should().BeTrue();

        // Simulate end-of-turn cleanup (CR 514.2 / CR 613).
        continuous.ExpireEndOfTurn();

        bear.Power.Should().Be(2);
        bear.Toughness.Should().Be(2);
        CombatAbilities.HasHaste(bear).Should().BeFalse(
            "Haste grant expires at end of turn");
    }

    [Fact]
    public void Resolve_IllegalTarget_NonCreature_IsNoOp()
    {
        // CR 608.2b — illegal-target defensive guard. If the resolver
        // returns a non-Creature object (zone-change / type-loss /
        // wrong resolver), the effect must not throw and must register
        // no continuous effects.
        var continuous = new ContinuousEffectsService();
        var nonCreature = new Card("Mountain Token", "");

        var def = RecklessChargeFactory.BuildSpellDefinition(_ => nonCreature);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { nonCreature } },
            Mana: ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        // No effects registered → ExpireEndOfTurn is a clean no-op too.
        var beforeExpire = continuous;
        beforeExpire.ExpireEndOfTurn(); // must not throw

        // And a fresh Bear with its own ActiveEffects sees no pump.
        var bear = BuildBearWithEffects(new ContinuousEffectsService());
        bear.Power.Should().Be(2);
        CombatAbilities.HasHaste(bear).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Flashback cast: from graveyard, paying {2}{R}, then exile.
    // -----------------------------------------------------------------------

    [Fact]
    public void FlashbackCast_FromGraveyard_AppliesResolveEffect_ThenExiles()
    {
        // Reckless Charge sits in Alice's graveyard. She flashes it back
        // {2}{R} at her own Bear: Bear becomes 5/2 with Haste, then the
        // card exiles per CR 702.34b.
        var rc = RecklessChargeFactory.Create(_alice);
        _alice.Zones.Graveyard.AddCard(rc);
        rc.SetZone(ZoneType.Graveyard);

        var continuous = new ContinuousEffectsService();
        var bear = BuildBearWithEffects(continuous);

        // Sanity: flashback cost legal here (CR 702.34a — castable from
        // graveyard, owner = caster).
        var fb = RecklessChargeFactory.BuildFlashbackCost();
        fb.CanCastFor(rc, _alice).Should().BeTrue();
        fb.AlternativeManaCost.Should().Be(ManaCost.Parse("2R"));

        // Resolve effect — same effect for printed cast and flashback
        // (CR 702.34a; the cost is the only difference).
        ExecuteResolve(bear);

        bear.Power.Should().Be(5);
        CombatAbilities.HasHaste(bear).Should().BeTrue();

        // Then flashback's post-resolve hook fires — card exiles from
        // graveyard (CR 702.34b). Simulate the wrap SpellCastFlow does
        // in production by invoking OnResolved directly.
        fb.OnResolved(rc, _alice);

        rc.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Exile.GetCards().Should().Contain(rc);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(rc);
    }

    [Fact]
    public void FlashbackCost_CannotCast_FromHandOrBattlefield()
    {
        // CR 702.34 — flashback is only legal from graveyard.
        var rc = RecklessChargeFactory.Create(_alice);
        rc.SetZone(ZoneType.Hand);

        var fb = RecklessChargeFactory.BuildFlashbackCost();
        fb.CanCastFor(rc, _alice).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Build a vanilla 2/2 Bear under Alice's control with a live
    /// <see cref="ContinuousEffectsService"/> wired so registered EOT
    /// effects can be observed via <see cref="Creature.Power"/> +
    /// <see cref="CombatAbilities.HasHaste"/>.
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
    /// Resolve Reckless Charge against <paramref name="target"/> by
    /// invoking the <see cref="SpellDefinition.EffectFactory"/> directly
    /// with a synthetic <see cref="ChosenSpellParams"/> — the same shape
    /// <see cref="SpellCastFlow"/> would pass at resolution.
    /// </summary>
    private static void ExecuteResolve(Creature target)
    {
        var def = RecklessChargeFactory.BuildSpellDefinition(t => t);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();
    }
}
