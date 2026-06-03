using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Xunit;

namespace Majik.Core.Tests.Keywords;

/// <summary>
/// Engine-level tests for <see cref="ClashAction"/> (CR 701.32 — Clash).
///
/// Covers the full keyword-action sequence:
/// - Each clashing player reveals the top card of their library, then puts it
///   on the top OR bottom of that library (their choice, CR 701.32a/b/c).
/// - "A player wins if their card had a greater mana value" (CR 701.32d) —
///   strictly greater; a tie wins for NEITHER player.
/// - An empty library reveals nothing (mana value 0 for that player,
///   CR 701.32a — "If there are no cards in their library, the player reveals
///   no cards").
/// </summary>
public class ClashActionTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public void Dispose()
    {
        AgentRegistry.Clear();
    }

    private static Card Spell(string name, string cost) =>
        new Sorcery(name, cost);

    // -----------------------------------------------------------------------
    // CR 701.32d — initiator's card has greater mana value → initiator wins.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Clash_InitiatorGreaterManaValue_InitiatorWins()
    {
        _alice.Zones.Library.AddCard(Spell("Big", "{4}{G}")); // mv 5, top
        _bob.Zones.Library.AddCard(Spell("Small", "{G}"));    // mv 1, top

        var result = await ClashAction.ClashAsync(
            initiator: _alice,
            other: _bob,
            initiatorAgent: new ScriptedAgent(),
            otherAgent: new ScriptedAgent(),
            game: null);

        result.InitiatorWon.Should().BeTrue(
            "CR 701.32d — Alice's revealed card had the greater mana value");
        result.InitiatorManaValue.Should().Be(5);
        result.OtherManaValue.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // CR 701.32d — equal mana value → NEITHER wins (strictly greater).
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Clash_EqualManaValue_InitiatorDoesNotWin()
    {
        _alice.Zones.Library.AddCard(Spell("A", "{1}{G}")); // mv 2
        _bob.Zones.Library.AddCard(Spell("B", "{1}{R}"));   // mv 2

        var result = await ClashAction.ClashAsync(
            initiator: _alice,
            other: _bob,
            initiatorAgent: new ScriptedAgent(),
            otherAgent: new ScriptedAgent(),
            game: null);

        result.InitiatorWon.Should().BeFalse(
            "CR 701.32d — a tie is not 'greater'; neither player wins");
    }

    // -----------------------------------------------------------------------
    // CR 701.32c — each player chooses top or bottom independently.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Clash_KeepOnTop_LeavesRevealedCardOnTop()
    {
        var top = Spell("Top", "{2}{G}");
        var below = Spell("Below", "{G}");
        _alice.Zones.Library.AddCard(top);   // top
        _alice.Zones.Library.AddCard(below); // second
        _bob.Zones.Library.AddCard(Spell("BobTop", "{G}"));

        var aliceAgent = new ScriptedAgent();
        aliceAgent.QueueClashTopOrBottom(keepOnTop: true);

        await ClashAction.ClashAsync(
            initiator: _alice,
            other: _bob,
            initiatorAgent: aliceAgent,
            otherAgent: new ScriptedAgent(),
            game: null);

        _alice.Zones.Library.GetCards().First().Should().Be(top,
            "CR 701.32c — Alice chose to keep her revealed card on top");
    }

    [Fact]
    public async Task Clash_PutOnBottom_MovesRevealedCardToBottom()
    {
        var top = Spell("Top", "{2}{G}");
        var below = Spell("Below", "{G}");
        _alice.Zones.Library.AddCard(top);   // top
        _alice.Zones.Library.AddCard(below); // second
        _bob.Zones.Library.AddCard(Spell("BobTop", "{G}"));

        var aliceAgent = new ScriptedAgent();
        aliceAgent.QueueClashTopOrBottom(keepOnTop: false);

        await ClashAction.ClashAsync(
            initiator: _alice,
            other: _bob,
            initiatorAgent: aliceAgent,
            otherAgent: new ScriptedAgent(),
            game: null);

        var lib = _alice.Zones.Library.GetCards().ToList();
        lib.First().Should().Be(below,
            "CR 701.32c — Alice put her revealed card on the bottom, so the " +
            "next card is now on top");
        lib.Last().Should().Be(top,
            "CR 701.32c — the revealed card is now on the bottom of the library");
    }

    // -----------------------------------------------------------------------
    // CR 701.32a — empty library reveals nothing (mana value 0).
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Clash_OtherPlayerEmptyLibrary_InitiatorWinsWithAnyCard()
    {
        _alice.Zones.Library.AddCard(Spell("A", "{G}")); // mv 1
        // Bob's library is empty → reveals nothing, mana value 0.

        var result = await ClashAction.ClashAsync(
            initiator: _alice,
            other: _bob,
            initiatorAgent: new ScriptedAgent(),
            otherAgent: new ScriptedAgent(),
            game: null);

        result.OtherManaValue.Should().Be(0,
            "CR 701.32a — an empty library reveals no card (mana value 0)");
        result.InitiatorWon.Should().BeTrue(
            "CR 701.32d — Alice's mv 1 beats Bob's empty-library mv 0");
    }

    [Fact]
    public async Task Clash_BothEmptyLibraries_InitiatorDoesNotWin()
    {
        var result = await ClashAction.ClashAsync(
            initiator: _alice,
            other: _bob,
            initiatorAgent: new ScriptedAgent(),
            otherAgent: new ScriptedAgent(),
            game: null);

        result.InitiatorManaValue.Should().Be(0);
        result.OtherManaValue.Should().Be(0);
        result.InitiatorWon.Should().BeFalse(
            "CR 701.32d — 0 is not greater than 0; neither player wins");
    }
}
