using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Spinewoods Armadillo (Bloomburrow, {4}{G}{G}).
///
/// Creature — Armadillo 7/7. Oracle text (Scryfall, verified):
///   "Reach
///    Ward {3} (Whenever this creature becomes the target of a spell or ability
///    an opponent controls, counter it unless that player pays {3}.)
///    {1}{G}, Discard this card: Search your library for a basic land card or a
///    Desert card, reveal it, put it into your hand, then shuffle. You gain 3
///    life."
///
/// ## Shape source
/// The entire card — identity (name, {4}{G}{G}, 7/7, Creature — Armadillo),
/// the Reach + Ward keyword markers, and the Channel-style discard-from-hand
/// activated ability — is loaded from
/// <c>Majik.Core/CardData/Cards/spinewoods-armadillo.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. Same fully-declarative posture as
/// <see cref="BoseijuFactory"/> (whose Channel ability is also
/// <c>{1}{G}</c> + <c>discard_self</c> in JSON).
///
/// ## Implemented (v1)
/// - <b>7/7 Creature — Armadillo</b> at {4}{G}{G}.
/// - <b>Reach (CR 702.9)</b> as a <see cref="Majik.Core.Abilities.KeywordAbility"/>
///   marker — lets the Armadillo block creatures with flying.
/// - <b>Ward {3} (CR 702.21)</b> as a keyword marker only. Same posture as
///   <see cref="AbolethSpawnFactory"/> / Tolarian Terror / Kappa Cannoneer —
///   the keyword is surfaced for introspection (UI / bots), but the
///   spell-resolution "counter unless they pay {3}" consultation is a deferred
///   cross-factory gap (no Ward trigger primitive on spell resolution yet). The
///   printed Ward cost ({3}) is therefore not carried as a value (the marker is
///   un-parameterized, matching every other Ward factory).
/// - <b>Channel-style activated ability (CR 702.74a)</b>:
///   <c>{1}{G}, Discard this card</c> — the <c>discard_self</c> cost
///   (<see cref="Majik.Core.Costs.DiscardSelfCost"/>) gates activation to the
///   Hand zone (CR 702.74a). On resolution:
///   <list type="bullet">
///     <item><b>Tutor (CR 701.19a / CR 701.20a)</b>: "Search your library for a
///     basic land card OR a Desert card, reveal it, put it into your hand, then
///     shuffle." Modelled by the shared <c>search_library</c> verb with
///     <c>subtypes: ["Desert"]</c> + the additive <c>includeBasicLands</c> flag,
///     so the found card may be EITHER a basic land (CR 205.4a — Land + Basic
///     supertype) OR a card with the Desert subtype (CR 205.3i) — a logical OR,
///     not the default AND. The shared verb prompts the controller's agent
///     (deterministic first-match fallback), moves the pick Library → Hand, and
///     shuffles once whether or not a card was found.</item>
///     <item><b>Lifegain (CR 119.3)</b>: "You gain 3 life." — the
///     <c>gain_life_self</c> verb on the ability's controller, after the
///     search.</item>
///   </list>
///
/// ## Deferred (v1 gaps)
/// - <b>Ward {3} trigger wiring</b>: keyword marker present; the
///   counter-unless-they-pay surface lands once the Ward trigger primitive is
///   plumbed onto spell resolution (sibling gap to every other Ward factory).
/// - <b>Reveal step</b>: the tutored card moves Library → Hand; the shared
///   <c>search_library</c> verb does not publish a public reveal event (same gap
///   as the rest of the tutor family). The card still reaches the hand, so the
///   observable game state is correct.
/// </summary>
[CardName("Spinewoods Armadillo")]
public static class SpinewoodsArmadilloFactory
{
    public const string CardName = "Spinewoods Armadillo";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("spinewoods-armadillo");

    /// <summary>
    /// Construct Spinewoods Armadillo. All behaviour (Reach + Ward keyword
    /// markers + the Channel-style discard tutor / lifegain activated ability)
    /// is JSON-driven through <see cref="CardDefinitionFactory"/>.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }
}
