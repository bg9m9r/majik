using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests the multiplayer-choose-opponent paydown for "Clash with an opponent"
/// (CR 701.32 / CR 601.2c — the caster chooses which opponent to clash with).
///
/// Recross the Paths' clause-2 opponent pick used to hardcode the FIRST
/// opponent (<c>AllPlayers.FirstOrDefault(p =&gt; p != caster)</c>). After the
/// paydown the pick routes through the caster's
/// <see cref="IPlayerAgent.ChoosePlayerAsync"/> over the live
/// <see cref="ContextOpponents"/> enumeration, so in a 3-player game the agent
/// can clash with the SECOND opponent rather than always the first.
/// </summary>
public class RecrossThePathsOpponentChoiceTests : IDisposable
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly ZoneService _zones;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly Player _carol = new("Carol", 20);

    public RecrossThePathsOpponentChoiceTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        ZoneServiceRegistry.Set(_alice, _zones);
        ZoneServiceRegistry.Set(_bob, _zones);
        ZoneServiceRegistry.Set(_carol, _zones);
    }

    public void Dispose()
    {
        AgentRegistry.Clear();
        ZoneServiceRegistry.Clear();
    }

    private GameContext Game() =>
        new(_alice, new[] { _alice, _bob, _carol }, _alice, 1, StepStateType.PreCombatMain, _stack);

    [Fact]
    public async Task Resolve_ClashWithChosenSecondOpponent_NotTheFirst()
    {
        // Alice (caster) has no land in library so clause-1 just bottoms cards;
        // give her a clash card so the clash comparison runs.
        _alice.Zones.Library.AddCard(new Instant("Alice Clash", "{2}{U}") { Owner = _alice }); // mv 3

        // Each opponent has a 2-card library so a top→bottom clash move is
        // observable (the top card becomes the second card).
        var bobTop = new Instant("Bob Top", "{U}") { Owner = _bob };       // mv 1
        var bobSecond = new Instant("Bob Second", "{U}") { Owner = _bob };
        _bob.Zones.Library.AddCard(bobTop);     // top
        _bob.Zones.Library.AddCard(bobSecond);

        var carolTop = new Instant("Carol Top", "{U}") { Owner = _carol }; // mv 1
        var carolSecond = new Instant("Carol Second", "{U}") { Owner = _carol };
        _carol.Zones.Library.AddCard(carolTop);     // top
        _carol.Zones.Library.AddCard(carolSecond);

        // The caster's agent CHOOSES the SECOND opponent (Carol) — index 1 in
        // the ContextOpponents enumeration [Bob, Carol]. Carol then puts her
        // revealed card on the bottom (QueueClashTopOrBottom(false)); Bob is
        // never consulted, so his top card stays put.
        var aliceAgent = new ScriptedAgent();
        aliceAgent.QueueChoice(candidates =>
        {
            // PickOne over the opponent pool — pick Carol (the second opponent).
            var carol = candidates.OfType<Player>().FirstOrDefault(p => ReferenceEquals(p, _carol));
            return carol is null ? System.Array.Empty<object>() : new object[] { carol };
        });

        var carolAgent = new ScriptedAgent();
        carolAgent.QueueClashTopOrBottom(false); // put Carol's revealed card on the bottom
        AgentRegistry.Set(_carol, carolAgent);

        var bobAgent = new ScriptedAgent();
        AgentRegistry.Set(_bob, bobAgent);

        var effects = RecrossThePathsFactory.BuildResolveEffect(_alice, card: null);
        var ctx = ResolutionContext.For(_alice, aliceAgent, Game(), chosenTargets: null);
        foreach (var e in effects)
        {
            await e.ExecuteAsync(ctx);
        }

        // Carol was the clash opponent: she chose BOTTOM, so her revealed top
        // card moved to the bottom of her library (CR 701.32b).
        _carol.Zones.Library.GetCards().First().Should().BeSameAs(carolSecond,
            "the clash ran against the CHOSEN opponent (Carol), who put her " +
            "revealed card on the bottom — so her library top changed");

        // Bob was NOT chosen — his library is untouched; his top card is still
        // on top (the first-opponent shortcut would have clashed with Bob).
        _bob.Zones.Library.GetCards().First().Should().BeSameAs(bobTop,
            "Bob was not the chosen opponent, so his library was never touched");
    }
}
