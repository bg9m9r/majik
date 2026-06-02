using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Painted Bluffs (Apocalypse).
///
/// Land — Desert, no mana cost. Oracle text (verified against Scryfall):
///   "{T}: Add {C}.
///    {1}, {T}: Add one mana of any color."
///
/// <para>
/// Painted Bluffs is the colourless-{C} sibling of Survivors' Encampment /
/// Rupture Spire: a vanilla <c>{T}: Add {C}</c> mode plus a five-colour
/// "add one mana of any color" mode whose additional cost is <c>{1}</c>
/// (generic mana) rather than tapping a creature (Survivors' Encampment) or
/// paying life (City of Brass). It is a strict "mana filter" — pay {1} +
/// {T}, get any one colour back — so it can fix colour but never ramp.
/// </para>
///
/// <para>
/// The Land shell (identity / Desert subtype / owner / controller) and the
/// vanilla <c>{T}: Add {C}</c> mana ability are declared declaratively in
/// <c>Majik.Core/CardData/Cards/painted-bluffs.json</c> and materialized via
/// <see cref="CardDefinitionFactory"/>, the same posture as
/// <see cref="SurvivorsEncampmentFactory"/>. The five any-colour
/// (<c>{1},{T}</c>) abilities are attached on top in C# because the data-only
/// <see cref="ManaAbilityDefinition"/> schema carries only a <c>Produces</c>
/// string — it can express neither the five-colour any-colour fan-out nor the
/// generic-mana <c>{1}</c> additional cost. The JSON therefore declares only
/// the {C} ability; this factory adds the rest.
/// </para>
///
/// ## Implemented (v1)
/// - <b>Land — Desert identity</b> — non-basic, single
///   <see cref="Majik.Core.Cards.Types.CardSubtype.Desert"/> subtype,
///   empty mana cost (JSON).
/// - <b>{T}: Add {C}</b> — vanilla mana ability (CR 605.1, no stack)
///   declared in JSON. {C} folds into the generic bucket per
///   <c>ManaCost.Parse</c> (same posture as Survivors' Encampment /
///   Rogue's Passage).
/// - <b>{1}, {T}: Add one mana of any color</b> — five
///   <see cref="PaintedBluffsManaAbility"/> slots (one per WUBRG). Each pays
///   the land's implicit self-{T} plus a <c>{1}</c> generic-mana additional
///   cost concurrently (CR 605.1; CR 602.2 — the {1} is part of the
///   activation cost, paid from the controller's mana pool). The
///   net mana is colour-neutral (one in, one out) so this never ramps.
///
/// ## Deferred (v1 gaps)
/// - <b>Agent prompt for which colour to add</b> — covered by the per-colour
///   ability shape: the activator picks the colour by picking the matching
///   ability slot, no separate prompt needed (same posture as Survivors'
///   Encampment / Springleaf Drum).
/// - <b>"May pay" interaction for {1}</b> — the {1} is a hard additional cost
///   here (not an optional pay-or-sacrifice tail), so no agent yes/no prompt
///   is involved; affordability is gated up-front by <c>canActivateCheck</c>
///   (CR 119.4 — you can't activate an ability whose cost you can't pay).
/// </summary>
[CardName("Painted Bluffs")]
public static class PaintedBluffsFactory
{
    public const string CardName = "Painted Bluffs";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("painted-bluffs");

    /// <summary>
    /// Construct Painted Bluffs owned and controlled by
    /// <paramref name="owner"/> with the {C} mana ability (from JSON) and all
    /// five any-colour ({1},{T}) mana abilities attached. No live runtime
    /// wiring is required — the card carries no triggers / continuous effects.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Land — Desert + {T}: Add {C}, materialized from JSON.
        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // {1}, {T}: Add one mana of any color. Five per-colour
        // PaintedBluffsManaAbility slots — the self-{T} and the {1}
        // generic-mana additional cost are paid concurrently (CR 605.1;
        // CR 602.2). The activator picks the colour by picking the slot.
        // ----------------------------------------------------------------
        foreach (var pip in new[] { "W", "U", "B", "R", "G" })
        {
            land.AddAbility(new PaintedBluffsManaAbility(land, owner, pip));
        }

        return land;
    }
}

/// <summary>
/// Painted Bluffs' per-colour <c>{1},{T}: Add one mana of any color</c>
/// ability. Subclasses <see cref="ManaAbility"/> and exposes its
/// <see cref="ColorPip"/> so tests / agents can inspect or select a specific
/// colour slot — same shape as <see cref="SpringleafDrumManaAbility"/>, but
/// the additional cost is a generic <c>{1}</c> paid from the controller's
/// mana pool rather than tapping a creature.
/// </summary>
public sealed class PaintedBluffsManaAbility : ManaAbility
{
    /// <summary>The generic-mana additional cost paid on activation.</summary>
    public const string AdditionalCost = "1";

    /// <summary>
    /// Colour pip this ability produces (one of W / U / B / R / G).
    /// </summary>
    public string ColorPip { get; }

    internal PaintedBluffsManaAbility(
        Land source,
        Player controller,
        string colorPip)
        : base(
            source: source,
            controller: controller,
            manaGenerated: ManaCost.Parse(colorPip),
            // CR 119.4 — only activatable when the full cost is payable:
            // the land is untapped (can pay {T}) AND the controller can
            // afford the {1} additional cost. The base ManaAbility already
            // applies the summoning-sickness/tap gate before this check; the
            // {T} mode never gates on zone either (same posture as
            // Survivors' Encampment / Springleaf Drum).
            canActivateCheck: () => !source.IsTapped
                && controller.ManaPool.CanPay(ManaCost.Parse(AdditionalCost)),
            // CR 602.2 — pay the {1} additional cost from the mana pool,
            // concurrently with the self-{T} (CR 605.1, no stack).
            additionalCostPayer: p => p.PayMana(ManaCost.Parse(AdditionalCost)))
    {
        ColorPip = colorPip;
    }
}
