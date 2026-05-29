using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Blade Splicer (New Phyrexia / many reprints,
/// {2}{W}). Creature — Phyrexian Human Artificer 1/1. Oracle text
/// (verified against Scryfall):
///   "When this creature enters, create a 3/3 colorless Phyrexian Golem
///    artifact creature token.
///    Golems you control have first strike."
///
/// The card's base shape (name, Creature, Phyrexian/Human/Artificer
/// subtypes, {2}{W}, 1/1) is materialised from the embedded JSON definition
/// (<c>blade-splicer.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two printed behaviours
/// (ETB token-creation, Golem first-strike anthem) are layered on top here —
/// the JSON <c>AbilityDefinition</c> schema doesn't express token-creation
/// effects or lord statics, so they live in the factory (same posture as
/// <see cref="StormscaleScionFactory"/> and the other JSON-backed cards
/// whose behaviour outgrows the schema).
///
/// ## Implemented (v1)
/// - 1/1 Creature — Phyrexian Human Artificer at {2}{W}.
/// - <b>ETB triggered ability (CR 603.6a)</b> over a
///   <see cref="CardMovedEvent"/> filtered to (this card, ToZone =
///   Battlefield). Resolution creates one 3/3 colourless Phyrexian Golem
///   <i>artifact</i> creature token (CR 111.4 / 301.1) under Blade
///   Splicer's controller via <see cref="CreatePhyrexianGolemToken"/>.
///   Same shape as <see cref="EldraziSkyspawnerFactory"/>'s ETB Scion; the
///   Artifact card type is flagged on the token exactly like
///   <see cref="WurmcoilEngineFactory"/>'s Phyrexian Wurm tokens.
/// - <b>Golem first-strike anthem (CR 613.1f)</b>: "Golems you control have
///   first strike." Wired via <see cref="LordStaticEffect"/> with
///   <c>matchingSubtype: Golem</c>, <c>power: 0, toughness: 0</c>,
///   <c>grantedKeywords: ["First strike"]</c>, <c>includeSelf: true</c>,
///   <c>opponentsOnly: false</c>, <c>allPlayers: false</c>. The keyword
///   string is "First strike" — the exact token
///   <see cref="Majik.Core.Combat.CombatAbilities.HasFirstStrike"/> reads.
///   <c>includeSelf: true</c> is harmless: the printed text has no "Other"
///   qualifier, and Blade Splicer is a Human Artificer (not a Golem) so it
///   never matches its own subtype filter anyway. Registered only when a
///   <see cref="ContinuousEffectsService"/> is supplied. Same posture as
///   <see cref="GoblinChieftainFactory"/>'s keyword-granting lord static.
///
/// ## Deferred (v1 gaps)
/// - <b>LTB unregister</b>: the registered <see cref="LordStaticEffect"/>
///   stays on the <see cref="ContinuousEffectsService"/> across zone
///   changes; <see cref="LordStaticEffect.IsActive"/> short-circuits when
///   Blade Splicer isn't on the battlefield so the first-strike grant lifts
///   correctly, but a future Prune pass could drop the entry. Same shape as
///   <see cref="GoblinChieftainFactory"/> / <see cref="StormscaleScionFactory"/>.
/// </summary>
[CardName("Blade Splicer")]
public static class BladeSplicerFactory
{
    public const string CardName = "Blade Splicer";
    public const string Slug = "blade-splicer";
    public const int TokenPower = 3;
    public const int TokenToughness = 3;

    /// <summary>
    /// Construct Blade Splicer with no live wiring. The ETB trigger is
    /// attached for shape observability (not registered with any
    /// <see cref="TriggerManager"/>, no <see cref="ZoneService"/> wiring);
    /// the Golem anthem is NOT registered (no continuous-effects service).
    /// Suitable for shape / dispatcher tests. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zones: null, triggers: null, continuousEffects: null);

    /// <summary>
    /// Construct Blade Splicer with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zones">When supplied, the Golem token's ETB routes
    /// through <see cref="ZoneService.MoveCardTo"/> so
    /// <see cref="CardMovedEvent"/> publishes for any zone-change
    /// subscribers (Soul Warden, etc.).</param>
    /// <param name="triggers">When supplied, the ETB trigger registers with
    /// the bus so the corresponding <see cref="CardMovedEvent"/> lands the
    /// ability on the stack automatically (CR 603.2).</param>
    /// <param name="continuousEffects">Layers service to register the Golem
    /// first-strike anthem against. May be null — no live grant.</param>
    public static Creature Create(
        Player owner,
        ZoneService? zones,
        TriggerManager? triggers,
        ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Phyrexian/Human/Artificer subtypes, {2}{W}, 1/1). The JSON carries
        // no abilities — ETB token + Golem anthem are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a / CR 111 (Token).
        //   "When this creature enters, create a 3/3 colorless Phyrexian
        //    Golem artifact creature token."
        // No targets — pure token-creation.
        // ----------------------------------------------------------------
        var etbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        var etbEffect = new Effect(
            $"{CardName}: create a 3/3 colourless Phyrexian Golem artifact creature token",
            () =>
            {
                var controller = card.Controller ?? owner;
                CreatePhyrexianGolemToken(controller, zones);
            });

        var etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect });

        card.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        // ----------------------------------------------------------------
        // Golem first-strike anthem — CR 613.1f (granted keyword).
        //   "Golems you control have first strike."
        // matchingSubtype: Golem; power/toughness 0 (keyword-only anthem).
        // includeSelf: true is harmless — no "Other" qualifier, and Blade
        // Splicer isn't a Golem so it never matches its own filter. Scoped
        // to the controller's battlefield (allPlayers: false).
        // ----------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(new LordStaticEffect(
                source: card,
                matchingSubtype: CardSubtype.Golem,
                power: 0,
                toughness: 0,
                grantedKeywords: new[] { "First strike" },
                includeSelf: true,
                opponentsOnly: false,
                allPlayers: false));
        }

        return card;
    }

    /// <summary>
    /// CR 111 / CR 111.4 / CR 301.1 — create one 3/3 colourless Phyrexian
    /// Golem <i>artifact</i> creature token under <paramref name="controller"/>'s
    /// control. The Artifact card type is flagged on the token via
    /// <see cref="Card.AddCardType"/> (same Artifact-Creature token pattern
    /// as <see cref="WurmcoilEngineFactory"/>'s Phyrexian Wurms). The
    /// explicit empty colour list stamps the colourless override
    /// (<see cref="Card.TokenColorsOverride"/>).
    /// </summary>
    public static Creature CreatePhyrexianGolemToken(
        Player controller,
        ZoneService? zones = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: "Phyrexian Golem",
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Phyrexian, CardSubtype.Golem },
            // CR 105 / CR 111.4 — printed "colourless" token. Explicit empty
            // colour list stamps the colourless override.
            Colors: Array.Empty<ManaColor>());

        var golem = TokenFactory.CreateOnBattlefield(spec, controller, zones);

        // CR 301.1 / 302.1 — flag the token as an Artifact Creature so
        // HasType(Artifact) returns true (the printed text mandates the
        // "artifact creature" type combination).
        golem.AddCardType(CardType.Artifact);

        return golem;
    }
}
