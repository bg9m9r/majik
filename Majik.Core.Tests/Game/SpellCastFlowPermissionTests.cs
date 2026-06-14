using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

public class SpellCastFlowPermissionTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public SpellCastFlowPermissionTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
    }

    [Fact]
    public async Task Sorcery_OnOpponentTurn_Throws()
    {
        var sorc = new Sorcery("Divination", "2U") { Owner = _alice, Zone = ZoneType.Hand };
        _alice.Zones.Hand.AddCard(sorc);
        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            activePlayer: _bob, 1, StepStateType.PreCombatMain, _stack);
        var agent = new ScriptedAgent();

        var act = async () => await _flow.CastAsync(_alice, sorc,
            SpellDefinition.Vanilla(_ => System.Array.Empty<IEffect>()),
            agent, ctx);

        await act.Should().ThrowAsync<System.InvalidOperationException>()
            .WithMessage("*your turn*");
    }

    [Fact]
    public async Task Instant_OnOpponentTurn_OK()
    {
        var bolt = new Instant("Bolt", "R") { Owner = _alice, Zone = ZoneType.Hand };
        _alice.Zones.Hand.AddCard(bolt);
        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            activePlayer: _bob, 1, StepStateType.End, _stack);
        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        await _flow.CastAsync(_alice, bolt,
            SpellDefinition.Vanilla(_ => System.Array.Empty<IEffect>()),
            agent, ctx);

        _stack.Count.Should().Be(1);
    }

    [Fact]
    public async Task TopOfLibraryNonland_WithNoGrant_Throws()
    {
        // CR 601.3e — a spell may be cast from the top of the library ONLY
        // while a continuous effect grants that permission. With no grant
        // registered, attempting to cast the top library card must be rejected
        // by SpellCastFlow (the cast-source authorization seam) — otherwise any
        // caller could move an arbitrary library card to the stack.
        using var _ = Majik.Core.Rules.LibraryTopPlayPermissions.PushScope();

        var creature = new Creature("Goblin Bear", "{R}", 2, 2) { Owner = _alice, Zone = ZoneType.Library };
        _alice.Zones.Library.AddCard(creature);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);
        var agent = new ScriptedAgent();

        var act = async () => await _flow.CastAsync(_alice, creature,
            SpellDefinition.Vanilla(_ => System.Array.Empty<IEffect>()),
            agent, ctx);

        await act.Should().ThrowAsync<System.InvalidOperationException>()
            .WithMessage("*top of your library*");
        _stack.Count.Should().Be(0);
    }

    [Fact]
    public async Task TopOfLibraryNonland_WithMatchingGrant_CastsOntoStack()
    {
        // CR 601.3e — with an Any (Bolas's Citadel-style) grant registered for
        // the controller, casting the top library card is authorized: it moves
        // Library → Stack and is marked cast-from-library.
        using var _ = Majik.Core.Rules.LibraryTopPlayPermissions.PushScope();

        var creature = new Creature("Goblin Bear", "{R}", 2, 2) { Owner = _alice, Zone = ZoneType.Library };
        _alice.Zones.Library.AddCard(creature);
        Majik.Core.Rules.LibraryTopPlayPermissions.AddGrant(
            new object(), _alice, Majik.Core.Rules.TopPlayFilter.Any);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);
        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var spell = await _flow.CastAsync(_alice, creature,
            SpellDefinition.Vanilla(_ => System.Array.Empty<IEffect>()),
            agent, ctx);

        _stack.Count.Should().Be(1);
        creature.Zone.Should().Be(ZoneType.Stack);
        spell.WasCastFromLibrary.Should().BeTrue();
    }

    [Fact]
    public async Task TopOfLibraryCreature_WithCreaturesGrant_CastsOntoStack()
    {
        // CR 601.3e — the Augur of Autumn Coven clause / Vivien-style "you may
        // cast creature spells from the top of your library" registers a
        // TopPlayFilter.Creatures grant. MayCastTopCard routes that grant through
        // MatchesCast (creatures ARE cast, CR 601.1, unlike lands), so the cast
        // flow authorizes a CREATURE on top: it moves Library → Stack and is
        // marked cast-from-library.
        using var _ = Majik.Core.Rules.LibraryTopPlayPermissions.PushScope();

        var creature = new Creature("Goblin Bear", "{R}", 2, 2) { Owner = _alice, Zone = ZoneType.Library };
        _alice.Zones.Library.AddCard(creature);
        Majik.Core.Rules.LibraryTopPlayPermissions.AddGrant(
            new object(), _alice, Majik.Core.Rules.TopPlayFilter.Creatures);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);
        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var spell = await _flow.CastAsync(_alice, creature,
            SpellDefinition.Vanilla(_ => System.Array.Empty<IEffect>()),
            agent, ctx);

        _stack.Count.Should().Be(1);
        creature.Zone.Should().Be(ZoneType.Stack);
        spell.WasCastFromLibrary.Should().BeTrue();
    }

    [Fact]
    public async Task TopOfLibraryNoncreature_WithCreaturesGrantOnly_Throws()
    {
        // CR 601.3e — a TopPlayFilter.Creatures grant (Augur Coven) authorizes
        // ONLY creature top-casts: MatchesCast(Creatures, card) is false for a
        // noncreature spell on top, so SpellCastFlow rejects casting an instant
        // from the top even though a creature-only grant is live. A Coven player
        // can't cast a Lightning Bolt off the top of their library.
        using var _ = Majik.Core.Rules.LibraryTopPlayPermissions.PushScope();

        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _alice, Zone = ZoneType.Library };
        _alice.Zones.Library.AddCard(bolt);
        Majik.Core.Rules.LibraryTopPlayPermissions.AddGrant(
            new object(), _alice, Majik.Core.Rules.TopPlayFilter.Creatures);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);
        var agent = new ScriptedAgent();

        var act = async () => await _flow.CastAsync(_alice, bolt,
            SpellDefinition.Vanilla(_ => System.Array.Empty<IEffect>()),
            agent, ctx);

        await act.Should().ThrowAsync<System.InvalidOperationException>()
            .WithMessage("*top of your library*");
        _stack.Count.Should().Be(0);
    }

    [Fact]
    public async Task BolasTopCast_WithPayLifeAltCost_CastsForZeroMana_AndPaysLifeOnResolve()
    {
        // CR 118.9 / 116.3a — Bolas's Citadel: a spell cast from the top of the
        // library under the Any grant whose mandatory alt cost is "pay life
        // equal to its mana value rather than pay its mana cost" goes onto the
        // stack paying ZERO mana; the life (== mana value) is paid on resolve.
        using var _ = Majik.Core.Rules.LibraryTopPlayPermissions.PushScope();

        var creature = new Creature("Goblin Bear", "{2}{R}", 2, 2) { Owner = _alice, Zone = ZoneType.Library };
        _alice.Zones.Library.AddCard(creature);
        Majik.Core.Rules.LibraryTopPlayPermissions.AddGrant(
            new object(), _alice, Majik.Core.Rules.TopPlayFilter.Any,
            revealsTop: true, extraPredicate: null,
            topCastAltCostFactory: () => new Majik.Core.Costs.PayLifeEqualToManaValueAlternativeCost());

        // The grant supplies the mandatory alt cost for the top card.
        var alt = Majik.Core.Rules.LibraryTopPlayPermissions
            .MandatoryTopCastAltCostFor(_alice, creature);
        alt.Should().NotBeNull();

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);
        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty); // alt mana cost is {0}

        var spell = await _flow.CastAsync(_alice, creature,
            SpellDefinition.Vanilla(_ => System.Array.Empty<IEffect>()),
            agent, ctx, alternativeCost: alt);

        _stack.Count.Should().Be(1);
        creature.Zone.Should().Be(ZoneType.Stack);
        spell.WasCastFromLibrary.Should().BeTrue();
        spell.WasFreeCast.Should().BeTrue("no mana is spent — the pay-life alt cost is {0} mana");

        // Resolve the spell so the alt cost's OnResolved fires (CR 118.8).
        foreach (var effect in spell.Effects) effect.Execute();
        _alice.LifeTotal.Should().Be(17, "mana value 3 paid as life on resolve");
    }

    [Fact]
    public async Task BolasTopCast_WithAltCost_ButNoGrant_StillRejected()
    {
        // CR 601.3e — the pay-life alt cost is NOT a zone permission on its own.
        // A library-zone cast carrying the alt cost but with no registered
        // cast-from-top grant must still be rejected (an arbitrary library card
        // can't be cast just by attaching the alt cost).
        using var _ = Majik.Core.Rules.LibraryTopPlayPermissions.PushScope();

        var creature = new Creature("Goblin Bear", "{2}{R}", 2, 2) { Owner = _alice, Zone = ZoneType.Library };
        _alice.Zones.Library.AddCard(creature);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);
        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var act = async () => await _flow.CastAsync(_alice, creature,
            SpellDefinition.Vanilla(_ => System.Array.Empty<IEffect>()),
            agent, ctx,
            alternativeCost: new Majik.Core.Costs.PayLifeEqualToManaValueAlternativeCost());

        await act.Should().ThrowAsync<System.InvalidOperationException>()
            .WithMessage("*top of your library*");
        _stack.Count.Should().Be(0);
    }

    [Fact]
    public async Task TopOfLibraryNonland_NotTheTopCard_Throws()
    {
        // CR 601.3e — only the TOP card is a legal cast source even under a
        // grant. A nonland buried below the top is not castable.
        using var _ = Majik.Core.Rules.LibraryTopPlayPermissions.PushScope();

        var topLand = new Sorcery("Filler", "{1}") { Owner = _alice, Zone = ZoneType.Library };
        var buried = new Creature("Goblin Bear", "{R}", 2, 2) { Owner = _alice, Zone = ZoneType.Library };
        _alice.Zones.Library.AddCard(topLand);  // top
        _alice.Zones.Library.AddCard(buried);   // second
        Majik.Core.Rules.LibraryTopPlayPermissions.AddGrant(
            new object(), _alice, Majik.Core.Rules.TopPlayFilter.Any);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);
        var agent = new ScriptedAgent();

        var act = async () => await _flow.CastAsync(_alice, buried,
            SpellDefinition.Vanilla(_ => System.Array.Empty<IEffect>()),
            agent, ctx);

        await act.Should().ThrowAsync<System.InvalidOperationException>()
            .WithMessage("*top of your library*");
    }

    [Fact]
    public async Task XSpell_PromptsForX_PaysGenericOnTop()
    {
        var fireball = new Instant("Fireball", "R") { Owner = _alice, Zone = ZoneType.Hand };
        _alice.Zones.Hand.AddCard(fireball);

        Majik.Core.ValueObjects.ManaCost? promptedCost = null;
        var agent = new InspectingAgent();
        agent.X = 3;
        agent.ManaCallback = c => promptedCost = c;

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

        await _flow.CastAsync(_alice, fireball,
            new SpellDefinition(
                Modes: System.Array.Empty<string>(),
                HasVariableX: true,
                TargetRequests: System.Array.Empty<TargetRequest>(),
                EffectFactory: _ => System.Array.Empty<IEffect>()),
            agent, ctx);

        promptedCost.Should().NotBeNull();
        promptedCost!.Generic.Should().Be(3);
        promptedCost.Red.Should().Be(1);
    }

    private sealed class InspectingAgent : IPlayerAgent
    {
        public int X { get; set; }
        public System.Action<Majik.Core.ValueObjects.ManaCost>? ManaCallback { get; set; }

        public Task<PriorityAction> ChoosePriorityActionAsync(GameContext c, CancellationToken ct = default) => Task.FromResult(PriorityAction.Pass);
        public Task<MulliganDecision> ChooseMulliganAsync(GameContext c, IReadOnlyList<ICard> h, int n, CancellationToken ct = default) => Task.FromResult(MulliganDecision.Keep);
        public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(GameContext c, IReadOnlyList<ICard> h, int n, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ICard>>(System.Array.Empty<ICard>());
        public Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext c, TargetRequest r, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<object>>(System.Array.Empty<object>());
        public Task<int> ChooseXAsync(GameContext c, ICard s, CancellationToken ct = default) => Task.FromResult(X);
        public Task<int> ChooseModeAsync(GameContext c, IReadOnlyList<string> modes, IReadOnlyList<Majik.Core.Cards.BotIntent>? modeIntents = null, CancellationToken ct = default) => Task.FromResult(0);
        public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(GameContext c, IReadOnlyList<ITriggeredAbility> mine, CancellationToken ct = default) => Task.FromResult(mine);
        public Task<ManaPayment> ChooseManaSourcesAsync(GameContext c, Majik.Core.ValueObjects.ManaCost cost, CancellationToken ct = default)
        { ManaCallback?.Invoke(cost); return Task.FromResult(ManaPayment.Empty); }
        public Task<CombatPlan> DeclareAttackersAsync(GameContext c, IReadOnlyList<Permanent> e, CancellationToken ct = default) => Task.FromResult(CombatPlan.None);
        public Task<BlockPlan> DeclareBlockersAsync(GameContext c, IReadOnlyList<Permanent> a, IReadOnlyList<Permanent> e, CancellationToken ct = default) => Task.FromResult(BlockPlan.None);
        public Task<Majik.Core.Keywords.ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => Task.FromResult(new Majik.Core.Keywords.ScryAction.ScryDecision(ToBottom: peeked.ToList(), TopOrder: System.Array.Empty<ICard>()));
        public Task<Majik.Core.Keywords.SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => Task.FromResult(new Majik.Core.Keywords.SurveilAction.SurveilDecision(ToGraveyard: peeked.ToList(), TopOrder: System.Array.Empty<ICard>()));
    }
}
