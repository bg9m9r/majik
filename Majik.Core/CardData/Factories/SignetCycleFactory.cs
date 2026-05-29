using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Parametric named-card factory for the Ravnica "Signet" two-colour
/// artifact mana-rock cycle:
///
/// <list type="bullet">
///   <item>Ravnica: City of Guilds + Guildpact + Dissension (allied):
///     Azorius Signet (WU), Dimir Signet (UB), Rakdos Signet (BR), Gruul
///     Signet (RG), Selesnya Signet (GW).</item>
///   <item>Guildpact / Dissension (enemy): Orzhov Signet (WB), Simic Signet
///     (GU), Golgari Signet (BG), Boros Signet (WR), Izzet Signet (UR).</item>
/// </list>
///
/// Each member shares the same printed shape:
/// <code>
/// Artifact {2}.
/// {1}, {T}: Add {A}{B}.
/// </code>
///
/// Only the produced colour pair (A, B) differs, so one factory handles the
/// whole 10-card cycle. This is the artifact analogue of
/// <see cref="FilterLandCycleFactory"/>'s filter mode and the
/// <see cref="TalismanCycleFactory"/>'s coloured mode — same {2} two-colour
/// mana rock parametric cycle. The signet swaps the talisman's pain rider
/// (<c>additionalCostPayer = LoseLife(1)</c>) for the filter-land's {1}
/// mana additional cost (<c>additionalCostPayer = PayMana({1})</c>), and
/// emits both colour pips at once (Add {A}{B}) rather than a one-of choice.
///
/// Args layout (forwarded by the source generator at dispatch time):
/// <c>[0] = printed card name</c>,
/// <c>[1] = first coloured pip (single-letter Scryfall code)</c>,
/// <c>[2] = second coloured pip</c>.
///
/// ## Implemented (v1)
/// - Artifact identity ({2}, owner / controller wiring) + correct printed
///   name per cycle member.
/// - <b>{1}, {T}: Add {A}{B}</b> — a single <see cref="ManaAbility"/> built
///   via the additional-cost overload:
///     - <c>manaGenerated</c> = <c>ManaCost.Parse(A + B)</c> — both coloured
///       pips emitted at once (CR 605.1 — the whole produced amount is added
///       in one atomic step).
///     - <c>canActivateCheck</c> = <c>!IsTapped &amp;&amp;
///       ManaPool.CanPay({1})</c> — the {T} half plus the {1}-affordability
///       gate (without the latter, activation would tap and then no-op on
///       payment).
///     - <c>additionalCostPayer</c> = <c>controller.PayMana({1})</c> — the
///       printed {1} extra cost, deducted from the mana pool atomically with
///       the {T} tap, exactly the filter-land shape.
///
/// ## Signet net mana
/// Activating the signet costs {1} (deducted from the pool) and adds
/// {A}{B} — a net gain of 1 mana plus conversion of a generic into two
/// coloured pips. This is the signature signet ramp/fix curve.
///
/// ## Deferred (v1 gaps)
/// - Activation requires {1} to already be in the mana pool. The engine
///   doesn't auto-tap other sources to feed the signet cost (no look-ahead
///   "mana-fixer" planner) — the same posture every other additional-mana-
///   cost activated ability takes (filter lands, Mind Stone's draw cost,
///   Springleaf Drum, etc.).
/// </summary>
// Dimir / Rakdos / Boros / Izzet Signet each ship a dedicated per-member
// factory (JSON-def + named factory) and so are intentionally NOT registered
// here — a name may map to only one [CardName] factory (source-gen MJK001).
// This parametric cycle factory still owns the remaining six members.
[CardName("Azorius Signet",  "W", "U")]
[CardName("Gruul Signet",    "R", "G")]
[CardName("Selesnya Signet", "G", "W")]
[CardName("Orzhov Signet",   "W", "B")]
[CardName("Simic Signet",    "G", "U")]
[CardName("Golgari Signet",  "B", "G")]
public static class SignetCycleFactory
{
    /// <summary>Printed mana cost shared by every cycle member.</summary>
    public const string PrintedManaCost = "{2}";

    /// <summary>
    /// Fallback overload — only reachable when constructed by hand.
    /// Default-builds Azorius Signet (the Ravnica WU member).
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, new[] { "Azorius Signet", "W", "U" });

    /// <summary>
    /// Construct the signet identified by <paramref name="args"/>.
    /// </summary>
    /// <param name="owner">The player who owns and initially controls the signet.</param>
    /// <param name="args">
    /// Source-generator-provided args. Layout:
    /// <c>[0] = printed card name</c> (e.g. "Dimir Signet"),
    /// <c>[1] = first coloured pip (single-letter Scryfall code, e.g. "U")</c>,
    /// <c>[2] = second coloured pip (e.g. "B")</c>.
    /// </param>
    public static Artifact Create(Player owner, string[] args)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length < 3)
        {
            throw new ArgumentException(
                $"SignetCycleFactory needs args = [name, colorA, colorB] (got {args.Length}).",
                nameof(args));
        }

        var cardName = args[0];
        var colorA = args[1];
        var colorB = args[2];

        if (string.IsNullOrWhiteSpace(colorA) || string.IsNullOrWhiteSpace(colorB))
        {
            throw new ArgumentException(
                "SignetCycleFactory requires non-empty colour codes.",
                nameof(args));
        }

        var signet = new Artifact(cardName, PrintedManaCost);
        signet.SetOwner(owner);
        signet.SetController(owner);

        // ----------------------------------------------------------------
        // {1}, {T}: Add {A}{B}. CR 605.1 — single mana ability; the {1}
        // extra cost is paid as part of activation, atomically with the
        // {T} tap. Both coloured pips are produced in one step. Mirrors
        // FilterLandCycleFactory.AttachFilterMode:
        //   canActivateCheck:    !IsTapped && ManaPool.CanPay({1})
        //   additionalCostPayer: controller.PayMana({1})
        // ----------------------------------------------------------------
        var output = ManaCost.Parse(colorA + colorB);
        var oneGeneric = ManaCost.Parse("1");

        signet.AddAbility(new ManaAbility(
            source: signet,
            controller: owner,
            manaGenerated: output,
            canActivateCheck: () => !signet.IsTapped && owner.ManaPool.CanPay(oneGeneric),
            additionalCostPayer: p => p.PayMana(oneGeneric)));

        return signet;
    }
}
