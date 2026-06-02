using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Murmuring Mystic (Guilds of Ravnica, {3}{U}).
///
/// Creature — Human Wizard 1/5. Oracle text (Scryfall, verified):
///   "Whenever you cast an instant or sorcery spell, create a 1/1 blue Bird
///    Illusion creature token with flying."
///
/// ## Implementation
///
/// The base shape (name, Creature, Human + Wizard subtypes, {3}{U}, 1/5) is
/// materialised from the embedded JSON definition (<c>murmuring-mystic.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The cast trigger is layered on in
/// C# — the JSON <c>AbilityDefinition</c> schema doesn't express the on-cast
/// token trigger (same posture as <see cref="SedgemoorWitchFactory"/> /
/// <see cref="YoungPyromancerFactory"/>).
///
/// - <b>Instant/sorcery-cast token trigger (CR 603.1)</b>: a
///   <see cref="TriggeredAbility"/> over <see cref="SpellCastEvent"/> that
///   matches when the spell's controller is Murmuring Mystic's controller AND
///   the spell's card has type <see cref="CardType.Instant"/> or
///   <see cref="CardType.Sorcery"/> (CR 300.1 / 307.1). On resolve, create one
///   1/1 blue Bird Illusion creature token with flying via
///   <see cref="TokenFactory.CreateOnBattlefield"/>.
///
/// ## Deferred (v1 gaps)
/// - None at this layer — token colour (blue), subtypes (Bird Illusion), and
///   the Flying keyword are all stamped via
///   <see cref="TokenFactory.TokenSpec"/> (CR 105 / 111.4 / 702.9).
/// </summary>
[CardName("Murmuring Mystic")]
public static class MurmuringMysticFactory
{
    public const string CardName = "Murmuring Mystic";
    public const string Slug = "murmuring-mystic";
    public const string PrintedManaCost = "{3}{U}";
    public const int Power = 1;
    public const int Toughness = 5;
    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    /// <summary>
    /// Construct Murmuring Mystic with no live bus / trigger-manager wiring.
    /// The token trigger is attached to the card for shape observability.
    /// Suitable for dispatcher / structural tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Murmuring Mystic with optional event bus + trigger manager.
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

        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(def, owner);

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 603.1 — "Whenever you cast an instant or sorcery spell, create a
        // 1/1 blue Bird Illusion creature token with flying."
        // Predicate: spell controller matches AND the spell has Instant or
        // Sorcery card type (CR 300.1 / 307.1). Creature spells do not fire even
        // if they happen to carry a secondary instant/sorcery type — the printed
        // oracle tests the card types of the spell as cast (CR 112.1).
        var tokenCondition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
            ReferenceEquals(e.Spell.Controller, owner)
            && (e.Spell.Card.HasType(CardType.Instant)
                || e.Spell.Card.HasType(CardType.Sorcery)));

        var tokenEffect = new Effect(
            $"{CardName}: create a 1/1 blue Bird Illusion token with flying (whenever you cast an instant or sorcery spell)",
            () =>
            {
                var controller = card.Controller ?? owner;
                CreateBirdIllusionToken(controller, zoneService);
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
    /// CR 111 / 111.4 — create one 1/1 blue Bird Illusion creature token with
    /// flying (CR 702.9) under <paramref name="controller"/>'s control.
    /// </summary>
    public static Creature CreateBirdIllusionToken(
        Player controller,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: "Bird Illusion",
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Bird, CardSubtype.Illusion },
            // CR 702.9 — printed "with flying".
            Keywords: new[] { "Flying" },
            // CR 105 / 111.4 — printed "1/1 blue Bird Illusion creature token".
            Colors: new[] { ManaColor.Blue });

        return TokenFactory.CreateOnBattlefield(spec, controller, zoneService);
    }
}
