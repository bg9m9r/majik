using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Talisman of Progress (Mirrodin, {2}).
///
/// Artifact. Oracle text:
///   "{T}: Add {C}.
///    {T}: Add {W} or {U}. Talisman of Progress deals 1 damage to you."
///
/// ## Implemented (v1)
/// - Card identity (Artifact, mana cost {2}, owner / controller wiring).
/// - <b>{T}: Add {C}</b> — single <see cref="ManaAbility"/> (CR 605.1).
///   {C} folds into the generic bucket via <see cref="ManaCost.Parse"/>
///   per CR 107.4c (see <c>ManaCost.cs:170</c>). Same shape as Mind
///   Stone's painless body.
/// - <b>{T}: Add {W} or {U}, deal 1 damage to you</b> — modelled as TWO
///   <see cref="ManaAbility"/> instances (one per allied colour W and U),
///   each using the (source, controller, manaGenerated, canActivateCheck,
///   additionalCostPayer) overload — the same painland shape
///   <see cref="HorizonLandBinder.AttachPayLifeMana"/> uses for the
///   Horizon Canopy cycle.
///     - <c>canActivateCheck</c> = <c>!IsTapped</c> (the {T} half of the
///       cost; the engine taps in <see cref="ManaAbility.Activate"/>).
///     - <c>additionalCostPayer</c> = <c>controller.LoseLife(1)</c> — the
///       printed "deals 1 damage to you" rider modelled as a non-mana
///       activation cost (rules-equivalent for the talisman cycle: the
///       coloured pip is gated by life loss). Same posture as
///       <see cref="HorizonLandBinder"/>'s painless dual cycle.
/// - Bot's source-picker scans by produced colour and uses the painful
///   abilities transparently when paying coloured costs.
///
/// ## Rules note — pain modelled as cost, not trigger
/// The printed Oracle text for the talisman cycle (post-CR cleanup) reads
/// "Talisman of Progress deals 1 damage to you" as a follow-on clause to
/// the activation, which strictly is a triggered ability of the mana
/// ability (CR 605.1b — mana abilities can have triggered side effects).
/// v1 collapses this into an additional activation cost (the painland
/// pattern) for two reasons:
/// 1. The engine's painland infrastructure (<see cref="HorizonLandBinder"/>)
///    already gives a clean, bot-friendly surface for "pay X to add coloured
///    mana"; reusing it keeps the talisman cycle in lockstep with the rest
///    of the pain-for-fixing family.
/// 2. CR 605.1b triggered-mana side effects don't compose with the engine's
///    mana-source picker yet — the picker only consults activation legality,
///    not in-flight triggers. Modelling the damage as a cost surfaces it to
///    the picker (which now sees "this ability costs 1 life") and matches
///    every test that asserts "tapping this for a coloured pip ledgers the
///    1 damage".
///
/// ## Implemented colour pair
/// W, U — Talisman of Progress is the Azorius member of the Mirrodin cycle
/// (CR 205.3 — colour identity per the printed pips). The factory is
/// per-card (one of five Mirrodin talismans); a parametric cycle factory
/// (cf. <see cref="HorizonLandCycleFactory"/>) is a candidate refactor
/// once the other four talismans land.
///
/// ## Deferred (v1 gaps)
/// - <b>True trigger surface</b>: see the rules note above. The printed
///   damage-to-you should fire as a triggered side effect of the mana
///   activation when the engine's mana-source picker grows trigger
///   awareness.
/// - <b>Single modal-colour mana ability</b>: "Add {W} or {U}" is bound
///   as two separate <see cref="ManaAbility"/> instances; the bot's
///   source-picker selects the right colour at payment time. Same posture
///   as every dual-colour mana source (Adarkar Wastes, Hallowed Fountain,
///   the rest of the Talisman cycle once shipped).
/// - <b>Damage vs. life-loss routing</b>: the rider routes through
///   <see cref="Player.LoseLife"/> rather than a full
///   <see cref="Majik.Core.Events.DamageDealtEvent"/>. Same scope decision
///   as Mana Vault / Manabarbs / Dark Confidant. Damage-prevention
///   subscribers won't see the talisman's ping.
/// </summary>
[CardName("Talisman of Progress")]
public static class TalismanOfProgressFactory
{
    public const string CardName = "Talisman of Progress";
    public const string PrintedManaCost = "{2}";

    /// <summary>
    /// Construct Talisman of Progress owned and controlled by
    /// <paramref name="owner"/>.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var talisman = new Artifact(CardName, PrintedManaCost);
        talisman.SetOwner(owner);
        talisman.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Add {C}. CR 605.1. {C} folds into the generic bucket per
        // ManaCost.Parse (CR 107.4c).
        // ----------------------------------------------------------------
        talisman.AddAbility(new ManaAbility(talisman, owner, ManaCost.Parse("C")));

        // ----------------------------------------------------------------
        // {T}: Add {W} or {U}. Talisman of Progress deals 1 damage to you.
        // Modelled as two ManaAbility instances (one per allied colour),
        // each riding the painland additional-cost shape:
        //   canActivateCheck: !IsTapped (the {T} half; tap happens in
        //                      ManaAbility.Activate)
        //   additionalCostPayer: controller.LoseLife(1)
        // Mirrors HorizonLandBinder.AttachPayLifeMana's pattern — the
        // talisman cycle is the artifact analogue of the Mirrodin /
        // Adarkar painland family.
        //
        // Note: no LifeTotal > 1 gate on activation (distinct from Horizon
        // Canopy's Pay-1-life). Damage is allowed to reduce the controller
        // to 0 or below — SBAs (CR 704.5a) handle the lethal-damage loss
        // condition, matching the printed talisman semantics.
        // ----------------------------------------------------------------
        foreach (var color in new[] { "W", "U" })
        {
            talisman.AddAbility(new ManaAbility(
                source: talisman,
                controller: owner,
                manaGenerated: ManaCost.Parse(color),
                canActivateCheck: () => !talisman.IsTapped,
                additionalCostPayer: p => p.LoseLife(1)));
        }

        return talisman;
    }
}
