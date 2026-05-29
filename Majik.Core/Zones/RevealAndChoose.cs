using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;

namespace Majik.Core.Zones;

/// <summary>
/// CR 701.15 — shared entry point for "reveal the top N cards of your
/// library, you may put one matching card into [zone], rest into [zone]"
/// effects (Malevolent Rumble, Impulse, Sleight of Hand, See the
/// Unwritten and the rest of the reveal-and-choose family).
///
/// Centralises three things every reveal-and-choose effect previously
/// duplicated (and frequently got wrong):
/// <list type="bullet">
///   <item>Library underflow — "top N" when the library has fewer than
///   N cards reveals what's there (CR 121.2 / CR 701.15a).</item>
///   <item>Agent prompting — prompts the registered agent EVEN when the
///   eligible subset is empty so the player sees the reveal pile and
///   can't be silently no-opped (same UX principle as the empty-tutor
///   modal shipped for <see cref="LibrarySearch"/>).</item>
///   <item>Zone routing — moves the picked + leftover cards through
///   <see cref="ZoneServiceRegistry"/> when registered, so ETB triggers
///   fire on the picked card and graveyard-leaves observers see the
///   discarded cards.</item>
/// </list>
///
/// Replaces the historical
/// <c>top4.FirstOrDefault(c => c.HasType(...))</c> auto-pick scattered
/// across every "may put one of them into …" factory / template.
/// </summary>
public static class RevealAndChoose
{
    /// <summary>
    /// Reveal the top <paramref name="count"/> cards of
    /// <paramref name="caster"/>'s library, prompt the registered agent
    /// to pick one card matching <paramref name="eligiblePredicate"/>
    /// (or decline if <paramref name="optional"/>), move the picked card
    /// to <paramref name="pickedDestination"/>, and move the rest to
    /// <paramref name="restDestination"/>.
    ///
    /// <para>Behaviour notes:</para>
    /// <list type="bullet">
    ///   <item>Library may hold fewer than <paramref name="count"/>
    ///   cards — the reveal pile shrinks to whatever's there.</item>
    ///   <item>Zero eligible cards still prompts the agent (when one is
    ///   registered) so the player sees the reveal pile even when no
    ///   pick is legal. Returns <see langword="null"/> after the prompt
    ///   resolves.</item>
    ///   <item>Mandatory pick (<paramref name="optional"/> = false) with
    ///   non-empty eligible cards: the agent's null return is treated as
    ///   "decline" by the engine (no card moves to
    ///   <paramref name="pickedDestination"/>) — engine-level validation
    ///   of mandatory clauses lives in the agent / wire-resolve layer
    ///   (RemoteAgent.Resolve) where the wire command's intent can be
    ///   inferred. The helper itself simply honours the agent's pick.
    ///   </item>
    ///   <item>Zone moves go through <see cref="ZoneServiceRegistry"/>
    ///   when registered (so ETB triggers / CardMovedEvent fire); fall
    ///   back to raw zone mutation for shape / dispatcher-test paths
    ///   that don't register a service.</item>
    /// </list>
    /// </summary>
    /// <param name="caster">Player whose library is being revealed.</param>
    /// <param name="count">How many cards to peek (clamped to library
    /// length).</param>
    /// <param name="eligiblePredicate">Filter producing the agent's
    /// eligible subset (e.g. "is a permanent card", "is colourless").
    /// </param>
    /// <param name="optional">Whether the player may decline (CR 116.1b
    /// — "you may" clauses). When <c>false</c> the prompt is mandatory;
    /// the player must pick from the eligible set if any exist.</param>
    /// <param name="label">Human-readable prompt label
    /// ("Permanent to put into hand", "Creature to put onto the
    /// battlefield"). Surfaced verbatim by remote-agent UIs.</param>
    /// <param name="pickedDestination">Zone the picked card moves to
    /// (typically <see cref="ZoneType.Hand"/> or
    /// <see cref="ZoneType.Battlefield"/>).</param>
    /// <param name="restDestination">Zone the unpicked revealed cards
    /// move to (typically <see cref="ZoneType.Graveyard"/> or
    /// <see cref="ZoneType.Library"/> — the latter re-bottoms in reveal
    /// order).</param>
    /// <param name="sourceTag">Source identifier for diagnostics
    /// ("malevolent-rumble", "impulse"). Currently unused but reserved
    /// for a future CardsRevealedEvent payload tag.</param>
    /// <returns>The picked card, or <see langword="null"/> when the
    /// agent declined (or eligible was empty).</returns>
    public static ICard? RevealTopAndChoose(
        Player caster,
        int count,
        Func<ICard, bool> eligiblePredicate,
        bool optional,
        string label,
        ZoneType pickedDestination,
        ZoneType restDestination,
        string sourceTag)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(eligiblePredicate);
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(sourceTag);
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));

        // Snapshot the reveal pile (library may be shorter than `count`).
        var revealed = caster.Zones.Library.GetCards().Take(count).ToList();
        if (revealed.Count == 0)
        {
            // Nothing to reveal — empty library yields no prompt and no
            // card moves. Caller (the spell effect) is responsible for
            // any unconditional side-effects (e.g. Malevolent Rumble's
            // token creation) that don't gate on library size.
            return null;
        }

        var eligible = revealed.Where(eligiblePredicate).ToList();

        // Prompt the agent (when one is registered) — EVEN with empty
        // eligible so the player sees the reveal pile and acknowledges
        // the failed pick. Falls back to the deterministic first-
        // eligible when no agent is registered (matches the historical
        // FirstOrDefault auto-pick the helper replaces).
        ICard? picked;
        var agent = AgentRegistry.Get(caster);
        if (agent != null)
        {
            // TODO: remove sync-over-async once IEffect.Execute becomes async.
            picked = agent.ChooseFromRevealedAsync(
                ctx: null,
                revealed: revealed,
                eligible: eligible,
                optional: optional,
                label: label).GetAwaiter().GetResult();

            // Defensive: if the agent returns a card that isn't in the
            // eligible set, fall back to declining. (RemoteAgent already
            // coerces out-of-set picks to null + warns; this guard
            // protects against custom IPlayerAgent implementations.)
            if (picked != null && !eligible.Contains(picked))
            {
                picked = null;
            }
        }
        else
        {
            // No agent registered (shape / dispatcher test path) — auto-
            // pick the first eligible when present, decline otherwise.
            picked = eligible.Count > 0 ? eligible[0] : null;
        }

        // Move the picked card to its destination + every other revealed
        // card to the rest destination. Use ZoneServiceRegistry when
        // available so ETB triggers / CardMovedEvent fire; fall back to
        // raw zone mutation for shape tests.
        var zones = ZoneServiceRegistry.Get(caster);
        foreach (var card in revealed)
        {
            var dest = ReferenceEquals(card, picked) ? pickedDestination : restDestination;
            if (zones != null)
            {
                zones.MoveCard(card, ZoneType.Library, dest, caster);
            }
            else
            {
                caster.Zones.Library.RemoveCard(card);
                var destZone = dest switch
                {
                    ZoneType.Hand => caster.Zones.Hand,
                    ZoneType.Battlefield => caster.Zones.Battlefield,
                    ZoneType.Graveyard => caster.Zones.Graveyard,
                    ZoneType.Library => caster.Zones.Library,
                    ZoneType.Exile => caster.Zones.Exile,
                    _ => throw new InvalidOperationException(
                        $"RevealTopAndChoose: unsupported destination zone {dest}."),
                };
                destZone.AddCard(card);
                card.SetZone(dest);
            }
        }

        return picked;
    }
}
