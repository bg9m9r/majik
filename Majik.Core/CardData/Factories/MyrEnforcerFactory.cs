using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Myr Enforcer (Mirrodin, {7}).
///
/// Artifact Creature — Myr 4/4. Oracle text:
///   "Affinity for artifacts (This spell costs {1} less to cast for each
///    artifact you control.)"
///
/// ## Implementation
///
/// - 4/4 Artifact Creature — Myr with printed mana cost {7}. The
///   Artifact type is layered on via <see cref="Card.AddCardType"/> so
///   both <c>HasType(Artifact)</c> + <c>HasType(Creature)</c> pass —
///   same shape as <see cref="FrogmiteFactory"/> / <see cref="ArcboundRavagerFactory"/>.
/// - <b>Affinity for artifacts (CR 702.40 / CR 117.7)</b>: wired via
///   <see cref="CostReductionAbility.AffinityFor"/>(<see cref="CardType.Artifact"/>).
///   At seven artifacts the spell floors to {0} (CR 117.7c). A
///   <see cref="KeywordAbility"/> marker "Affinity" is also attached
///   for keyword-scan callers, matching <see cref="FrogmiteFactory"/>.
///
/// The data-driven Scryfall path also picks up the reduction via the
/// <see cref="AffinityBinder"/> regex against the reminder text; this
/// factory wires the same shape so <see cref="NamedCardFactory.Create"/>
/// (test seam, no binder run) returns the fully-equipped card.
/// </summary>
[CardName("Myr Enforcer")]
public static class MyrEnforcerFactory
{
    public const string CardName = "Myr Enforcer";
    public const string PrintedManaCost = "{7}";
    public const int Power = 4;
    public const int Toughness = 4;

    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Myr });

        // CR 301.1 / 302.1 — Artifact Creature.
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        // Affinity for artifacts (CR 702.40 / CR 117.7).
        card.AddAbility(CostReductionAbility.AffinityFor(CardType.Artifact));
        card.AddAbility(new KeywordAbility("Affinity", card, owner));

        return card;
    }
}
