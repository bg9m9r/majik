using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.Costs;

/// <summary>
/// CR 118.9 — Energy alternative cost. Models the "rather than pay this
/// spell's mana cost, pay {E}{E}…{E}" idiom (Wrath of the Skies, etc.).
///
/// Replaces the printed mana cost entirely (<see cref="AlternativeManaCost"/>
/// is <see cref="ManaCost.Zero"/>); the entire cost is paid by spending
/// <see cref="EnergyAmount"/> energy from the caster's player-scoped
/// <see cref="Player.EnergyCounters"/> ledger (CR 106.13).
///
/// ## Timing / legality
///
/// No timing restriction is encoded here — the cost is legal whenever the
/// caster has enough energy. Sorcery-speed gating for sorcery spells is
/// already enforced upstream by <see cref="Majik.Core.Game.SpellCastFlow"/>
/// when no alt-cost is supplied; when an alt-cost IS supplied
/// <see cref="Majik.Core.Game.SpellCastFlow"/> skips the generic
/// <c>CastingPermission</c> gate. Sorcery-speed energy alt-costs are still
/// implicitly sorcery-speed because the printed spell is a sorcery; the
/// engine resolves that via the printed-type check in
/// <see cref="Majik.Core.Game.CastingPermission"/> at the call site that
/// hands a sorcery + energy alt-cost to <c>SpellCastFlow</c>. (Same posture
/// as <see cref="FlashbackAlternativeCost"/> — flashback grants instant /
/// sorcery speed via the printed type, not the alt-cost itself.)
///
/// ## Payment + X interaction
///
/// Per CR 107.3b, when an alt-cost replaces a spell's mana cost and the
/// alt-cost does not itself specify a value for X, X = 0. Wrath of the
/// Skies' printed energy alt-cost ("You may pay {E}{E}{E}{E} rather than
/// pay this spell's mana cost") does not specify a value for X, so X = 0
/// when paid via this alt-cost. The factory's <c>BuildResolveEffect</c>
/// closure receives X explicitly, so this cost type doesn't need to thread
/// the X value — it just records that energy was paid.
///
/// ## Why this lives next to <see cref="PitchAlternativeCost"/>
///
/// Same overall shape: a printed alt-cost that replaces the mana cost
/// with a non-mana resource payment (PitchAlternativeCost: exile a hand
/// card of a colour; EnergyAlternativeCost: spend N energy). Both expose
/// <see cref="ManaCost.Zero"/> as <see cref="AlternativeManaCost"/> so
/// the cast flow's "totalCost = AlternativeManaCost + X" line resolves
/// to the correct zero-mana payment shape.
/// </summary>
public sealed class EnergyAlternativeCost : IAlternativeCost
{
    /// <summary>Energy pips to spend. Defaults to 4 (Wrath of the
    /// Skies' printed alt-cost: {E}{E}{E}{E}). Callers can construct with
    /// a different count if a future card prints a different energy alt-cost.</summary>
    public int EnergyAmount { get; }

    public string Description =>
        EnergyAmount == 1
            ? "Pay {E} rather than this spell's mana cost"
            : $"Pay {string.Concat(Enumerable.Repeat("{E}", EnergyAmount))} rather than this spell's mana cost";

    /// <summary>No mana is paid. CR 118.9.</summary>
    public ManaCost AlternativeManaCost => ManaCost.Zero;

    public EnergyAlternativeCost(int energyAmount = 4)
    {
        if (energyAmount <= 0) throw new ArgumentOutOfRangeException(nameof(energyAmount));
        EnergyAmount = energyAmount;
    }

    /// <summary>
    /// Legal iff the caster currently has at least <see cref="EnergyAmount"/>
    /// energy (CR 119.4 — you can't pay a resource you don't have). The
    /// spell-cast flow additionally enforces sorcery-speed for sorcery
    /// spells via the printed-type check at the call site (this alt-cost
    /// doesn't itself relax timing).
    /// </summary>
    public bool CanCastFor(ICard card, Player caster)
    {
        if (caster == null) return false;
        return caster.EnergyCounters >= EnergyAmount;
    }

    /// <summary>
    /// Pay the energy on resolution (CR 118.8 — costs are paid as the
    /// spell is cast; we apply on resolved-cleanup for symmetry with
    /// <see cref="PitchAlternativeCost.OnResolved"/>'s post-resolve exile
    /// rider — see the cleanup wrapping in
    /// <see cref="Majik.Core.Game.SpellCastFlow"/>). Idempotent-ish:
    /// <see cref="Player.PayEnergy"/> returns false if the ledger has
    /// drained below the threshold (some other effect spent energy
    /// between cast and resolve); we accept that gracefully rather than
    /// throwing — same posture as PitchAlternativeCost's idempotent
    /// exile-already-moved branch.
    /// </summary>
    public void OnResolved(ICard card, Player caster)
    {
        if (caster == null) return;
        caster.PayEnergy(EnergyAmount);
    }
}
