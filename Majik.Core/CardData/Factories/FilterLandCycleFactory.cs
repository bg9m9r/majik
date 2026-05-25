using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Parametric named-card factory for the Shadowmoor + Eventide "filter
/// land" cycle:
///
/// <list type="bullet">
///   <item>Shadowmoor (allied): Mystic Gate (W/U), Sunken Ruins (U/B),
///     Graven Cairns (B/R), Fire-Lit Thicket (R/G), Wooded Bastion (G/W).</item>
///   <item>Eventide (enemy):   Cascade Bluffs (U/R), Twilight Mire (B/G),
///     Fetid Heath (W/B), Rugged Prairie (R/W), Flooded Grove (G/U).</item>
/// </list>
///
/// Each member shares the same printed oracle shape:
/// <code>
/// {T}: Add {C}.
/// {1}, {T}: Add {A}{A}, {A}{B}, or {B}{B}.
/// </code>
///
/// Only the produced colour pair differs, so one factory handles the
/// whole 10-card cycle.
/// <code>
/// [CardName("Mystic Gate",    "W", "U")]
/// [CardName("Fetid Heath",    "W", "B")]
/// </code>
///
/// Args layout (forwarded by the source generator at dispatch time):
/// <c>[0] = printed card name</c>,
/// <c>[1] = first coloured option (single-letter Scryfall code)</c>,
/// <c>[2] = second coloured option</c>.
///
/// ## Implemented (v1)
/// - Land identity (non-Basic, no subtype) + correct printed name per
///   cycle member.
/// - <c>{T}: Add {C}</c> vanilla <see cref="ManaAbility"/> (CR 605.1 —
///   mana ability, never on the stack; no additional cost).
/// - Filter mode modelled as THREE separate <see cref="ManaAbility"/>
///   slots — <c>{A}{A}</c>, <c>{A}{B}</c>, <c>{B}{B}</c> — each wired via
///   the additional-cost overload of <see cref="ManaAbility"/>:
///   <c>canActivateCheck = !land.IsTapped &amp;&amp;
///   controller.ManaPool.CanPay({1})</c>,
///   <c>additionalCostPayer = controller.PayMana({1})</c>. This mirrors
///   the per-colour fan-out shape Aether Hub / Cavern of Souls /
///   Springleaf Drum use for "Add one mana of any colour" abilities — the
///   bot's source-picker iterates abilities by produced colour combo and
///   picks the slot matching the spell it's paying for.
///
///   CR 605.1 — these are still mana abilities (don't use the stack);
///   the {1} extra cost is paid as part of activation, atomically with
///   the {T} tap.
///
/// ## Filter-land net mana
/// Activating one filter mode costs {1} (deducted from the mana pool)
/// and adds {A}{A} / {A}{B} / {B}{B} — a net gain of 1 mana plus
/// conversion of a generic into two coloured pips. This is the
/// signature filter-land tempo curve (1G → WW for Wooded Bastion etc.).
///
/// ## Deferred (v1 gaps)
/// - The printed oracle is a single modal mana ability with three modes;
///   v1 splits it into three separate <see cref="ManaAbility"/>
///   instances. Functionally equivalent for payment — bots/agents pick
///   the slot whose produced mana matches the cost they're paying. No
///   user-facing semantic difference until a future modal-mana-ability
///   primitive is introduced.
/// - Activation requires {1} to already be in the mana pool. The engine
///   doesn't auto-tap other sources to feed the filter cost (no
///   look-ahead "mana-fixer" planner) — same posture every other
///   additional-mana-cost activated ability takes (Mind Stone's draw
///   cost, Springleaf Drum, etc.).
/// </summary>
[CardName("Mystic Gate",        "W", "U")]
[CardName("Sunken Ruins",       "U", "B")]
[CardName("Graven Cairns",      "B", "R")]
[CardName("Fire-Lit Thicket",   "R", "G")]
[CardName("Wooded Bastion",     "G", "W")]
[CardName("Cascade Bluffs",     "U", "R")]
[CardName("Twilight Mire",      "B", "G")]
[CardName("Fetid Heath",        "W", "B")]
[CardName("Rugged Prairie",     "R", "W")]
[CardName("Flooded Grove",      "G", "U")]
public static class FilterLandCycleFactory
{
    /// <summary>
    /// Fallback overload — only reachable when constructed by hand.
    /// Default-builds Mystic Gate.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, new[] { "Mystic Gate", "W", "U" });

    /// <summary>
    /// Construct the filter land identified by <paramref name="args"/>.
    /// </summary>
    /// <param name="owner">The player who owns and initially controls the land.</param>
    /// <param name="args">
    /// Source-generator-provided args. Layout:
    /// <c>[0] = printed card name</c> (e.g. "Mystic Gate"),
    /// <c>[1] = first coloured option (single-letter Scryfall code, e.g. "W")</c>,
    /// <c>[2] = second coloured option (e.g. "U")</c>.
    /// </param>
    public static Land Create(Player owner, string[] args)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length < 3)
        {
            throw new ArgumentException(
                $"FilterLandCycleFactory needs args = [name, colorA, colorB] (got {args.Length}).",
                nameof(args));
        }

        var cardName = args[0];
        var colorA = args[1];
        var colorB = args[2];

        if (string.IsNullOrWhiteSpace(colorA) || string.IsNullOrWhiteSpace(colorB))
        {
            throw new ArgumentException(
                "FilterLandCycleFactory requires non-empty colour codes.",
                nameof(args));
        }

        var land = new Land(cardName);
        land.SetOwner(owner);
        land.SetController(owner);

        // {T}: Add {C}. CR 605.1 — vanilla colourless mana ability, no
        // extra cost (the {1} rider applies only to the filter modes).
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("C")));

        // {1}, {T}: Add {A}{A}, {A}{B}, or {B}{B}. Three sibling
        // ManaAbility slots — agents/bots pick by produced colour combo.
        AttachFilterMode(land, owner, colorA + colorA); // {A}{A}
        AttachFilterMode(land, owner, colorA + colorB); // {A}{B}
        AttachFilterMode(land, owner, colorB + colorB); // {B}{B}

        return land;
    }

    /// <summary>
    /// Attach a <c>{1}, {T}: Add &lt;pips&gt;</c> filter mana ability.
    /// Built via the additional-cost overload of <see cref="ManaAbility"/>:
    /// the {T} tap is paid by the default tap-as-cost path; the
    /// <paramref name="pips"/> output is the produced mana; the
    /// <c>additionalCostPayer</c> deducts {1} from the controller's mana
    /// pool. The <c>canActivateCheck</c> gates the activation on both the
    /// untap state and the {1}-affordability check — without the latter,
    /// activation would tap the land and then no-op on the payment.
    /// </summary>
    private static void AttachFilterMode(Land land, Player controller, string pips)
    {
        var output = ManaCost.Parse(pips);
        var oneGeneric = ManaCost.Parse("1");

        land.AddAbility(new ManaAbility(
            source: land,
            controller: controller,
            manaGenerated: output,
            canActivateCheck: () => !land.IsTapped && controller.ManaPool.CanPay(oneGeneric),
            additionalCostPayer: p => p.PayMana(oneGeneric)));
    }
}
