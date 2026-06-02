using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cast Out (Hour of Devastation, {3}{W}).
///
/// Enchantment. Oracle text:
///   "Flash
///    When this enchantment enters, exile target nonland permanent an
///    opponent controls until this enchantment leaves the battlefield.
///    Cycling {W} ({W}, Discard this card: Draw a card.)"
///
/// Cast Out is the "Oblivion Ring" exile-until-leaves template
/// (CR 701.21) bolted onto two riders: instant-speed casting via Flash
/// (CR 702.8) and a {W} cycling escape hatch (CR 702.32) for when the
/// exile body is dead in hand. The exile-on-ETB / return-on-LTB backbone
/// is byte-identical to <see cref="BanishingLightFactory"/> — both share
/// the printed "target nonland permanent an opponent controls" target
/// and the same per-source closure that captures the exiled card between
/// the two triggered abilities — so it is routed through the shared
/// <see cref="BanishingLightFactory.WireExileEnchantmentTriggers"/>
/// primitive (same posture as <see cref="ConclaveTribunalFactory"/>).
///
/// ## Implemented (v1)
/// - <b>Enchantment {3}{W}</b>. Owner / controller wired.
/// - <b>Flash</b> (CR 702.8) — <see cref="KeywordAbility"/> marker; the
///   spell-casting rules consult the marker to allow casting at instant
///   speed.
/// - <b>ETB + LTB exile-until-leaves pair</b> — delegated to
///   <see cref="BanishingLightFactory.WireExileEnchantmentTriggers"/>:
///   <list type="bullet">
///     <item>ETB (CR 603.6a / CR 701.21): single 1..1 "target nonland
///       permanent an opponent controls" target; on resolve a CR 608.2b
///       legality re-check then a raw exile, captured in a per-Cast-Out
///       closure shared with the LTB ability.</item>
///     <item>LTB (CR 603.6c): when Cast Out leaves the battlefield the
///       captured card returns to the battlefield under its owner's
///       control (CR 110.2).</item>
///   </list>
/// - <b>Cycling {W}</b> (CR 702.32) — routed through the shared
///   <see cref="CyclingFactory.Build"/> primitive with cycle cost
///   <see cref="ManaCostCost"/>("{W}"). The primitive attaches the
///   <see cref="ActivatedAbility"/> + a "Cycling" <see cref="KeywordAbility"/>
///   marker, layers the <see cref="DiscardSelfCost"/> hand-zone gate
///   (CR 702.32a), and on resolve publishes
///   <see cref="CardCycledEvent"/> for any "Whenever a player cycles"
///   triggers (CR 702.32d).
///
/// ## Deferred (v1 gaps)
/// - Inherits the Banishing Light closure caveats (single permanent per
///   ETB resolution; flicker creates a fresh ICard identity with an empty
///   closure) — see
///   <see cref="BanishingLightFactory.WireExileEnchantmentTriggers"/>.
/// </summary>
[CardName("Cast Out")]
public static class CastOutFactory
{
    public const string CardName = "Cast Out";
    public const string PrintedManaCost = "{3}{W}";
    public const string CyclingManaCost = "{W}";

    /// <summary>
    /// Construct Cast Out with no runtime services. The ETB / LTB exile
    /// triggers + Flash + Cycling activated ability are attached to the
    /// card shape; neither triggered ability is registered with a
    /// <see cref="TriggerManager"/> and the cycling resolve body has no
    /// event bus (no <see cref="CardCycledEvent"/> publish). Suitable for
    /// shape / dispatcher tests.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Cast Out with optional runtime services. When
    /// <paramref name="triggers"/> is supplied, both ETB and LTB
    /// abilities are registered so the bus drives them via
    /// <see cref="CardMovedEvent"/>. When <paramref name="eventBus"/> is
    /// supplied the cycling resolve publishes <see cref="CardCycledEvent"/>
    /// (CR 702.32d).
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

        // CR 702.8 — Flash. Allows casting at instant speed.
        card.AddAbility(new KeywordAbility("Flash", card, owner));

        // CR 701.21 — exile target nonland permanent an opponent controls
        // until this leaves. Identical backbone to Banishing Light, so it
        // is routed through the shared wiring primitive.
        BanishingLightFactory.WireExileEnchantmentTriggers(card, owner, triggers);

        // CR 702.32 — Cycling {W}. Routed through the shared primitive;
        // the DiscardSelfCost hand-zone gate (CR 702.32a) + CardCycledEvent
        // publish (CR 702.32d) are appended automatically.
        CyclingFactory.Build(card, new ManaCostCost(CyclingManaCost), eventBus);

        return card;
    }
}
