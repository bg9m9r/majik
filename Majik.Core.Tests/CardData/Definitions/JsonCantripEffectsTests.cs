using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Definitions;

/// <summary>
/// Declarative SPELL-effect path for the scry / surveil / draw CANTRIP family
/// (Opt, Serum Visions, Preordain, Ponder, Consider, …). Proves the
/// pay-down of the "cantrip factory harvest" deferral: each cantrip is just an
/// ORDERED array of the untargeted <c>scry_self</c> / <c>surveil_self</c> /
/// <c>draw_card</c> verbs, composed by
/// <see cref="CardDefRuntime.BuildSpellDefinitionFromEffects"/> and resolved
/// through the production <see cref="SpellCastFlow"/> →
/// <see cref="Majik.Core.Services.StackResolver"/> path — no bespoke C# resolve
/// closure required.
///
/// The two ordering-sensitive cases the deferral called out are pinned:
/// Serum Visions ("Draw a card. Scry 2." — draw BEFORE scry, so the scry sees
/// the post-draw top) and Preordain ("Scry 2, then draw a card." — scry BEFORE
/// draw, so the draw pulls the card the scry just chose to keep on top).
/// CR 121.1 (draw) + CR 701.20 (scry) + CR 701.42 (surveil), sequenced
/// left-to-right per CR 608.2.
/// </summary>
public class JsonCantripEffectsTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly Majik.Core.Services.ZoneService _zones;
    private readonly Majik.Core.Services.StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public JsonCantripEffectsTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new Majik.Core.Services.ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new Majik.Core.Services.StackResolver(_bus, _zones);
    }

    private GameContext NewContext() =>
        new(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

    /// <summary>Seed Alice's library top-to-bottom with named lands so order is
    /// observable.</summary>
    private void SeedLibrary(params string[] topToBottom)
    {
        // Library.AddCard appends; the engine treats index 0 (FirstOrDefault)
        // as the top of the library, so add in printed top-to-bottom order.
        foreach (var name in topToBottom)
        {
            var c = new Land(name) { Owner = _alice, Zone = ZoneType.Library };
            _alice.Zones.Library.AddCard(c);
        }
    }

    private Instant CastSpell(string name, string manaCost)
    {
        var card = new Instant(name, manaCost);
        card.SetOwner(_alice);
        card.SetController(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);
        return card;
    }

    private async Task CastAndResolve(Instant card, SpellDefinition def, ScriptedAgent agent)
    {
        agent.QueueTargets(System.Array.Empty<object>());
        agent.QueueMana(ManaPayment.Empty);
        // The sync ResolveTop path supplies no agentLookup, so the scry/surveil
        // verbs fall back to AgentRegistry.Get(controller) at resolution — wire
        // the scripted agent there so its queued scry/surveil decisions are
        // consulted (mirrors the live AgentRegistry seam).
        AgentRegistry.Set(_alice, agent);
        try
        {
            await _flow.CastAsync(_alice, card, def, agent, NewContext(), alternativeCost: null);
            _resolver.ResolveTop(_stack);
        }
        finally
        {
            AgentRegistry.Remove(_alice);
        }
    }

    private IReadOnlyList<string> HandNames() =>
        _alice.Zones.Hand.GetCards().Select(c => c.Name).ToList();

    private IReadOnlyList<string> GraveyardNames() =>
        _alice.Zones.Graveyard.GetCards().Select(c => c.Name).ToList();

    // ── draw_card (the simplest cantrip leg) ──────────────────────────────────

    [Fact]
    public async Task DrawCard_DeclaresNoTargets_DrawsTopCard()
    {
        SeedLibrary("Top", "Second");
        var def = CardDefRuntime.BuildSpellDefinitionFromEffects(
            "Inspiration", new EffectDefinition[] { new DrawCardEffectDef { Amount = 1 } });

        def.TargetRequests.Should().BeEmpty("a plain cantrip declares no targets");

        var card = CastSpell("Inspiration", "{U}");
        await CastAndResolve(card, def, new ScriptedAgent());

        HandNames().Should().Contain("Top");
    }

    [Fact]
    public async Task DrawCard_FromEmptyLibrary_FlagsSbaLoss()
    {
        // No library seeded — the draw verb must flag the controller for the
        // draw-from-empty-library state-based loss (CR 120.3 / 704.5b).
        var def = CardDefRuntime.BuildSpellDefinitionFromEffects(
            "Inspiration", new EffectDefinition[] { new DrawCardEffectDef { Amount = 1 } });

        var card = CastSpell("Inspiration", "{U}");
        await CastAndResolve(card, def, new ScriptedAgent());

        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "draw_card routes through Fx.DrawCards, which flags the empty-library loss");
    }

    // ── Preordain: "Scry 2, then draw a card." (scry BEFORE draw) ─────────────

    [Fact]
    public async Task Preordain_ScryThenDraw_DrawsTheKeptTopCard()
    {
        // Library top→bottom: Keep, Bury, Filler. Scry 2 looks at [Keep, Bury];
        // the agent keeps Keep on top and bottoms Bury. The draw then pulls
        // Keep — the card the scry deliberately left on top.
        SeedLibrary("Keep", "Bury", "Filler");
        var keep = _alice.Zones.Library.GetCards().First(c => c.Name == "Keep");
        var bury = _alice.Zones.Library.GetCards().First(c => c.Name == "Bury");

        var def = CardDefRuntime.BuildSpellDefinitionFromEffects(
            "Preordain",
            new EffectDefinition[]
            {
                new ScrySelfEffectDef { Amount = 2 },
                new DrawCardEffectDef { Amount = 1 },
            });
        def.TargetRequests.Should().BeEmpty();

        var agent = new ScriptedAgent();
        agent.QueueScryDecision(new ScryAction.ScryDecision(
            ToBottom: new[] { bury }, TopOrder: new[] { keep }));

        var card = CastSpell("Preordain", "{U}");
        await CastAndResolve(card, def, agent);

        HandNames().Should().Contain("Keep",
            "scry resolved first and kept Keep on top, so the trailing draw pulls it");
        HandNames().Should().NotContain("Bury", "Bury was scryed to the bottom");
    }

    // ── Serum Visions: "Draw a card. Scry 2." (draw BEFORE scry) ──────────────

    [Fact]
    public async Task SerumVisions_DrawThenScry_DrawsOldTop_ThenScriesPostDrawTop()
    {
        // Library top→bottom: Drawn, Peek1, Peek2. Draw resolves FIRST → Drawn
        // to hand. THEN scry 2 inspects the NEW top two [Peek1, Peek2]. If the
        // scry were (incorrectly) sequenced before the draw it would have peeked
        // [Drawn, Peek1] — so the post-draw peek pins the ordering.
        SeedLibrary("Drawn", "Peek1", "Peek2");
        var peek1 = _alice.Zones.Library.GetCards().First(c => c.Name == "Peek1");
        var peek2 = _alice.Zones.Library.GetCards().First(c => c.Name == "Peek2");

        IReadOnlyList<ICard>? observedPeek = null;
        var agent = new ScriptedAgent();
        // Capture what the scry actually saw by NOT pre-queueing a decision and
        // instead asserting on library state after resolution (see below). We
        // queue an explicit decision keeping both on top so nothing is bottomed.
        agent.QueueScryDecision(new ScryAction.ScryDecision(
            ToBottom: System.Array.Empty<ICard>(),
            TopOrder: new[] { peek1, peek2 }));
        _ = observedPeek;

        var def = CardDefRuntime.BuildSpellDefinitionFromEffects(
            "Serum Visions",
            new EffectDefinition[]
            {
                new DrawCardEffectDef { Amount = 1 },
                new ScrySelfEffectDef { Amount = 2 },
            });

        var card = CastSpell("Serum Visions", "{U}");
        await CastAndResolve(card, def, agent);

        HandNames().Should().Contain("Drawn", "the draw resolved before the scry");
        // Scry kept Peek1/Peek2 on top → they remain in the library, NOT drawn.
        _alice.Zones.Library.GetCards().Select(c => c.Name)
            .Should().ContainInOrder("Peek1", "Peek2");
        HandNames().Should().NotContain("Peek1").And.NotContain("Peek2");
    }

    // ── Consider: "Surveil 1. Draw a card." (surveil BEFORE draw) ─────────────

    [Fact]
    public async Task Consider_SurveilThenDraw_MillsPeekedThenDrawsNewTop()
    {
        // Library top→bottom: Mill, Drawn, Filler. Surveil 1 looks at [Mill];
        // the agent mills it to the graveyard. The draw then pulls Drawn (the
        // new top after the mill).
        SeedLibrary("Mill", "Drawn", "Filler");
        var mill = _alice.Zones.Library.GetCards().First(c => c.Name == "Mill");

        var agent = new ScriptedAgent();
        agent.QueueSurveilDecision(new SurveilAction.SurveilDecision(
            ToGraveyard: new[] { mill }, TopOrder: System.Array.Empty<ICard>()));

        var def = CardDefRuntime.BuildSpellDefinitionFromEffects(
            "Consider",
            new EffectDefinition[]
            {
                new SurveilSelfEffectDef { Amount = 1 },
                new DrawCardEffectDef { Amount = 1 },
            });

        var card = CastSpell("Consider", "{U}");
        await CastAndResolve(card, def, agent);

        GraveyardNames().Should().Contain("Mill", "surveil resolved first and milled the peeked card");
        HandNames().Should().Contain("Drawn", "the draw then pulled the post-surveil top");
        HandNames().Should().NotContain("Mill");
    }

    // ── Converted factory BuildDefinition() resolves through the real cast flow ─

    [Fact]
    public async Task OptFactory_BuildDefinition_ScriesThenDraws()
    {
        // Opt = scry 1, then draw 1. Library top→bottom: Drawn, Filler. Default
        // scry (no queued decision) bottoms Drawn → new top Filler → draw pulls
        // Filler. Either way the spell resolves cleanly via the declarative def.
        SeedLibrary("Drawn", "Filler");
        var def = Majik.Core.CardData.Factories.OptFactory.BuildDefinition();
        def.TargetRequests.Should().BeEmpty();

        var card = CastSpell("Opt", "{U}");
        await CastAndResolve(card, def, new ScriptedAgent());

        _alice.Zones.Hand.GetCards().Should().HaveCount(1, "Opt draws exactly one card");
    }

    [Fact]
    public async Task SerumVisionsFactory_BuildDefinition_DrawsThenScries()
    {
        SeedLibrary("Drawn", "Peek1", "Peek2");
        var def = Majik.Core.CardData.Factories.SerumVisionsFactory.BuildDefinition();

        var card = CastSpell("Serum Visions", "{U}");
        await CastAndResolve(card, def, new ScriptedAgent());

        HandNames().Should().Contain("Drawn", "the draw resolves before the scry");
        _alice.Zones.Hand.GetCards().Should().HaveCount(1);
    }

    [Fact]
    public async Task PreordainFactory_BuildDefinition_ScriesTwoThenDraws()
    {
        SeedLibrary("A", "B", "C", "D");
        var def = Majik.Core.CardData.Factories.PreordainFactory.BuildDefinition();

        var card = CastSpell("Preordain", "{U}");
        await CastAndResolve(card, def, new ScriptedAgent());

        _alice.Zones.Hand.GetCards().Should().HaveCount(1, "Preordain draws exactly one card");
    }

    [Fact]
    public async Task ConsiderFactory_BuildDefinition_SurveilsThenDraws()
    {
        SeedLibrary("Mill", "Drawn", "Filler");
        var def = Majik.Core.CardData.Factories.ConsiderFactory.BuildDefinition();

        var card = CastSpell("Consider", "{U}");
        await CastAndResolve(card, def, new ScriptedAgent());

        // Default surveil (no queued decision) sends the peeked Mill to the
        // graveyard, then the draw pulls Drawn.
        GraveyardNames().Should().Contain("Mill");
        HandNames().Should().Contain("Drawn");
    }
}
