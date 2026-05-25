using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Keywords;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Arcbound Stinger (Darksteel, {2}).
///
/// Artifact Creature — Insect 1/1. Oracle text:
///   "Flying.
///    Modular 1 (This creature enters with a +1/+1 counter on it. When it
///    dies, you may put its +1/+1 counters on target artifact creature.)"
///
/// - Flying (CR 702.9) keyword marker.
/// - Modular 1 (CR 702.43) via <see cref="ModularFactory.Build"/>.
/// </summary>
[CardName("Arcbound Stinger")]
public static class ArcboundStingerFactory
{
    public const string CardName = "Arcbound Stinger";
    public const string PrintedManaCost = "{2}";
    public const int Power = 1;
    public const int Toughness = 1;
    public const int ModularValue = 1;

    /// <summary>
    /// Construct Arcbound Stinger with no live wiring. Suitable for
    /// dispatcher / shape tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, replacements: null, triggers: null);

    /// <summary>
    /// Construct Arcbound Stinger with optional runtime services. Modular 1
    /// is wired against <paramref name="replacements"/> +
    /// <paramref name="triggers"/> via <see cref="ModularFactory.Build"/>.
    /// </summary>
    public static Creature Create(
        Player owner,
        ReplacementBus? replacements,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Insect });

        // CR 301.1 / 302.1 — Artifact Creature.
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        // Flying (CR 702.9) — marker; combat reads this directly.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        ModularFactory.Build(
            source: card,
            n: ModularValue,
            effects: null,
            replacements: replacements,
            triggers: triggers);

        return card;
    }

    /// <summary>
    /// Shape-only fallback — stamps Arcbound Stinger's Modular-1 ETB
    /// +1/+1 counter manually. See <see cref="ModularFactory.MarkEntersWithCounters"/>.
    /// </summary>
    public static void MarkEntersWithCounter(Creature stinger)
    {
        ArgumentNullException.ThrowIfNull(stinger);
        ModularFactory.MarkEntersWithCounters(stinger, ModularValue);
    }
}
