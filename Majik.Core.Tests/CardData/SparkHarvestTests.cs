using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="SparkHarvestFactory"/> — Sorcery {B} (Ikoria).
///
/// Oracle text:
///   "As an additional cost to cast this spell, sacrifice a creature or pay {3}{B}.
///    Destroy target creature or planeswalker."
///
/// Spark Harvest is the canonical card unblocked by the
/// `cast-pipeline-additional-cost-after-targets` deferral: it has BOTH a
/// required targeting clause AND a non-mana (sacrifice) additional cost, so
/// it exercises the CR 601.2h ordering fix (non-mana costs paid with the
/// total cost, AFTER target collection CR 601.2c) — an illegal targeted cast
/// rewinds with the sacrifice STILL UNPAID (CR 731.1).
///
/// Covers:
///   - Identity (Sorcery, {B}, CMC 1, black) + NamedCardFactory dispatch.
///   - SpellDefinition shape: SacrificeCreatureOrPayManaAdditionalCost +
///     one 1..1 "target creature or planeswalker" target request.
///   - Resolve: destroys target creature / planeswalker (CR 701.7).
///   - Resolve: target left battlefield → no-op (CR 608.2b).
///   - Cost: sacrifice mode removes a creature and sets Sacrificed.
///   - Cost: pay-mana mode pays {3}{B} when no creature is controlled.
///   - Cost.CanPay edge cases (OR gate, CR 117.1).
///   - SpellCastFlow: full cast sacrifices the creature and pushes the spell.
///   - CR 731.1: a targeting failure does NOT pay the sacrifice.
/// </summary>
public class SparkHarvestTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Identity_NameTypeManaCost()
    {
        var card = SparkHarvestFactory.Create(_alice);

        card.Name.Should().Be("Spark Harvest");
        card.ManaCost.Should().Be("{B}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SparkHarvest()
    {
        var card = NamedCardFactory.Create("Spark Harvest", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Spark Harvest");
        card.ManaCost.Should().Be("{B}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // SpellDefinition — structural shape
    // -----------------------------------------------------------------------

    [Fact]
    public void SpellDefinition_DeclaresSacOrPayManaCost_AndCreatureOrPlaneswalkerTarget()
    {
        var def = SparkHarvestFactory.BuildDefinition(t => t);

        def.AdditionalCostsOrEmpty.Should().ContainSingle()
            .Which.Should().BeOfType<SacrificeCreatureOrPayManaAdditionalCost>(
                "Spark Harvest prints 'As an additional cost, sacrifice a creature or pay {3}{B}.' (CR 601.2f)");
        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().ContainSingle();

        var tr = def.TargetRequests[0];
        tr.MinTargets.Should().Be(1);
        tr.MaxTargets.Should().Be(1);
        tr.Description.Should().Contain("creature or planeswalker");
        tr.Intent.Should().Be(BotIntent.Removal);
    }

    // -----------------------------------------------------------------------
    // Resolve — destroys creature / planeswalker
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DestroysTargetCreature()
    {
        var bear = NewControlledCreature(_bob, "Grizzly Bears", "{1}{G}");

        Resolve(bear);

        bear.Zone.Should().Be(ZoneType.Graveyard,
            "Spark Harvest destroys the target creature (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(bear);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bear);
    }

    [Fact]
    public void Resolve_DestroysTargetPlaneswalker()
    {
        var liliana = new Planeswalker("Liliana of the Veil", "{1}{B}{B}", 3);
        liliana.SetOwner(_bob);
        liliana.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(liliana);
        liliana.SetZone(ZoneType.Battlefield);

        Resolve(liliana);

        liliana.Zone.Should().Be(ZoneType.Graveyard,
            "Spark Harvest destroys the target planeswalker (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(liliana);
    }

    [Fact]
    public void Resolve_TargetNotOnBattlefield_DoesNothing()
    {
        // Target already left the battlefield before resolution (CR 608.2b).
        var goyf = new Creature("Tarmogoyf", "{1}{G}", 1, 1);
        goyf.SetOwner(_bob);
        goyf.SetController(_bob);
        _bob.Zones.Graveyard.AddCard(goyf);
        goyf.SetZone(ZoneType.Graveyard);

        Resolve(goyf);

        goyf.Zone.Should().Be(ZoneType.Graveyard,
            "Spark Harvest is a no-op when the target is no longer on the battlefield (CR 608.2b)");
    }

    // -----------------------------------------------------------------------
    // Cost: sacrifice mode
    // -----------------------------------------------------------------------

    [Fact]
    public void Cost_SacrificesACreature_WhenControlled()
    {
        var alice = new Player("Alice", 20);
        var token = new Creature("Zombie", "", 2, 2);
        token.SetOwner(alice);
        token.SetController(alice);
        alice.Zones.Battlefield.AddCard(token);
        token.SetZone(ZoneType.Battlefield);

        var cost = new SacrificeCreatureOrPayManaAdditionalCost(ManaCost.Parse("{3}{B}"));
        cost.CanPay(alice).Should().BeTrue();
        cost.Pay(alice).Should().BeTrue();

        cost.Sacrificed.Should().Be(token,
            "sacrifice mode is preferred when a creature is available (v1 deterministic)");
        cost.PaidMana.Should().BeFalse();
        token.Zone.Should().Be(ZoneType.Graveyard,
            "sacrificed creature moves to the graveyard (CR 701.16a)");
        alice.Zones.Battlefield.GetCards().Should().NotContain(token);
    }

    // -----------------------------------------------------------------------
    // Cost: pay-mana mode
    // -----------------------------------------------------------------------

    [Fact]
    public void Cost_PaysMana_WhenNoCreatureControlled()
    {
        var alice = new Player("Alice", 20);
        alice.AddManaToPool(ManaCost.Parse("{3}{B}"));

        var cost = new SacrificeCreatureOrPayManaAdditionalCost(ManaCost.Parse("{3}{B}"));
        cost.CanPay(alice).Should().BeTrue();
        cost.Pay(alice).Should().BeTrue();

        cost.PaidMana.Should().BeTrue(
            "pay-mana mode is used when the caster controls no creature (v1 fallback)");
        cost.Sacrificed.Should().BeNull();
    }

    [Fact]
    public void Cost_CanPay_FalseWhenNoCreatureAndNoMana()
    {
        var alice = new Player("Alice", 20); // empty board, empty pool

        var cost = new SacrificeCreatureOrPayManaAdditionalCost(ManaCost.Parse("{3}{B}"));
        cost.CanPay(alice).Should().BeFalse(
            "neither sacrifice (no creature) nor pay-mana (empty pool) is payable (CR 117.1)");
    }

    // -----------------------------------------------------------------------
    // SpellCastFlow — full cast (sacrifice mode), then rewind on targeting fail
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SpellCastFlow_FullCast_SacrificesCreature_AndPushesSpell()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var flow = new SpellCastFlow(stack, new ZoneService(bus), bus);

        var token = new Creature("Zombie", "", 2, 2);
        token.SetOwner(_alice);
        token.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(token);
        token.SetZone(ZoneType.Battlefield);

        var bear = NewControlledCreature(_bob, "Grizzly Bears", "{1}{G}");

        var card = SparkHarvestFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { bear });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(
            _alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, stack);

        var def = SparkHarvestFactory.BuildDefinition(t => t);

        var spell = await flow.CastAsync(_alice, card, def, agent, ctx);

        token.Zone.Should().Be(ZoneType.Graveyard,
            "the sacrifice additional cost is paid (CR 601.2h)");
        card.Zone.Should().Be(ZoneType.Stack);
        stack.Count.Should().Be(1);

        spell.Resolve();
        bear.Zone.Should().Be(ZoneType.Graveyard, "the destroy resolves (CR 701.7)");
    }

    [Fact]
    public async Task SpellCastFlow_TargetingFails_DoesNotPaySacrifice_CR731Rewind()
    {
        // The deferral this card unblocks: a targeted spell with a non-mana
        // additional cost must NOT pay the sacrifice when target collection
        // fails (CR 601.2h — non-mana costs are paid with the total cost at
        // the END, after target collection CR 601.2c; CR 731.1 rewind).
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var flow = new SpellCastFlow(stack, new ZoneService(bus), bus);

        var token = new Creature("Zombie", "", 2, 2);
        token.SetOwner(_alice);
        token.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(token);
        token.SetZone(ZoneType.Battlefield);

        var card = SparkHarvestFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);

        var agent = new ScriptedAgent();
        agent.QueueTargets(System.Array.Empty<object>()); // no legal target picked
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(
            _alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, stack);

        var def = SparkHarvestFactory.BuildDefinition(t => t);

        var act = async () => await flow.CastAsync(_alice, card, def, agent, ctx);

        await act.Should().ThrowAsync<InvalidOperationException>(
            "target collection (CR 601.2c) fails with no legal target picked");

        token.Zone.Should().Be(ZoneType.Battlefield,
            "CR 731.1 — the illegal cast rewinds, so the sacrifice (paid at " +
            "CR 601.2h, after target collection) is never committed");
        _alice.Zones.Battlefield.GetCards().Should().Contain(token);
        card.Zone.Should().Be(ZoneType.Hand);
        stack.Count.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void Resolve(object targetToken)
    {
        var def = SparkHarvestFactory.BuildDefinition(targetResolver: t => t);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { targetToken } },
            Mana: ManaPayment.Empty);

        foreach (var fx in def.EffectFactory(chosen))
        {
            fx.Execute();
        }
    }

    private static Creature NewControlledCreature(Player owner, string name, string cost)
    {
        var c = new Creature(name, cost, 1, 1);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }
}
