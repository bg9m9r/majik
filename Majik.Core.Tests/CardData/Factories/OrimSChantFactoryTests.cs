using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="OrimSChantFactory"/>.
///
/// Orim's Chant — {W} Instant (Planeshift / Modern-legal via Time Spiral Remastered):
///   "Kicker {W} (You may pay an additional {W} as you cast this spell.)
///    Target player can't cast spells this turn.
///    If this spell was kicked, creatures can't attack this turn."
///
/// Covers:
/// - Identity: {W} white Instant, mana value 1, owner/controller.
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Kicker {W}: kicker additional cost present; WasKicked stamp correct.
/// - Resolve (not kicked): target player acquires a total-cast block.
/// - Resolve (not kicked): other players are NOT restricted.
/// - Resolve (kicked): cast block applies AND mass CannotAttack restriction registered.
/// - Resolve (kicked, no effects service): cast block still applies, no combat crash.
/// - Kicked: creatures can't attack (CombatRestriction.CannotAttack).
/// - Cast block restriction expires when cleared.
/// </summary>
public class OrimSChantFactoryTests : IDisposable
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public OrimSChantFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        CastingRestrictions.Clear();
    }

    public void Dispose() => CastingRestrictions.Clear();

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void OrimSChant_IsWhiteInstant_ManaCostW_ManaValue1()
    {
        var card = OrimSChantFactory.Create(_alice);

        card.Name.Should().Be("Orim's Chant");
        card.ManaCost.Should().Be("{W}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.HasType(CardType.Sorcery).Should().BeFalse();
        ManaCost.Parse(card.ManaCost).TotalValue.Should().Be(1,
            "{W} has mana value 1 (CR 202.3)");
        CardColors.GetColors(card).Should().Contain(ManaColor.White,
            "single {W} pip → white card (CR 105.2)");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_OrimSChant()
    {
        var card = NamedCardFactory.Create("Orim's Chant", _alice);

        card.Should().BeOfType<Instant>("Orim's Chant is an Instant");
        card.Name.Should().Be("Orim's Chant");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Kicker {W}
    // -----------------------------------------------------------------------

    [Fact]
    public void OrimSChant_KickerAdditionalCost_IsKickerW()
    {
        var card = OrimSChantFactory.Create(_alice);
        var kicker = OrimSChantFactory.BuildAdditionalCost(card);

        kicker.Should().BeOfType<KickerAdditionalCost>(
            "Orim's Chant has Kicker {W} (CR 702.33)");
        var kickerCost = (KickerAdditionalCost)kicker;
        kickerCost.KickerCost.Should().Be(ManaCost.Parse("{W}"),
            "the kicker cost is {W}");
    }

    [Fact]
    public async Task OrimSChant_NotKicked_WasKickedFalse()
    {
        var card = OrimSChantFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { _bob });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, PhaseStateType.PreCombatMain, _stack);

        var spell = await _flow.CastAsync(
            _alice, card,
            OrimSChantFactory.BuildDefinition(card, o => o),
            agent, ctx,
            additionalCosts: null);

        // WasKicked must be false when no kicker cost was paid.
        (card is Card c && c.WasKicked).Should().BeFalse(
            "WasKicked is false when the kicker was not paid (CR 702.33b)");
    }

    [Fact]
    public async Task OrimSChant_Kicked_WasKickedTrue()
    {
        var card = OrimSChantFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        // Pay kicker mana into the pool.
        _alice.AddManaToPool(ManaCost.Parse("{W}"));

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { _bob });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, card,
            OrimSChantFactory.BuildDefinition(card, o => o),
            agent, ctx,
            additionalCosts: new[] { OrimSChantFactory.BuildAdditionalCost(card) });

        // WasKicked must be true immediately after cast (before post-resolve cleanup).
        (card is Card c && c.WasKicked).Should().BeTrue(
            "WasKicked is true when the kicker cost was paid (CR 702.33b)");
    }

    // -----------------------------------------------------------------------
    // Resolve (not kicked): target player can't cast spells this turn
    // -----------------------------------------------------------------------

    [Fact]
    public void OrimSChant_Resolve_NotKicked_TargetPlayerCannotCastSpells()
    {
        var card = OrimSChantFactory.Create(_alice);

        var def = OrimSChantFactory.BuildDefinition(card, o => o);
        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { _bob } }, ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        CastingRestrictions.CannotCastAnySpell(_bob).Should().BeTrue(
            "target player can't cast spells this turn (CR 601.3)");
    }

    [Fact]
    public void OrimSChant_Resolve_NotKicked_OtherPlayersNotRestricted()
    {
        var card = OrimSChantFactory.Create(_alice);
        var charlie = new Player("Charlie", 20);

        var def = OrimSChantFactory.BuildDefinition(card, o => o);
        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { _bob } }, ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        // Alice (the caster) and Charlie are not restricted.
        CastingRestrictions.CannotCastAnySpell(_alice).Should().BeFalse(
            "the caster is not restricted by their own Orim's Chant");
        CastingRestrictions.CannotCastAnySpell(charlie).Should().BeFalse(
            "untargeted players are not restricted");
    }

    [Fact]
    public void OrimSChant_Resolve_NotKicked_NoCreatureAttackRestriction()
    {
        var card = OrimSChantFactory.Create(_alice);
        var effects = new ContinuousEffectsService();

        var dummy = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _bob, Controller = _bob };
        dummy.SetZone(ZoneType.Battlefield);

        var def = OrimSChantFactory.BuildDefinition(card, o => o, effects);
        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { _bob } }, ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        effects.HasRestriction(dummy, CombatRestriction.CannotAttack).Should().BeFalse(
            "the CannotAttack restriction is only added when kicked");
    }

    // -----------------------------------------------------------------------
    // Resolve (kicked): cast block + creatures can't attack
    // -----------------------------------------------------------------------

    [Fact]
    public void OrimSChant_Resolve_Kicked_TargetPlayerCannotCastSpells()
    {
        var card = OrimSChantFactory.Create(_alice);
        // Simulate WasKicked = true by paying the kicker and stamping the flag.
        if (card is Card concrete) concrete.SetWasKicked(true);

        var effects = new ContinuousEffectsService();

        var def = OrimSChantFactory.BuildDefinition(card, o => o, effects);
        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { _bob } }, ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        CastingRestrictions.CannotCastAnySpell(_bob).Should().BeTrue(
            "kicked Orim's Chant still restricts the target player from casting");
    }

    [Fact]
    public void OrimSChant_Resolve_Kicked_CreaturesCannotAttack()
    {
        var card = OrimSChantFactory.Create(_alice);
        if (card is Card concrete) concrete.SetWasKicked(true);

        var effects = new ContinuousEffectsService();

        var attacker = new Creature("War Falcon", "{W}", 2, 1)
        { Owner = _bob, Controller = _bob };
        attacker.SetZone(ZoneType.Battlefield);

        var def = OrimSChantFactory.BuildDefinition(card, o => o, effects);
        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { _bob } }, ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        // All creatures — regardless of controller — have CannotAttack.
        effects.HasRestriction(attacker, CombatRestriction.CannotAttack).Should().BeTrue(
            "kicked Orim's Chant: creatures can't attack this turn (CR 508.1c)");
    }

    [Fact]
    public void OrimSChant_Resolve_Kicked_AttackRestriction_ExpiresAtEndOfTurn()
    {
        // The CombatRestrictionEffect is EOT-scoped: after ExpireEndOfTurn the
        // restriction no longer applies to any creature (CR 514.2).
        var card = OrimSChantFactory.Create(_alice);
        if (card is Card concrete) concrete.SetWasKicked(true);

        var effects = new ContinuousEffectsService();

        var attacker = new Creature("War Falcon", "{W}", 2, 1)
        { Owner = _bob, Controller = _bob };
        attacker.SetZone(ZoneType.Battlefield);

        var def = OrimSChantFactory.BuildDefinition(card, o => o, effects);
        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { _bob } }, ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        // Pre-expiry: restriction is active.
        effects.HasRestriction(attacker, CombatRestriction.CannotAttack)
            .Should().BeTrue("restriction is active before EOT");

        // Simulate end-of-turn expiry.
        effects.ExpireEndOfTurn();

        effects.HasRestriction(attacker, CombatRestriction.CannotAttack)
            .Should().BeFalse(
                "EOT-scoped CannotAttack restriction expires at end of turn (CR 514.2)");
    }

    [Fact]
    public void OrimSChant_Resolve_Kicked_NoEffectsService_CastBlockStillApplies()
    {
        // When no ContinuousEffectsService is supplied, the cast block still
        // applies — only the combat restriction is skipped.
        var card = OrimSChantFactory.Create(_alice);
        if (card is Card concrete) concrete.SetWasKicked(true);

        // No effects service supplied (null).
        var def = OrimSChantFactory.BuildDefinition(card, o => o, continuousEffects: null);
        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { _bob } }, ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        CastingRestrictions.CannotCastAnySpell(_bob).Should().BeTrue(
            "cast block applies even when no ContinuousEffectsService is supplied");
    }

    [Fact]
    public void OrimSChant_Resolve_CastBlock_ClearedByRemoveToken()
    {
        var card = OrimSChantFactory.Create(_alice);

        var def = OrimSChantFactory.BuildDefinition(card, o => o);
        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { _bob } }, ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        CastingRestrictions.CannotCastAnySpell(_bob).Should().BeTrue("restriction active");

        // Simulate end-of-turn cleanup using the card as the source token.
        CastingRestrictions.RemoveCannotCastAnySpell(card);

        CastingRestrictions.CannotCastAnySpell(_bob).Should().BeFalse(
            "restriction cleared after RemoveCannotCastAnySpell(card)");
    }
}
