using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Gifts Ungiven (Champions of Kamigawa, {3}{U}).
///
/// Instant. Oracle text:
///   "Search your library for up to four cards with different names and
///    reveal them. Target opponent chooses two of those cards. Put the
///    chosen cards into your graveyard and the rest into your hand. Then
///    shuffle."
///
/// Classic Modern reanimator / graveyard-pillar tutor: pile-of-four
/// arranged so the opponent's "pick 2 to graveyard" choice is exactly
/// what the caster wanted in the graveyard (Unburial Rites + a fatty,
/// plus two hand-card threats). v1 ships the printed sequence end-to-end
/// against agent prompts (caster picks up to 4 distinct-name cards;
/// target opponent picks 2 of the revealed pile to go to graveyard).
///
/// ## Implemented (v1)
/// - Instant {3}{U} (Blue) card shape with owner / controller wired.
/// - <b>1..1 "target opponent"</b> <see cref="TargetRequest"/> — same
///   shape as Tendrils of Agony / Grief / Thought-Knot Seer's pick-card
///   prompt; the chosen opponent's agent is queried at resolution time
///   for the two-card grist pick (CR 608.2b — illegal target at
///   resolution = whole spell does nothing; we surface that as an early
///   no-op when the resolver returns a non-Player).
/// - <b>Up-to-4 distinct-name tutor</b> — at resolution the caster's
///   agent is prompted via <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>
///   four times in sequence; after each pick the library is re-filtered
///   to cards whose <see cref="ICard.Name"/> is not already in the
///   running revealed pile (CR 701.19a + the printed "different names"
///   restriction — same per-slot agent loop shape as
///   <see cref="ScapeshiftFactory"/> / <see cref="PrimevalTitanFactory"/>).
///   The caster may decline early (agent returns <c>null</c>) — a clean
///   stop, same posture as Drift of Phantasms / Mystical Tutor (CR
///   701.19a "may choose"). When fewer than two cards are found the
///   opponent's pick-2 step is clamped — every revealed card goes to the
///   graveyard, hand pile is empty (CR 121.4 / 119.x — do as much as
///   possible).
/// - <b>Opponent picks 2 to graveyard</b> — target opponent's agent is
///   prompted via <see cref="IPlayerAgent.ChooseFromPileAsync"/> twice
///   in sequence with the revealed pile as candidates; after the first
///   pick the second prompt's candidate list excludes the first pick
///   (CR 700.1 — "two of those cards" = two distinct cards). When the
///   opponent declines (returns <c>null</c>) we fall back to the first
///   remaining candidate so the printed two-card split still happens
///   (printed text is mandatory — the opponent doesn't get to skip
///   choosing).
/// - <b>Split chosen → graveyard, rest → hand</b> — chosen cards leave
///   the library for the caster's graveyard; the remainder of the
///   revealed pile leaves the library for the caster's hand. CR 401.4 /
///   608.2c — both halves happen, then the library is shuffled
///   (<see cref="LibraryShuffle.ShuffleLibrary"/>, CR 701.20a).
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal event</b> — the four picks are moved Library → Hand /
///   Graveyard without publishing a reveal event, same gap as every
///   other tutor factory (Mystical Tutor / Stoneforge Mystic / Drift of
///   Phantasms / Scapeshift).
/// - <b>Action validator opponent filtering</b>: the "target opponent"
///   TargetRequest is open-cardinality candidate-wise; the chosen target
///   is resolved verbatim — same posture as Tendrils of Agony / Grief
///   (no extra opponent-only filtering at validator time).
/// - <b>Empty library</b>: if the library has zero cards the tutor loop
///   no-ops on the first slot; library is still shuffled per CR 701.20a
///   ("search happened" = shuffle even on empty result, matching Drift
///   of Phantasms).
/// </summary>
[CardName("Gifts Ungiven")]
public static class GiftsUngivenFactory
{
    public const string CardName = "Gifts Ungiven";
    public const string PrintedManaCost = "{3}{U}";
    public const int MaxTutorPicks = 4;
    public const int OpponentGraveyardPicks = 2;

    /// <summary>
    /// Construct Gifts Ungiven as an Instant card. Card shape only — the
    /// resolve effect is wired via <see cref="BuildDefinition"/> which
    /// the cast flow / tests drive.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the Gifts Ungiven SpellDefinition (1..1 target opponent +
    /// pile-of-four tutor + opponent-picks-2 graveyard split).
    /// </summary>
    /// <param name="controller">The caster — tutor reads from this
    /// player's library and the chosen cards go to their graveyard /
    /// hand (CR 401 / 704.5 — "your" library / graveyard / hand).</param>
    /// <param name="targetResolver">Resolves the raw target token chosen
    /// by the caster (expected to yield a <see cref="Player"/>). When
    /// the resolver returns anything that isn't a <see cref="Player"/>
    /// the entire effect no-ops per CR 608.2b — Gifts Ungiven's printed
    /// resolution chain is single-target so an illegal target voids the
    /// search and the choose step.</param>
    public static SpellDefinition BuildDefinition(
        Player controller,
        Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target opponent",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName} — search up to {MaxTutorPicks} distinct-name cards; target opponent splits {OpponentGraveyardPicks}→graveyard, rest→hand",
                        () => Resolve(controller, resolved as Player)),
                };
            });
    }

    /// <summary>
    /// Drive the four-step Gifts Ungiven resolution against live agents.
    /// CR 608.2b — illegal target at resolution: whole effect no-ops.
    /// </summary>
    internal static void Resolve(Player controller, Player? targetOpponent)
    {
        if (targetOpponent == null)
        {
            // CR 608.2b — illegal target (resolver returned non-Player).
            // Gifts Ungiven's whole printed resolution is single-target
            // gated, so we no-op cleanly: no search, no shuffle.
            return;
        }

        // ----------------------------------------------------------------
        // CR 701.19a + printed "different names" — caster's agent picks
        // up to 4 cards from their library with distinct names. The
        // per-slot loop re-filters the candidate set each pass so the
        // distinct-name rider is enforced agent-side (same shape as
        // Scapeshift's per-slot loop).
        // ----------------------------------------------------------------
        var casterAgent = AgentRegistry.Get(controller);
        var revealed = new List<ICard>(MaxTutorPicks);
        var revealedNames = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < MaxTutorPicks; i++)
        {
            var libSnapshot = controller.Zones.Library.GetCards()
                .Where(c => !revealedNames.Contains(c.Name))
                .ToList();

            if (libSnapshot.Count == 0) break;

            ICard? pick = casterAgent != null
                ? casterAgent.ChooseLibraryPickAsync(
                    ctx: null,
                    libSnapshot,
                    $"card #{i + 1} of up to {MaxTutorPicks} with a different name")
                    .GetAwaiter().GetResult()
                : libSnapshot[0];

            // CR 701.19a — caster may decline; clean stop, smaller pile.
            if (pick == null) break;

            // Defensive: agent picked a card not in the filtered list
            // (e.g. duplicate name) — skip it but continue the loop so
            // the caster gets fresh prompts.
            if (revealedNames.Contains(pick.Name)) continue;
            if (!controller.Zones.Library.GetCards().Contains(pick)) continue;

            revealed.Add(pick);
            revealedNames.Add(pick.Name);
        }

        // No legal picks → still shuffle (CR 701.20a, "search happened").
        if (revealed.Count == 0)
        {
            LibraryShuffle.ShuffleLibrary(controller, "gifts-ungiven");
            return;
        }

        // ----------------------------------------------------------------
        // CR 700.1 — target opponent's agent picks two cards to go to
        // graveyard. When fewer than two cards were revealed, every
        // revealed card goes to graveyard (do as much as possible — CR
        // 119.x / 121.4).
        // ----------------------------------------------------------------
        var opponentAgent = AgentRegistry.Get(targetOpponent);
        var toGraveyard = new List<ICard>(OpponentGraveyardPicks);

        if (revealed.Count <= OpponentGraveyardPicks)
        {
            // All revealed cards go to graveyard, hand pile is empty.
            toGraveyard.AddRange(revealed);
        }
        else
        {
            var remaining = new List<ICard>(revealed);
            for (var i = 0; i < OpponentGraveyardPicks; i++)
            {
                ICard? pick = opponentAgent != null
                    ? opponentAgent.ChooseFromPileAsync(
                        targetOpponent,
                        remaining,
                        $"card #{i + 1} of {OpponentGraveyardPicks} to put into the caster's graveyard",
                        Majik.Core.Cards.BotIntent.Removal)
                        .GetAwaiter().GetResult()
                    : remaining[0];

                // Mandatory pick — printed text doesn't let the opponent
                // skip choosing. Fall back to the first remaining
                // candidate if the agent declines / picks something
                // outside the candidate list.
                if (pick == null || !remaining.Contains(pick))
                {
                    pick = remaining[0];
                }

                toGraveyard.Add(pick);
                remaining.Remove(pick);
            }
        }

        // ----------------------------------------------------------------
        // Split the revealed pile — chosen cards → caster's graveyard,
        // rest → caster's hand. CR 401.4 / 608.2c.
        // ----------------------------------------------------------------
        foreach (var card in revealed)
        {
            // Defensive: the card must still be in the controller's
            // library to move (no concurrent mutation in v1, but the
            // zone-membership check matches every other tutor factory).
            if (!controller.Zones.Library.GetCards().Contains(card)) continue;
            controller.Zones.Library.RemoveCard(card);

            if (toGraveyard.Contains(card))
            {
                controller.Zones.Graveyard.AddCard(card);
                card.SetZone(ZoneType.Graveyard);
            }
            else
            {
                controller.Zones.Hand.AddCard(card);
                card.SetZone(ZoneType.Hand);
            }
        }

        // CR 701.20a — shuffle after search.
        LibraryShuffle.ShuffleLibrary(controller, "gifts-ungiven");
    }
}
