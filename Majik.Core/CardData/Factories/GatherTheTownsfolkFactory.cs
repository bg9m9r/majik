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

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Gather the Townsfolk (Dark Ascension / reprints,
/// {1}{W}).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "Create two 1/1 white Human creature tokens.
///    Fateful hour — If you have 5 or less life, create five of those tokens
///    instead."
///
/// ## Why it gets its own factory
/// Same "create N 1/1 white tokens" shape as <see cref="RaiseTheAlarmFactory"/>,
/// but with a fateful-hour rider. Both halves use primitives that already ship:
/// the tokens are <see cref="TokenFactory.CreateOnBattlefield"/> mints of a 1/1
/// white Human (same TokenSpec shape used elsewhere), and the count is chosen by
/// a resolution-time life-total check (same idiom as the inter-player life
/// comparison in <see cref="TimelyReinforcementsFactory"/>). No new engine
/// mechanic is required.
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {1}{W}, white (CR 105.2 / 202.2a). Mana value 2
///   (CR 202.3). Card shape comes from the embedded JSON
///   (<c>gather-the-townsfolk.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - <b>Default clause (CR 111 / 111.4)</b>: "Create two 1/1 white Human
///   creature tokens."
/// - <b>Fateful hour (CR 119.4)</b>: "Fateful hour" is an ability word with no
///   rules meaning of its own (CR 207.2c); the functional clause is "If you
///   have 5 or less life, create five of those tokens instead." Evaluated as
///   the spell resolves (CR 608.2) against the caster's current life total.
///   "5 or less life" is <c>LifeTotal &lt;= 5</c> — the boundary at exactly 5
///   fires the fateful-hour branch.
/// - <b>"instead" (CR 119.4)</b>: the five-token count <i>replaces</i> the two —
///   the spell creates two OR five, never two plus five. Implemented as a single
///   count chosen up front.
/// - No target requests (CR 115.1 — "Create … tokens" names no targets).
///
/// ## Rules citations
/// - CR 608.2 — one-shot spell resolution; the life-total check is made as the
///   spell resolves.
/// - CR 207.2c — "Fateful hour" is an ability word (flavor); no rules meaning.
/// - CR 111 / 111.4 — "create … 1/1 white Human creature token(s)."
/// - CR 119 — life totals.
///
/// ## Deferred (v1 gaps)
/// - None. Gather the Townsfolk has no riders beyond the printed oracle. The
///   caster's life total is read directly off the supplied <see cref="Player"/>
///   at resolution.
/// </summary>
[CardName("Gather the Townsfolk")]
public static class GatherTheTownsfolkFactory
{
    public const string CardName = "Gather the Townsfolk";
    public const string Slug = "gather-the-townsfolk";
    public const string PrintedManaCost = "{1}{W}";

    /// <summary>CR 111 — default "create two 1/1 white Human creature tokens."</summary>
    public const int DefaultTokenCount = 2;

    /// <summary>CR 119.4 — fateful hour "create five of those tokens instead."</summary>
    public const int FatefulHourTokenCount = 5;

    /// <summary>CR 119.4 — fateful hour fires at "5 or less life".</summary>
    public const int FatefulHourLifeThreshold = 5;

    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// CR 119.4 — true when the caster has "5 or less life", i.e. their current
    /// life total is at most <see cref="FatefulHourLifeThreshold"/>.
    /// </summary>
    public static bool IsFatefulHour(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return caster.LifeTotal <= FatefulHourLifeThreshold;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> for Gather the Townsfolk. No
    /// modes, no X, no target requests — the resolve body picks the token count
    /// from the caster's life total and mints that many Human tokens.
    /// </summary>
    /// <param name="caster">The player who cast Gather the Townsfolk; receives
    /// the Human tokens, and whose life total decides the count.</param>
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
    /// Build the resolve effect (CR 608.2): create two 1/1 white Human creature
    /// tokens, or five "instead" if the caster has 5 or less life (CR 119.4).
    /// </summary>
    /// <param name="caster">Player resolving the spell — token controller, life
    /// source for the fateful-hour check.</param>
    /// <param name="zoneService">Optional zone service for token ETB events.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: create two 1/1 white Human tokens; fateful hour — if you have 5 or less life, create five instead.",
                () =>
                {
                    // CR 608.2 — life-total check made as the spell resolves.
                    // CR 119.4 — the fateful-hour count replaces ("instead") the
                    // default, so we choose a single count rather than adding.
                    var count = IsFatefulHour(caster)
                        ? FatefulHourTokenCount
                        : DefaultTokenCount;

                    var spec = new TokenFactory.TokenSpec(
                        Name: "Human",
                        Power: TokenPower,
                        Toughness: TokenToughness,
                        Subtypes: new[] { CardSubtype.Human },
                        Keywords: null,
                        // CR 111.4 — printed "1/1 white Human creature token".
                        Colors: new[] { ManaColor.White });

                    for (var i = 0; i < count; i++)
                    {
                        TokenFactory.CreateOnBattlefield(spec, caster, zoneService);
                    }
                }),
        };
    }
}
