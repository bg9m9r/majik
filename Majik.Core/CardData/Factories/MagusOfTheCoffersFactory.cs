using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Magus of the Coffers (Future Sight — {4}{B}).
///
/// Creature — Human Wizard 4/4. Oracle text (verified against Scryfall):
///   "{2}, {T}: Add {B} for each Swamp you control."
///
/// A "creature Cabal Coffers": the same {2},{T} Swamp-scaled black mana
/// ability (<see cref="CabalCoffersFactory"/>) printed on a 4/4 Human Wizard
/// body. The base shape (name, Creature — Human Wizard, {4}{B}, 4/4) is
/// materialised from the embedded JSON definition (<c>magus-of-the-coffers.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The single mana ability is
/// layered on top here — the JSON <c>AbilityDefinition</c> schema doesn't
/// express a {2},{T} dynamic-mana ability (same posture as
/// <see cref="MarwynTheNurturerFactory"/> / <see cref="CabalCoffersFactory"/>).
///
/// ## Implemented (v1)
///
/// ### "{2}, {T}: Add {B} for each Swamp you control." (CR 605.1 / 305.6)
/// Modelled as a <see cref="ManaAbility"/> with the dynamic
/// <c>Func&lt;ManaCost&gt;</c> generator overload + the
/// <c>additionalCostPayer</c> ctor, identical to
/// <see cref="CabalCoffersFactory"/>:
///   * <b>canActivateCheck</b> — gates on the source being untapped (standard
///     {T} cost) AND the controller's pool being able to pay {2}
///     (read-only <c>ManaPool.CanPay</c>; CR 119.4 — can't pay a cost you
///     can't afford).
///   * <b>manaGenerator</b> — counts the Swamps the controller controls
///     (<see cref="DefileFactory.CountSwamps"/>, CR 305.6) and returns that
///     many black pips ({B}×N), or <see cref="ManaCost.Zero"/> when N == 0
///     (CR 605.1c — activating a mana ability that yields no mana is legal).
///   * <b>additionalCostPayer</b> — drains {2} from the controller's pool as
///     part of the same atomic activation cost (CR 602.2a). The
///     canActivateCheck already verified affordability.
///
/// Magus of the Coffers is NOT a Swamp itself (no land type / Swamp subtype —
/// it's a creature), so it never counts toward its own ability (CR 305.6).
///
/// ## Why the controller is read live
/// The mana ability reads <see cref="Card.Controller"/> (falling back to the
/// owner) at activation time so control-changing effects pick the correct
/// Swamp pool. Mirrors Marwyn's power-read posture (CR 605.1 — counted at
/// activation).
///
/// ## Deferred (v1 gaps)
/// - <b>N × {B} as concatenated string</b>: reuses
///   <see cref="CabalCoffersFactory.BuildBlackMana"/>; a native
///   <c>ManaCost.BlackMana(n)</c> factory would be tidier (same deferral the
///   Coffers factory notes).
/// - <b>Summoning sickness</b>: a {T} ability on a creature is gated by the
///   summoning-sickness check upstream at activation validation
///   (CR 302.6); the <c>canActivateCheck</c> here covers only the
///   untapped + {2}-affordable predicate (same posture as
///   <see cref="MarwynTheNurturerFactory"/>).
/// </summary>
[CardName("Magus of the Coffers")]
public static class MagusOfTheCoffersFactory
{
    public const string CardName = "Magus of the Coffers";
    public const string Slug = "magus-of-the-coffers";

    private static readonly ManaCost TapAdditionalCost = ManaCost.Parse("2");

    /// <summary>
    /// Construct a Magus of the Coffers owned and controlled by
    /// <paramref name="owner"/>.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (Creature — Human
        // Wizard, {4}{B}, 4/4). The JSON carries no abilities — the single
        // mana ability is layered below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // {2}, {T}: Add {B} for each Swamp you control.
        //
        // CR 605.1 — mana ability (produces mana, no target, doesn't use the
        // stack). Same wiring as Cabal Coffers: the {2} is an additional mana
        // cost paid via additionalCostPayer alongside the {T}, composing with
        // the Func<ManaCost> Swamp-counting generator.
        // ----------------------------------------------------------------
        card.AddAbility(new ManaAbility(
            source: card,
            controller: owner,
            manaGenerator: () =>
                CabalCoffersFactory.BuildBlackMana(
                    DefileFactory.CountSwamps(card.Controller ?? owner)),
            canActivateCheck: () =>
                !card.IsTapped
                && (card.Controller ?? owner).ManaPool.CanPay(TapAdditionalCost),
            additionalCostPayer: controller => controller.PayMana(TapAdditionalCost)));

        return card;
    }
}
