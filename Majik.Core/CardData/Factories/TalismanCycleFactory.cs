using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Parametric named-card factory for the Mirrodin / Mirrodin Besieged
/// "Talisman" two-colour artifact cycle:
///
/// <list type="bullet">
///   <item>Mirrodin (allied):   Talisman of Progress (WU), Talisman of
///     Dominance (UB), Talisman of Indulgence (BR), Talisman of Impulse
///     (RG), Talisman of Unity (GW).</item>
///   <item>Mirrodin Besieged (enemy): Talisman of Hierarchy (WB), Talisman
///     of Curiosity (GU), Talisman of Resilience (BG), Talisman of
///     Conviction (WR), Talisman of Creativity (UR).</item>
/// </list>
///
/// Each member shares the same printed shape:
/// <code>
/// Artifact {2}.
/// {T}: Add {C}.
/// {T}: Add {A} or {B}. &lt;Name&gt; deals 1 damage to you.
/// </code>
///
/// Only the colour pair (A, B) differs, so one factory handles the whole
/// 10-card cycle. Mirrors <see cref="PainLandCycleFactory"/> — the talisman
/// cycle is the artifact analogue of the Ice Age / Apocalypse painland
/// family.
///
/// Args layout (forwarded by the source generator at dispatch time):
/// <c>[0] = printed card name</c>,
/// <c>[1] = first coloured option (single-letter Scryfall code)</c>,
/// <c>[2] = second coloured option</c>.
///
/// ## Implemented (v1)
/// - Artifact identity ({2}, owner / controller wiring) + correct printed
///   name per cycle member.
/// - <b>{T}: Add {C}</b> — single <see cref="ManaAbility"/> (CR 605.1).
///   {C} folds into the generic bucket via <see cref="ManaCost.Parse"/>
///   per CR 107.4c. Same painless body as Mind Stone.
/// - <b>{T}: Add {A} or {B}, deal 1 damage to you</b> — modelled as TWO
///   <see cref="ManaAbility"/> instances (one per coloured option),
///   each using the (source, controller, manaGenerated, canActivateCheck,
///   additionalCostPayer) overload — the same painland shape
///   <see cref="PainLandCycleFactory"/> uses.
///     - <c>canActivateCheck</c> = <c>!IsTapped</c> (the {T} half of the
///       cost; the engine taps in <see cref="ManaAbility.Activate"/>).
///     - <c>additionalCostPayer</c> = <c>controller.LoseLife(1)</c> — the
///       printed "deals 1 damage to you" rider modelled as a non-mana
///       activation cost.
///
/// ## Rules note — pain modelled as cost, not trigger
/// The printed Oracle text for the talisman cycle (post-CR cleanup) reads
/// "&lt;Name&gt; deals 1 damage to you" as a follow-on clause to the
/// activation, which strictly is a triggered ability of the mana ability
/// (CR 605.1b — mana abilities can have triggered side effects). v1
/// collapses this into an additional activation cost (the painland
/// pattern) for two reasons:
/// 1. The engine's painland infrastructure (<see cref="PainLandCycleFactory"/>)
///    already gives a clean, bot-friendly surface for "pay X to add coloured
///    mana"; reusing it keeps the talisman cycle in lockstep with the rest
///    of the pain-for-fixing family.
/// 2. CR 605.1b triggered-mana side effects don't compose with the engine's
///    mana-source picker yet — the picker only consults activation legality,
///    not in-flight triggers. Modelling the damage as a cost surfaces it to
///    the picker (which now sees "this ability costs 1 life") and matches
///    every test that asserts "tapping for a coloured pip ledgers the 1
///    damage".
///
/// Like painlands, CR 119.4 does NOT gate the rider — talismans can drop
/// you to 0 or below (damage, not "Pay 1 life"). Distinct from the Horizon
/// Canopy life-floor shape.
///
/// ## Deferred (v1 gaps)
/// - <b>True trigger surface</b>: see the rules note above. The printed
///   damage-to-you should fire as a triggered side effect of the mana
///   activation when the engine's mana-source picker grows trigger
///   awareness.
/// - <b>Single modal-colour mana ability</b>: "Add {A} or {B}" is bound
///   as two separate <see cref="ManaAbility"/> instances; the bot's
///   source-picker selects the right colour at payment time.
/// - <b>Damage vs. life-loss routing</b>: the rider routes through
///   <see cref="Player.LoseLife"/> rather than a full
///   <see cref="Majik.Core.Events.DamageDealtEvent"/>. Damage-prevention
///   subscribers won't see the talisman's ping. Same scope decision as
///   <see cref="PainLandCycleFactory"/>.
/// </summary>
[CardName("Talisman of Progress",   "W", "U")]
[CardName("Talisman of Dominance",  "U", "B")]
[CardName("Talisman of Indulgence", "B", "R")]
[CardName("Talisman of Impulse",    "R", "G")]
[CardName("Talisman of Unity",      "G", "W")]
[CardName("Talisman of Hierarchy",  "W", "B")]
[CardName("Talisman of Curiosity",  "G", "U")]
[CardName("Talisman of Resilience", "B", "G")]
[CardName("Talisman of Conviction", "W", "R")]
[CardName("Talisman of Creativity", "U", "R")]
public static class TalismanCycleFactory
{
    /// <summary>Printed mana cost shared by every cycle member.</summary>
    public const string PrintedManaCost = "{2}";

    /// <summary>
    /// Fallback overload — only reachable when constructed by hand.
    /// Default-builds Talisman of Progress (the Mirrodin Azorius member,
    /// matching the pre-cycle <c>TalismanOfProgressFactory</c> shape).
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, new[] { "Talisman of Progress", "W", "U" });

    /// <summary>
    /// Construct the talisman identified by <paramref name="args"/>.
    /// </summary>
    /// <param name="owner">The player who owns and initially controls the talisman.</param>
    /// <param name="args">
    /// Source-generator-provided args. Layout:
    /// <c>[0] = printed card name</c> (e.g. "Talisman of Dominance"),
    /// <c>[1] = first coloured option (single-letter Scryfall code, e.g. "U")</c>,
    /// <c>[2] = second coloured option (e.g. "B")</c>.
    /// </param>
    public static Artifact Create(Player owner, string[] args)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length < 3)
        {
            throw new ArgumentException(
                $"TalismanCycleFactory needs args = [name, colorA, colorB] (got {args.Length}).",
                nameof(args));
        }

        var cardName = args[0];
        var colorA = args[1];
        var colorB = args[2];

        var talisman = new Artifact(cardName, PrintedManaCost);
        talisman.SetOwner(owner);
        talisman.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Add {C}. CR 605.1. {C} folds into the generic bucket per
        // ManaCost.Parse (CR 107.4c). Painless — only the coloured mode
        // carries the "deals 1 damage to you" rider.
        // ----------------------------------------------------------------
        talisman.AddAbility(new ManaAbility(talisman, owner, ManaCost.Parse("C")));

        // ----------------------------------------------------------------
        // {T}: Add {A}. <Name> deals 1 damage to you.
        // {T}: Add {B}. <Name> deals 1 damage to you.
        // Modelled as two ManaAbility instances (one per coloured option),
        // each riding the painland additional-cost shape:
        //   canActivateCheck:    !IsTapped (the {T} half; tap happens in
        //                         ManaAbility.Activate)
        //   additionalCostPayer: controller.LoseLife(1)
        //
        // Note: no LifeTotal > 1 gate on activation (distinct from Horizon
        // Canopy's Pay-1-life). Damage is allowed to reduce the controller
        // to 0 or below — SBAs (CR 704.5a) handle the lethal-damage loss
        // condition, matching the printed talisman semantics.
        // ----------------------------------------------------------------
        AttachPainColoredMana(talisman, owner, colorA);
        AttachPainColoredMana(talisman, owner, colorB);

        return talisman;
    }

    /// <summary>
    /// Attach a <c>{T}: Add &lt;color&gt;. This deals 1 damage to you.</c>
    /// mana ability. Built via the additional-cost overload of
    /// <see cref="ManaAbility"/>: tapping pays {T}; the
    /// <c>additionalCostPayer</c> then reduces the controller's life by 1
    /// (CR 120.3 — damage to a player causes loss of life equal to that
    /// damage). No life-floor gate (unlike the Horizon Canopy "Pay 1 life"
    /// shape, CR 119.4) — talismans can deal lethal damage to you.
    /// </summary>
    private static void AttachPainColoredMana(Artifact talisman, Player controller, string color)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            throw new ArgumentException("Color required", nameof(color));
        }

        var mana = ManaCost.Parse(color);
        talisman.AddAbility(new ManaAbility(
            source: talisman,
            controller: controller,
            manaGenerated: mana,
            canActivateCheck: () => !talisman.IsTapped,
            additionalCostPayer: p => p.LoseLife(1)));
    }
}
