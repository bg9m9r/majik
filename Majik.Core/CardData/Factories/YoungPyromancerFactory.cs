using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Young Pyromancer (Magic 2014, {1}{R}).
///
/// Creature — Human Shaman 2/1. Oracle text:
///   "Whenever you cast an instant or sorcery spell, create a 1/1 red
///    Elemental creature token."
///
/// ## Implementation
///
/// - 2/1 Human Shaman, mana cost {1}{R}.
/// - <b>Instant/sorcery-cast token trigger (CR 603.1)</b>: a
///   <see cref="TriggeredAbility"/> over <see cref="SpellCastEvent"/> that
///   matches when the spell's controller is Young Pyromancer's controller AND
///   the spell's card has type <see cref="CardType.Instant"/> or
///   <see cref="CardType.Sorcery"/>. Effect: create a 1/1 Elemental creature
///   token via <see cref="TokenFactory.CreateOnBattlefield"/>.
///
/// ## Deferred (v1 gaps)
/// - None at this layer — token colour identity (red) is now stamped via
///   <see cref="TokenFactory.TokenSpec.Colors"/> (CR 105 / CR 903.4).
/// </summary>
[CardName("Young Pyromancer")]
public static class YoungPyromancerFactory
{
    public const string CardName = "Young Pyromancer";
    public const string PrintedManaCost = "{1}{R}";
    public const int Power = 2;
    public const int Toughness = 1;
    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    /// <summary>
    /// Construct Young Pyromancer with no live bus / trigger-manager wiring.
    /// The token trigger is attached to the card for shape observability.
    /// Suitable for dispatcher / structural tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Young Pyromancer with optional event bus + trigger manager.
    /// When <paramref name="triggers"/> is supplied the token trigger is
    /// registered so the bus surfaces it as pending on a matching
    /// <see cref="SpellCastEvent"/>.
    /// </summary>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Shaman });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 603.1 — "Whenever you cast an instant or sorcery spell, create
        // a 1/1 red Elemental creature token."
        // Predicate: spell controller matches AND spell has Instant or Sorcery
        // card type (CR 300.1 / 307.1). Creature spells do not fire even if
        // they happen to have a secondary instant/sorcery type via
        // DFCs/adventures — the printed oracle tests the card types of the
        // spell as cast (CR 112.1).
        var tokenCondition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
            ReferenceEquals(e.Spell.Controller, owner)
            && (e.Spell.Card.HasType(CardType.Instant)
                || e.Spell.Card.HasType(CardType.Sorcery)));

        var tokenEffect = new Effect(
            $"{CardName}: create 1/1 Elemental token (whenever you cast an instant or sorcery spell)",
            () =>
            {
                var controller = card.Controller ?? owner;
                CreateElementalToken(controller, zoneService);
            });

        var tokenTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: tokenCondition,
            effects: new IEffect[] { tokenEffect });

        card.AddAbility(tokenTrigger);
        triggers?.RegisterTriggeredAbility(tokenTrigger);

        return card;
    }

    /// <summary>
    /// CR 111 / 111.6 — create one 1/1 Elemental creature token under
    /// <paramref name="controller"/>'s control.
    /// </summary>
    public static Creature CreateElementalToken(
        Player controller,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: "Elemental",
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Elemental },
            Keywords: null,
            // CR 105 / CR 111.4 — printed "1/1 red Elemental creature token".
            Colors: new[] { Majik.Core.ValueObjects.ManaColor.Red });

        return TokenFactory.CreateOnBattlefield(spec, controller, zoneService);
    }
}
