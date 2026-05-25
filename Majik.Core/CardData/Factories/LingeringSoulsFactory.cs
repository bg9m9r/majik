using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Lingering Souls (Innistrad / Dark Ascension,
/// {2}{W}).
///
/// Sorcery. Oracle text:
///   "Create two 1/1 white Spirit creature tokens with flying.
///    Flashback {1}{B}."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {2}{W}.
/// - Resolve effect (<see cref="BuildResolveEffect"/>): create two 1/1
///   white Spirit creature tokens with Flying via
///   <see cref="TokenFactory.CreateOnBattlefield"/>. Colour is stamped
///   explicitly via <see cref="TokenFactory.TokenSpec.Colors"/> as White
///   (CR 105 / CR 111.4 — tokens have no printed mana cost, so colour is
///   declared by the effect). Flying is a granted
///   <see cref="KeywordAbility"/> via the spec's Keywords list
///   (CR 702.9). The same effect body is reused for both the printed-cost
///   cast and the flashback cast — flashback's post-resolve exile is
///   performed by <see cref="FlashbackAlternativeCost.OnResolved"/>, not
///   here (mirrors <see cref="FaithlessLootingFactory"/>'s posture).
/// - <b>Flashback {1}{B}</b> alt-cost: produced via
///   <see cref="BuildFlashbackCost"/> by routing
///   <see cref="OracleText"/> through <see cref="FlashbackOracleParser"/>
///   so the named-factory path and the data-driven oracle parser path
///   agree on shape. Callers pass the returned
///   <see cref="FlashbackAlternativeCost"/> into
///   <see cref="Majik.Core.Game.SpellCastFlow.CastAsync"/> when casting
///   Lingering Souls from the graveyard; the cost handles the
///   post-resolve exile (CR 702.34b).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape-only path. Card is constructed
///   without a resolve effect body bound; tests build the spell body via
///   <see cref="BuildResolveEffect"/> and splice it into a
///   <see cref="Majik.Core.Game.SpellDefinition"/> or
///   <see cref="Majik.Core.Spells.Spell"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>"With flashback you may pay {1}{B}" choice prompt</b>: the
///   alt-cost itself is exposed; agent-side selection between "cast for
///   printed cost from hand" and "cast for flashback cost from graveyard"
///   lives upstream in <see cref="RuntimeFlashbackAltCostProbe"/> /
///   bot decision policies. Same posture as Past in Flames /
///   Faithless Looting.
/// - <b>White/black colour identity bias</b>: Lingering Souls is famously
///   the "white-black" graveyard spirits spell, but per CR 202.2 the
///   card's colour identity (NOT colour) folds in {B} from the printed
///   flashback cost. v1 reports the printed mana cost colour ({W}); the
///   {B} pip from flashback is observable via
///   <see cref="BuildFlashbackCost"/> at decision time. Colour identity
///   for deck-construction is computed elsewhere from the printed text.
/// </summary>
[CardName("Lingering Souls")]
public static class LingeringSoulsFactory
{
    public const string CardName = "Lingering Souls";
    public const string PrintedManaCost = "{2}{W}";
    public const string FlashbackManaCost = "{1}{B}";
    public const int TokensCreated = 2;
    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    /// <summary>
    /// Oracle text used by <see cref="BuildFlashbackCost"/> to derive the
    /// flashback cost via <see cref="FlashbackOracleParser"/>. Kept on the
    /// factory so the production load path (Scryfall row → oracle text →
    /// parser) and the named-factory test path bind the same shape.
    /// </summary>
    public const string OracleText =
        "Create two 1/1 white Spirit creature tokens with flying.\nFlashback {1}{B}";

    /// <summary>
    /// Construct the Lingering Souls sorcery shape with no resolve effect
    /// bound. Use <see cref="BuildResolveEffect"/> to compose the
    /// create-two-Spirits body into a
    /// <see cref="Majik.Core.Game.SpellDefinition"/> or
    /// <see cref="Majik.Core.Spells.Spell"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build Lingering Souls's resolve effect — create two 1/1 white
    /// Spirit creature tokens with Flying under <paramref name="caster"/>.
    /// Same effect body is reused for the printed-cost cast and the
    /// flashback cast; the flashback alt-cost's
    /// <see cref="FlashbackAlternativeCost.OnResolved"/> performs the
    /// post-resolve exile (CR 702.34b).
    /// </summary>
    /// <param name="caster">The resolving caster — token controller.</param>
    /// <param name="zoneService">Optional zone service so each spawned
    /// Spirit token publishes <see cref="Majik.Core.Events.CardMovedEvent"/>
    /// on ETB (Soul Warden / Impact Tremors fire). When null, tokens are
    /// placed on the battlefield via raw zone moves.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: create {TokensCreated} 1/1 white Spirit tokens with flying",
                () =>
                {
                    for (var i = 0; i < TokensCreated; i++)
                    {
                        CreateSpiritToken(caster, zoneService);
                    }
                }),
        };
    }

    /// <summary>
    /// Build the <see cref="FlashbackAlternativeCost"/> for Lingering Souls
    /// (printed Flashback {1}{B}) by running <see cref="OracleText"/>
    /// through <see cref="FlashbackOracleParser"/>. Going through the
    /// parser (rather than hard-coding the cost here) keeps the named-
    /// factory path and the data-driven oracle binder path agreeing on
    /// shape.
    /// </summary>
    public static FlashbackAlternativeCost BuildFlashbackCost()
    {
        var descriptor = FlashbackOracleParser.TryParse(OracleText)
            ?? throw new InvalidOperationException(
                "FlashbackOracleParser failed to parse Lingering Souls' oracle text.");
        return new FlashbackAlternativeCost(descriptor.ManaCost);
    }

    /// <summary>
    /// CR 111 / CR 111.4 — create one 1/1 white Spirit creature token with
    /// Flying under <paramref name="controller"/>. White colour is stamped
    /// via <see cref="TokenFactory.TokenSpec.Colors"/>; Flying is added as
    /// a granted <see cref="KeywordAbility"/> via the spec's Keywords list
    /// (CR 702.9).
    /// </summary>
    public static Creature CreateSpiritToken(
        Player controller,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: "Spirit",
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Spirit },
            Keywords: new[] { "Flying" },
            Colors: new[] { ManaColor.White });

        return TokenFactory.CreateOnBattlefield(spec, controller, zoneService);
    }
}
