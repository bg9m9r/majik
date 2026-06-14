using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Servo Exhibition (Kaladesh, {1}{W}).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "Create two 1/1 colorless Servo artifact creature tokens."
///
/// ## Why it gets its own factory
/// Same "create N tokens" spell shape as <see cref="GatherTheTownsfolkFactory"/>
/// (Sorcery, {1}{W}, white, card shape loaded from embedded JSON via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/>), but the minted token is the 1/1
/// colourless Servo <i>artifact creature</i> token from
/// <see cref="ServoSchematicFactory"/> rather than a 1/1 white Human. Both
/// halves use primitives that already ship — no new engine mechanic is
/// required.
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {1}{W}. The card itself is white (CR 105.2 /
///   202.2a — colour comes from the white pip in its cost), mana value 2
///   (CR 202.3). Card shape comes from the embedded JSON
///   (<c>servo-exhibition.json</c>).
/// - Resolve effect (<see cref="BuildResolveEffect"/>): create two 1/1
///   colourless Servo artifact creature tokens under the caster's control
///   (CR 111 / 111.4 — one token per "create"). Each token is a
///   <see cref="CardSubtype.Servo"/> creature with an explicit empty colour
///   set (CR 105 — "colorless"), additively stamped
///   <see cref="CardType.Artifact"/> so it reports Artifact Creature — Servo
///   (CR 111.1; same multi-type stamp as Servo Schematic / Animation Module).
/// - No target requests (CR 115.1 — "Create … tokens" names no targets).
///
/// ## Rules citations
/// - CR 608.2 — one-shot spell resolution; tokens are created as it resolves.
/// - CR 111 / 111.4 — "create … 1/1 colorless Servo artifact creature token(s)."
/// - CR 105 — colourless is the absence of colour, represented by an empty
///   colour set on the token.
///
/// ## Deferred (v1 gaps)
/// - None. Servo Exhibition has no riders beyond the printed oracle.
/// </summary>
[CardName("Servo Exhibition")]
public static class ServoExhibitionFactory
{
    public const string CardName = "Servo Exhibition";
    public const string Slug = "servo-exhibition";
    public const string PrintedManaCost = "{1}{W}";

    /// <summary>CR 111 — "create two … tokens."</summary>
    public const int TokenCount = 2;

    public const int TokenPower = 1;
    public const int TokenToughness = 1;
    public const string ServoTokenName = "Servo";

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> for Servo Exhibition. No modes,
    /// no X, no target requests — the resolve body mints two Servo tokens for
    /// the caster.
    /// </summary>
    /// <param name="caster">The player who cast Servo Exhibition; receives the
    /// Servo tokens.</param>
    /// <param name="zoneService">Optional zone service — routes each token's ETB
    /// through <see cref="ZoneService"/> so
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> publishes (enabling
    /// downstream ETB triggers). Null → direct zone move, suitable for
    /// shape-only paths.</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => BuildResolveEffect(caster, zoneService));
    }

    /// <summary>
    /// Build the resolve effect (CR 608.2): create two 1/1 colourless Servo
    /// artifact creature tokens under the caster's control (CR 111 / 111.4).
    /// </summary>
    /// <param name="caster">Player resolving the spell — token controller.</param>
    /// <param name="zoneService">Optional zone service for token ETB events.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: create two 1/1 colorless Servo artifact creature tokens.",
                () =>
                {
                    for (var i = 0; i < TokenCount; i++)
                    {
                        CreateServoToken(caster, zoneService);
                    }
                }),
        };
    }

    /// <summary>
    /// CR 111.1 / CR 111.4 — create one 1/1 colourless Servo artifact creature
    /// token under <paramref name="controller"/>'s control. The token is a
    /// <see cref="CardSubtype.Servo"/> creature with an explicit empty colour
    /// set (CR 105 — "colorless"), then additively stamped
    /// <see cref="CardType.Artifact"/> for the artifact-creature multi-type
    /// (TokenFactory mints a Creature shell; same wiring as Servo Schematic).
    /// </summary>
    public static Creature CreateServoToken(
        Player controller,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: ServoTokenName,
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Servo },
            Keywords: null,
            // CR 105 — "colorless": empty colour set rather than probing the
            // empty mana cost.
            Colors: Array.Empty<ManaColor>());

        var token = TokenFactory.CreateOnBattlefield(spec, controller, zoneService);

        // CR 111.1 — Servo tokens are artifact creatures. Stamp Artifact
        // additively so the token reports both types.
        token.AddCardType(CardType.Artifact);

        return token;
    }
}
