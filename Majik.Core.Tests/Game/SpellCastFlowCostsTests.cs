using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Majik.Core.Cards;
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
using Creature = Majik.Core.Cards.Creature;

public class SpellCastFlowCostsTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public SpellCastFlowCostsTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _flow = new SpellCastFlow(_stack, new ZoneService(_bus), _bus);
    }

    [Fact]
    public async Task AdditionalCost_Sacrifice_PaidBeforeSpellHitsStack()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        _alice.Zones.Battlefield.AddCard(bear);

        var spell = new Instant("Carnage Charm", "B") { Owner = _alice, Zone = ZoneType.Hand };
        _alice.Zones.Hand.AddCard(spell);

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, spell,
            SpellDefinition.Vanilla(_ => System.Array.Empty<IEffect>()),
            agent, ctx,
            additionalCosts: new[] { new SacrificeCreatureCost(bear) });

        bear.Zone.Should().Be(ZoneType.Graveyard);
        _stack.Count.Should().Be(1);
    }

    [Fact]
    public async Task FlashbackCost_CastsFromGraveyard_ExilesOnResolve()
    {
        var firebolt = new Instant("Firebolt", "R") { Owner = _alice, Zone = ZoneType.Graveyard };
        _alice.Zones.Graveyard.AddCard(firebolt);

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        var ran = false;
        var spell = await _flow.CastAsync(
            _alice, firebolt,
            new SpellDefinition(
                Modes: System.Array.Empty<string>(), HasVariableX: false,
                TargetRequests: System.Array.Empty<TargetRequest>(),
                EffectFactory: _ => new IEffect[] { new Effect("dmg", () => { _bob.LoseLife(3); ran = true; }) }),
            agent, ctx,
            alternativeCost: new FlashbackAlternativeCost(ManaCost.Parse("4R")));

        // Spell on stack now in Stack zone.
        firebolt.Zone.Should().Be(ZoneType.Stack);
        spell.Resolve();

        ran.Should().BeTrue();
        _bob.LifeTotal.Should().Be(17);
        // Flashback cleanup effect runs as part of Resolve.
        firebolt.Zone.Should().Be(ZoneType.Exile);
    }

    // -----------------------------------------------------------------
    // New: enforced additional costs declared on SpellDefinition itself
    // (template-bound "As an additional cost to cast this spell, …")
    // -----------------------------------------------------------------

    [Fact]
    public async Task DefinitionAdditionalCost_PaidAlongsideCallerCosts()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        _alice.Zones.Battlefield.AddCard(bear);

        var spell = new Sorcery("Stub Spell", "B") { Owner = _alice, Zone = ZoneType.Hand };
        _alice.Zones.Hand.AddCard(spell);

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        var def = new SpellDefinition(
            Modes: System.Array.Empty<string>(), HasVariableX: false,
            TargetRequests: System.Array.Empty<TargetRequest>(),
            EffectFactory: _ => System.Array.Empty<IEffect>(),
            ModeIntents: null,
            AdditionalCosts: new IAdditionalCost[] { new SacrificeACreatureAdditionalCost() });

        await _flow.CastAsync(_alice, spell, def, agent, ctx);

        bear.Zone.Should().Be(ZoneType.Graveyard);
        _stack.Count.Should().Be(1);
    }

    [Fact]
    public async Task DefinitionAdditionalCost_FailsWhenCostUnpayable()
    {
        // No creatures on battlefield → SacrificeACreatureAdditionalCost
        // can't pay. SpellCastFlow must throw before the card hits the
        // stack and before the (already-paid) mana is consumed.
        var spell = new Sorcery("Stub Spell", "B") { Owner = _alice, Zone = ZoneType.Hand };
        _alice.Zones.Hand.AddCard(spell);

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        var def = new SpellDefinition(
            Modes: System.Array.Empty<string>(), HasVariableX: false,
            TargetRequests: System.Array.Empty<TargetRequest>(),
            EffectFactory: _ => System.Array.Empty<IEffect>(),
            ModeIntents: null,
            AdditionalCosts: new IAdditionalCost[] { new SacrificeACreatureAdditionalCost() });

        var act = async () => await _flow.CastAsync(_alice, spell, def, agent, ctx);

        await act.Should().ThrowAsync<System.InvalidOperationException>()
            .WithMessage("*sacrifice a creature*");
        _stack.Count.Should().Be(0);
    }

    [Fact]
    public async Task DefinitionAdditionalCost_SacrificedRefAvailableInEffect()
    {
        // Fling-style: effect resolves against the sacrificed creature's
        // power. The cost object's Sacrificed reference must be available
        // via ChosenSpellParams.AdditionalCostPayments inside EffectFactory.
        var bigCreature = new Creature("Giant", "4G", 5, 5)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        _alice.Zones.Battlefield.AddCard(bigCreature);

        var fling = new Instant("Fling", "1R") { Owner = _alice, Zone = ZoneType.Hand };
        _alice.Zones.Hand.AddCard(fling);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { _bob });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        var def = new SpellDefinition(
            Modes: System.Array.Empty<string>(), HasVariableX: false,
            TargetRequests: new[] { new TargetRequest("any target", 1, 1, System.Array.Empty<object>()) },
            EffectFactory: p =>
            {
                Creature? sacrificed = null;
                foreach (var cost in p.AdditionalCostPaymentsOrEmpty)
                {
                    if (cost is SacrificeACreatureAdditionalCost sac && sac.Sacrificed is Creature c)
                    {
                        sacrificed = c;
                        break;
                    }
                }
                return new IEffect[]
                {
                    new Effect("fling damage", () =>
                    {
                        if (sacrificed == null) return;
                        _bob.LoseLife(sacrificed.Power);
                    }),
                };
            },
            ModeIntents: null,
            AdditionalCosts: new IAdditionalCost[] { new SacrificeACreatureAdditionalCost() });

        var castSpell = await _flow.CastAsync(_alice, fling, def, agent, ctx);
        castSpell.Resolve();

        bigCreature.Zone.Should().Be(ZoneType.Graveyard);
        _bob.LifeTotal.Should().Be(15);
    }

    [Fact]
    public async Task Fling_EndToEnd_ViaOracleSpellBinder()
    {
        // Cast Fling through the real OracleSpellBinder registry, paying
        // its template-declared sacrifice cost and verifying the
        // sacrificed creature's power feeds the damage effect.
        var bear = new Creature("Bear", "1G", 4, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        _alice.Zones.Battlefield.AddCard(bear);

        var fling = new Instant("Fling", "1R") { Owner = _alice, Zone = ZoneType.Hand };
        _alice.Zones.Hand.AddCard(fling);

        var entity = new CardEntity
        {
            Name = "Fling",
            OracleText = "As an additional cost to cast this spell, sacrifice a creature. " +
                "Fling deals damage equal to the sacrificed creature's power to any target.",
        };

        var def = OracleSpellBinder.Bind(
            entity, _alice, o => o,
            effects: new ContinuousEffectsService(),
            stack: _stack);
        def.Should().NotBeNull("Fling oracle text should bind through FlingLikeTemplate");
        def!.AdditionalCostsOrEmpty.Should().HaveCount(1);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { _bob });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        var spell = await _flow.CastAsync(_alice, fling, def, agent, ctx);
        spell.Resolve();

        bear.Zone.Should().Be(ZoneType.Graveyard);
        _bob.LifeTotal.Should().Be(16); // 20 - 4 (bear's power)
    }

    // -----------------------------------------------------------------
    // Escape (CR 702.138) — cast-from-graveyard alt-cost with the
    // "exile N other graveyard cards" rider. Validates the
    // EscapeAlternativeCost integration into SpellCastFlow:
    //   * the exile rider is paid before the card moves to the stack,
    //   * Spell.WasCastForEscape + Card.WasCastForEscape are stamped,
    //   * insufficient-others fails the cast cleanly (no zone mutation),
    //   * the rider does NOT pick the spell card itself.
    // -----------------------------------------------------------------

    [Fact]
    public async Task EscapeCost_FromGraveyard_PaysExileRider_StampsEscapedFlag()
    {
        var phlage = new Creature("Phlage, Titan of Fire's Fury", "{2}{R}{W}", 4, 4)
        { Owner = _alice, Zone = ZoneType.Graveyard };
        _alice.Zones.Graveyard.AddCard(phlage);

        // Three OTHER cards in the graveyard for Phlage's escape rider.
        var fillers = new[]
        {
            new Instant("F1", "{1}"),
            new Instant("F2", "{1}"),
            new Instant("F3", "{1}"),
        };
        foreach (var f in fillers)
        {
            f.SetOwner(_alice);
            f.SetZone(ZoneType.Graveyard);
            _alice.Zones.Graveyard.AddCard(f);
        }

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        var spell = await _flow.CastAsync(
            _alice, phlage,
            SpellDefinition.Vanilla(_ => System.Array.Empty<IEffect>()),
            agent, ctx,
            alternativeCost: new EscapeAlternativeCost(ManaCost.Parse("{2}{R}{W}"), 3));

        // Exile rider paid — three fillers in exile, Phlage on stack.
        foreach (var f in fillers)
        {
            f.Zone.Should().Be(ZoneType.Exile);
            _alice.Zones.Exile.GetCards().Should().Contain(f);
        }
        phlage.Zone.Should().Be(ZoneType.Stack,
            "Escape cast moves the card from graveyard → stack (SpellCastFlow.ZoneService.MoveCard)");
        _alice.Zones.Graveyard.GetCards().Should().NotContain(phlage);

        // CR 702.138b — escape sentinel stamped on both spell + card.
        spell.WasCastForEscape.Should().BeTrue();
        phlage.WasCastForEscape.Should().BeTrue();

        // Phlage is NOT in the exile pile (the rider is "other" cards).
        _alice.Zones.Exile.GetCards().Should().NotContain(phlage);
    }

    [Fact]
    public async Task EscapeCost_InsufficientOthers_ThrowsBeforeStack()
    {
        var phlage = new Creature("Phlage, Titan of Fire's Fury", "{2}{R}{W}", 4, 4)
        { Owner = _alice, Zone = ZoneType.Graveyard };
        _alice.Zones.Graveyard.AddCard(phlage);

        // Only one other card — escape rider needs 3.
        var only = new Instant("Only", "{1}") { Owner = _alice, Zone = ZoneType.Graveyard };
        _alice.Zones.Graveyard.AddCard(only);

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        Func<System.Threading.Tasks.Task> act = async () => await _flow.CastAsync(
            _alice, phlage,
            SpellDefinition.Vanilla(_ => System.Array.Empty<IEffect>()),
            agent, ctx,
            alternativeCost: new EscapeAlternativeCost(ManaCost.Parse("{2}{R}{W}"), 3));

        await act.Should().ThrowAsync<System.InvalidOperationException>();

        // No zone mutation — Phlage + the only filler still in graveyard,
        // nothing exiled, stack empty.
        phlage.Zone.Should().Be(ZoneType.Graveyard);
        only.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Exile.GetCards().Should().BeEmpty();
        _stack.Count.Should().Be(0);
    }

    [Fact]
    public async Task EscapeCost_NotFromGraveyard_ThrowsLegalityCheck()
    {
        // Phlage in hand → EscapeAlternativeCost.CanCastFor returns
        // false → SpellCastFlow's CR 118.9 legality guard throws.
        var phlage = new Creature("Phlage, Titan of Fire's Fury", "{2}{R}{W}", 4, 4)
        { Owner = _alice, Zone = ZoneType.Hand };
        _alice.Zones.Hand.AddCard(phlage);
        // Plenty of graveyard padding to rule out the candidate-pool guard.
        for (int i = 0; i < 5; i++)
        {
            var f = new Instant($"F{i}", "{1}") { Owner = _alice, Zone = ZoneType.Graveyard };
            _alice.Zones.Graveyard.AddCard(f);
        }

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        Func<System.Threading.Tasks.Task> act = async () => await _flow.CastAsync(
            _alice, phlage,
            SpellDefinition.Vanilla(_ => System.Array.Empty<IEffect>()),
            agent, ctx,
            alternativeCost: new EscapeAlternativeCost(ManaCost.Parse("{2}{R}{W}"), 3));

        await act.Should().ThrowAsync<System.InvalidOperationException>();
        _stack.Count.Should().Be(0);
    }
}
