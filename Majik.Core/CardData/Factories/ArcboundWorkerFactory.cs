using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Keywords;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Arcbound Worker (Darksteel, {1}).
///
/// Artifact Creature — Construct 0/0. Oracle text:
///   "Modular 1 (This creature enters with a +1/+1 counter on it. When it
///    dies, you may put its +1/+1 counters on target artifact creature.)"
///
/// Modular 1 is wired via <see cref="ModularFactory.Build"/> — same primitive
/// as Arcbound Ravager / Arcbound Stinger. The factory itself contributes only
/// the printed type/subtype/P/T/cost.
/// </summary>
[CardName("Arcbound Worker")]
public static class ArcboundWorkerFactory
{
    public const string CardName = "Arcbound Worker";
    public const string PrintedManaCost = "{1}";
    public const int Power = 0;
    public const int Toughness = 0;
    public const int ModularValue = 1;

    /// <summary>
    /// Construct Arcbound Worker with no live wiring. Suitable for
    /// dispatcher / shape tests; the Modular ETB counter can be stamped
    /// manually via <see cref="MarkEntersWithCounter"/>.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, replacements: null, triggers: null);

    /// <summary>
    /// Construct Arcbound Worker with optional runtime services. Modular 1
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
            subtypes: new[] { CardSubtype.Construct });

        // CR 301.1 / 302.1 — Artifact Creature.
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        ModularFactory.Build(
            source: card,
            n: ModularValue,
            effects: null,
            replacements: replacements,
            triggers: triggers);

        return card;
    }

    /// <summary>
    /// Shape-only fallback — stamps Arcbound Worker's Modular-1 ETB
    /// +1/+1 counter manually. See <see cref="ModularFactory.MarkEntersWithCounters"/>.
    /// </summary>
    public static void MarkEntersWithCounter(Creature worker)
    {
        ArgumentNullException.ThrowIfNull(worker);
        ModularFactory.MarkEntersWithCounters(worker, ModularValue);
    }
}
