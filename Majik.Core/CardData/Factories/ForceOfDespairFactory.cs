using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Force of Despair (Modern Horizons 2, {3}{B}).
///
/// Instant. Oracle text:
///   "If it's not your turn, you may exile a black card from your hand
///    rather than pay this spell's mana cost.
///    Destroy all creatures that entered the battlefield this turn."
///
/// ## Implemented (v1)
/// - Instant card shape ({3}{B}, Black) — built via the fluent
///   <see cref="CardDef"/> DSL.
/// - Pitch alternative cost (<see cref="Majik.Core.Costs.PitchAlternativeCost"/>,
///   <c>RequiredColor = Black</c>, <c>LifeCost = 0</c>) — same shape as
///   Force of Negation modulo colour.
/// - Bot probe wired through <see cref="PitchAltCostProbe.DefaultLookup"/>.
/// - Resolve effect (<see cref="BuildSpellDefinition"/>): destroys every
///   <see cref="Creature"/> that is currently on the battlefield AND was
///   recorded as entering this turn by <see cref="TurnState"/>'s
///   <see cref="TurnState.PermanentsEnteredThisTurn"/> ledger.
///   - "Destroy" = move from controller's battlefield → owner's graveyard
///     (CR 701.7); regen / indestructible bypass is the same lossy gap
///     inherited from the rest of the destroy family (Wrath of God,
///     Slaughter Pact, etc.).
///   - When no <see cref="TurnState"/> is wired (shape-only / dispatcher
///     tests) the effect is a clean no-op rather than degrading to "destroy
///     every creature" (that would be a totally different card — Wrath
///     of God shape). Production callers thread the live TurnState in via
///     the <c>turnStateResolver</c> callback the same way
///     <see cref="SearingBlazeFactory"/> does.
///
/// ## Deferred (v1 gaps)
/// - <b>Indestructible / regenerate riders</b>: same lossy gap as the rest
///   of the destroy family (Wrath of God, Slaughter Pact, Drown in the
///   Loch, Murderous Rider, Defile-with-damage, etc.). The
///   <see cref="OracleSpellBinder.MoveToGraveyard"/> path doesn't yet
///   consult <c>CharacteristicsRow.Indestructible</c> nor replacement-bus
///   regenerate-shields.
/// - <b>Multi-controller battlefield scan</b>: the ETB set already contains
///   permanents from every controller (TurnDriver subscribes to the global
///   <see cref="Majik.Core.Events.CardMovedEvent"/> bus), so the
///   destroy-each-such-creature scan is already correctly all-player —
///   no per-player resolver needed (distinct from Force of Vigor /
///   Pernicious Deed which still take an <c>allPlayersResolver</c>).
/// </summary>
[CardName("Force of Despair")]
public static class ForceOfDespairFactory
{
    public const string CardName = "Force of Despair";
    public const string PrintedManaCost = "{3}{B}";

    /// <summary>CardDef DSL — card shape only. The destroy-all-creatures-
    /// that-ETB'd-this-turn body lives in <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/>. Reads the live
    /// <see cref="TurnState"/> via <paramref name="turnStateResolver"/> at
    /// resolution and snapshots every <see cref="Creature"/> still on the
    /// battlefield from the per-turn ETB ledger; each is destroyed
    /// (CR 701.7). No <see cref="TargetRequest"/>s — printed text scans
    /// the whole game state, no per-creature target picks (CR 700.6 — "all
    /// creatures" is enumeration, not targeting).
    /// </summary>
    /// <param name="turnStateResolver">Callback returning the live
    /// <see cref="TurnState"/> at resolution time. When the callback returns
    /// null (no driver wired — typical for shape / dispatcher tests) the
    /// resolve body is a clean no-op (no creatures destroyed). Production
    /// callers pass <c>() =&gt; turnDriver.TurnState</c>.</param>
    public static SpellDefinition BuildSpellDefinition(Func<TurnState?> turnStateResolver)
    {
        ArgumentNullException.ThrowIfNull(turnStateResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => new IEffect[]
            {
                Fx.Inline(
                    $"{CardName} — destroy all creatures that entered the battlefield this turn",
                    () => Resolve(turnStateResolver)),
            });
    }

    /// <summary>
    /// Shared resolve helper. Snapshots the per-turn ETB ledger, filters to
    /// creatures still on the battlefield, and destroys each
    /// (battlefield → owner's graveyard, CR 701.7). The snapshot avoids
    /// "concurrent modification" if a destroy somewhere triggers an
    /// effect that mutates the ledger mid-iteration.
    /// </summary>
    private static void Resolve(Func<TurnState?> turnStateResolver)
    {
        var turnState = turnStateResolver.Invoke();
        if (turnState == null) return;

        // Snapshot the ledger so destroy-triggered effects can't reshape
        // the iteration target (CR 608.2c — simultaneous, but we walk in
        // a stable order for determinism).
        var victims = turnState.PermanentsEnteredThisTurn
            .OfType<Creature>()
            .Where(c => c.Zone == ZoneType.Battlefield)
            .ToList();

        foreach (var victim in victims)
        {
            // CR 701.7 — destroy: move from controller's battlefield to
            // owner's graveyard. Indestructible / regenerate riders are
            // the same lossy gap as Wrath / Slaughter Pact (see class
            // xmldoc).
            Fx.MoveToGraveyard(victim);
        }
    }
}
