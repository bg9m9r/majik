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
/// Named-card factory for Third Path Iconoclast (Dominaria United, {U}{R}).
///
/// Creature — Human Monk 2/1. Oracle text (verified against Scryfall):
///   "Whenever you cast a noncreature spell, create a 1/1 colorless Soldier
///    artifact creature token."
///
/// The base shape (name, Creature, Human/Monk subtypes, {U}{R}, 2/1) is
/// materialised from the embedded JSON definition
/// (<c>third-path-iconoclast.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The noncreature-cast token
/// trigger is layered on here — the JSON <c>AbilityDefinition</c> schema
/// doesn't yet express this trigger shape (same posture as
/// <see cref="StormscaleScionFactory"/> and the other JSON-backed cards
/// whose behaviour outgrows the schema).
///
/// ## Implementation
///
/// - 2/1 Human Monk, mana cost {U}{R}.
/// - <b>Noncreature-cast token trigger (CR 603.1)</b>: a
///   <see cref="TriggeredAbility"/> over <see cref="SpellCastEvent"/> that
///   fires whenever this card's controller casts a spell whose card does NOT
///   have the Creature card type (CR 205.3 / 302.1 — a "noncreature spell" is
///   any spell that isn't a creature spell, so artifact creatures and other
///   creature spells are excluded). Effect: mint a 1/1 colourless
///   <see cref="CardSubtype.Soldier"/> creature token, then additively stamp
///   <see cref="CardType.Artifact"/> so the resulting token reports
///   Artifact + Creature — Soldier (CR 111.1; same multi-type pattern as
///   <see cref="SaiMasterThopteristFactory"/>'s Thopters and
///   <see cref="HangarbackWalkerFactory"/>'s Thopters). Same noncreature
///   predicate as <see cref="MonasteryMentorFactory"/>'s token trigger.
///
/// ## Deferred (v1 gaps)
/// - None at this layer — colour identity (colourless) is stamped via an
///   empty <see cref="TokenFactory.TokenSpec.Colors"/> list (CR 105 / 111.4)
///   and the Artifact card type is layered additively post-build (CR 111.1).
/// </summary>
[CardName("Third Path Iconoclast")]
public static class ThirdPathIconoclastFactory
{
    public const string CardName = "Third Path Iconoclast";
    public const string Slug = "third-path-iconoclast";
    public const int TokenPower = 1;
    public const int TokenToughness = 1;
    public const string TokenName = "Soldier";

    /// <summary>
    /// Construct Third Path Iconoclast with no live bus / trigger-manager
    /// wiring. The token trigger is attached to the card for shape
    /// observability. Suitable for dispatcher / structural tests. This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Third Path Iconoclast with optional event bus + trigger
    /// manager. When <paramref name="triggers"/> is supplied the token trigger
    /// is registered so the bus surfaces it as pending on a matching
    /// <see cref="SpellCastEvent"/>.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Not used directly by this factory; reserved for
    /// future lifecycle subscribers (e.g. LTB unregister).</param>
    /// <param name="triggers">TriggerManager for the token trigger. May be
    /// null — the trigger is still attached to the card shape.</param>
    /// <param name="zoneService">Optional zone service so the token-ETB
    /// CardMovedEvent fires. Pass null for a raw battlefield placement.</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Human/Monk subtypes, {U}{R}, 2/1). The JSON carries no abilities —
        // the token trigger is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 603.1 — "Whenever you cast a noncreature spell, create a 1/1
        // colorless Soldier artifact creature token."
        // Predicate: spell controller matches AND the spell's card is NOT a
        // Creature (CR 205.3 / 302.1). Same noncreature filter as Monastery
        // Mentor's token trigger.
        var tokenCondition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
            ReferenceEquals(e.Spell.Controller, owner)
            && !e.Spell.Card.HasType(CardType.Creature));

        var tokenEffect = new Effect(
            $"{CardName}: create 1/1 colourless Soldier artifact creature token (whenever you cast a noncreature spell)",
            () =>
            {
                var controller = card.Controller ?? owner;
                CreateSoldierToken(controller, zoneService);
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
    /// CR 111 / 111.1 / 111.6 — mint one 1/1 colourless Soldier artifact
    /// creature token under <paramref name="controller"/>'s control. The
    /// <see cref="TokenFactory"/> shell stamps Creature only; the Artifact
    /// card type is layered additively post-build so the token reports
    /// Artifact + Creature — Soldier (same multi-type pattern as
    /// <see cref="SaiMasterThopteristFactory"/>'s Thopters).
    /// </summary>
    public static Creature CreateSoldierToken(
        Player controller,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: TokenName,
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Soldier },
            Keywords: null,
            // CR 105 / 111.4 — printed "1/1 colorless Soldier artifact
            // creature token". Empty colour list declares colourless.
            Colors: Array.Empty<ManaColor>());

        var token = TokenFactory.CreateOnBattlefield(spec, controller, zoneService);

        // CR 111.1 / 301.1 — Soldier tokens here are Artifact Creatures.
        // TokenFactory's shell stamps Creature only; layer Artifact on
        // additively (same posture as Sai's Thopters / Hangarback Walker).
        token.AddCardType(CardType.Artifact);

        return token;
    }
}
