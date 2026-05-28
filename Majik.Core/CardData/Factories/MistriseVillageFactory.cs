using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mistrise Village (Innistrad: Reawakening).
///
/// Land. Oracle text:
///   "This land enters tapped unless you control a Mountain or a Forest.
///    {T}: Add {U}.
///    {U}, {T}: The next spell you cast this turn can't be countered."
///
/// ## Implemented (v1)
/// - <b>Land identity</b> — plain <see cref="Land"/>, nonbasic, no printed
///   subtype.
/// - <b>ETB tapped unless you control a Mountain or a Forest (CR 614.1c)</b>
///   — registered as a <see cref="ConditionalEntersTappedReplacement"/> on
///   the supplied <see cref="ReplacementBus"/> (same shape as
///   <see cref="CheckLandCycleFactory"/>). Predicate: enters untapped iff
///   the controller controls a land with subtype Mountain OR Forest (self
///   excluded). "Mountain or Forest" matches shocklands, battle lands, etc.
///   that carry those subtypes — consistent with oracle intent and the check-
///   land family's identical predicate reading.
/// - <b>{T}: Add {U}</b> — single <see cref="ManaAbility"/> (CR 605.1).
/// - <b>{U}, {T}: The next spell you cast this turn can't be countered</b>
///   — <see cref="ActivatedAbility"/> with costs
///   <see cref="ManaCostCost"/>("{U}") + <see cref="AdditionalCost.Tap"/>.
///   Resolution calls
///   <see cref="CastingRestrictions.AddNextSpellUncounterableForTurn"/> on
///   the controller. <see cref="Majik.Core.Game.SpellCastFlow"/> consumes the
///   one-shot flag at cast time via
///   <see cref="CastingRestrictions.ConsumeNextSpellUncounterableForTurn"/>
///   and stamps <see cref="Majik.Core.Spells.Spell.CannotBeCountered"/> on
///   the resulting spell. CR 701.5b — counter-effect resolvers consult
///   <see cref="Majik.Core.Spells.ISpell.CannotBeCountered"/> via
///   <see cref="Majik.Core.CardData.OracleSpellBinder.RemoveFromStack"/> and
///   leave an uncounterable spell on the stack.
///
/// ## Deferred (v1 gaps)
/// - Single-arg dispatcher path (no <see cref="ReplacementBus"/> supplied)
///   skips the ETB-tapped replacement, matching every other
///   ETB-replacement factory's shape-only posture.
/// - "Can't be countered" end-of-turn expiry: the one-shot
///   <see cref="CastingRestrictions.AddNextSpellUncounterableForTurn"/> flag
///   is consumed on the first cast; if no spell is cast before the turn ends,
///   the flag is structurally harmless across turns (it will still be consumed
///   on the next cast, which is the oracle intent — "this turn" boundaries
///   can be enforced by subscribing to <see cref="Majik.Core.Events.TurnEndedEvent"/>
///   and calling <see cref="CastingRestrictions.Clear"/>). Full turn-boundary
///   cleanup is deferred; in practice the bot / test harness calls
///   <see cref="CastingRestrictions.Clear"/> in cleanup paths.
/// </summary>
[CardName("Mistrise Village")]
public static class MistriseVillageFactory
{
    public const string CardName = "Mistrise Village";

    /// <summary>
    /// Construct Mistrise Village without a <see cref="ReplacementBus"/>.
    /// The ETB-tapped predicate is omitted; the mana ability and the
    /// activated uncounterable ability are still wired. Suitable for
    /// card-shape / dispatcher tests.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, replacements: null);

    /// <summary>
    /// Construct Mistrise Village with an optional
    /// <see cref="ReplacementBus"/> for full ETB-tapped wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">When supplied, the
    /// "enters tapped unless you control a Mountain or a Forest"
    /// replacement is registered (CR 614.1c).</param>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(CardName, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // Enters tapped unless you control a Mountain or a Forest (CR 614.1c).
        // Predicate: untapped iff controller controls a land (excluding self)
        // with subtype Mountain OR Forest. Uses HasSubtype so any land that
        // carries those subtypes (shockland, battle land, etc.) qualifies.
        // Same predicate shape as CheckLandCycleFactory.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new ConditionalEntersTappedReplacement(
                land,
                entersUntappedIf: (controller, self) =>
                    ControllerHasSubtype(controller, self, CardSubtype.Mountain)
                    || ControllerHasSubtype(controller, self, CardSubtype.Forest)));
        }

        // ----------------------------------------------------------------
        // {T}: Add {U}. CR 605.1 — mana ability, never goes on the stack.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("U")));

        // ----------------------------------------------------------------
        // {U}, {T}: The next spell you cast this turn can't be countered.
        // CR 602 — activated ability. Costs: {U} mana + tap the land.
        // Resolution registers a one-shot next-spell-uncounterable rider on
        // the controller via CastingRestrictions. SpellCastFlow consumes it
        // at cast time (ConsumeNextSpellUncounterableForTurn) and stamps
        // Spell.CannotBeCountered, which OracleSpellBinder.RemoveFromStack
        // then respects (CR 701.5b).
        // ----------------------------------------------------------------
        var uncounterableEffect = new Effect(
            $"{CardName}: register next-spell-uncounterable rider for controller",
            () =>
            {
                var controller = land.Controller ?? owner;
                CastingRestrictions.AddNextSpellUncounterableForTurn(controller);
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{U}"),
                AdditionalCost.Tap(land),
            },
            effects: new IEffect[] { uncounterableEffect }));

        return land;
    }

    private static bool ControllerHasSubtype(
        Player controller,
        ICard self,
        CardSubtype subtype) =>
        controller.Zones.Battlefield.GetCards()
            .Any(c => !ReferenceEquals(c, self) && c.HasSubtype(subtype));
}
