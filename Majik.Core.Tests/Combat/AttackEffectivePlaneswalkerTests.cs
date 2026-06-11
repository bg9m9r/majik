using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;
using Planeswalker = Majik.Core.Cards.Planeswalker;

namespace Majik.Core.Tests.Combat;

/// <summary>
/// Sub-slice 4C — attack AGAINST an EFFECTIVE planeswalker (CR 711). A
/// creature-front transform DFC flipped to its planeswalker back is a
/// <see cref="Creature"/> C# instance carrying a transient loyalty body; it
/// can be ATTACKED like a real planeswalker, combat damage removes its
/// effective loyalty, and the loyalty=0 death SBA destroys it. A real
/// <see cref="Planeswalker"/> still routes identically (same loyalty
/// reduction).
/// </summary>
public class AttackEffectivePlaneswalkerTests
{
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;
    private readonly StateBasedActions _sba;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public AttackEffectivePlaneswalkerTests()
    {
        _zones = new ZoneService(_bus);
        _sba = new StateBasedActions(_bus, _zones);
    }

    private static Creature NewCreature(string name, int p, int t, Player owner) =>
        new(name, "1", p, t) { Owner = owner, Controller = owner };

    /// <summary>Build a creature-front DFC already flipped to a planeswalker
    /// back (a Creature instance carrying a transient loyalty body).</summary>
    private Creature MakeFlippedPlaneswalkerDfc(int loyalty, Player owner)
    {
        var card = new Creature("Ral, Monsoon Mage", "1", power: 1, toughness: 3)
        { Owner = owner, Controller = owner };
        var svc = new ContinuousEffectsService();
        card.ActiveEffects = svc;
        card.MdfcState = new MdfcState(
            "Ral, Monsoon Mage", "Ral, Leyline Prodigy",
            new BackFaceCharacteristics(
                name: "Ral, Leyline Prodigy",
                isCreature: false,
                power: 0,
                toughness: 0,
                types: new[] { CardType.Planeswalker },
                loyalty: loyalty));
        card.MdfcState.Transform(); // flip to the planeswalker back
        return card;
    }

    [Fact]
    public void FlippedDfc_IsEffectivePlaneswalker_WithBackFaceLoyalty()
    {
        var dfc = MakeFlippedPlaneswalkerDfc(5, _bob);
        dfc.IsEffectivePlaneswalker().Should().BeTrue();
        dfc.GetEffectiveLoyalty().Should().Be(5);
    }

    [Fact]
    public void CanAttackEffectivePlaneswalker_FlippedDfc_IsLegalTarget()
    {
        var dfc = MakeFlippedPlaneswalkerDfc(5, _bob);
        dfc.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(dfc);

        var validator = new CombatValidator();
        validator.CanAttackPlaneswalker(dfc, _alice).Should().BeTrue();
    }

    [Fact]
    public async Task AttackEffectivePlaneswalker_UnblockedAttacker_RemovesEffectiveLoyalty()
    {
        var dfc = MakeFlippedPlaneswalkerDfc(4, _bob);
        dfc.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(dfc);

        var bear = NewCreature("Bear", 2, 2, _alice);
        bear.SetZone(ZoneType.Battlefield);
        bear.HasSummoningSickness = false;
        _alice.Zones.Battlefield.AddCard(bear);

        var flow = new CombatFlow(_bus, _sba);
        var atk = new ScriptedAgent();
        atk.QueueAttackers(new CombatPlan(new[]
        {
            new Majik.Core.Players.Agents.AttackerDeclaration(bear, dfc),
        }));
        var blk = new ScriptedAgent();
        blk.QueueBlockers(BlockPlan.None);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice,
            1, StepStateType.DeclareAttackers, new Majik.Core.Stack.Stack());

        await flow.RunCombatAsync(_alice, _bob, atk, blk,
            new[] { bear }, Array.Empty<Creature>(), ctx);

        dfc.GetEffectiveLoyalty().Should().Be(2); // 4 - 2
        _bob.LifeTotal.Should().Be(20);
    }

    [Fact]
    public async Task AttackEffectivePlaneswalker_LethalDamage_DiesViaSba()
    {
        var dfc = MakeFlippedPlaneswalkerDfc(3, _bob);
        dfc.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(dfc);

        var giant = NewCreature("Giant", 5, 5, _alice);
        giant.SetZone(ZoneType.Battlefield);
        giant.HasSummoningSickness = false;
        _alice.Zones.Battlefield.AddCard(giant);

        var flow = new CombatFlow(_bus, _sba);
        var atk = new ScriptedAgent();
        atk.QueueAttackers(new CombatPlan(new[]
        {
            new Majik.Core.Players.Agents.AttackerDeclaration(giant, dfc),
        }));
        var blk = new ScriptedAgent();
        blk.QueueBlockers(BlockPlan.None);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice,
            1, StepStateType.DeclareAttackers, new Majik.Core.Stack.Stack());

        await flow.RunCombatAsync(_alice, _bob, atk, blk,
            new[] { giant }, Array.Empty<Creature>(), ctx);

        dfc.GetEffectiveLoyalty().Should().Be(0);
        dfc.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public async Task AttackRealPlaneswalker_StillRemovesLoyalty_Identically()
    {
        // Regression — the widened path must not change real-planeswalker behaviour.
        var pw = new Planeswalker("Jace", "2UU", startingLoyalty: 4) { Owner = _bob, Controller = _bob };
        pw.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(pw);

        var bear = NewCreature("Bear", 2, 2, _alice);
        bear.SetZone(ZoneType.Battlefield);
        bear.HasSummoningSickness = false;
        _alice.Zones.Battlefield.AddCard(bear);

        var flow = new CombatFlow(_bus, _sba);
        var atk = new ScriptedAgent();
        atk.QueueAttackers(new CombatPlan(new[]
        {
            new Majik.Core.Players.Agents.AttackerDeclaration(bear, pw),
        }));
        var blk = new ScriptedAgent();
        blk.QueueBlockers(BlockPlan.None);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice,
            1, StepStateType.DeclareAttackers, new Majik.Core.Stack.Stack());

        await flow.RunCombatAsync(_alice, _bob, atk, blk,
            new[] { bear }, Array.Empty<Creature>(), ctx);

        pw.Loyalty.Should().Be(2);
        pw.GetEffectiveLoyalty().Should().Be(2);
    }

    // --- back-face loyalty abilities (4C-ii) ---

    [Fact]
    public void TransformToPlaneswalkerBack_AttachesBackFaceLoyaltyAbilities()
    {
        var owner = _bob;
        var card = new Creature("Ral, Monsoon Mage", "1", power: 1, toughness: 3)
        { Owner = owner, Controller = owner };
        var svc = new ContinuousEffectsService();
        card.ActiveEffects = svc;

        var backOracle = "+1: Draw a card.\n-3: You gain 3 life.";
        card.MdfcState = new MdfcState(
            "Ral, Monsoon Mage", "Ral, Leyline Prodigy",
            new BackFaceCharacteristics(
                name: "Ral, Leyline Prodigy",
                isCreature: false,
                power: 0,
                toughness: 0,
                types: new[] { CardType.Planeswalker },
                loyalty: 4,
                oracleText: backOracle));

        // front face — no loyalty abilities yet
        card.Abilities.OfType<LoyaltyAbility>().Should().BeEmpty();

        card.MdfcState.Transform(); // flip to the planeswalker back

        var loyalties = card.Abilities.OfType<LoyaltyAbility>().ToList();
        loyalties.Should().HaveCount(2);
        loyalties.Select(l => l.LoyaltyChange).Should().BeEquivalentTo(new[] { 1, -3 });
        loyalties.Should().OnlyContain(l => ReferenceEquals(l.Source, card));

        // flip back — abilities removed again
        card.MdfcState.Transform();
        card.Abilities.OfType<LoyaltyAbility>().Should().BeEmpty();
    }

    [Fact]
    public void FlippedBack_LoyaltyAbility_CanActivate_OnTransientBody()
    {
        var owner = _bob;
        var card = new Creature("Ral, Monsoon Mage", "1", power: 1, toughness: 3)
        { Owner = owner, Controller = owner };
        var svc = new ContinuousEffectsService();
        card.ActiveEffects = svc;
        card.MdfcState = new MdfcState(
            "Ral, Monsoon Mage", "Ral, Leyline Prodigy",
            new BackFaceCharacteristics(
                name: "Ral, Leyline Prodigy",
                isCreature: false, power: 0, toughness: 0,
                types: new[] { CardType.Planeswalker },
                loyalty: 4,
                oracleText: "+1: Draw a card.\n-3: You gain 3 life."));
        card.MdfcState.Transform();

        var plus1 = card.Abilities.OfType<LoyaltyAbility>().First(l => l.LoyaltyChange == 1);
        plus1.CanActivate().Should().BeTrue();
        plus1.PayLoyaltyCost();
        card.GetEffectiveLoyalty().Should().Be(5);
    }
}
