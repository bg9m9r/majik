using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Pristine Talisman (Magic 2012, {3}).
///
/// Artifact. Oracle text (verified against Scryfall):
///   "{T}: Add {C}. You gain 1 life."
///
/// ## Implemented (v1)
/// - Artifact body / identity ({3}, owner / controller wiring) built from
///   <c>Majik.Core/CardData/Cards/pristine-talisman.json</c> via
///   <see cref="CardDefinitionFactory"/>.
/// - <b>{T}: Add {C}. You gain 1 life.</b> — a single
///   <see cref="ManaAbility"/> producing {C} (CR 605.1 — mana abilities
///   don't use the stack; {C} folds into the generic bucket via
///   <see cref="ManaCost.Parse"/> per CR 107.4c) carrying the
///   "you gain 1 life" rider through the additional-effect overload of
///   <see cref="ManaAbility"/> (the same seam the Horizon Canopy "Pay 1
///   life" painless-dual cycle uses for its life side-effect, see
///   <see cref="Majik.Core.CardData.HorizonLandBinder.AttachPayLifeMana"/>,
///   and the Ice Age painlands' damage rider — only the sign of the life
///   change differs).
///
/// <para>
/// CR 605.1b — a mana ability may have an additional non-mana effect; the
/// life gain happens as the ability resolves. Because mana abilities don't
/// use the stack, modelling the gain via the activation-side payer closure
/// is observationally identical: the {C} and the +1 life land together in
/// the same atomic step.
/// </para>
///
/// <para>
/// Unlike the Horizon Canopy "Pay 1 life" mode there is NO life-floor
/// activation gate: gaining life is always legal (CR 119.3 — a life gain
/// has no payment prerequisite), so the ability has no custom
/// <c>canActivateCheck</c> beyond the default untapped check.
/// </para>
/// </summary>
[CardName("Pristine Talisman")]
public static class PristineTalismanFactory
{
    public const string Slug = "pristine-talisman";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Pristine Talisman owned and controlled by
    /// <paramref name="owner"/>.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var talisman = (Artifact)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // {T}: Add {C}. You gain 1 life.
        // CR 605.1 — mana ability, doesn't use the stack. {C} folds into
        // the generic bucket via ManaCost.Parse (CR 107.4c). The "you gain
        // 1 life" rider rides on the additional-effect overload of
        // ManaAbility (CR 605.1b — a mana ability may carry a non-mana
        // effect); same seam as the Horizon Canopy / painland life riders,
        // only the sign differs. No life-floor gate — gaining life is
        // always legal (CR 119.3).
        // ----------------------------------------------------------------
        talisman.AddAbility(new ManaAbility(
            source: talisman,
            controller: owner,
            manaGenerated: ManaCost.Parse("C"),
            canActivateCheck: () => !talisman.IsTapped,
            additionalCostPayer: p => p.GainLife(1)));

        return talisman;
    }
}
