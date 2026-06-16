using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Definitions;

/// <summary>
/// Declarative SPELL-effect path for the <c>look_top_n_one_to_hand_rest_to_graveyard</c>
/// verb — "Look at the top N cards of your library. Put one of them into your
/// hand and the other(s) into your graveyard." (Nagging Thoughts / Sight Beyond
/// Sight). The verb is the JSON-expressible serialization of the Impulse-style
/// dig that the <see cref="Majik.Core.CardData.SpellTemplates.Templates.Library.LookAtTopPutOneInHandTemplate"/>
/// regex template already routes through, lifted onto the shared
/// <see cref="Majik.Core.Zones.RevealAndChoose"/> primitive (same look-and-pick
/// sink Impulse / Anticipate / Sleight of Hand use).
///
/// <para>
/// Pins: (1) the mandatory pick goes to the HAND and the rest to the GRAVEYARD,
/// resolved through the production <see cref="SpellCastFlow"/> →
/// <see cref="Majik.Core.Services.StackResolver"/> path; (2) the agent's choice
/// is honoured (a real choice, not the auto-pick-first default); (3) the
/// no-agent fallback is deterministic (top card to hand); (4) a short / empty
/// library is a clean no-op. CR 120.6 (reveal) + CR 116.1b (player choice) + CR
/// 701.13 (graveyard routing).
/// </para>
/// </summary>
public class JsonLookTopNOneToHandRestToGraveyardEffectTests : IDisposable
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly Majik.Core.Services.ZoneService _zones;
    private readonly Majik.Core.Services.StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public JsonLookTopNOneToHandRestToGraveyardEffectTests()
    {
        AgentRegistry.Clear();
        Majik.Core.Services.ZoneServiceRegistry.Clear();
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new Majik.Core.Services.ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new Majik.Core.Services.StackResolver(_bus, _zones);
    }

    public void Dispose()
    {
        AgentRegistry.Clear();
        Majik.Core.Services.ZoneServiceRegistry.Clear();
    }

    private GameContext NewContext() =>
        new(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

    private void SeedLibrary(params string[] topToBottom)
    {
        foreach (var name in topToBottom)
        {
            var c = new Land(name) { Owner = _alice, Zone = ZoneType.Library };
            _alice.Zones.Library.AddCard(c);
        }
    }

    private Sorcery CastSpell(string name, string manaCost)
    {
        var card = new Sorcery(name, manaCost);
        card.SetOwner(_alice);
        card.SetController(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);
        return card;
    }

    private async Task CastAndResolve(Sorcery card, SpellDefinition def, ScriptedAgent agent)
    {
        agent.QueueTargets(System.Array.Empty<object>());
        agent.QueueMana(ManaPayment.Empty);
        AgentRegistry.Set(_alice, agent);
        await _flow.CastAsync(_alice, card, def, agent, NewContext(), alternativeCost: null);
        _resolver.ResolveTop(_stack);
    }

    private IReadOnlyList<string> HandNames() =>
        _alice.Zones.Hand.GetCards().Select(c => c.Name).ToList();

    // Graveyard names EXCLUDING the resolved spell card itself (a Sorcery moves
    // to its owner's graveyard on resolution, CR 608.2m) — so the assertions
    // see only the looked-at library cards the verb routed there.
    private IReadOnlyList<string> GraveyardNames() =>
        _alice.Zones.Graveyard.GetCards()
            .Where(c => c.Name != "Nagging Thoughts")
            .Select(c => c.Name)
            .ToList();

    private static SpellDefinition Def(int amount = 2) =>
        CardDefRuntime.BuildSpellDefinitionFromEffects(
            "Nagging Thoughts",
            new EffectDefinition[]
            {
                new LookTopNOneToHandRestToGraveyardEffectDef { Amount = amount },
            });

    [Fact]
    public void Verb_DeclaresNoTargets()
    {
        Def().TargetRequests.Should().BeEmpty(
            "look-top-N is an untargeted self-library dig");
    }

    [Fact]
    public async Task AgentPicksSpecificCard_GoesToHand_OtherToGraveyard()
    {
        // Top two are [First, Second]; the agent picks SECOND for the hand,
        // proving a real choice. The unchosen First goes to the graveyard.
        SeedLibrary("First", "Second", "Bottom");
        var second = _alice.Zones.Library.GetCards().First(c => c.Name == "Second");

        var agent = new ScriptedAgent();
        agent.QueueFromRevealed(second);

        var card = CastSpell("Nagging Thoughts", "{1}{U}");
        await CastAndResolve(card, def: Def(), agent);

        HandNames().Should().Contain("Second", "the agent's chosen card goes to hand");
        GraveyardNames().Should().Contain("First",
            "the other looked-at card goes to the graveyard");
        HandNames().Should().NotContain("First");
        GraveyardNames().Should().NotContain("Second");
        // The third card was never looked at — it stays on the library.
        _alice.Zones.Library.GetCards().Select(c => c.Name).Should().ContainSingle()
            .Which.Should().Be("Bottom");
    }

    [Fact]
    public async Task NoAgent_DeterministicFallback_TopToHand_RestToGraveyard()
    {
        // No agent registered → the shared RevealAndChoose fallback auto-picks
        // the FIRST (top) looked-at card for the hand.
        SeedLibrary("Top", "Next");

        var card = CastSpell("Nagging Thoughts", "{1}{U}");
        // Pass a bare scripted agent that does NOT queue a reveal pick; clear
        // the registry first so the verb hits the no-agent deterministic path.
        var agent = new ScriptedAgent();
        agent.QueueTargets(System.Array.Empty<object>());
        agent.QueueMana(ManaPayment.Empty);
        await _flow.CastAsync(_alice, card, Def(), agent, NewContext(), alternativeCost: null);
        // Remove any agent so resolution uses the deterministic first-pick path.
        AgentRegistry.Remove(_alice);
        _resolver.ResolveTop(_stack);

        HandNames().Should().Contain("Top", "the top looked-at card goes to hand");
        GraveyardNames().Should().Contain("Next", "the other goes to the graveyard");
    }

    [Fact]
    public async Task ShortLibrary_OneCard_GoesToHand_NothingToGraveyard()
    {
        SeedLibrary("Only");

        var agent = new ScriptedAgent();
        agent.QueueFromRevealed(_alice.Zones.Library.GetCards().First());

        var card = CastSpell("Nagging Thoughts", "{1}{U}");
        await CastAndResolve(card, Def(), agent);

        HandNames().Should().Contain("Only");
        GraveyardNames().Should().BeEmpty(
            "a one-card library has no 'other' to send to the graveyard");
    }

    [Fact]
    public async Task EmptyLibrary_IsCleanNoOp()
    {
        var card = CastSpell("Nagging Thoughts", "{1}{U}");
        var act = async () => await CastAndResolve(card, Def(), new ScriptedAgent());

        await act.Should().NotThrowAsync("an empty-library look is a no-op");
        HandNames().Should().BeEmpty();
        GraveyardNames().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeFalse(
            "the verb never draws, so no draw-from-empty SBA fires");
    }
}
