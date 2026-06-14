using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.Zones;

/// <summary>
/// CR 701.19a / CR 701.20a — shared entry point for "search your library
/// for X, then shuffle" effects. Encapsulates the canonical sequence so
/// every tutor in the codebase makes the same decisions about empty
/// candidate lists, agent prompting, and post-search shuffles.
///
/// The lesson from the silent-no-op bug (Green Sun's Zenith into a deck
/// containing zero green creatures): a player who is instructed to
/// search their library has STILL performed the search even if no
/// matching cards exist. Per CR 701.19a the player isn't required to
/// find anything, but the action still happened — UX-wise the human
/// agent must see the prompt (with the full library shown but no card
/// highlighted as eligible) and acknowledge it. And per CR 701.20a the
/// library is shuffled afterward regardless of whether a card was
/// actually moved.
///
/// Centralising this here means every tutor / search closure shares the
/// same empty-candidates + agent + shuffle semantics rather than each
/// effect re-deciding when to short-circuit.
/// </summary>
public static class LibrarySearch
{
    /// <summary>
    /// Prompt the searching player for one pick from
    /// <paramref name="candidates"/>. Does NOT shuffle — the caller is
    /// responsible for the post-search shuffle (CR 701.20a) so it can
    /// happen after the picked card has been moved out of the library
    /// to its destination zone. The two helpers
    /// <see cref="PromptOnly"/> + <see cref="LibraryShuffle.ShuffleLibrary"/>
    /// together replace the historical
    /// <c>if (candidates.Count == 0) return; agent != null ? agent.Choose...</c>
    /// short-circuit found in every tutor.
    ///
    /// Behaviour:
    /// <list type="bullet">
    /// <item><description>When an <see cref="IPlayerAgent"/> is registered
    /// for <paramref name="searcher"/> via <see cref="AgentRegistry"/>,
    /// invokes <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>
    /// (even when <paramref name="candidates"/> is empty — the remote
    /// agent's modal renders the full library with no eligible cards
    /// highlighted and a single Acknowledge/Done button, so the player
    /// SEES that no matching cards exist rather than the spell
    /// silently no-opping).</description></item>
    /// <item><description>When no agent is registered, falls back to the
    /// first candidate (deterministic test path). Empty candidates =
    /// returns <see langword="null"/>.</description></item>
    /// </list>
    /// </summary>
    /// <param name="searcher">The player whose library is being searched.</param>
    /// <param name="candidates">Pre-filtered candidate list. May be empty —
    /// the agent is still prompted so a human searcher can see that no
    /// matching cards exist (CR 701.19a).</param>
    /// <param name="kindLabel">Human-readable description ("green creature
    /// card with mana value 3 or less", "basic land card").</param>
    /// <returns>The card the agent picked, or <see langword="null"/> for
    /// "find nothing" (which is always legal under CR 701.19a, and is
    /// also the only option when <paramref name="candidates"/> is empty).</returns>
    /// <remarks>
    /// PLAN 01 (Slice D) — the <c>GameContext? ctx = null</c> overload was
    /// dropped: no production caller threads a context through this sync
    /// shim anymore (every tutor effect closure now calls the async
    /// <see cref="PromptOnlyAsync"/> with its live <see cref="ResolutionContext"/>).
    /// This sync entry point survives only for direct-call unit tests; it
    /// routes through the async path on a registry-derived agent so the
    /// agent is still genuinely prompted.
    /// </remarks>
    public static ICard? PromptOnly(
        Player searcher,
        IReadOnlyList<ICard> candidates,
        string kindLabel,
        string? revealReason = null)
    {
        ArgumentNullException.ThrowIfNull(searcher);
        ArgumentNullException.ThrowIfNull(candidates);

        return PromptOnlyAsync(
                ResolutionContext.For(
                    searcher, AgentRegistry.Get(searcher), game: null, chosenTargets: null),
                searcher, candidates, kindLabel, revealReason)
            .GetAwaiter().GetResult();
    }

    /// <summary>
    /// PLAN 01 (Slice D) — async prompt-only. Reads the resolving player's
    /// agent + live game off <paramref name="ctx"/> and genuinely
    /// <c>await</c>s <see cref="IPlayerAgent.ChooseLibraryPickAsync"/> so
    /// every tutor routing through this primitive prompts for real instead
    /// of auto-picking <c>candidates[0]</c>. Falls back to
    /// <see cref="AgentRegistry"/> (and finally the deterministic first
    /// candidate) only when no agent is available on the context / registry.
    /// Does NOT shuffle — the caller owns the post-search shuffle.
    /// </summary>
    public static async ValueTask<ICard?> PromptOnlyAsync(
        ResolutionContext ctx,
        Player searcher,
        IReadOnlyList<ICard> candidates,
        string kindLabel,
        string? revealReason = null)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(searcher);
        ArgumentNullException.ThrowIfNull(candidates);

        ICard? pick;
        var agent = ctx.Agent ?? AgentRegistry.Get(searcher);
        if (agent != null)
        {
            // Prompt the agent even when candidates is empty — remote-agent
            // UIs render the full library with all cards muted and a single
            // Acknowledge button so the player SEES the failed search.
            // CR 701.18a — declining (returning null) is always legal.
            pick = await agent.ChooseLibraryPickAsync(ctx.Game, candidates, kindLabel, ctx.Ct)
                .ConfigureAwait(false);
        }
        else
        {
            // No agent available (shape / dispatcher test path) — deterministic
            // first-candidate behaviour matches the historical short-circuit.
            pick = candidates.Count > 0 ? candidates[0] : null;
        }

        // CR 701.18 — most tutors print "search …, reveal it, put it into your
        // hand". When the caller supplied a reveal reason AND a card was
        // actually found, make it public: publish one CardRevealedEvent tagged
        // ZoneType.Library so "whenever you reveal a card" payoffs + the
        // portal's reveal-flash UI observe the tutor reveal. The reveal fires
        // while the card is still in the library (before the caller moves it),
        // mirroring the printed sequence. Best-effort: no-op when no reason is
        // given (tutors that don't reveal — Wood Elves), nothing was found, or
        // no bus is registered. Same EventBusRegistry seam LibraryShuffle uses.
        PublishRevealIfRequested(searcher, pick, revealReason);
        return pick;
    }

    /// <summary>
    /// CR 701.18 — publish a single <see cref="CardRevealedEvent"/> for a
    /// tutored-and-revealed card. No-op when <paramref name="revealReason"/> is
    /// null (the tutor doesn't reveal), <paramref name="found"/> is null
    /// (nothing was found — finding nothing is legal under CR 701.18a), or no
    /// <see cref="IEventBus"/> is registered for <paramref name="searcher"/>.
    /// The card is still in the library at this point, so the event is tagged
    /// <see cref="ZoneType.Library"/>.
    ///
    /// <para>Public so hand-rolled tutor closures that don't route their pick
    /// through <see cref="PromptOnlyAsync"/> (Fierce Empath, Civic Wayfinder,
    /// Recruiter of the Guard, Worldly Tutor's top-of-library closure, …) can
    /// surface the printed "reveal it" step with the same shape + the same
    /// <see cref="EventBusRegistry"/> seam.</para>
    /// </summary>
    public static void PublishRevealIfRequested(Player searcher, ICard? found, string? revealReason)
    {
        ArgumentNullException.ThrowIfNull(searcher);
        if (revealReason is null || found is null) return;
        var bus = EventBusRegistry.Get(searcher);
        bus?.Publish(new CardRevealedEvent(found, searcher, ZoneType.Library, revealReason));
    }

    /// <summary>
    /// Convenience wrapper: <see cref="PromptOnly"/> followed by
    /// <see cref="LibraryShuffle.ShuffleLibrary"/>. Use only for tutors
    /// that don't actually move the pick (or move it AFTER the shuffle
    /// for some reason). Most tutors should call <see cref="PromptOnly"/>
    /// + move-to-destination + <see cref="LibraryShuffle.ShuffleLibrary"/>
    /// so the published <c>LibraryShuffledEvent.CountBefore</c> reflects
    /// the post-move library count.
    /// </summary>
    public static ICard? PromptAndShuffle(
        Player searcher,
        IReadOnlyList<ICard> candidates,
        string kindLabel,
        string shuffleReason,
        string? revealReason = null)
    {
        var pick = PromptOnly(searcher, candidates, kindLabel, revealReason);
        // CR 701.20a — shuffle whether or not a card was found.
        LibraryShuffle.ShuffleLibrary(searcher, shuffleReason);
        return pick;
    }

    /// <summary>
    /// PLAN 01 (Slice D) — async <see cref="PromptOnlyAsync"/> followed by
    /// the CR 701.20a shuffle. Use from tutor effect closures built on the
    /// async <see cref="Effect"/> ctor so the search genuinely prompts.
    /// </summary>
    public static async ValueTask<ICard?> PromptAndShuffleAsync(
        ResolutionContext ctx,
        Player searcher,
        IReadOnlyList<ICard> candidates,
        string kindLabel,
        string shuffleReason,
        string? revealReason = null)
    {
        var pick = await PromptOnlyAsync(ctx, searcher, candidates, kindLabel, revealReason)
            .ConfigureAwait(false);
        // CR 701.20a — shuffle whether or not a card was found.
        LibraryShuffle.ShuffleLibrary(searcher, shuffleReason);
        return pick;
    }
}
