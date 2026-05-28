using Majik.Core.Cards;
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
    /// <param name="ctx">Optional game context for the agent's scoring
    /// policy. Null is acceptable — same sync-over-async wart as the rest
    /// of the v1 effect closures.</param>
    /// <returns>The card the agent picked, or <see langword="null"/> for
    /// "find nothing" (which is always legal under CR 701.19a, and is
    /// also the only option when <paramref name="candidates"/> is empty).</returns>
    public static ICard? PromptOnly(
        Player searcher,
        IReadOnlyList<ICard> candidates,
        string kindLabel,
        GameContext? ctx = null)
    {
        ArgumentNullException.ThrowIfNull(searcher);
        ArgumentNullException.ThrowIfNull(candidates);

        var agent = AgentRegistry.Get(searcher);
        if (agent != null)
        {
            // Prompt the agent even when candidates is empty — remote-agent
            // UIs render the full library with all cards muted and a single
            // Acknowledge button so the player SEES the failed search.
            // CR 701.19a — declining (returning null) is always legal.
            return agent.ChooseLibraryPickAsync(ctx, candidates, kindLabel)
                .GetAwaiter().GetResult();
        }
        // No agent registered (shape / dispatcher test path) — deterministic
        // first-candidate behaviour matches the historical short-circuit.
        return candidates.Count > 0 ? candidates[0] : null;
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
        GameContext? ctx = null)
    {
        var pick = PromptOnly(searcher, candidates, kindLabel, ctx);
        // CR 701.20a — shuffle whether or not a card was found.
        LibraryShuffle.ShuffleLibrary(searcher, shuffleReason);
        return pick;
    }
}
