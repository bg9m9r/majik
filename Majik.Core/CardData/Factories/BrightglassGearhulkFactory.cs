using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Brightglass Gearhulk (Edge of Eternities, {G}{G}{W}{W}).
///
/// Artifact Creature — Construct 4/4. Oracle text:
///   "First strike, trample
///    When this creature enters, you may search your library for up to two
///    artifact, creature, and/or enchantment cards with mana value 1 or less,
///    reveal them, put them into your hand, then shuffle."
///
/// ## Shape source
/// Card identity (name, {G}{G}{W}{W}, 4/4, Artifact Creature — Construct,
/// First strike + Trample keyword markers) is loaded from
/// <c>Majik.Core/CardData/Cards/brightglass-gearhulk.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The ETB tutor is attached in code
/// below: the declarative <c>search_library</c> verb does not yet express
/// "up to two cards", a "mana value ≤ N" filter, or a multi-card-type OR
/// (artifact/creature/enchantment), so the bespoke ETB effect is hand-rolled
/// here — same posture as the suggested analogues
/// <see cref="TrinketMageFactory"/> / <see cref="StoneforgeMysticFactory"/>
/// (ETB tutor for a mana-value-restricted permanent into hand) extended to
/// "up to two" and the artifact/creature/enchantment type filter.
///
/// ## Implemented (v1)
/// - 4/4 Construct Artifact Creature at {G}{G}{W}{W}.
/// - <b>First strike (CR 702.7)</b> + <b>Trample (CR 702.19)</b> as
///   <see cref="KeywordAbility"/> markers (loaded from JSON).
/// - <b>ETB tutor (CR 603.6a / CR 701.19a)</b>: When Brightglass Gearhulk
///   enters, the controller's library is searched deterministically for up to
///   the first two cards that are an artifact, creature, and/or enchantment
///   with <see cref="ValueObjects.ManaCost.TotalValue"/> ≤ 1; each found card
///   is moved Library → Hand. Per CR 701.19a the search is a "may" and the v1
///   deterministic picker simply takes the first (up to two) eligible cards.
///   CR 701.20a shuffle is wired via
///   <see cref="LibraryShuffle.ShuffleLibrary"/>. The single-arg factory
///   attaches the trigger to the card but does NOT register it with a
///   <see cref="TriggerManager"/>; tests exercise the ETB effect by firing the
///   trigger manually or driving the card through ZoneService.
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal event</b>: the ETB tutor moves the cards to hand without
///   emitting a CardRevealedEvent. Wire a reveal when CardRevealedEvent
///   plumbing is exercised by an in-engine prompt path (same gap as
///   <see cref="TrinketMageFactory"/>).
/// - <b>Agent prompt for "you may" / "up to two"</b>: the deterministic picker
///   auto-takes the first up-to-two eligible cards; a full implementation would
///   prompt the controller for the choice (including declining or taking fewer
///   than two).
/// </summary>
[CardName("Brightglass Gearhulk")]
public static class BrightglassGearhulkFactory
{
    public const string CardName = "Brightglass Gearhulk";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("brightglass-gearhulk");

    /// <summary>
    /// Construct Brightglass Gearhulk with its keyword markers and ETB tutor
    /// trigger attached to the card shape but NOT registered with a
    /// <see cref="TriggerManager"/>. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Brightglass Gearhulk with optional <see cref="TriggerManager"/>
    /// wiring. When <paramref name="triggers"/> is supplied, the ETB trigger is
    /// registered so the relevant ETB event places it on the stack
    /// automatically (CR 603.3).
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a / CR 701.19a.
        //   "When this creature enters, you may search your library for up to
        //    two artifact, creature, and/or enchantment cards with mana value
        //    1 or less, reveal them, put them into your hand, then shuffle."
        //
        // v1: deterministic — take the first up-to-two eligible cards in the
        // library. CR 701.20a shuffle is wired via LibraryShuffle. Reveal-event
        // emission is the only outstanding gap (see class xmldoc).
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: tutor up to two artifact/creature/enchantment cards (mana value 1 or less) to hand",
            () =>
            {
                var picks = owner.Zones.Library.GetCards()
                    .OfType<Card>()
                    .Where(IsEligible)
                    .Take(2) // CR 701.19a — "up to two": the v1 picker takes the first two.
                    .ToList();

                // CR 701.19a — declining or no candidates is a legal no-op.
                foreach (var pick in picks)
                {
                    owner.Zones.Library.RemoveCard(pick);
                    owner.Zones.Hand.AddCard(pick);
                    pick.SetZone(ZoneType.Hand);
                }

                // CR 701.20a — shuffle after the search resolves, whether or
                // not any card was found.
                LibraryShuffle.ShuffleLibrary(owner, "brightglass-gearhulk");
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }

    /// <summary>
    /// Eligibility predicate (CR 109.2 / CR 202.3): an artifact, creature,
    /// and/or enchantment card with mana value 1 or less. Card types are an OR
    /// (a card matches if it is any one of them) — e.g. an Artifact Creature
    /// qualifies, as does a plain Enchantment.
    /// </summary>
    private static bool IsEligible(Card c) =>
        c.ManaCostValue.TotalValue <= 1
        && (c.HasType(CardType.Artifact)
            || c.HasType(CardType.Creature)
            || c.HasType(CardType.Enchantment));
}
