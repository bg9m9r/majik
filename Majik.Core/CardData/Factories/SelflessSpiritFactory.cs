using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Selfless Spirit (Eldritch Moon, {1}{W}).
///
/// Creature — Spirit 2/1. Oracle text:
///   "Flying
///    Sacrifice this creature: Creatures you control gain indestructible
///    until end of turn."
///
/// ## Implemented (v1)
/// - 2/1 Creature — Spirit, mana cost {1}{W}, owner / controller wired.
/// - <b>Flying</b> keyword marker (CR 702.9) via <see cref="KeywordAbility"/>.
/// - <b>Sacrifice self → team indestructible EOT</b> as a single
///   <see cref="ActivatedAbility"/>:
///   <list type="bullet">
///     <item>Cost: <see cref="AdditionalCost.Sacrifice"/> on Selfless Spirit
///       itself (no mana component — pure sacrifice).</item>
///     <item>Effect: for every creature the controller controls on the
///       battlefield at resolution time, register a
///       <see cref="GrantKeywordUntilEndOfTurnEffect"/> granting
///       "Indestructible" (CR 514 cleanup-step expiry).</item>
///   </list>
///   The Spirit sacrifices itself as part of cost payment, so it's already
///   in the graveyard when the effect resolves and is correctly excluded
///   from the anthem (CR 117.7c — pay costs before effect; CR 608.2c).
///
/// ## Notes
/// - The grants are per-creature live <see cref="ContinuousEffect"/>
///   registrations on the supplied <see cref="ContinuousEffectsService"/>
///   (Layer 6 — granted ability per CR 613.1f), expiring at cleanup
///   (CR 514.2).
/// - The "team save" is the famous Eldritch Moon Spirits sideboard answer
///   to wraths (Anger of the Gods, Damnation, Wrath of God) and combat
///   tricks alike.
///
/// ## Deferred (v1 gaps)
/// - Generic <see cref="AdditionalCost.Pay"/> sacrifice payment is a no-op
///   stub (same posture as Caustic Caterpillar / Aether Spellbomb /
///   Cursecatcher); the activated ability closure performs the zone move
///   directly so the sac is observable. Remove the explicit move once the
///   AdditionalCost.Pay sacrifice path is wired.
/// - No <see cref="ContinuousEffectsService"/> overload skips the team
///   anthem (no service ≡ shape-only path); callers that want functional
///   team-save behaviour must supply the layers service.
/// </summary>
[CardName("Selfless Spirit")]
public static class SelflessSpiritFactory
{
    public const string CardName = "Selfless Spirit";
    public const string PrintedManaCost = "{1}{W}";
    public const int Power = 2;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Selfless Spirit without a live continuous-effects service.
    /// The sacrifice ability is attached structurally; activating it
    /// performs the sacrifice but the team-indestructible anthem is a
    /// no-op (no layers service to register grants against).
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Construct Selfless Spirit. When <paramref name="continuousEffects"/>
    /// is supplied, activating the sacrifice ability registers a
    /// <see cref="GrantKeywordUntilEndOfTurnEffect"/> granting
    /// "Indestructible" to every creature controlled by
    /// <paramref name="owner"/> at resolution time (the sac source itself
    /// is in the graveyard by then — CR 117.7c).
    /// </summary>
    public static Creature Create(Player owner, ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Spirit });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying keyword marker.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // Sacrifice this creature: Creatures you control gain
        // indestructible until end of turn. (CR 602 activated ability.)
        // Cost = AdditionalCost.Sacrifice on self, no mana. Resolution
        // enumerates the controller's battlefield creatures and registers
        // a per-creature Layer-6 "Indestructible" grant that expires at
        // cleanup (CR 514.2 / CR 702.12).
        // ----------------------------------------------------------------
        var sacEffect = new Effect(
            $"{CardName}: sacrifice self + grant team indestructible EOT",
            () =>
            {
                SacrificeSelf(card, owner);

                if (continuousEffects == null) return;

                foreach (var creature in owner.Zones.Battlefield
                    .GetCards()
                    .OfType<Creature>()
                    .ToList())
                {
                    continuousEffects.Register(
                        new GrantKeywordUntilEndOfTurnEffect(creature, "Indestructible"));
                }
            });

        var sacAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.Sacrifice(card),
            },
            effects: new IEffect[] { sacEffect });

        card.AddAbility(sacAbility);

        return card;
    }

    /// <summary>
    /// CR 701.16 — move <paramref name="card"/> from the battlefield to its
    /// owner's graveyard. Idempotent. Mirrors the closure used by Caustic
    /// Caterpillar / Cursecatcher — the generic
    /// <see cref="AdditionalCost.Pay"/> sacrifice path is a no-op stub, so
    /// the effect closure performs the zone move directly.
    /// </summary>
    private static void SacrificeSelf(Creature card, Player owner)
    {
        if (card.Zone != ZoneType.Battlefield) return;
        owner.Zones.Battlefield.RemoveCard(card);
        owner.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);
    }
}
