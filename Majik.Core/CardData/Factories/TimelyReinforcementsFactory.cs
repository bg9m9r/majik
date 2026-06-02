using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Timely Reinforcements (Magic 2012 / reprints,
/// {2}{W}).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "If you have less life than an opponent, you gain 6 life. If you control
///    fewer creatures than an opponent, create three 1/1 white Soldier
///    creature tokens."
///
/// ## Why it gets its own factory
/// Two independent conditional clauses, each gated on an inter-player
/// comparison ("than an opponent"), resolving on the caster. Both primitives
/// already ship: the life clause is a guarded <see cref="Fx.GainLife"/>; the
/// token clause is three <see cref="TokenFactory.CreateOnBattlefield"/> mints
/// of a 1/1 white Soldier (same TokenSpec shape as the white Soldier tokens
/// minted elsewhere). The only composite piece is the comparison predicate,
/// evaluated at resolution against the supplied player set — no new engine
/// mechanic is required.
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {2}{W}, white. Card shape comes from the
///   embedded JSON (<c>timely-reinforcements.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - <b>Life clause (CR 119.3)</b>: "If you have less life than an opponent,
///   you gain 6 life." Evaluated at resolution (CR 608.2) — the caster gains
///   6 life iff their current life total is strictly less than at least one
///   opponent's. CR 109.5 / 102.1 — "an opponent" is any other player in the
///   game.
/// - <b>Token clause (CR 111 / 111.4)</b>: "If you control fewer creatures
///   than an opponent, create three 1/1 white Soldier creature tokens." The
///   caster creates three 1/1 white Soldier tokens iff the number of
///   creatures they control is strictly less than the number at least one
///   opponent controls. Creature count uses
///   <see cref="ICard.HasType(CardType)"/> over each player's battlefield
///   (same idiom as <see cref="AdelineResplendentCatharFactory.CountCreatures"/>).
/// - <b>Clause independence (CR 608.2)</b>: each "if" is checked
///   independently against the same resolution snapshot — both, either, or
///   neither may fire. The life clause does not change creature counts and
///   the token clause does not change life totals, so the two snapshots
///   coincide regardless of evaluation order; the life clause is evaluated
///   first to match the printed order.
///
/// ## Rules citations
/// - CR 608.2 — one-shot spell resolution; intervening-"if"-style conditions
///   on a resolving spell are checked as the spell resolves.
/// - CR 109.5 / 102.1 — "an opponent" = any other player in the game.
/// - CR 119.3 — gaining life.
/// - CR 111 / 111.4 — "create three 1/1 white Soldier creature tokens."
///
/// ## Deferred (v1 gaps)
/// - <b>Live player provider</b>: like the other resolve-time multi-player
///   factories (e.g. <see cref="AllIsDustFactory"/>), the opponent set is
///   passed in explicitly rather than read off a live game accessor inside
///   the closure. Production callers supply <c>caster</c> + its opponents;
///   when only the caster is supplied (no opponents) neither clause can fire
///   (there is no opponent to be "less than"), which is the correct vacuous
///   result.
/// </summary>
[CardName("Timely Reinforcements")]
public static class TimelyReinforcementsFactory
{
    public const string CardName = "Timely Reinforcements";
    public const string Slug = "timely-reinforcements";
    public const string PrintedManaCost = "{2}{W}";

    /// <summary>CR 119.3 — "you gain 6 life."</summary>
    public const int LifeGain = 6;

    /// <summary>CR 111 — "create three 1/1 white Soldier creature tokens."</summary>
    public const int TokenCount = 3;
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
    /// Build the <see cref="SpellDefinition"/> for Timely Reinforcements. No
    /// modes, no X, no target requests — the resolve body evaluates the two
    /// conditional clauses against the caster + its opponents.
    /// </summary>
    /// <param name="caster">The player who cast Timely Reinforcements; gains
    /// the life and receives the Soldier tokens.</param>
    /// <param name="opponents">The caster's opponents (CR 102.1) — the
    /// comparison set for both "than an opponent" clauses. May be empty (e.g.
    /// shape-only paths); both clauses are then vacuously false.</param>
    /// <param name="zoneService">Optional zone service — routes the Soldier
    /// ETB through <see cref="ZoneService"/> so
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> publishes (enabling
    /// downstream ETB triggers). Null → direct zone move, suitable for
    /// shape-only paths.</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        IReadOnlyList<Player> opponents,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(opponents);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => BuildResolveEffect(caster, opponents, zoneService));
    }

    /// <summary>
    /// Build the resolve effect (CR 608.2): independently evaluate the two
    /// printed clauses against the caster + its <paramref name="opponents"/>.
    /// </summary>
    /// <param name="caster">Player resolving the spell.</param>
    /// <param name="opponents">Comparison set for "than an opponent".</param>
    /// <param name="zoneService">Optional zone service for Soldier ETB events.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        IReadOnlyList<Player> opponents,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(opponents);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: if you have less life than an opponent gain 6 life; if you control fewer creatures than an opponent create three 1/1 white Soldiers.",
                () =>
                {
                    // CR 119.3 / 109.5 — "If you have less life than an
                    // opponent, you gain 6 life." Strictly-less-than against
                    // at least one opponent.
                    if (HasLessLifeThanAnOpponent(caster, opponents))
                    {
                        Fx.GainLife(caster, LifeGain);
                    }

                    // CR 111 / 109.5 — "If you control fewer creatures than an
                    // opponent, create three 1/1 white Soldier creature
                    // tokens." Strictly-fewer than at least one opponent.
                    if (ControlsFewerCreaturesThanAnOpponent(caster, opponents))
                    {
                        var spec = new TokenFactory.TokenSpec(
                            Name: "Soldier",
                            Power: TokenPower,
                            Toughness: TokenToughness,
                            Subtypes: new[] { CardSubtype.Soldier },
                            Keywords: null,
                            // CR 111.4 — printed "1/1 white Soldier creature token".
                            Colors: new[] { ManaColor.White });

                        for (var i = 0; i < TokenCount; i++)
                        {
                            TokenFactory.CreateOnBattlefield(spec, caster, zoneService);
                        }
                    }
                }),
        };
    }

    /// <summary>
    /// CR 119.3 / 109.5 — true when <paramref name="caster"/>'s life total is
    /// strictly less than at least one of <paramref name="opponents"/>' life
    /// totals.
    /// </summary>
    public static bool HasLessLifeThanAnOpponent(
        Player caster,
        IReadOnlyList<Player> opponents)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(opponents);

        foreach (var opp in opponents)
        {
            if (opp == null || ReferenceEquals(opp, caster)) continue;
            if (caster.LifeTotal < opp.LifeTotal) return true;
        }
        return false;
    }

    /// <summary>
    /// CR 111 / 109.5 — true when the number of creatures
    /// <paramref name="caster"/> controls is strictly fewer than the number at
    /// least one of <paramref name="opponents"/> controls.
    /// </summary>
    public static bool ControlsFewerCreaturesThanAnOpponent(
        Player caster,
        IReadOnlyList<Player> opponents)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(opponents);

        var mine = CountCreatures(caster);
        foreach (var opp in opponents)
        {
            if (opp == null || ReferenceEquals(opp, caster)) continue;
            if (mine < CountCreatures(opp)) return true;
        }
        return false;
    }

    /// <summary>
    /// Count the creatures <paramref name="player"/> controls on the
    /// battlefield (CR 109.5). Same idiom as
    /// <see cref="AdelineResplendentCatharFactory.CountCreatures"/>.
    /// </summary>
    public static int CountCreatures(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return player.Zones.Battlefield.GetCards()
            .Count(c => c.HasType(CardType.Creature));
    }
}
