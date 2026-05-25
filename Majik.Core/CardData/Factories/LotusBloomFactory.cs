using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Lotus Bloom (Time Spiral, no printed mana cost
/// — Suspend 3—{0}).
///
/// Artifact. Oracle text:
///   "Suspend 3—{0} (Rather than cast this card from your hand, you may pay
///    {0} and exile it with three time counters on it. At the beginning of
///    your upkeep, remove a time counter. When the last is removed, cast it
///    without paying its mana cost.)
///    {T}, Sacrifice Lotus Bloom: Add three mana of any one color."
///
/// ## Implemented (v1)
/// - Card identity: Artifact with no printed mana cost. Per Scryfall (and
///   CR 202.1a), a card with no mana cost has mana value 0 and is treated
///   as "can't be cast for its mana cost"; the only legal path on the
///   stack is via the Suspend alt-cost. We surface this with
///   <see cref="Card.RestrictedCastZones"/> stamping <c>ZoneType.Hand</c>
///   (same plumbing as Hogaak — <see cref="HogaakFactory"/>) so any
///   <see cref="Majik.Core.Rules.CastSpellAction"/> originating from the
///   hand is rejected by <see cref="Majik.Core.Rules.ActionValidator"/>.
///   Suspend casts originate from <see cref="ZoneType.Exile"/> via
///   <see cref="CastFromExileAlternativeCost"/> + <c>isSuspendCast: true</c>
///   and bypass the hand-only restriction.
///   The stored mana-cost string is the empty string <c>""</c> mirroring
///   Scryfall's <c>mana_cost</c> field for Lotus Bloom (no pip — distinct
///   from <c>"{0}"</c> which renders the zero pip).
/// - "{T}, Sacrifice Lotus Bloom: Add three mana of any one color" is
///   modeled as five <see cref="ManaAbility"/> instances (one per WUBRG),
///   each producing three mana of that colour. Each ability uses the
///   <c>(source, controller, manaGenerated, canActivateCheck,
///   additionalCostPayer)</c> constructor; <c>canActivateCheck</c> ANDs
///   <c>!IsTapped</c> with "Lotus Bloom is still on the battlefield" so
///   only one of the five may activate per Bloom; <c>additionalCostPayer</c>
///   performs the sacrifice (CR 701.16) inline by moving the Bloom from
///   its controller's battlefield to its owner's graveyard.
/// - CR 605.1 — the ability is still a mana ability (no stack); the
///   sacrifice cost rides the activation as part of the cost. Same shape
///   as <see cref="LotusPetalFactory"/> but produces three mana of the
///   chosen colour (3 × Petal output, single-colour per activation).
/// - Suspend 3—{0} surfaced via <see cref="BuildSuspendCost"/> →
///   <see cref="SuspendAlternativeCost"/> (3 time counters, mana cost {0}).
///   Mirrors <see cref="RiftBoltFactory"/> / <see cref="SearchForTomorrowFactory"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>"Three mana of any one color" as a modal single ability</b>: bound
///   as five separate ManaAbility instances, one per WUBRG. The bot's
///   source-picker selects the right colour at payment time. Same gap as
///   Mox Opal / Delighted Halfling / City of Brass / Lotus Petal.
/// - <b>Oracle binder discovery for Suspend</b>: a binder pass for
///   "Suspend N—[cost]" isn't wired into
///   <see cref="OracleSpellBinder"/> yet — bots see suspend via
///   <see cref="BuildSuspendCost"/> or direct factory construction.
/// </summary>
[CardName("Lotus Bloom")]
public static class LotusBloomFactory
{
    public const string CardName = "Lotus Bloom";

    /// <summary>
    /// Lotus Bloom has no printed mana cost (Scryfall <c>mana_cost</c> is
    /// the empty string). Distinct from <c>"{0}"</c> which would render
    /// the zero pip — Lotus Bloom prints with no cost at all and can
    /// ONLY be cast via the Suspend alt-cost (CR 202.1a / CR 117.7c).
    /// </summary>
    public const string PrintedManaCost = "";

    public const string SuspendCostText = "{0}";
    public const int SuspendTimeCounters = 3;

    /// <summary>
    /// Output of one Bloom activation: three mana of the chosen colour.
    /// One <see cref="ManaAbility"/> per WUBRG, each producing
    /// <c>{C}{C}{C}</c> of its colour (e.g. <c>RRR</c> for the red mode).
    /// </summary>
    public const int ManaPerActivation = 3;

    /// <summary>
    /// Construct Lotus Bloom owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var bloom = new Artifact(
            CardName,
            PrintedManaCost,
            supertypes: null,
            subtypes: null);
        bloom.SetOwner(owner);
        bloom.SetController(owner);

        // CR 117.7c / 202.1a — Lotus Bloom has no printed mana cost, so it
        // can't be cast from hand for its mana cost. Suspend is the only
        // legal cast path; Suspend resolves to a cast from exile, so the
        // hand restriction doesn't block the post-suspend free cast.
        bloom.AddRestrictedCastZone(ZoneType.Hand);

        // ----------------------------------------------------------------
        // {T}, Sacrifice Lotus Bloom: Add three mana of any one color.
        // Five ManaAbility instances, one per WUBRG. Each is gated on:
        //   (1) Lotus Bloom is untapped, AND
        //   (2) Lotus Bloom is still on the battlefield (i.e. not yet
        //       sacrificed by a sibling activation).
        // The additionalCostPayer performs the sacrifice (CR 701.16)
        // inline — same shape as Lotus Petal but producing three mana of
        // the chosen colour per activation rather than one.
        // ----------------------------------------------------------------
        foreach (var color in new[] { "W", "U", "B", "R", "G" })
        {
            var triple = new string(color[0], ManaPerActivation); // "RRR" etc.
            bloom.AddAbility(new ManaAbility(
                source: bloom,
                controller: owner,
                manaGenerated: ManaCost.Parse(triple),
                canActivateCheck: () => !bloom.IsTapped
                                        && bloom.Zone == ZoneType.Battlefield,
                additionalCostPayer: _ => SacrificeBloom(bloom)));
        }

        return bloom;
    }

    /// <summary>The Suspend 3—{0} alt-cost printed on Lotus Bloom.
    /// CR 702.62.</summary>
    public static SuspendAlternativeCost BuildSuspendCost() =>
        new(SuspendTimeCounters, ManaCost.Parse(SuspendCostText));

    /// <summary>
    /// CR 701.16 — sacrifice: the owner moves their permanent from the
    /// battlefield to their graveyard. Idempotent: if Lotus Bloom has
    /// already been moved (defensive — shouldn't happen given the
    /// canActivateCheck gate) we no-op. Mirrors
    /// <see cref="LotusPetalFactory"/>'s SacrificePetal closure.
    /// </summary>
    private static void SacrificeBloom(Artifact bloom)
    {
        if (bloom.Zone != ZoneType.Battlefield) return;

        var controller = bloom.Controller;
        var owner = bloom.Owner;
        if (controller == null || owner == null) return;

        controller.Zones.Battlefield.RemoveCard(bloom);
        owner.Zones.Graveyard.AddCard(bloom);
        bloom.SetZone(ZoneType.Graveyard);
    }
}
