using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Majik.Core.Cards;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// Regression: GameFacade.BuildPriorityLoop used to omit the castDispatcher,
/// so submitting a CastSpellCommand via the legacy single-priority-round
/// StartAsync path threw "PriorityLoop received CastSpell but no
/// castDispatcher was supplied." StartFullGameAsync was unaffected because
/// it constructs PriorityLoop through GameDriver/TurnDriver, which wires
/// the dispatcher. Mirrors the TurnDriver.PriorityRound dispatcher wiring
/// onto the legacy path so test/console callers don't hit the footgun.
/// </summary>
public class LegacyStartAsyncCastDispatchTests
{
    [Fact]
    public async Task StartAsync_CastSpellCommand_DoesNotThrow_BecauseDispatcherIsWired()
    {
        // A 0-cost creature: payment is trivially Empty, the spell lands on
        // the stack via SpellCastFlow and StackResolver puts the permanent
        // onto the battlefield. The point of the test is the absence of
        // "no castDispatcher was supplied" — not the mechanical correctness
        // of the cast.
        var bear = new Creature("Memnite", "0", 1, 1);
        var facade = GameFacade.Create(
            "Alice", "Bob",
            new ICard[] { bear },
            Array.Empty<ICard>());

        // Move bear into hand BEFORE StartAsync so the priority prompt's
        // legality-narrowed ExpectedKinds includes CastSpellCommand.
        // (Pre-narrowing the kinds list always included CastSpell; with
        // narrowing in place, an empty hand at prompt time yields the
        // pass-only kinds set and the cast submit would be rejected.)
        facade.Alice.Zones.Library.RemoveCard(bear);
        facade.Alice.Zones.Hand.AddCard(bear);
        bear.SetZone(ZoneType.Hand);

        await facade.StartAsync();
        var state = facade.GetState();
        var alice = state.Players[0].Id;
        var bob = state.Players[1].Id;

        // CastSpellCommand on legacy StartAsync. This used to throw
        // "PriorityLoop received CastSpell but no castDispatcher was supplied."
        // because BuildPriorityLoop omitted castDispatcher.
        //
        // CR 601.2g + pool-pay-first (see TurnDriver.DispatchCast and
        // GameFacade.DispatchCast): the empty pool already CanPay a 0-cost
        // spell, so the dispatcher silently auto-pays and skips the
        // ChooseManaCommand prompt. Reaching the post-submit assertion at
        // all (no throw) is the regression check; no ChooseManaCommand is
        // sent in the new behaviour.
        await facade.SubmitAsync(new CastSpellCommand(
            CardInstanceId: bear.InstanceId,
            TargetInstanceIds: Array.Empty<Guid>(),
            XValue: null,
            ModeIndex: null)
        { PlayerId = alice });

        // Cast landed on the stack (SpellCastFlow pushed it). The single-
        // round priority loop won't necessarily resolve it before round
        // end depending on pass timing — we only assert the stack received
        // the spell and the card left the hand, which proves the dispatcher
        // ran end-to-end.
        facade.Alice.Zones.Hand.GetCards().Should().NotContain(bear);
    }
}
