using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bontu's Last Reckoning (Hour of Devastation,
/// {1}{B}{B}).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "Destroy all creatures. Lands you control don't untap during your
///    next untap step."
///
/// ## Implemented (v1)
/// - <b>Card shape</b>: Sorcery, black, {1}{B}{B}. Materialised from the
///   embedded JSON definition (<c>bontus-last-reckoning.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> — same shape-from-JSON
///   posture as <see cref="BoomBustFactory"/>.
/// - <b>Resolve</b> (via <see cref="BuildResolveEffect"/>) — two ordered
///   clauses (CR 608.2c — resolve text top to bottom):
///     1. <b>Destroy all creatures.</b> Every <see cref="Creature"/> on every
///        supplied player's battlefield is routed to its owner's graveyard
///        via <see cref="OracleSpellBinder.MoveToGraveyard"/> with a plain
///        <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/> (CR 701.7).
///        Unlike <see cref="WrathOfGodFactory"/>, Bontu's has NO "can't be
///        regenerated" rider, so regeneration shields (CR 701.15) and
///        indestructible (CR 702.12) both apply. Symmetric sweep — no
///        controller restriction (CR 700.3). Battlefields are snapshotted
///        up front because <c>MoveToGraveyard</c> mutates the zone in place.
///     2. <b>Lands you control don't untap during your next untap step.</b>
///        Each <see cref="Land"/> the caster controls at resolution is
///        registered with
///        <see cref="UntapStepRestrictions.MarkPermanentDoesNotUntap"/>
///        (CR 502.1). When an <see cref="IEventBus"/> is supplied, a one-shot
///        <see cref="StepStartedEvent"/> handler removes all of this cast's
///        skip tokens on the FIRST Untap step belonging to the caster
///        ("your next untap step") — the same one-shot-cleanup mechanism
///        <see cref="SleepFactory"/> uses, but scoped to the caster's lands
///        rather than a target player's creatures.
///
/// ## Notes
/// - The skip applies only to lands the caster controls AT RESOLUTION; lands
///   that arrive later untap normally (the delayed effect is fixed to the
///   permanents it tags, CR 502.1).
/// </summary>
[CardName("Bontu's Last Reckoning")]
public static class BontusLastReckoningFactory
{
    public const string CardName = "Bontu's Last Reckoning";
    public const string Slug = "bontus-last-reckoning";
    public const string PrintedManaCost = "{1}{B}{B}";

    /// <summary>
    /// Build the Bontu's Last Reckoning sorcery shape (Sorcery, black,
    /// {1}{B}{B}) from the embedded JSON definition. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to; the resolve body is
    /// built on demand via <see cref="BuildResolveEffect"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build Bontu's Last Reckoning's resolve effect:
    ///   1. Destroy all creatures (plain destroy — regen/indestructible apply).
    ///   2. Lands <paramref name="caster"/> controls don't untap during the
    ///      caster's next untap step (CR 502.1).
    /// </summary>
    /// <param name="caster">The player who cast the spell. Used to scope the
    /// untap-skip to "lands you control" and to match the one-shot cleanup to
    /// "your next untap step".</param>
    /// <param name="allPlayers">All players whose battlefields are swept for
    /// the "destroy all creatures" clause — typically <c>Game.Players</c>.</param>
    /// <param name="eventBus">Event bus for the one-shot "your next untap
    /// step" cleanup. When null, the skip-untap registrations persist until
    /// the caller clears <see cref="UntapStepRestrictions"/> (test-isolation
    /// posture shared with <see cref="SleepFactory"/>).</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        IReadOnlyList<Player> allPlayers,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(allPlayers);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: destroy all creatures; lands you control skip your next untap step.",
                () =>
                {
                    DestroyAllCreatures(allPlayers);
                    SkipLandsNextUntap(caster, eventBus);
                }),
        };
    }

    // -----------------------------------------------------------------------
    // Clause 1 — Destroy all creatures (CR 701.7, plain destroy)
    // -----------------------------------------------------------------------

    private static void DestroyAllCreatures(IReadOnlyList<Player> allPlayers)
    {
        // Snapshot every battlefield up front — MoveToGraveyard mutates the
        // source zone in place.
        foreach (var pl in allPlayers)
        {
            if (pl == null) continue;
            var creatures = pl.Zones.Battlefield.GetCards()
                .OfType<Creature>()
                .ToList();
            foreach (var c in creatures)
            {
                // CR 701.7 — plain Destroy. No "can't be regenerated" rider on
                // Bontu's, so regeneration (CR 701.15) and indestructible
                // (CR 702.12) both apply.
                OracleSpellBinder.MoveToGraveyard(
                    c, Majik.Core.Zones.ZoneMoveReason.Destroy);
            }
        }
    }

    // -----------------------------------------------------------------------
    // Clause 2 — Lands you control don't untap next untap step (CR 502.1)
    // -----------------------------------------------------------------------

    private static void SkipLandsNextUntap(Player caster, IEventBus? eventBus)
    {
        var lands = caster.Zones.Battlefield
            .GetCards()
            .OfType<Permanent>()
            .Where(p => p.HasType(CardType.Land))
            .ToList();

        // Each land gets its own skip token so per-permanent idempotency in
        // UntapStepRestrictions is maintained (same approach as SleepFactory).
        // All tokens are collected so a single one-shot cleanup handler can
        // sweep them.
        var skipTokens = new List<object>(lands.Count);
        foreach (var land in lands)
        {
            // CR 502.1 — "don't untap during your next untap step".
            var skipToken = new object();
            UntapStepRestrictions.MarkPermanentDoesNotUntap(skipToken, land);
            skipTokens.Add(skipToken);
        }

        if (skipTokens.Count == 0 || eventBus == null) return;

        // One-shot subscription: on the FIRST Untap step belonging to the
        // caster ("your next untap step"), remove all skip registrations and
        // unsubscribe (CR 502.1). Same one-shot pattern as SleepFactory.
        Action<StepStartedEvent>? cleanupHandler = null;
        cleanupHandler = ev =>
        {
            if (ev.StepType != StepStateType.Untap) return;
            if (!ReferenceEquals(ev.Player, caster)) return;

            foreach (var token in skipTokens)
            {
                UntapStepRestrictions.RemoveAll(token);
            }

            if (cleanupHandler != null)
                eventBus.Unsubscribe(cleanupHandler);
        };
        eventBus.Subscribe(cleanupHandler);
    }
}
