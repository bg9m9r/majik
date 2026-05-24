using Majik.Core.Abilities;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Cards;

/// <summary>
/// Base interface for all cards.
/// </summary>
public interface ICard
{
    /// <summary>
    /// Per-instance identity (stable for the lifetime of this Card object).
    /// DTOs reference cards by this Guid to avoid serializing object graphs.
    /// </summary>
    Guid InstanceId { get; }

    /// <summary>
    /// The name of the card.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The mana cost of the card.
    /// </summary>
    string ManaCost { get; }

    /// <summary>
    /// The card types (cards can have multiple types).
    /// </summary>
    IReadOnlyList<CardType> CardTypes { get; }

    /// <summary>
    /// The card supertypes.
    /// </summary>
    IReadOnlyList<CardSupertype> Supertypes { get; }

    /// <summary>
    /// The card subtypes.
    /// </summary>
    IReadOnlyList<CardSubtype> Subtypes { get; }

    /// <summary>
    /// The owner of the card. Mutation goes through
    /// <see cref="Card.ChangeOwner"/> on the concrete <see cref="Card"/>.
    /// </summary>
    Player? Owner { get; }

    /// <summary>
    /// The current controller of the card. Mutation goes through
    /// <see cref="Card.ChangeController"/> on the concrete <see cref="Card"/>.
    /// </summary>
    Player? Controller { get; }

    /// <summary>
    /// The current zone the card is in. Mutation is the engine's
    /// responsibility — go through <see cref="ZoneService"/>, never set
    /// the zone field directly.
    /// </summary>
    ZoneType Zone { get; }

    /// <summary>
    /// Abilities attached to this card.
    /// </summary>
    IReadOnlyList<IAbility> Abilities { get; }

    /// <summary>
    /// True when this card is a "vanilla shell" — the
    /// <see cref="Majik.Core.CardData.ScryfallCardFactory"/> recognised the
    /// name and constructed the correct typed shell (Creature/Sorcery/etc.
    /// with power/toughness + mana cost), but no binders / templates / named
    /// factory matched the oracle text, so none of the printed rules text is
    /// actually enforced. The card still has a legal cast path (resolves to
    /// a vanilla permanent or a do-nothing spell, per
    /// <see cref="Majik.Core.Game.SpellDefinition.Vanilla"/>); the bot uses
    /// this flag to deprioritise such cards in EV scoring and emit a one-
    /// shot "unimplemented" warning so callers (engine logs, portal UI)
    /// know game decisions are unreliable.
    /// <para>False for fully-implemented cards, for true blank-oracle
    /// vanilla creatures (which DO have their printed rules covered — there
    /// are none), and for the unknown-name fallback shell (which is also
    /// flagged true, since by definition the engine doesn't know what the
    /// card does).</para>
    /// </summary>
    bool IsVanillaShell { get; }

    /// <summary>
    /// Attach an ability to this card.
    /// </summary>
    void AddAbility(IAbility ability);

    /// <summary>
    /// Check if the card has a specific type.
    /// </summary>
    bool HasType(CardType type);

    /// <summary>
    /// Check if the card has a specific supertype.
    /// </summary>
    bool HasSupertype(CardSupertype supertype);

    /// <summary>
    /// Check if the card has a specific subtype.
    /// </summary>
    bool HasSubtype(CardSubtype subtype);
}
