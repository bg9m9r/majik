using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Crypt of Agadeem (Zendikar / reprints).
///
/// Land. Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    {T}: Add {B}.
///    {2}, {T}: Add {B} for each black creature card in your graveyard."
///
/// Scryfall-confirmed type line: Land (no basic supertype, no subtypes,
/// empty mana cost).
///
/// ## Card identity comes from JSON
///
/// Name / type and the basic <b>{T}: Add {B}</b> mana ability are loaded
/// from the embedded JSON definition (<c>crypt-of-agadeem.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The unconditional enters-tapped
/// replacement and the <b>{2},{T}: Add {B} for each black creature card in
/// your graveyard</b> dynamic ability are attached in code (the JSON schema
/// models neither).
///
/// ## Implemented (v1)
/// - <b>Land</b> with no printed subtype (non-basic, empty mana cost).
/// - <b>Enters-tapped replacement (CR 614.1c)</b> — unconditional "This
///   land enters tapped." Registered via <see cref="EntersTappedReplacement"/>
///   on a supplied <see cref="ReplacementBus"/>, mirroring
///   <see cref="BojukaBogFactory"/>. Shape-only path (no
///   <see cref="ReplacementBus"/>) skips registration and the land enters
///   untapped — the same posture every always-tapped factory takes.
/// - <b>{T}: Add {B}</b> — vanilla <see cref="ManaAbility"/> from JSON
///   (CR 605.1 — mana ability, no stack).
/// - <b>{2}, {T}: Add {B} for each black creature card in your graveyard.</b>
///   Modelled with the dynamic <c>Func&lt;ManaCost&gt; manaGenerator</c> +
///   <c>additionalCostPayer</c> <see cref="ManaAbility"/> ctor, mirroring
///   <see cref="CabalCoffersFactory"/> (which counts Swamps on the
///   battlefield; Crypt counts black creature cards in the graveyard
///   instead). The <c>canActivateCheck</c> gates on untapped state AND
///   affordability of the {2}; the {2} is drained by the additional-cost
///   payer after the {T} tap (both part of the same atomic activation cost
///   per CR 602.2a / 605.1).
///
/// ## "black creature card in your graveyard" (CR 105 / 202.2 / 700.4)
/// Delegates to <see cref="CountBlackCreatureCardsInGraveyard"/>: counts
/// cards in the controller's graveyard that have the Creature card type
/// (CR 308) AND include black among their colors
/// (<see cref="CardColors.GetColors"/> — CR 105 colour from mana cost +
/// color indicator). A card can be both another type and a creature; only
/// the Creature type matters here.
///
/// ## Zero-creature activation (CR 605.1c)
/// Activating a mana ability is legal even when the net mana produced is
/// zero. With no black creature cards in the graveyard the generator
/// returns <see cref="ManaCost.Zero"/>; the {2} was still paid and the land
/// is still tapped. Intentional and correct (mirrors Cabal Coffers'
/// zero-Swamp case).
///
/// ## References
/// - <see cref="CabalCoffersFactory"/> — "{2},{T}: Add {B} for each …"
///   dynamic-mana shape this factory directly mirrors.
/// - <see cref="BojukaBogFactory"/> — unconditional enters-tapped land +
///   {T}: Add {B}.
/// </summary>
[CardName("Crypt of Agadeem")]
public static class CryptOfAgadeemFactory
{
    public const string CardName = "Crypt of Agadeem";

    private static readonly ManaCost TapAdditionalCost = ManaCost.Parse("2");

    /// <summary>
    /// Construct Crypt of Agadeem with no live wiring. Identity + the
    /// {T}: Add {B} mana ability (from JSON) and the {2},{T} dynamic
    /// ability are attached for shape inspection; the enters-tapped
    /// replacement is omitted (no <see cref="ReplacementBus"/> available),
    /// so on this path the land enters untapped — the same shape-only
    /// posture every always-tapped factory takes. Suitable for card-shape /
    /// dispatcher tests.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>
    /// Construct Crypt of Agadeem. When <paramref name="replacements"/> is
    /// supplied the unconditional enters-tapped restriction is registered
    /// (CR 614.1c) so the land enters tapped.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">When supplied, the "This land enters
    /// tapped" replacement is registered (CR 614.1c).</param>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity + the {T}: Add {B} mana ability come from JSON.
        var definition = CardDefinitionLoader.FromEmbeddedResource("crypt-of-agadeem");
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // Enters-tapped replacement — CR 614.1c.
        //   "This land enters tapped."
        // Unconditional; no gate. Shape-only path (no ReplacementBus)
        // skips registration and the land enters untapped.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        // ----------------------------------------------------------------
        // {2}, {T}: Add {B} for each black creature card in your graveyard.
        //
        // CR 605.1 — mana ability (produces mana, no target, doesn't use
        // the stack). The {2} is an additional mana cost paid alongside the
        // {T}, declared via the dynamic-mana + additionalCostPayer ctor so
        // it composes cleanly with the graveyard-counting generator
        // (mirrors CabalCoffersFactory).
        //
        //   canActivateCheck:
        //     1. Land is not already tapped (standard {T} gate).
        //     2. Controller's mana pool can pay {2} (read-only check).
        //
        //   manaGenerator lambda:
        //     1. Count black creature cards in the owner's graveyard.
        //     2. Return N × {B} (ManaCost.Zero when N == 0 — CR 605.1c).
        //
        //   additionalCostPayer (runs after the {T} tap):
        //     owner.PayMana({2}) — drains 2 generic mana from pool. Part of
        //     the same atomic activation cost (CR 602.2a); the
        //     canActivateCheck already verified affordability.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(
            source: land,
            controller: owner,
            manaGenerator: () => BuildBlackMana(CountBlackCreatureCardsInGraveyard(owner)),
            canActivateCheck: () =>
                !land.IsTapped
                && owner.ManaPool.CanPay(TapAdditionalCost),
            additionalCostPayer: controller => controller.PayMana(TapAdditionalCost)));

        return land;
    }

    /// <summary>
    /// Count how many black creature cards are currently in
    /// <paramref name="controller"/>'s graveyard. A card counts when it has
    /// the Creature card type (CR 308) AND includes black among its colors
    /// (CR 105 — derived from mana cost + color indicator via
    /// <see cref="CardColors.GetColors"/>). Returns 0 for null input.
    /// Exposed as a public helper for tests and bot policies.
    /// </summary>
    public static int CountBlackCreatureCardsInGraveyard(Player controller)
    {
        if (controller == null) return 0;

        var count = 0;
        foreach (var card in controller.Zones.Graveyard.GetCards())
        {
            if (card.HasType(CardType.Creature)
                && CardColors.GetColors(card).Contains(ManaColor.Black))
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// Build a <see cref="ManaCost"/> representing <paramref name="n"/>
    /// black mana pips. Returns <see cref="ManaCost.Zero"/> when
    /// <paramref name="n"/> is ≤ 0. Mirrors
    /// <see cref="CabalCoffersFactory.BuildBlackMana"/>.
    /// </summary>
    internal static ManaCost BuildBlackMana(int n)
    {
        if (n <= 0) return ManaCost.Zero;
        return ManaCost.Parse(string.Concat(Enumerable.Repeat("{B}", n)));
    }
}
