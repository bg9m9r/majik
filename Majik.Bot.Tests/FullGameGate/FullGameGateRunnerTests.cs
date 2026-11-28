using FluentAssertions;
using Majik.Core.Api;
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
}
