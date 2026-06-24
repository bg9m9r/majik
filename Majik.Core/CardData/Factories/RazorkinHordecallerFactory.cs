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
/// Named-card factory for Razorkin Hordecaller (Duskmourn: House of Horror,
/// {4}{R}). Creature — Human Clown Berserker 4/4. Oracle text (verified
/// against Scryfall):
///   "Haste
///    Whenever you attack, create a 1/1 red Gremlin creature token."
///
/// The base shape (name, Creature, Human + Clown + Berserker subtypes,
/// {4}{R}, 4/4, Haste keyword) is materialised from the embedded JSON
/// definition (<c>razorkin-hordecaller.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — Haste rides on the JSON
/// <c>keywords</c> array as a <see cref="KeywordAbility"/> marker (CR 702.10).
/// The "Whenever you attack" trigger is layered on here — the JSON
/// <c>AbilityDefinition</c> schema doesn't yet express attack triggers (same
/// posture as <see cref="AdelineResplendentCatharFactory"/> /
/// <see cref="IntiSeneschalOfTheSunFactory"/>).
///
/// ## Implemented (v1)
///
/// - <b>Haste (CR 702.10)</b> — a <see cref="KeywordAbility"/> marker from the
///   JSON definition so the printed line is reflected on
///   <c>ICard.Abilities</c> and Scryfall keyword parsing matches.
///
/// - <b>"Whenever you attack, create a 1/1 red Gremlin creature token"
///   (CR 508.1 / 603.1)</b> — a <see cref="TriggeredAbility"/> scoped to
///   <see cref="AttackersDeclaredEvent"/> where the attacking player is
///   Razorkin Hordecaller's controller ("Whenever you attack", CR 508.1 /
///   109.5 — the controller-scoped attack trigger, same gate as
///   <see cref="AdelineResplendentCatharFactory"/>). On resolution a single
///   1/1 RED Gremlin creature token is created under the controller via
///   <see cref="TokenFactory.CreateOnBattlefield"/> (CR 111.4). Unlike
///   Adeline / Hero of Bladehold, the token is NOT put onto the battlefield
///   tapped-and-attacking — Razorkin's token is a plain creature token that
///   simply enters under the controller's control, so there is no combat
///   splice (CR 111.6 — it enters the battlefield directly). When a
///   <see cref="ZoneService"/> is supplied the token enters through it so its
///   ETB <see cref="CardMovedEvent"/> fires.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — the <see cref="NamedCardFactory"/>
///   dispatch target. The attack trigger is mounted for shape observability
///   but, without a live <see cref="TriggerManager"/>, it is not registered as
///   pending (same shape-only posture as
///   <see cref="AdelineResplendentCatharFactory.Create(Player)"/>).
/// - <see cref="Create(Player, TriggerManager?, ZoneService?)"/> — fully
///   wired: an <see cref="AttackersDeclaredEvent"/> by the controller lands
///   the trigger, and the token enters through the supplied zone service.
/// </summary>
[CardName("Razorkin Hordecaller")]
public static class RazorkinHordecallerFactory
{
    public const string CardName = "Razorkin Hordecaller";
    public const string Slug = "razorkin-hordecaller";

    /// <summary>Gremlin token — 1/1 red.</summary>
    public const string TokenName = "Gremlin";
    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    /// <summary>
    /// Construct Razorkin Hordecaller with no live runtime wiring (the
    /// dispatcher / shape path). The attack trigger is attached for shape
    /// observability but not registered (no trigger manager) and creates no
    /// token live (no zone service). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, zones: null);

    /// <summary>
    /// Construct Razorkin Hordecaller with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">TriggerManager the attack trigger is registered
    /// with so an <see cref="AttackersDeclaredEvent"/> by the controller lands
    /// it on the stack. May be null.</param>
    /// <param name="zones">When supplied, the Gremlin token enters through the
    /// zone service so its ETB <see cref="CardMovedEvent"/> fires. May be null
    /// — the token then enters the battlefield directly.</param>
    public static Creature Create(Player owner, TriggerManager? triggers, ZoneService? zones)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Human +
        // Clown + Berserker, {4}{R}, 4/4, Haste keyword). The attack trigger is
        // layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        AddAttackTrigger(card, owner, triggers, zones);

        return card;
    }

    /// <summary>
    /// CR 111 / 111.4 — create one 1/1 RED Gremlin creature token under
    /// <paramref name="controller"/>'s control. The printed token is red, so
    /// the spec carries <see cref="ManaColor.Red"/>.
    /// </summary>
    public static Creature CreateGremlinToken(Player controller, ZoneService? zones = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: TokenName,
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Gremlin },
            Keywords: null,
            Colors: new[] { ManaColor.Red });

        return TokenFactory.CreateOnBattlefield(spec, controller, zones);
    }

    // -----------------------------------------------------------------------
    // Attack trigger — "Whenever you attack, create a 1/1 red Gremlin creature
    // token." (CR 508.1 / 603.1.)
    // -----------------------------------------------------------------------
    private static void AddAttackTrigger(
        Creature card,
        Player owner,
        TriggerManager? triggers,
        ZoneService? zones)
    {
        var condition = new EventTriggerCondition<AttackersDeclaredEvent>((e, _) =>
            // "Whenever you attack" — only when Razorkin's controller is the
            // attacking player (CR 508.1 / 109.5).
            ReferenceEquals(e.Combat.AttackingPlayer, card.Controller ?? owner));

        var effect = new Effect(
            $"{CardName}: on attack, create a 1/1 red Gremlin creature token",
            () => CreateGremlinToken(card.Controller ?? owner, zones));

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { effect },
            // CR 113.6 — functions only from the battlefield.
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);
    }
}
