using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.Rules;

/// <summary>
/// CR 702.16 — <b>Domain</b>. "Domain abilities count the number of basic
/// land types among lands you control." The five basic land types
/// ({Plains, Island, Swamp, Mountain, Forest}) contribute; Wastes is a
/// basic LAND without a basic LAND TYPE (CR 305.6) so it doesn't count.
/// Non-basic lands that print basic land types — shocklands (Hallowed
/// Fountain = Plains + Island), original duals (Tropical Island = Forest
/// + Island), Triomes — contribute every basic type in their subtype
/// list. Fetchlands (Bloodstained Mire) have no basic land type and
/// contribute zero.
///
/// This is the canonical Domain counter used by all Domain-scaling cards
/// (Tribal Flames, Leyline Binding, Scion of Draco, and the rest of the
/// Domain / Coalition / WAR cycle). Cards that need cost reduction
/// scaled by domain should compose a
/// <see cref="Majik.Core.Abilities.DomainCostReductionAbility"/>; cards
/// that need a resolve-time scalar (Tribal Flames, Coalition Honor Guard)
/// can call <see cref="CountTypes(Player, ContinuousEffectsService?)"/>
/// directly.
///
/// Layer-pipeline awareness: when a
/// <see cref="ContinuousEffectsService"/> is supplied the count uses
/// effective subtypes from the CR 613 layer pipeline (Blood Moon,
/// Spreading Seas, Urborg, Yavimaya feed through). When null the printed
/// subtypes are used — suitable for cost-calculation paths that don't
/// hold a live layers service.
/// </summary>
public static class Domain
{
    /// <summary>
    /// The five basic land subtypes that contribute to Domain
    /// (CR 702.16 / CR 205.3i / 305.6). Wastes is NOT a basic LAND TYPE
    /// — it's a basic land without a basic land type — so it's
    /// deliberately omitted.
    /// </summary>
    public static readonly IReadOnlySet<CardSubtype> BasicLandTypes = new HashSet<CardSubtype>
    {
        CardSubtype.Plains,
        CardSubtype.Island,
        CardSubtype.Swamp,
        CardSubtype.Mountain,
        CardSubtype.Forest,
    };

    /// <summary>
    /// CR 702.16 — count distinct basic land types among lands the
    /// controller controls, using printed subtypes only. Convenience
    /// overload for cost-calculation callers without a live
    /// <see cref="ContinuousEffectsService"/>.
    /// </summary>
    public static int CountTypes(Player controller) =>
        CountTypes(controller, effects: null);

    /// <summary>
    /// CR 702.16 — count distinct basic land types among lands the
    /// controller controls. When <paramref name="effects"/> is supplied
    /// the count uses effective subtypes from the CR 613 layer pipeline
    /// (Blood Moon retyping nonbasics to Mountain etc.); otherwise
    /// printed subtypes are used.
    /// </summary>
    public static int CountTypes(Player controller, ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var seen = new HashSet<CardSubtype>();
        foreach (var card in controller.Zones.Battlefield.GetCards())
        {
            if (card is not Land land) continue;

            IEnumerable<CardSubtype> subtypes = effects is not null
                ? effects.Compute(land).Subtypes
                : land.Subtypes;

            foreach (var st in subtypes)
            {
                if (BasicLandTypes.Contains(st))
                {
                    seen.Add(st);
                    if (seen.Count == BasicLandTypes.Count) return seen.Count;
                }
            }
        }
        return seen.Count;
    }
}
