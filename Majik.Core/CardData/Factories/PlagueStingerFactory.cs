using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Plague Stinger (Scars of Mirrodin, {1}{B}).
///
/// Creature — Phyrexian Insect 1/1. Oracle text:
///   "Flying.
///    Infect (This creature deals damage to creatures in the form of
///    -1/-1 counters and to players in the form of poison counters.)"
///
/// ## Implemented (v1)
///
/// - 1/1 <see cref="Creature"/> at {1}{B} with subtypes Phyrexian, Insect.
/// - <b>Flying (CR 702.9)</b>: <see cref="KeywordAbility"/> marker
///   "Flying". The combat block-restriction is read by the combat system
///   through the keyword catalog (same wiring as
///   <see cref="BygoneBishopFactory"/> /
///   <see cref="SelflessSpiritFactory"/>).
/// - <b>Infect (CR 702.90)</b>: <see cref="KeywordAbility"/> marker
///   "Infect". The combat-damage replacement (-1/-1 counters to creatures
///   + poison counters to players) is deferred at the primitive level;
///   the marker surfaces the keyword so a downstream Infect primitive
///   picks Plague Stinger up without re-touching the factory (same
///   posture as <see cref="PhyrexianCrusaderFactory"/> /
///   <see cref="BlightedAgentFactory"/> / <see cref="PlagueMyrFactory"/>).
///
/// ## Deferred (v1 gaps)
///
/// - <b>Infect damage-replacement</b>: poison counter tracking on
///   <see cref="Player"/> + the layered combat replacement land in a
///   follow-up infrastructure PR. Plague Stinger's keyword marker
///   becomes live behaviour for free at that point.
/// </summary>
[CardName("Plague Stinger")]
public static class PlagueStingerFactory
{
    public const string CardName = "Plague Stinger";
    public const string PrintedManaCost = "{1}{B}";
    public const int Power = 1;
    public const int Toughness = 1;

    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[]
            {
                CardSubtype.Phyrexian,
                CardSubtype.Insect,
            });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying. Keyword marker; combat block-restriction is
        // read by the combat system via the keyword catalog.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // CR 702.90 — Infect. Keyword marker; combat-damage replacement
        // is deferred (see class xmldoc).
        card.AddAbility(new KeywordAbility("Infect", card, owner));

        return card;
    }
}
