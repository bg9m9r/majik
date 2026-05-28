using System.Text.Json;
using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Majik.Core.Api.Dtos;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// Tests for the LibraryView field added to the library-search prompt
/// payload (CR 701.19a). The portal shows the full library with eligible
/// cards highlighted and ineligible cards muted so it looks like flipping
/// through the deck, rather than just a flat list of candidates.
/// </summary>
public class PromptLibraryViewTests
{
    private readonly Player _alice = new("Alice", 20);

    // ── RemoteAgent / PromptPayload unit tests ───────────────────────────

    [Fact]
    public async Task LibrarySearch_60CardLibrary_4Candidates_LibraryViewCount60()
    {
        // CR 701.19a — the portal needs the full library snapshot (all 60
        // cards) to render the deck-flip view, with the 4 eligible candidates
        // highlighted. LibraryView must contain every card in the library at
        // the time the prompt fires, not just the candidates.
        var library = BuildLibrary(_alice, 60);
        var candidates = library.Take(4).ToList<ICard>();
        var agent = new RemoteAgent(_alice);

        _ = agent.ChooseLibraryPickAsync(ctx: null, candidates, "basic land card");

        agent.PendingPayload.Should().NotBeNull();
        agent.PendingPayload!.LibraryView.Should().NotBeNull();
        agent.PendingPayload!.LibraryView!.Count.Should().Be(60);
        agent.PendingPayload.Candidates.Should().HaveCount(4);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task LibrarySearch_CandidatesAreSubsetOfLibraryView()
    {
        // Every Candidates.InstanceId must appear in LibraryView —
        // the portal uses this containment to decide which cards to highlight.
        var library = BuildLibrary(_alice, 20);
        var candidates = library.Skip(5).Take(3).ToList<ICard>();
        var agent = new RemoteAgent(_alice);

        _ = agent.ChooseLibraryPickAsync(ctx: null, candidates, "creature card");

        var payload = agent.PendingPayload!;
        var libraryIds = payload.LibraryView!.Select(c => c.InstanceId).ToHashSet();
        foreach (var candidate in payload.Candidates!)
        {
            libraryIds.Should().Contain(candidate.InstanceId,
                "every candidate must be in the full library view");
        }
        await Task.CompletedTask;
    }

    [Fact]
    public async Task LibrarySearch_LibraryViewOrderMatchesLibraryGetCards()
    {
        // The portal may display the library top-to-bottom; order must
        // match exactly. CR 701.19a allows the searching player to see
        // the order of their own library during a search.
        var library = BuildLibrary(_alice, 10);
        var candidates = library.Take(2).ToList<ICard>();
        var agent = new RemoteAgent(_alice);

        _ = agent.ChooseLibraryPickAsync(ctx: null, candidates, "land card");

        var expectedOrder = _alice.Zones.Library.GetCards()
            .Select(c => c.InstanceId)
            .ToList();
        var actualOrder = agent.PendingPayload!.LibraryView!
            .Select(c => c.InstanceId)
            .ToList();
        actualOrder.Should().Equal(expectedOrder,
            "LibraryView must preserve the exact top-to-bottom library order");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task NonLibrarySearchPrompt_Mulligan_LibraryViewIsNull()
    {
        // Non-library-search prompts must not set LibraryView — the field
        // is null/absent for every prompt kind that doesn't use it, keeping
        // backwards compatibility with existing portal consumers.
        var agent = new RemoteAgent(_alice);

        _ = agent.ChooseMulliganAsync(NewContext(), hand: Array.Empty<ICard>(), mulligansTaken: 0);

        // Mulligan has no PendingPayload at all (null) — that is fine.
        // If it were somehow set, LibraryView must still be null.
        if (agent.PendingPayload != null)
        {
            agent.PendingPayload.LibraryView.Should().BeNull(
                "mulligan prompts must not include a library view");
        }
        await Task.CompletedTask;
    }

    [Fact]
    public async Task NonLibrarySearchPrompt_Priority_LibraryViewIsNull()
    {
        // Priority prompt — common case, must never carry LibraryView.
        var agent = new RemoteAgent(_alice);

        _ = agent.ChoosePriorityActionAsync(NewContext());

        if (agent.PendingPayload != null)
        {
            agent.PendingPayload.LibraryView.Should().BeNull(
                "priority prompts must not include a library view");
        }
        await Task.CompletedTask;
    }

    [Fact]
    public async Task LibrarySearch_LibraryViewClearedAfterSubmit()
    {
        // LibraryView must not leak past the prompt that set it —
        // same lifecycle as Candidates and Label (cleared in Submit).
        var library = BuildLibrary(_alice, 5);
        var candidates = library.Take(2).ToList<ICard>();
        var agent = new RemoteAgent(_alice);

        var task = agent.ChooseLibraryPickAsync(ctx: null, candidates, "land card");
        agent.PendingPayload!.LibraryView.Should().NotBeNull("set before submit");

        agent.Submit(new ChooseLibraryPickCommand(candidates[0].InstanceId) { PlayerId = _alice.Id });
        await task;

        agent.PendingPayload.Should().BeNull("payload cleared after submit");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task LibrarySearch_EmptyLibrary_LibraryViewIsEmptyNotNull()
    {
        // Edge case: search on an empty library should still produce a
        // LibraryView, just an empty one. The portal must not crash on
        // a zero-length deck.
        var agent = new RemoteAgent(_alice);

        _ = agent.ChooseLibraryPickAsync(ctx: null, Array.Empty<ICard>(), "any card");

        agent.PendingPayload.Should().NotBeNull();
        agent.PendingPayload!.LibraryView.Should().NotBeNull();
        agent.PendingPayload!.LibraryView!.Should().BeEmpty();
        await Task.CompletedTask;
    }

    // ── PromptDto wire-shape tests ───────────────────────────────────────

    [Fact]
    public async Task GameFacade_LibrarySearchPrompt_PromptDtoCarriesLibraryView()
    {
        // End-to-end: the wire PromptDto built by GameFacade.BuildPrompt
        // must forward LibraryView from PendingPayload onto the DTO, so
        // the portal sees it in the SignalR push.
        var library = BuildLibrary(null, 12); // no owner needed for facade test
        var facade = GameFacade.Create(
            "Alice", "Bob",
            aliceDeck: Array.Empty<ICard>(),
            bobDeck: Array.Empty<ICard>());

        // Seed Alice's library with cards so the PromptDto carries them.
        foreach (var card in library)
        {
            card.SetOwner(facade.Alice);
            facade.Alice.Zones.Library.AddCard(card);
        }

        var prompts = new List<PromptDto>();
        using var _ = facade.SubscribePrompts(prompts.Add);

        // Manually trigger a library-search prompt on Alice's agent.
        var aliceAgent = (RemoteAgent)typeof(GameFacade)
            .GetField("_aliceAgent",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(facade)!;

        var candidates = library.Take(3).ToList<ICard>();
        var task = aliceAgent.ChooseLibraryPickAsync(ctx: null, candidates, "land card");

        prompts.Should().ContainSingle("one library-search prompt fired");
        var dto = prompts[0];
        dto.LibraryView.Should().NotBeNull();
        dto.LibraryView!.Count.Should().Be(library.Count,
            "LibraryView carries all cards in Alice's library");
        dto.Candidates.Should().HaveCount(3);

        // Cleanup — resolve the pending task so the agent isn't left hanging.
        aliceAgent.Submit(new ChooseLibraryPickCommand(candidates[0].InstanceId)
        {
            PlayerId = facade.Alice.Id,
        });
        await task;
    }

    [Fact]
    public async Task GameFacade_NonLibrarySearchPrompt_PromptDtoLibraryViewIsNull()
    {
        // Non-library prompts (priority, etc.) must emit a PromptDto where
        // LibraryView is null so the portal doesn't render a stale deck view.
        var facade = GameFacade.Create(
            "Alice", "Bob",
            aliceDeck: Array.Empty<ICard>(),
            bobDeck: Array.Empty<ICard>());

        var prompts = new List<PromptDto>();
        using var _ = facade.SubscribePrompts(prompts.Add);

        await facade.StartAsync();

        prompts.Should().NotBeEmpty();
        prompts[0].LibraryView.Should().BeNull(
            "priority prompts must not carry a library view");
    }

    // ── JSON serialization sanity ────────────────────────────────────────

    [Fact]
    public void PromptDto_WithLibraryView_SerializesLibraryViewField()
    {
        // The wire contract: PromptDto serialized to JSON must include
        // "libraryView" (camelCase) when LibraryView is non-null, and omit
        // it (or emit null) when null. Portal reads either tolerantly.
        var snap = new CardSnapshotDto(
            InstanceId: Guid.NewGuid(),
            Name: "Forest", ManaCost: "", Types: new[] { "Land" },
            Power: null, Toughness: null, Tapped: false, SummoningSickness: false,
            Abilities: Array.Empty<AbilityDto>());
        var dto = new PromptDto(
            GameId: Guid.NewGuid(),
            PlayerId: Guid.NewGuid(),
            ExpectedKinds: new[] { "ChooseLibraryPickCommand" },
            Candidates: new[] { snap },
            Label: "basic land card",
            LibraryView: new[] { snap });

        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var json = JsonSerializer.Serialize(dto, opts);

        json.Should().Contain("\"libraryView\"",
            "libraryView must serialize as camelCase");
        json.Should().Contain("\"Forest\"");
    }

    [Fact]
    public void PromptDto_WithoutLibraryView_DoesNotContainLibraryViewField()
    {
        // Non-library-search PromptDto must not carry libraryView on the
        // wire — keeps the message compact and avoids confusing the portal.
        var dto = new PromptDto(
            GameId: Guid.NewGuid(),
            PlayerId: Guid.NewGuid(),
            ExpectedKinds: new[] { "PassPriorityCommand" });

        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var json = JsonSerializer.Serialize(dto, opts);

        // null default → serialized as null or omitted; either is fine.
        // The portal reads it tolerantly. We assert the field is absent
        // or null so the payload stays lean.
        if (json.Contains("\"libraryView\""))
        {
            // If present, it must be null (not a non-null object).
            json.Should().Contain("\"libraryView\":null");
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static List<Land> BuildLibrary(Player? owner, int count)
    {
        var cards = new List<Land>(count);
        for (var i = 0; i < count; i++)
        {
            var card = new Land($"Forest #{i + 1}");
            if (owner != null)
            {
                card.SetOwner(owner);
                owner.Zones.Library.AddCard(card);
            }
            cards.Add(card);
        }
        return cards;
    }

    private GameContext NewContext() =>
        new(_alice, new[] { _alice }, _alice, 1,
            Majik.Core.StateMachine.PhaseStateType.PreCombatMain,
            new Majik.Core.Stack.Stack());
}
