using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// CR 122.1m — Global replacement effect for finality counters:
///
///   "If a creature with one or more finality counters on it would die,
///    exile it instead."
///
/// <para>
/// Funnels through <see cref="Majik.Core.Services.ZoneService.MoveCard"/>'s
/// <see cref="ZoneMoveIntent"/> path — every Battlefield → Graveyard
/// move on a creature whose <see cref="Permanent.Counters"/> bag contains
/// at least one <see cref="CounterType.Finality"/> entry is rewritten to
/// Battlefield → Exile. Mirrors the static-replacement posture of
/// <see cref="ContainmentPriestExileReplacementEffect"/> and the
/// die-replacement test pattern in
/// <see cref="Majik.Core.Tests.Effects.DieReplacementTests"/>, but the
/// gate is on the moving card's own counter bag — no source / lifecycle
/// permanent required.
/// </para>
///
/// <para>
/// <b>Registration</b>: call <see cref="Register"/> once per
/// <see cref="ReplacementBus"/> instance at game-setup time (or any
/// factory that produces a finality-marked permanent can call it
/// idempotently — registration is keyed off the bus's tag table so a
/// double-register is a no-op). The effect is non-OneShot (CR 614 — a
/// global replacement is consulted on every matching event for the
/// lifetime of the game).
/// </para>
///
/// <para>
/// <b>Edge cases</b>:
/// <list type="bullet">
///   <item><b>Sacrifice / lethal damage / -X/-X / destroy</b> all route
///   through the same Battlefield → Graveyard <see cref="ZoneMoveIntent"/>
///   (CR 701.16 / CR 704.5g / CR 701.7), so finality redirects every
///   path. Indestructible cancels the destroy upstream of the move so
///   the redirect never fires on a destroy that didn't actually move
///   the creature.</item>
///   <item><b>Multiple finality counters</b> behave identically to one
///   (CR 122.1m — "one or more"); each redirect to exile fires once per
///   would-die event.</item>
///   <item><b>Non-creature permanents</b>: the gate checks
///   <see cref="CardType.Creature"/> on the moving card. Finality
///   counters on a non-creature (off-rules / Conspiracy / Mycosynth
///   Lattice corner cases) do NOT redirect, matching the printed text
///   ("If a CREATURE … would die").</item>
///   <item><b>Tokens</b> are still creatures (CR 111.6), so a tokens
///   with a finality counter on it is redirected to exile by this
///   effect; the post-exile SBA (CR 704.5d) then cleans the token up
///   from exile (a token ceasing to exist while in exile is the
///   game's normal disappear path).</item>
/// </list>
/// </para>
/// </summary>
public static class FinalityCounterReplacement
{
    /// <summary>
    /// Tag object used to dedupe registrations on a single bus. A second
    /// <see cref="Register"/> call on the same bus is a silent no-op so
    /// factories can call it eagerly without coordination.
    /// </summary>
    private static readonly object _tag = new();

    /// <summary>
    /// Register the finality-counter die-replacement on
    /// <paramref name="replacements"/>. Idempotent: repeated calls on
    /// the same bus install at most one replacement.
    /// </summary>
    /// <param name="replacements">The bus to register on. Must be
    /// non-null.</param>
    /// <returns>The registered replacement on first call, or the
    /// previously-registered one on subsequent calls (callers that want
    /// to unregister at end-of-game can hold the returned reference).</returns>
    public static IReplacementEffect<ZoneMoveIntent> Register(
        ReplacementBus replacements)
    {
        ArgumentNullException.ThrowIfNull(replacements);

        // Idempotence: scan the bus for an existing registration tagged
        // with our private _tag sentinel. ReplacementBus exposes a
        // typed enumeration via TryFindByTag; we fall back to building
        // a fresh effect when nothing is found.
        var existing = replacements.FindByTag<ZoneMoveIntent>(_tag);
        if (existing != null) return existing;

        var effect = new LambdaReplacement<ZoneMoveIntent>(
            applies: static (intent, _) => Applies(intent),
            replace: static (intent, _) => intent with { ToZone = ZoneType.Exile },
            oneShot: false,
            tag: _tag);

        replacements.Register(effect);
        return effect;
    }

    /// <summary>
    /// Predicate exposed for tests. True iff <paramref name="intent"/>
    /// is a Battlefield → Graveyard move on a creature card whose
    /// <see cref="Permanent.Counters"/> bag has at least one
    /// <see cref="CounterType.Finality"/> entry.
    /// </summary>
    public static bool Applies(ZoneMoveIntent intent)
    {
        if (intent == null) return false;
        if (intent.FromZone != ZoneType.Battlefield) return false;
        if (intent.ToZone != ZoneType.Graveyard) return false;
        if (intent.Card is not Permanent permanent) return false;
        if (!permanent.HasType(CardType.Creature)) return false;
        return permanent.Counters.Count(CounterType.Finality) > 0;
    }
}
