using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Conclave Tribunal (Guilds of Ravnica, {4}{W}).
///
/// Enchantment. Oracle text:
///   "Convoke (Your creatures can help cast this spell. Each creature you
///    tap while casting this spell pays for {1} or one mana of that
///    creature's color.)
///    When Conclave Tribunal enters, exile target nonland permanent an
///    opponent controls until Conclave Tribunal leaves the battlefield."
///
/// Conclave Tribunal is Banishing Light with Convoke stapled on (the
/// printed mana cost is {4}{W} but the Convoke alt cost typically lands
/// it at {W} + four taps in a wide token deck). The ETB exile + LTB
/// return shape is identical — see
/// <see cref="BanishingLightFactory.WireExileEnchantmentTriggers"/> for
/// the shared closure.
///
/// ## Implemented (v1)
/// - <b>Enchantment {4}{W}</b>. Owner / controller wired.
/// - <b>Convoke keyword marker</b> (CR 702.51) — same inline
///   <see cref="KeywordAbility"/> shape as
///   <see cref="ChordOfCallingFactory"/> / <see cref="HogaakFactory"/>.
///   The marker is purely descriptive; per-cast cost reduction is
///   surfaced via <see cref="ConvokeAdditionalCost"/> (built on demand
///   by <see cref="BuildAdditionalCost"/>) — caller threads it through
///   the cast flow's <c>additionalCosts</c> parameter.
/// - <b>ETB triggered ability</b> + <b>LTB triggered ability</b>:
///   identical to Banishing Light (delegated to
///   <see cref="BanishingLightFactory.WireExileEnchantmentTriggers"/>).
///
/// ## Deferred (v1 gaps)
/// - Same Convoke-flow gaps documented on <see cref="ChordOfCallingFactory"/>:
///   the v1 cost-reduction path is the per-tap reducer
///   <see cref="ConvokeAlternativeCost.ReduceCost"/>; agent-driven
///   creature-tap prompts on the cast flow are deferred.
/// </summary>
[CardName("Conclave Tribunal")]
public static class ConclaveTribunalFactory
{
    public const string CardName = "Conclave Tribunal";
    public const string PrintedManaCost = "{4}{W}";

    /// <summary>
    /// Construct Conclave Tribunal with no runtime services. Convoke
    /// marker + ETB / LTB triggers are attached. Suitable for shape /
    /// dispatcher tests.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Conclave Tribunal with optional runtime services. When
    /// <paramref name="triggers"/> is supplied, both ETB and LTB
    /// abilities are registered so the bus drives them via
    /// <see cref="CardMovedEvent"/>.
    /// </summary>
    public static Enchantment Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(
            CardName,
            PrintedManaCost,
            supertypes: null,
            subtypes: null);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.51 — Convoke keyword marker. Marker is descriptive; the
        // cost-reduction primitive lives on the ConvokeAdditionalCost
        // returned by BuildAdditionalCost. Same inline attach pattern as
        // Chord of Calling.
        card.AddAbility(new KeywordAbility("Convoke", card, owner));

        // ETB exile + LTB return — identical shape to Banishing Light.
        BanishingLightFactory.WireExileEnchantmentTriggers(card, owner, triggers);

        return card;
    }

    /// <summary>
    /// Build the legacy marker-only <see cref="ConvokeAlternativeCost"/>
    /// that surfaces Convoke on this card without an attached creature
    /// selection. Returns the printed cost unchanged — useful for
    /// shape / template tests that just need a Convoke alt-cost marker.
    /// </summary>
    public static ConvokeAlternativeCost BuildAlternativeCost() =>
        new(ManaCost.Parse(PrintedManaCost));

    /// <summary>
    /// CR 702.51 — build the Convoke additional cost for this Conclave
    /// Tribunal spell with the caller-selected untapped creatures. Same
    /// shape as <see cref="ChordOfCallingFactory.BuildAdditionalCost"/>.
    /// </summary>
    public static ConvokeAdditionalCost BuildAdditionalCost(
        ICard card, IReadOnlyList<Creature> tappedCreatures) =>
        new(card, tappedCreatures);
}
