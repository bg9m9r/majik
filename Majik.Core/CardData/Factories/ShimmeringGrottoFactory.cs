using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Shimmering Grotto. Oracle text (verified against
/// Scryfall):
/// <code>
/// {T}: Add {C}.
/// {1}, {T}: Add one mana of any color.
/// </code>
///
/// A functional reprint of Unknown Shores. Composed entirely from existing
/// engine primitives — same posture as the cited analogues:
/// <list type="bullet">
///   <item><see cref="CrumblingVestigeFactory"/> / Wasteland — the repeatable
///     <b>{T}: Add {C}</b> <see cref="ManaAbility"/> (CR 605.1, no stack).
///     {C} (colourless, CR 107.4c) is the colorless mana TYPE:
///     <c>ManaCost.Parse("C")</c> tags it colorless (a subset of Generic per
///     CR 106.1b) so it pays generic pips yet is the only mana that can pay a
///     {C} cost pip.</item>
///   <item><see cref="FilterLandCycleFactory"/> — the <b>{1}</b> mana
///     additional cost on a mana ability, modelled via the additional-cost
///     overload of <see cref="ManaAbility"/>:
///     <c>canActivateCheck = !land.IsTapped &amp;&amp;
///     controller.ManaPool.CanPay({1})</c>,
///     <c>additionalCostPayer = controller.PayMana({1})</c>.</item>
///   <item><see cref="ManaConfluenceFactory"/> — the "Add one mana of any
///     color" any-colour fan-out, modelled as five sibling
///     <see cref="ManaAbility"/> slots (one per WUBRG). Bots/agents pick the
///     colour by picking the matching ability slot.</item>
/// </list>
///
/// The Land shell (identity / owner / controller) is declared declaratively
/// in <c>Majik.Core/CardData/Cards/shimmering-grotto.json</c> and materialized
/// via <see cref="CardDefinitionFactory"/>, the same posture as
/// <see cref="ManaConfluenceFactory"/>. The mana abilities are attached on top
/// in C# because the data-only <see cref="ManaAbilityDefinition"/> schema only
/// carries a <c>Produces</c> string — it cannot express the {1} additional
/// activation cost nor the five-colour any-colour fan-out. The JSON therefore
/// declares no mana abilities; this factory adds them.
///
/// ## Implemented (v1)
/// - Land identity (non-Basic, no subtype) via JSON.
/// - <b>{T}: Add {C}</b> — one vanilla <see cref="ManaAbility"/>, no extra
///   cost (CR 605.1; the {1} rider applies only to the any-colour modes).
/// - <b>{1}, {T}: Add one mana of any color</b> — five
///   <see cref="ManaAbility"/> instances (one per WUBRG), each built via the
///   additional-cost overload: <c>canActivateCheck</c> gates on
///   <c>!IsTapped &amp;&amp; ManaPool.CanPay({1})</c> and
///   <c>additionalCostPayer = PayMana({1})</c> deducts the printed {1} from
///   the controller's mana pool atomically with the {T} tap. The two modes
///   share the single {T} — once any mode is activated the land is tapped and
///   no further mode can pay {T} this turn (CR 605.1 — the tap is the shared
///   activation cost).
///
/// ## Deferred (v1 gaps)
/// - Activation of an any-colour mode requires {1} to already be in the mana
///   pool. The engine doesn't auto-tap other sources to feed the {1} cost (no
///   look-ahead "mana-fixer" planner) — the same posture every other
///   additional-mana-cost activated ability takes (filter lands, signets,
///   Springleaf Drum, etc.).
/// </summary>
[CardName("Shimmering Grotto")]
public static class ShimmeringGrottoFactory
{
    public const string CardName = "Shimmering Grotto";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("shimmering-grotto");

    /// <summary>Construct Shimmering Grotto owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // {T}: Add {C}. CR 605.1 — vanilla colourless mana ability, no extra
        // cost (the {1} rider applies only to the any-colour modes). {C}
        // parses to a colorless unit (a subset of Generic, CR 106.1b).
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("C")));

        // {1}, {T}: Add one mana of any color.
        //   Five ManaAbility instances (one per WUBRG) — same any-colour
        //   fan-out as Mana Confluence / Aether Hub. Each carries:
        //     - canActivateCheck: land untapped AND controller can pay {1}.
        //     - additionalCostPayer: deduct {1} from the mana pool (CR 605.1 —
        //       the {1} extra cost is paid as part of activation, atomically
        //       with the {T} tap), exactly the filter-land shape.
        var oneGeneric = ManaCost.Parse("1");
        foreach (var color in new[] { "W", "U", "B", "R", "G" })
        {
            var mana = ManaCost.Parse(color);
            land.AddAbility(new ManaAbility(
                source: land,
                controller: owner,
                manaGenerated: mana,
                canActivateCheck: () =>
                    !land.IsTapped &&
                    (land.Controller ?? owner).ManaPool.CanPay(oneGeneric),
                additionalCostPayer: p => p.PayMana(oneGeneric)));
        }

        return land;
    }
}
