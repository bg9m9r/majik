using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Borrowed Time (Murders at Karlov Manor, {2}{W}).
///
/// Enchantment. Oracle text (verified against Scryfall 2026-06-02):
///   "When this enchantment enters, exile target nonland permanent an
///    opponent controls until this enchantment leaves the battlefield."
///
/// This is the pure "Oblivion Ring" backbone — printed text is identical to
/// <see cref="BanishingLightFactory"/> modulo the card name and the
/// "this enchantment" self-reference wording (same cost {2}{W}, same single
/// 1..1 "target nonland permanent an opponent controls", same exile-on-ETB /
/// return-on-LTB shape). Because the mechanics are byte-for-byte the Banishing
/// Light pair, the ETB/LTB triggers are delegated to the shared
/// <see cref="BanishingLightFactory.WireExileEnchantmentTriggers"/> closure
/// rather than re-implemented (the same reuse posture Conclave Tribunal takes).
///
/// ## Implemented (v1)
/// - <b>Enchantment {2}{W}</b>. Base shape (name / Enchantment / cost)
///   materialised from the embedded JSON definition (<c>borrowed-time.json</c>)
///   via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> — same posture as
///   <see cref="DetentionSphereFactory"/> (the JSON schema doesn't express the
///   exile-and-return triggers, so they are layered on here).
/// - <b>ETB triggered ability</b> (CR 603.6a / CR 701.21): single 1..1
///   "target nonland permanent an opponent controls". On resolve, after a
///   CR 608.2b legality re-check (still on the battlefield, still nonland,
///   controlled by an opponent of the Borrowed Time controller — CR 109.5),
///   it exiles the target and captures it (paired with its owner) in a
///   per-instance closure shared with the LTB ability.
/// - <b>LTB triggered ability</b> (CR 603.6c / CR 603.10c): fires whenever
///   Borrowed Time moves OUT of the battlefield (any destination — covers
///   dies + bounce + flicker, matching "leaves the battlefield" wording). On
///   resolve, the still-exiled captured card returns to the battlefield under
///   its owner's control (CR 110.2 — Controller := Owner on the way back).
///
/// ## Deferred (v1 gaps)
/// - <b>Flicker race</b>: if Borrowed Time is flickered, the LTB returns the
///   exiled card before the flickered enchantment re-enters; the re-entered
///   enchantment is a new object (CR 400.7) with an empty closure — matching
///   real MTG. (Inherited verbatim from the shared Banishing Light wiring.)
/// </summary>
[CardName("Borrowed Time")]
public static class BorrowedTimeFactory
{
    public const string CardName = "Borrowed Time";
    public const string Slug = "borrowed-time";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Borrowed Time with no runtime services. Both triggered
    /// abilities are attached to the card shape; neither is registered with a
    /// <see cref="TriggerManager"/>. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Borrowed Time with optional runtime services. When
    /// <paramref name="triggers"/> is supplied, both ETB and LTB abilities are
    /// registered so the bus drives them via
    /// <see cref="Majik.Core.Domain.DomainEvents.CardMovedEvent"/>.
    /// </summary>
    public static Enchantment Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var built = CardDefinitionFactory.Build(Definition, owner);
        if (built is not Enchantment card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as an Enchantment but got "
                + $"'{built.GetType().Name}'.");
        }
        card.SetOwner(owner);
        card.SetController(owner);

        // Printed text is the Banishing Light "exile target nonland permanent
        // an opponent controls until this leaves" pair verbatim — delegate to
        // the shared closure rather than duplicate it.
        BanishingLightFactory.WireExileEnchantmentTriggers(card, owner, triggers);
        return card;
    }
}
