using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Insolent Neonate (Shadows over Innistrad, {R}).
///
/// Creature — Vampire Wizard 1/1. Oracle text:
///   "Menace (This creature can't be blocked except by two or more
///    creatures.)
///    Discard a card, Sacrifice this creature: Draw a card."
///
/// ## Implemented (v1)
///
/// - 1/1 Vampire Wizard with mana cost {R}, owner / controller stamped.
/// - <see cref="KeywordAbility"/> marker for Menace (CR 702.110), consumed
///   by <see cref="Majik.Core.Combat.CombatAbilities.HasMenace"/> (same
///   posture as Grief / Hive of the Eye Tyrant / Lord of Atlantis).
/// - <b>"Discard a card, Sacrifice this creature: Draw a card"</b> —
///   <see cref="ActivatedAbility"/> (CR 602.1) with two costs:
///   <list type="number">
///     <item><see cref="DiscardACardCost"/> — first cost, picks the first
///       card in the controller's hand (deterministic v1 picker, same
///       policy as <see cref="PsychicFrogFactory"/>'s pump activation /
///       <see cref="FaithlessSalvagingFactory"/>'s resolve-time discard).</item>
///     <item><see cref="AdditionalCost.Sacrifice"/> on the Neonate itself —
///       the cost surface registers the intent; the actual battlefield →
///       graveyard zone move is performed inside the effect closure
///       (mirrors <see cref="CausticCaterpillarFactory"/> / Aether
///       Spellbomb / Mind Stone — the generic <see cref="AdditionalCost.Pay"/>
///       sacrifice path is a no-op stub).</item>
///   </list>
///   Effect: draw one card from the top of the controller's library
///   (CR 121.1 — single top-of-library draw, empty library flags the SBA
///   loss via <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/>).
///
/// ## Order of operations
///
/// CR 117.1c — all costs for an activated ability are paid simultaneously
/// from the player's perspective. The implementation pays discard first,
/// then sacrifice, then resolves the draw effect, but the cost surface
/// makes both atomic (legality is checked before any payment).
///
/// ## Deferred (v1 gaps)
///
/// - <b>Discard pick prompt</b>: <see cref="DiscardACardCost.Target"/>
///   may be set by an agent before activation, otherwise the deterministic
///   first-in-hand picker fires. A real agent-driven "choose a card to
///   discard" prompt waits on the shared discard-prompt surface
///   (same gap as Faithless Looting / Psychic Frog / Liliana of the Veil).
/// - <b>Activation-zone gate</b>: <see cref="ActivatedAbility"/> doesn't
///   gate on <see cref="ZoneType.Battlefield"/> yet; the effect closure
///   guards on the Neonate's current zone before sacrificing so a stale
///   activation re-entry can't double-sacrifice.
/// </summary>
[CardName("Insolent Neonate")]
public static class InsolentNeonateFactory
{
    public const string CardName = "Insolent Neonate";
    public const string PrintedManaCost = "{R}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Insolent Neonate owned and controlled by
    /// <paramref name="owner"/>. Menace keyword marker + the discard-sac-
    /// draw activated ability are attached to the card. The ability is
    /// fully self-contained — no service wiring required (no event bus, no
    /// trigger manager, no continuous-effects service).
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Vampire, CardSubtype.Wizard });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.110 — Menace keyword marker. Consumed by
        // CombatAbilities.HasMenace at block-declaration time.
        card.AddAbility(new KeywordAbility("Menace", card, owner));

        // ----------------------------------------------------------------
        // Discard a card, Sacrifice this creature: Draw a card.
        // CR 602.1 — activated ability. Two costs (discard + sacrifice-
        // self), single effect (draw one). The sacrifice payment is
        // performed inside the effect closure because the generic
        // AdditionalCost.Sacrifice payment is a no-op stub (mirrors
        // Caustic Caterpillar / Aether Spellbomb / Mind Stone).
        // ----------------------------------------------------------------
        var drawEffect = new Effect(
            $"{CardName}: sacrifice self + draw a card",
            () =>
            {
                // Sacrifice payment — battlefield → owner's graveyard.
                // CR 701.16 — idempotent guard against stale activations.
                if (card.Zone == ZoneType.Battlefield)
                {
                    owner.Zones.Battlefield.RemoveCard(card);
                    owner.Zones.Graveyard.AddCard(card);
                    card.SetZone(ZoneType.Graveyard);
                }

                // CR 121.1 — draw one card from the top of the controller's
                // library. Empty library flags the CR 704.5b SBA loss via
                // MarkTriedToDrawFromEmptyLibrary (same handling as
                // Faithless Looting / Faithless Salvaging / Psychic Frog).
                var top = owner.Zones.Library.GetCards().FirstOrDefault();
                if (top == null)
                {
                    owner.MarkTriedToDrawFromEmptyLibrary();
                    return;
                }
                owner.Zones.Library.RemoveCard(top);
                owner.Zones.Hand.AddCard(top);
                top.SetZone(ZoneType.Hand);
            });

        var drawAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new DiscardACardCost(),
                AdditionalCost.Sacrifice(card),
            },
            effects: new IEffect[] { drawEffect });

        card.AddAbility(drawAbility);

        return card;
    }
}
