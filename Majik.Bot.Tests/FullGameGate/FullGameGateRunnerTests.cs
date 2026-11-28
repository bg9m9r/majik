using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Zones;
using Majik.Core.Events;
using Xunit;

namespace Majik.Bot.Tests.FullGameGate;

/// <summary>
/// The gate's own plumbing, run in the ordinary test loop (not the
/// FullGameGate category): a throwing event handler in a gate game is a
/// failure signature, and the gate restores the process-wide handler-error
/// hook when it is done.
/// </summary>
public class FullGameGateRunnerTests
{
    private static GateGame AnyGame()
    {
        var a = Majik.Bot.Decks.BotDeckCatalog.Archetypes.OrderBy(x => x, StringComparer.Ordinal).First();
        return new GateGame(a, a, 0);
    }

    [Fact]
    public async Task AThrowingEventHandler_IsAHandlerFaultSignature_AndTurnsTheGateRed()
    {
        GateGameResult result;
        using (FullGameGateRunner.CaptureHandlerFaults())
        {
            result = await FullGameGateRunner.RunAsync(AnyGame(), maxTurns: 2, TimeSpan.FromSeconds(20),
                configure: f => f.EventBus.Subscribe<TurnStartedEvent>(
                    _ => throw new InvalidOperationException("injected handler fault")));
        }

        var sig = result.Signatures.Keys.Should().ContainSingle(k => k.StartsWith("HandlerFault|")).Subject;
        sig.Should().StartWith("HandlerFault|InvalidOperationException|Majik.Core.",
            "kind | exception type | top Majik frame (the throwing test lambda is skipped)");
        result.Signatures[sig].Should().Contain("TurnStartedEvent").And.Contain("injected handler fault");

        var verdict = FullGameGateRunner.Evaluate(new[] { result }, new Dictionary<string, bool>());
        verdict.Unlisted.Should().ContainSingle(u => u.StartsWith(sig), "an unlisted signature fails the gate");
    }

    [Fact]
    public void CaptureHandlerFaults_RestoresThePreviousHook()
    {
        var previous = GameFacade.OnEventHandlerError;
        Action<Exception, Majik.Core.Events.GameEvent> mine = (_, _) => { };
        GameFacade.OnEventHandlerError = mine;
        try
        {
            using (FullGameGateRunner.CaptureHandlerFaults())
            {
                GameFacade.OnEventHandlerError.Should().NotBeSameAs(mine, "the gate installs its own hook");
                using (FullGameGateRunner.CaptureHandlerFaults()) { }
                GameFacade.OnEventHandlerError.Should().NotBeSameAs(mine, "a nested capture keeps the hook until the outer one ends");
            }
            GameFacade.OnEventHandlerError.Should().BeSameAs(mine);
        }
        finally
        {
            GameFacade.OnEventHandlerError = previous;
        }
    }

    [Fact]
    public void AZoneDesync_SignatureNamesTheCardAndBothZones_InAFixedOrder()
    {
        // CR 400.1: a card in two zones at once. The pair is part of the
        // signature (Stack+Battlefield and Stack+Graveyard are different bugs)
        // and is ordered by name so the scan order of the zones cannot change it.
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var opal = new Artifact("Mox Opal", "{0}");
        alice.Zones.Battlefield.AddCard(opal);
        bob.Zones.Stack.AddCard(opal);

        var dup = ZoneIntegrity.FindDuplicates(new[] { alice, bob }).Should().ContainSingle().Subject;
        dup.Signature.Should().Be("ZoneIntegrity|Mox Opal|Battlefield+Stack");
        new ZoneIntegrity.Duplicate(opal, ZoneType.Stack, ZoneType.Battlefield).Signature
            .Should().Be(dup.Signature);
    }

    [Theory]
    [InlineData(10_001, 3, "Draw|LoopSafetyNet|ActionsPerStep")]
    [InlineData(0, 1_001, "Draw|LoopSafetyNet|StackDepth")]
    [ExpectsContainedFault] // the trip is reported as a contained fault; the gate accounts for it
    public async Task ALoopSafetyNetTrip_IsADrawSignature_NamingTheLimit(int actions, int depth, string expected)
    {
        // The engine ends a runaway game as a draw (CR 104.4b) and publishes
        // LoopSafetyNetTrippedEvent (actions = 0 for a stack-depth trip).
        // GameFacade also reports it as a contained fault; the gate keeps only
        // the draw signature, since the exception is built, never thrown, and
        // would read "<no engine frame>".
        var published = false;
        var result = await FullGameGateRunner.RunAsync(AnyGame(), maxTurns: 2, TimeSpan.FromSeconds(20),
            configure: f => f.EventBus.Subscribe<TurnStartedEvent>(_ =>
            {
                if (published) return;
                published = true;
                f.EventBus.Publish(new Majik.Core.Game.LoopSafetyNetTrippedEvent("tripped (test)", actions, depth));
            }));

        result.Signatures.Keys.Should().Equal(expected);
        result.Signatures[expected].Should().Contain("tripped (test)");
    }
}
