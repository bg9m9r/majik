using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bedlam Reveler (Eldritch Moon, {6}{R}{R}).
///
/// Creature — Devil Horror 3/4. Oracle text:
///   "Bedlam Reveler costs {1} less to cast for each instant and sorcery
///    card in your graveyard.
///    Prowess (Whenever you cast a noncreature spell, this creature gets
///    +1/+1 until end of turn.)
///    When Bedlam Reveler enters, discard your hand, then draw three cards."
///
/// ## Implemented (v1)
/// - 3/4 Devil Horror, mana cost {6}{R}{R}.
/// - <b>Cost reduction (CR 117.7)</b>: wired via
///   <see cref="SpellCostReductionAbility"/> on the card itself. The
///   reducer counts instant and sorcery cards in the caster's graveyard
///   at cost-calculation time and subtracts that many generic mana from
///   the printed {6} (floor-at-zero enforced inside
///   <see cref="CostReduction.GetEffectiveCost"/>). Coloured {R}{R} pips
///   are untouched (CR 117.7c). The ability lives on the spell (not a
///   battlefield permanent), so the scanning loop in
///   <c>GetEffectiveCost</c> for <see cref="SpellCostReductionAbility"/>
///   is bypassed; instead the card carries a <see cref="CostReductionAbility"/>
///   with a <c>TotalReducer</c> closure, which is the pattern used by
///   Scion of Draco / Domain (per-cast whole-reduction shape).
/// - <b>Prowess (CR 702.108)</b>: keyword marker wired as a
///   <see cref="KeywordAbility"/>. Live pump via <see cref="ProwessFactory.Build"/>
///   when a <see cref="ContinuousEffectsService"/> is supplied (same
///   pattern as MonasteryMentor / LedgerShredder).
/// - <b>ETB trigger (CR 603.1)</b>: fires when Bedlam Reveler enters the
///   battlefield. Effect: discard the entire hand, then draw three cards.
///   Discard walks <c>controller.Zones.Hand.GetCards().ToList()</c> and
///   moves each card hand → graveyard (deterministic, no choice — the
///   printed oracle says "discard your hand", not "choose cards to
///   discard"). Draw uses the standard top-of-library draw loop (CR 121.1).
///
/// ## Deferred (v1 gaps)
/// - Prowess live pump requires the (owner, triggers, effects) overload;
///   the single-arg path attaches the Prowess keyword marker only.
/// - Real per-card discard events (CardDiscardedEvent / DiscardEvent bus)
///   are not yet wired — the zone move is raw and compatible with the rest
///   of the v1 discard surface (same as Faithless Looting / Connive).
/// </summary>
public static class BedlamRevelerFactory
{
    public const string CardName = "Bedlam Reveler";
    public const string PrintedManaCost = "{6}{R}{R}";
    public const int Power = 3;
    public const int Toughness = 4;

    /// <summary>
    /// Construct Bedlam Reveler with no live bus / trigger-manager wiring.
    /// The ETB trigger and Prowess keyword are attached to the card for
    /// shape observability; Prowess pump is not wired (no effects service).
    /// Suitable for dispatcher / structural tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, effects: null);

    /// <summary>
    /// Construct Bedlam Reveler with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Not used directly by this factory; reserved
    /// for future lifecycle subscribers.</param>
    /// <param name="triggers">TriggerManager for the ETB trigger and (when
    /// effects is supplied) the Prowess trigger. May be null — both
    /// triggers are still attached to the card shape.</param>
    /// <param name="effects">ContinuousEffectsService for the Prowess pump
    /// (CR 613.1f, Layer 7c). May be null — Prowess pump is not wired
    /// when null (keyword marker still attaches).</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Devil, CardSubtype.Horror });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Cost reduction — CR 117.7: "This spell costs {1} less to cast
        // for each instant and sorcery card in your graveyard."
        // Uses the TotalReducer shape on CostReductionAbility (same as
        // Scion of Draco's Domain cost reduction) so the entire reduction
        // comes from one closure call at cost-calc time. The caster's
        // graveyard is sampled live at cast time, so mill effects that
        // occurred during the same turn feed through correctly.
        // Floor-at-zero is enforced in CostReduction.GetEffectiveCost;
        // coloured {R}{R} pips are untouched (CR 117.7c).
        // ----------------------------------------------------------------
        card.AddAbility(new CostReductionAbility(
            totalReducer: caster =>
                caster.Zones.Graveyard.GetCards()
                    .Count(c => c.HasType(CardType.Instant) || c.HasType(CardType.Sorcery)),
            description: "Bedlam Reveler costs {1} less to cast for each instant and sorcery card in your graveyard."));

        // ----------------------------------------------------------------
        // Prowess (CR 702.108) — "Whenever you cast a noncreature spell,
        // this creature gets +1/+1 until end of turn."
        // Keyword marker always attached. Live pump via ProwessFactory when
        // a ContinuousEffectsService is supplied (same pattern as
        // MonasteryMentor / LedgerShredder).
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Prowess", card, owner));

        if (effects != null)
        {
            card.ActiveEffects = effects;
            var prowessTrigger = ProwessFactory.Build(card, effects);
            card.AddAbility(prowessTrigger);
            triggers?.RegisterTriggeredAbility(prowessTrigger);
        }

        // ----------------------------------------------------------------
        // ETB trigger — CR 603.1: "When Bedlam Reveler enters, discard
        // your hand, then draw three cards."
        // The oracle says "discard your hand" — deterministic full-hand
        // discard with no choice (unlike FaithlessLooting's choose-2).
        // Walk the hand list to avoid mutation during enumeration.
        // Draw loop mirrors TreasureCruise's BuildResolveEffect (CR 121.1).
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: discard hand, draw 3 (when it enters)",
            () =>
            {
                var controller = card.Controller ?? owner;

                // CR 701.16 — "Discard your hand." Move every card from
                // hand to graveyard (no ordering requirement on the full
                // discard; same raw-move pattern as Liliana +1 / Wheel
                // of Fortune). ToList() snapshots before mutation.
                foreach (var handCard in controller.Zones.Hand.GetCards().ToList())
                {
                    controller.Zones.Hand.RemoveCard(handCard);
                    controller.Zones.Graveyard.AddCard(handCard);
                    handCard.SetZone(ZoneType.Graveyard);
                }

                // CR 121.1 — "Draw three cards." Three top-of-library
                // draws; empty library mid-draw flags the player for the
                // SBA loss (CR 704.5b) — handled by other systems.
                for (var i = 0; i < 3; i++)
                {
                    var top = controller.Zones.Library.GetCards().FirstOrDefault();
                    if (top == null)
                    {
                        controller.MarkTriedToDrawFromEmptyLibrary();
                        break;
                    }
                    controller.Zones.Library.RemoveCard(top);
                    controller.Zones.Hand.AddCard(top);
                    top.SetZone(ZoneType.Hand);
                }
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}
