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
/// Named-card factory for Twin-Silk Spider (Bloomburrow, {2}{G}).
///
/// Creature — Spider 1/2. Oracle text (verified against Scryfall):
///   "Reach
///    When this creature enters, create a 1/2 green Spider creature token
///    with reach."
///
/// The base shape (name, Creature, Spider subtype, {2}{G}, 1/2) is
/// materialised from the embedded JSON definition
/// (<c>twin-silk-spider.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The printed Reach keyword and
/// the ETB token-creation trigger are layered on top here — the JSON
/// <c>AbilityDefinition</c> schema doesn't express keyword markers or
/// token-creation effects, so those live in the factory (same posture as
/// <see cref="StormscaleScionFactory"/> and the code-only
/// <see cref="EldraziSkyspawnerFactory"/>, whose ETB also mints a single
/// token carrying a keyword).
///
/// ## Implemented (v1)
/// - 1/2 <see cref="Creature"/> — Spider at {2}{G}.
/// - <b>Reach (CR 702.17)</b> attached as a <see cref="KeywordAbility"/>
///   marker so combat surfaces (<c>CombatAbilities.HasReach</c>) observe
///   it — same shape as <see cref="SentinelSpiderFactory"/>'s Reach.
/// - <b>ETB triggered ability (CR 603.6a)</b> over a
///   <see cref="CardMovedEvent"/> filtered to (this card, ToZone =
///   Battlefield). Resolution creates one 1/2 green Spider creature token
///   that itself carries the Reach keyword, under this card's controller,
///   via <see cref="TokenFactory.CreateOnBattlefield"/>. No targets — pure
///   token-creation (same wiring shape as
///   <see cref="EldraziSkyspawnerFactory"/>).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. The ETB trigger is attached
///   for shape observability; not registered with any
///   <see cref="TriggerManager"/>, no <see cref="ZoneService"/> wiring.
///   This is the overload <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, ZoneService?, TriggerManager?)"/> — fully
///   wired. The trigger registers with <paramref name="triggers"/>; the
///   token's ETB routes through <paramref name="zones"/> so its own
///   <see cref="CardMovedEvent"/> publishes (downstream ETB subscribers see
///   the token's arrival).
/// </summary>
[CardName("Twin-Silk Spider")]
public static class TwinSilkSpiderFactory
{
    public const string CardName = "Twin-Silk Spider";
    public const string Slug = "twin-silk-spider";
    public const int TokenPower = 1;
    public const int TokenToughness = 2;
    private const string ReachKeyword = "Reach";

    /// <summary>
    /// Construct Twin-Silk Spider with no live wiring. The ETB trigger is
    /// attached for shape observability; not registered with any
    /// <see cref="TriggerManager"/>, no <see cref="ZoneService"/> wiring.
    /// Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zones: null, triggers: null);

    /// <summary>
    /// Construct Twin-Silk Spider with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zones">When supplied, the Spider token's ETB routes
    /// through <see cref="ZoneService.MoveCardTo"/> so
    /// <see cref="CardMovedEvent"/> publishes for any zone-change
    /// subscribers.</param>
    /// <param name="triggers">When supplied, the ETB trigger registers with
    /// the bus so the corresponding <see cref="CardMovedEvent"/> lands the
    /// ability on the stack automatically (CR 603.2).</param>
    public static Creature Create(
        Player owner,
        ZoneService? zones,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Spider subtype, {2}{G}, 1/2). The JSON carries no abilities —
        // Reach + the ETB token trigger are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.17 — Reach. Keyword marker only; consumed by
        // CombatAbilities.HasReach so the Spider may block creatures with
        // Flying. Same shape as Sentinel Spider's Reach.
        card.AddAbility(new KeywordAbility(ReachKeyword, card, owner));

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a / CR 111 (Token).
        //   "When this creature enters, create a 1/2 green Spider creature
        //    token with reach."
        // No targets — pure token-creation.
        // ----------------------------------------------------------------
        var etbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        var etbEffect = new Effect(
            $"{CardName}: create a 1/2 green Spider creature token with reach",
            () =>
            {
                var controller = card.Controller ?? owner;
                CreateSpiderToken(controller, zones);
            });

        var etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect });

        card.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        return card;
    }

    /// <summary>
    /// CR 111 / CR 111.4 — create one 1/2 green Spider creature token under
    /// <paramref name="controller"/>'s control, carrying the Reach keyword.
    /// </summary>
    public static Creature CreateSpiderToken(
        Player controller,
        ZoneService? zones = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: "Spider",
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Spider },
            // CR 702.17 — the token itself has reach.
            Keywords: new[] { ReachKeyword },
            // CR 105.2a — printed green token. Explicit single-colour list
            // stamps the green override onto Card.TokenColorsOverride.
            Colors: new[] { ManaColor.Green });

        return TokenFactory.CreateOnBattlefield(spec, controller, zones);
    }
}
