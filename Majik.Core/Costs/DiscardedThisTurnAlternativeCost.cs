using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.Costs;

/// <summary>
/// CR 118.9 — "As long as you've discarded a card this turn, you may pay
/// [cost] rather than pay this spell's mana cost." The
/// Asmoranomardicadaistinaculdacar pattern (Modern Horizons 2).
///
/// A fixed-mana alternative cost gated on a per-turn game-state condition:
/// the caster must have discarded at least one card this turn. The discard
/// count lives on <see cref="Majik.Core.Game.TurnState"/>
/// (<see cref="Majik.Core.Game.TurnState.DiscardsByPlayer"/>, CR 701.16) —
/// the same counter Hollow One's cost reduction reads. Rather than couple
/// this cost to the TurnState type, the factory injects a
/// <see cref="System.Func{Player,System.Int32}"/> discard accessor at
/// construction time (mirrors <see cref="HollowOneFactory"/>'s reducer
/// closure over an optional TurnState). When no accessor is supplied
/// (shape-only path), the gate reads zero and the alternative cost is
/// unavailable.
///
/// No card is pitched and no life is paid — the entire cost is the
/// <see cref="AlternativeManaCost"/> (Asmoran: {B/R}, a single hybrid pip).
/// CR 118.9 — the alternative cost is offered INSTEAD of the printed mana
/// cost (Asmoran's printed cost is {0}/empty, so the alt-cost path actually
/// raises the cost to a coloured pip in exchange for being castable without
/// the normal restriction — the printed {0} cast is always available too).
///
/// <see cref="CanCastFor"/> imposes no zone restriction on the spell — the
/// <paramref name="card"/> argument is consulted only for null-safety; the
/// gate is purely "have you discarded a card this turn?".
/// </summary>
public sealed class DiscardedThisTurnAlternativeCost : IAlternativeCost
{
    private readonly Func<Player, int>? _discardCountOf;

    /// <summary>The mana paid in lieu of the printed cost (Asmoran: {B/R}).</summary>
    public ManaCost AlternativeManaCost { get; }

    public string Description =>
        $"Pay {_printedAltCost} (if you've discarded a card this turn)";

    private readonly string _printedAltCost;

    /// <summary>
    /// Build the discard-gated alternative cost.
    /// </summary>
    /// <param name="altManaCost">The mana cost string paid instead of the
    /// printed cost (Asmoran: <c>"{B/R}"</c>). Parsed via
    /// <see cref="ManaCost.Parse"/>, so hybrid / coloured pips are
    /// supported.</param>
    /// <param name="discardCountOf">Accessor returning how many cards the
    /// supplied player has discarded this turn (CR 701.16) — typically
    /// <c>turnState.DiscardsByPlayer</c>. When <see langword="null"/>
    /// (shape-only path) the gate reads zero and the alt-cost is never
    /// available.</param>
    public DiscardedThisTurnAlternativeCost(
        string altManaCost,
        Func<Player, int>? discardCountOf)
    {
        ArgumentException.ThrowIfNullOrEmpty(altManaCost);
        _printedAltCost = altManaCost;
        AlternativeManaCost = ManaCost.Parse(altManaCost);
        _discardCountOf = discardCountOf;
    }

    /// <summary>
    /// CR 118.9 / CR 601.3e — the alternative cost is available only if the
    /// caster has discarded at least one card this turn. With no discard
    /// accessor wired (shape-only construction), the gate is closed.
    /// </summary>
    public bool CanCastFor(ICard card, Player caster)
    {
        if (caster == null || _discardCountOf == null) return false;
        return _discardCountOf(caster) > 0;
    }

    /// <summary>
    /// No post-resolution side-effect — the mana payment is the whole cost
    /// (no card exiled, no life paid). Required by
    /// <see cref="IAlternativeCost"/>.
    /// </summary>
    public void OnResolved(ICard card, Player caster)
    {
        // Intentionally empty — Asmoran's alt-cost pays only mana.
    }
}
