using FluentAssertions;
using Majik.Core.Abilities;
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
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="BecomeImmenseFactory"/>.
///
/// Card: Become Immense — Instant {5}{G} (Khans of Tarkir).
///   "Delve. Target creature gets +6/+6 until end of turn."
///
/// Covers:
///   - Identity (Instant, green, {5}{G}, Delve keyword marker).
///   - NamedCardFactory dispatch.
///   - SpellDefinition shape (1 target creature request, no modes, no X).
///   - Resolve grants +6/+6 EOT.
///   - End-of-turn cleanup lifts the pump (CR 514.2).
///   - Fizzle: target not on battlefield → no-op (CR 608.2b).
///   - Cast with Delve exiles graveyard cards and pumps the target.
/// </summary>
public class BecomeImmenseFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public BecomeImmenseFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
    }

    // ── Identity ──────────────────────────────────────────────────────────────

    [Fact]
    public void Create_HasInstantShape_Green_AtCost5G()
    {
        var bi = BecomeImmenseFactory.Create(_alice);

        bi.Name.Should().Be("Become Immense");
        bi.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(bi).Should().Contain(ManaColor.Green);
        bi.Owner.Should().BeSameAs(_alice);
        bi.Controller.Should().BeSameAs(_alice);
        bi.ManaCost.Should().Be("{5}{G}");
    }

    [Fact]
    public void Create_AttachesDelveKeywordMarker()
    {
        var bi = BecomeImmenseFactory.Create(_alice);

        bi.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword)
            .Should().Contain("Delve", because: "Become Immense is a Delve card (CR 702.66)");
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsBecomeImmenseShape()
    {
        var card = NamedCardFactory.Create("Become Immense", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Become Immense");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{5}{G}");
        card.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword).Should().Contain("Delve");
    }

    // ── SpellDefinition shape ─────────────────────────────────────────────────

    [Fact]
    public void BuildSpellDefinition_SingleTargetCreatureRequest()
    {
        var def = BecomeImmenseFactory.BuildSpellDefinition(t => t);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Contain("creature");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // ── Resolve effect ────────────────────────────────────────────────────────

    [Fact]
    public void EffectFactory_TargetCreature_GetsPlusSixPlusSix()
    {
        var continuous = new ContinuousEffectsService();
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = continuous,
        };
        _alice.Zones.Battlefield.AddCard(bear);

        ExecuteResolve(bear);

        bear.GetPower().Should().Be(8, "+6/+6 via PumpUntilEndOfTurnEffect");
        bear.GetToughness().Should().Be(8);
    }

    [Fact]
    public void PumpEffect_ExpiresAtEndOfTurn()
    {
        var continuous = new ContinuousEffectsService();
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = continuous,
        };
        _alice.Zones.Battlefield.AddCard(bear);

        ExecuteResolve(bear);
        bear.GetPower().Should().Be(8);

        // CR 514.2 — EOT-flagged effects expire on cleanup.
        continuous.ExpireEndOfTurn();

        bear.GetPower().Should().Be(2);
        bear.GetToughness().Should().Be(2);
    }

    [Fact]
    public void EffectFactory_TargetNotOnBattlefield_IsNoOp()
    {
        var continuous = new ContinuousEffectsService();
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Graveyard,
            ActiveEffects = continuous,
        };
        _bob.Zones.Graveyard.AddCard(bear);

        ExecuteResolve(bear);

        bear.GetPower().Should().Be(2,
            because: "CR 608.2b — illegal target → no-op (target is not on the battlefield)");
        bear.GetToughness().Should().Be(2);
    }

    [Fact]
    public void EffectFactory_NonCreatureResolverResult_IsNoOp()
    {
        var nonCreature = new Card("Mountain Token", "");

        var def = BecomeImmenseFactory.BuildSpellDefinition(_ => nonCreature);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { nonCreature } },
            Mana: ManaPayment.Empty);

        // CR 608.2b — non-Creature resolver result → effect resolves as no-op.
        // Contract: must not throw.
        var act = () => { foreach (var e in def.EffectFactory(chosen)) e.Execute(); };
        act.Should().NotThrow();
    }

    // ── Delve cast wiring ─────────────────────────────────────────────────────

    [Fact]
    public async Task BecomeImmense_CastWithDelve_ExilesGraveyardCards_AndPumpsTarget()
    {
        // Alice has 5 cards in her graveyard for delve. Become Immense {5}{G}
        // — delve all 5 generic, pay {G}.
        var fodder = SeedGraveyard(_alice, 5);

        var continuous = new ContinuousEffectsService();
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = continuous,
        };
        _alice.Zones.Battlefield.AddCard(bear);

        var bi = BecomeImmenseFactory.Create(_alice);
        bi.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(bi);

        var delve = new DelveCost(bi, fodder);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { bear });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, PhaseStateType.PreCombatMain, _stack);

        var spell = await _flow.CastAsync(
            _alice, bi,
            BecomeImmenseFactory.BuildSpellDefinition(t => t),
            agent, ctx,
            delveCost: delve);

        // Delve payment exiled all 5 graveyard cards.
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        _alice.Zones.Exile.GetCards().Should().HaveCount(5);
        foreach (var c in fodder) c.Zone.Should().Be(ZoneType.Exile);

        bi.Zone.Should().Be(ZoneType.Stack);
        spell.Resolve();

        bear.GetPower().Should().Be(8);
        bear.GetToughness().Should().Be(8);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void ExecuteResolve(Creature target)
    {
        var def = BecomeImmenseFactory.BuildSpellDefinition(t => t);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();
    }

    private static IReadOnlyList<ICard> SeedGraveyard(Player p, int count)
    {
        var list = new List<ICard>(count);
        for (var i = 0; i < count; i++)
        {
            var c = new Card($"Yard{i}", "");
            c.SetOwner(p);
            c.SetZone(ZoneType.Graveyard);
            p.Zones.Graveyard.AddCard(c);
            list.Add(c);
        }
        return list;
    }
}
