using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.Keywords;

/// <summary>
/// Primitive shared builder for the typed-cycling family (CR 702.32d —
/// "typecycling" — Plainscycling / Islandcycling / Swampcycling /
/// Mountaincycling / Forestcycling / Wizardcycling / Slivercycling /
/// Landcycling, etc.).
///
/// <para>
/// CR 702.32d — "Typecycling is a variant of the cycling ability. '[Type]
/// cycling [cost]' means '[Cost], Discard this card: Search your library
/// for a [type] card, reveal it, put it into your hand, then shuffle.'"
/// Differs from generic <see cref="CyclingFactory"/> in two ways: the
/// resolve body tutors a typed card instead of drawing one, and the
/// keyword surfaces under the typed name (Forestcycling, Slivercycling,
/// etc.) rather than the bare "Cycling" marker — though the engine still
/// treats the activated ability as Cycling for CR 702.32d "Whenever a
/// player cycles" subscribers.
/// </para>
///
/// <para>
/// Cost stack mirrors generic Cycling — caller-supplied cycle cost
/// (typically <see cref="ManaCostCost"/>, but <see cref="PayLifeCost"/>
/// alt-cost is honored) layered with <see cref="DiscardSelfCost"/> for
/// the CR 702.32a hand-zone gate. The predicate
/// (<see cref="Func{ICard,Boolean}"/>) parameterizes the tutor target:
/// <c>c =&gt; c.HasSubtype(CardSubtype.Forest)</c> for Forestcycling,
/// <c>c =&gt; c.HasSubtype(CardSubtype.Sliver)</c> for Slivercycling,
/// <c>c =&gt; c.HasType(CardType.Land)</c> for Landcycling, etc.
/// </para>
///
/// <para>
/// Resolve body posture mirrors every other library-tutor primitive
/// (Stoneforge Mystic / Expedition Map / Sylvan Scrying): filter the
/// controller's library down to predicate matches, consult
/// <see cref="IPlayerAgent.ChooseLibraryPickAsync"/> (deterministic
/// first-match fallback when no agent registered — CR 701.19a's
/// decline-to-find branch is honored only when the agent explicitly
/// returns null), zone-move the pick Library → Hand, then shuffle via
/// <see cref="LibraryShuffle.ShuffleLibrary"/> (CR 701.20a — the
/// shuffle happens regardless of whether anything was found, because
/// the search occurred).
/// </para>
///
/// <para>
/// CR 702.32d also stipulates that typed-cycling activated abilities
/// publish the same <see cref="CardCycledEvent"/> as generic Cycling so
/// CR 702.32d "Whenever a player cycles" subscribers (Lightning Rift,
/// Astral Slide, Astral Drift, Decree of Justice) fire uniformly across
/// generic Cycling and every Type-cycling variant. Publication happens
/// at the tail of the resolve body — after the discard + tutor + shuffle
/// — matching the post-resolve timing the generic
/// <see cref="CyclingFactory"/> uses.
/// </para>
///
/// <para>
/// This primitive co-exists with the generic
/// <see cref="CyclingFactory"/>. Cards with both generic Cycling and a
/// typed-cycling rider (Krosan Tusker: "Cycling {2}" + on-cycle basic
/// land tutor trigger) should attach generic Cycling via
/// <see cref="CyclingFactory.Build"/> and wire the typed rider as a
/// <see cref="TriggeredAbility"/> over
/// <see cref="EventTriggerCondition{CardCycledEvent}"/>. Cards with
/// ONLY typed cycling (Generous Ent: pure Forestcycling {G}) route
/// through this builder.
/// </para>
/// </summary>
public static class TypedCyclingFactory
{
    /// <summary>
    /// Attach a typed-cycling activated ability + the
    /// <see cref="KeywordAbility"/> typed-name marker (e.g.
    /// "Forestcycling") AND the generic "Cycling" marker — CR 702.32d
    /// stipulates typecycling is a Cycling variant, so both keywords
    /// surface for oracle audits and keyword scans.
    ///
    /// <para>
    /// The activated ability resolves to: search the controller's
    /// library for the first card matching <paramref name="predicate"/>,
    /// reveal it (no event emission in v1 — same posture as Stoneforge
    /// Mystic / Sylvan Scrying), zone-move to hand, then shuffle (CR
    /// 701.20a). When an <paramref name="eventBus"/> is supplied the
    /// resolve body publishes <see cref="CardCycledEvent"/> at the tail
    /// so CR 702.32d "Whenever a player cycles" subscribers fire.
    /// </para>
    ///
    /// <para>
    /// CR 701.19a — search is an action a player may decline to resolve
    /// to a chosen card. Empty candidate list → clean no-op (shuffle
    /// still happens). Agent returning null → clean no-op (player chose
    /// not to find anything; shuffle still happens). No-agent fallback
    /// picks the first candidate deterministically — same posture as
    /// every existing tutor primitive.
    /// </para>
    /// </summary>
    /// <param name="source">The card the typed-cycling ability lives on.
    /// Must have its <see cref="ICard.Owner"/> already wired — the
    /// resolve body tutors against the owner's library.</param>
    /// <param name="cycleCost">The "[Cost]" half of "[Cost], Discard
    /// this card: Search your library for a [type] card …". Typically a
    /// <see cref="ManaCostCost"/> ({G}, {2}, etc.). Must NOT include the
    /// discard-self half — that's appended automatically.</param>
    /// <param name="predicate">Filter for tutor candidates. The first
    /// card in the controller's library (or the agent's pick) that
    /// satisfies this predicate is tutored to hand. Examples:
    /// <c>c =&gt; c.HasSubtype(CardSubtype.Forest)</c> for Forestcycling,
    /// <c>c =&gt; c.HasType(CardType.Land)</c> for Landcycling.</param>
    /// <param name="typedKeyword">Human-readable typed-cycling keyword
    /// (e.g. "Forestcycling", "Slivercycling", "Landcycling") attached
    /// as a <see cref="KeywordAbility"/> marker alongside the generic
    /// "Cycling" marker.</param>
    /// <param name="kindLabel">Human-readable description of the tutor
    /// target for agent prompt UIs ("Forest card", "Sliver card",
    /// "land card"). Default is <paramref name="typedKeyword"/> minus
    /// the trailing "cycling" — i.e. "Forestcycling" → "Forest card".</param>
    /// <param name="eventBus">Optional event bus the resolve body
    /// publishes <see cref="CardCycledEvent"/> against. When null no
    /// event fires (shape-only path).</param>
    /// <returns>The attached <see cref="ActivatedAbility"/>, for callers
    /// that need to wire test assertions or stamp additional metadata.
    /// The ability has already been added to <paramref name="source"/>.</returns>
    public static ActivatedAbility Build(
        ICard source,
        ICost cycleCost,
        Func<ICard, bool> predicate,
        string typedKeyword,
        string? kindLabel = null,
        IEventBus? eventBus = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(cycleCost);
        ArgumentNullException.ThrowIfNull(predicate);
        if (string.IsNullOrWhiteSpace(typedKeyword))
        {
            throw new ArgumentException(
                "TypedCyclingFactory.Build: typedKeyword must be non-empty (e.g. 'Forestcycling').",
                nameof(typedKeyword));
        }
        if (source.Owner is null)
        {
            throw new ArgumentException(
                "TypedCyclingFactory.Build: card.Owner must be set before attaching the typed-cycling ability — the resolve body tutors against the owner's library.",
                nameof(source));
        }

        var owner = source.Owner;
        var label = string.IsNullOrWhiteSpace(kindLabel)
            ? InferKindLabel(typedKeyword)
            : kindLabel!;

        // CR 702.32d — typecycling is a Cycling variant. Surface BOTH
        // the typed name and the generic "Cycling" marker so consumers
        // that key on either keyword presence (oracle audits, bot
        // decision layer, CR 702.32d subscribers) see both.
        source.AddAbility(new KeywordAbility(typedKeyword, source, owner));
        source.AddAbility(new KeywordAbility("Cycling", source, owner));

        // CR 702.32d — "[Cost], Discard this card: Search your library
        // for a [type] card, reveal it, put it into your hand, then
        // shuffle." Cost stack: caller-supplied cycle cost +
        // DiscardSelfCost. The DiscardSelfCost provides the
        // activated-from-hand zone gate (CR 702.32a).
        var tutorEffect = new Effect(
            $"{source.Name}: {typedKeyword} — tutor a {label} -> hand + shuffle",
            async ctx =>
            {
                // PLAN 01 (Slice D) — genuinely prompt the controller off
                // the live ResolutionContext rather than auto-picking the
                // first match.
                await TutorTypedCardAsync(ctx, owner, predicate, label, source.Name)
                    .ConfigureAwait(false);

                // CR 702.32d — publish the "cycled" event after the
                // tutor + shuffle so subscribers (Lightning Rift,
                // Astral Slide, etc.) see the post-resolve state
                // (card in graveyard + tutored card in hand).
                eventBus?.Publish(new CardCycledEvent(source, owner));
            });

        var ability = new ActivatedAbility(
            source: source,
            controller: owner,
            costs: new ICost[]
            {
                cycleCost,
                new DiscardSelfCost(source),
            },
            effects: new IEffect[] { tutorEffect });

        source.AddAbility(ability);
        return ability;
    }

    /// <summary>
    /// Helper: execute the typed-tutor body against
    /// <paramref name="owner"/>'s library. Filters the library by
    /// <paramref name="predicate"/>, consults the registered agent
    /// (deterministic first-match fallback), moves the pick Library →
    /// Hand, and shuffles via <see cref="LibraryShuffle.ShuffleLibrary"/>.
    ///
    /// <para>
    /// Exposed for cards whose printed text isn't a typed-cycling
    /// activated ability but uses the same tutor primitive — e.g.
    /// Krosan Tusker's "When you cycle this card, you may search your
    /// library for a basic land card …" on-cycle trigger. The CR
    /// 701.19a / CR 701.20a shape is identical, so the tutor body
    /// factors cleanly.
    /// </para>
    /// </summary>
    /// <param name="owner">The player whose library is searched.</param>
    /// <param name="predicate">Tutor candidate filter.</param>
    /// <param name="kindLabel">Human-readable target description for
    /// agent prompt UIs.</param>
    /// <param name="shuffleReason">Short identifier passed to
    /// <see cref="LibraryShuffle.ShuffleLibrary"/> for diagnostics
    /// (e.g. "forestcycling", "krosan-tusker-cycle").</param>
    /// <returns>The tutored card, or null when nothing was found
    /// (empty candidate pool or agent declined).</returns>
    public static ICard? TutorTypedCard(
        Player owner,
        Func<ICard, bool> predicate,
        string kindLabel,
        string shuffleReason)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Legacy sync entry point (direct-call tests + any not-yet-migrated
        // caller). Routes through the async path on a registry-derived
        // context so the agent is still genuinely prompted. The typed-cycling
        // resolve closure now calls TutorTypedCardAsync with the live
        // ResolutionContext — see PLAN 01 Slice D.
        return TutorTypedCardAsync(
                ResolutionContext.For(
                    owner, AgentRegistry.Get(owner), game: null, chosenTargets: null),
                owner, predicate, kindLabel, shuffleReason)
            .GetAwaiter().GetResult();
    }

    /// <summary>
    /// PLAN 01 (Slice D) — async typed-tutor body. Reads the resolving
    /// player's agent + live game off <paramref name="ctx"/> and genuinely
    /// <c>await</c>s <see cref="IPlayerAgent.ChooseLibraryPickAsync"/> so
    /// every typed-cycling / typed-tutor caller routing through this
    /// primitive prompts for real instead of auto-picking the first match.
    /// Falls back to <see cref="AgentRegistry"/> (then the deterministic
    /// first candidate) only when no agent is available.
    /// </summary>
    public static async ValueTask<ICard?> TutorTypedCardAsync(
        ResolutionContext ctx,
        Player owner,
        Func<ICard, bool> predicate,
        string kindLabel,
        string shuffleReason)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(predicate);

        var candidates = owner.Zones.Library.GetCards()
            .Where(predicate)
            .ToList();

        if (candidates.Count == 0)
        {
            // CR 701.19a — empty candidate pool is a legal outcome.
            // CR 701.20a — still shuffle since the search occurred.
            LibraryShuffle.ShuffleLibrary(owner, shuffleReason);
            return null;
        }

        var agent = ctx.Agent ?? AgentRegistry.Get(owner);
        ICard? pick = agent != null
            ? await agent.ChooseLibraryPickAsync(
                    ctx: ctx.Game,
                    candidates: candidates,
                    kindLabel: kindLabel,
                    ct: ctx.Ct)
                .ConfigureAwait(false)
            : candidates[0];

        if (pick != null)
        {
            owner.Zones.Library.RemoveCard(pick);
            owner.Zones.Hand.AddCard(pick);
            pick.SetZone(ZoneType.Hand);
        }

        // CR 701.20a — shuffle after the search resolves.
        LibraryShuffle.ShuffleLibrary(owner, shuffleReason);
        return pick;
    }

    /// <summary>
    /// Heuristic: derive a tutor-prompt kind label from the typed
    /// keyword. "Forestcycling" → "Forest card"; "Wizardcycling" →
    /// "Wizard card"; "Landcycling" → "Land card". Callers with a
    /// non-standard mapping pass <c>kindLabel</c> explicitly.
    /// </summary>
    private static string InferKindLabel(string typedKeyword)
    {
        const string suffix = "cycling";
        if (typedKeyword.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            && typedKeyword.Length > suffix.Length)
        {
            return $"{typedKeyword.Substring(0, typedKeyword.Length - suffix.Length)} card";
        }
        return $"{typedKeyword} card";
    }
}
