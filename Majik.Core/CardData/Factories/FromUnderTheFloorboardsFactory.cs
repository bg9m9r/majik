using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for From Under the Floorboards (Shadows over Innistrad,
/// {3}{B}{B}).
///
/// Sorcery. Oracle text (Scryfall, verified 2026-06-10):
///   "Madness {X}{B}{B} (If you discard this card, discard it into exile. When
///    you do, cast it for its madness cost or put it into your graveyard.)
///    Create three tapped 2/2 black Zombie creature tokens and you gain 3 life.
///    If this spell's madness cost was paid, instead create X of those tokens
///    and you gain X life."
///
/// ## Madness is intrinsic
/// "Madness {X}{B}{B}" needs NO factory wiring. CR 702.35 madness works for
/// every catalogued card via <see cref="Majik.Core.Keywords.MadnessCatalog"/>
/// (this card is catalogued at <c>{X}{B}{B}</c>) consulted by the central
/// discard funnel <see cref="Fx.DiscardCard"/> — a discarded madness card is
/// routed to exile and offered for its madness cost automatically. This factory
/// implements only the token-minting + gain-life spell body.
///
/// ## Shape source
/// Card identity (name, {3}{B}{B}, Sorcery) is loaded from
/// <c>Majik.Core/CardData/Cards/from-under-the-floorboards.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The resolve-time spell body is supplied
/// via <see cref="BuildSpellDefinition"/>.
///
/// ## Implemented (v1)
/// - Sorcery identity at {3}{B}{B} (the printed / normal cast cost).
/// - <b>Madness-vs-normal count</b>: the number of tokens (and the life gained)
///   is the discriminator the oracle text turns on — "if this spell's madness
///   cost was paid, instead create X … and you gain X life." The madness cost
///   is <c>{X}{B}{B}</c>, so a madness cast supplies a chosen X
///   (<see cref="Game.ChosenSpellParams.X"/> is non-null); a normal {3}{B}{B}
///   cast supplies no X. The factory therefore reads <c>chosen.X</c>: when
///   present (madness paid) the count is X; when absent (normal cast) the count
///   is the printed 3. Same <c>chosen.X ?? &lt;printed&gt;</c> madness-X shape as
///   <see cref="AvacynsJudgmentFactory"/> — no new engine mechanic needed.
/// - <b>Tokens (CR 111 / CR 111.4)</b>: create <c>count</c> 2/2 black Zombie
///   creature tokens (same token shape as <see cref="GraveTitanFactory"/>),
///   each <b>tapped</b> on entry (CR 110.5h — a token told to enter tapped is
///   tapped via <see cref="Permanent.Tap()"/> immediately after creation).
/// - <b>Gain life (CR 119.3)</b>: the controller gains <c>count</c> life —
///   3 on a normal cast, X on a madness cast.
/// - No target requests — the effect resolves entirely on the caster (CR 115.1).
/// - <b>X = 0 / count = 0 (CR 107.3)</b>: zero tokens, zero life — the loop runs
///   zero times and <c>GainLife(0)</c> is a no-op.
/// </summary>
[CardName("From Under the Floorboards")]
public static class FromUnderTheFloorboardsFactory
{
    public const string CardName = "From Under the Floorboards";
    public const string Slug = "from-under-the-floorboards";

    /// <summary>CR 119.3 / oracle — the normal-cast count (no madness): "Create
    /// three tapped 2/2 black Zombie creature tokens and you gain 3 life."</summary>
    public const int NormalCount = 3;

    public const int TokenPower = 2;
    public const int TokenToughness = 2;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct the From Under the Floorboards sorcery shape (name, Sorcery,
    /// {3}{B}{B}) from the embedded JSON definition. No resolve effect is bound —
    /// callers build the create-tokens + gain-life body via
    /// <see cref="BuildSpellDefinition"/>. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Sorcery)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/>. No modes, no target
    /// requests (CR 115.1); <see cref="SpellDefinition.HasVariableX"/> is true so
    /// the madness {X}{B}{B} cast prompts for X. A normal {3}{B}{B} cast leaves X
    /// null and the count defaults to <see cref="NormalCount"/> = 3.
    /// </summary>
    /// <param name="caster">The player casting From Under the Floorboards.</param>
    /// <param name="zones">Optional zone service so spawned tokens publish
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> on ETB.</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        ZoneService? zones = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            // HasVariableX so the madness {X}{B}{B} cast prompts for X. A normal
            // {3}{B}{B} cast leaves X null and the count defaults to 3.
            HasVariableX: true,
            TargetRequests: Array.Empty<TargetRequest>(),
            // chosen.X is non-null only when the {X}{B}{B} madness cost was paid;
            // a normal cast → count = 3, a madness cast → count = X.
            EffectFactory: chosen => BuildResolveEffect(caster, chosen.X ?? NormalCount, zones));
    }

    /// <summary>
    /// Build the resolve effect: create <paramref name="count"/> tapped 2/2 black
    /// Zombie creature tokens (CR 111 / CR 111.4 / CR 110.5h) on the caster's
    /// battlefield and gain <paramref name="count"/> life (CR 119.3). CR 107.3 —
    /// X is locked at cast time; a count of 0 creates no tokens and gains no life.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        int count,
        ZoneService? zones = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: create {count} tapped 2/2 black Zombie creature tokens and gain {count} life.",
                () =>
                {
                    // CR 107.3 — negative/zero count produces no tokens and no life.
                    var safeCount = Math.Max(0, count);
                    for (var i = 0; i < safeCount; i++)
                    {
                        CreateTappedZombieToken(caster, zones);
                    }

                    // CR 119.3 — gain `count` life (GainLife(0) is a no-op).
                    if (safeCount > 0)
                    {
                        caster.GainLife(safeCount);
                    }
                }),
        };
    }

    /// <summary>
    /// CR 111 / CR 111.4 / CR 110.5h — create one 2/2 black Zombie creature token
    /// under <paramref name="controller"/> and tap it (the token is told to enter
    /// tapped). Black colour is stamped via
    /// <see cref="TokenFactory.TokenSpec.Colors"/> (CR 105 / CR 111.4).
    /// </summary>
    public static Creature CreateTappedZombieToken(
        Player controller,
        ZoneService? zones = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: "Zombie",
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Zombie },
            Keywords: null,
            // CR 111.4 — printed "2/2 black Zombie creature token".
            Colors: new[] { ManaColor.Black });

        var token = TokenFactory.CreateOnBattlefield(spec, controller, zones);

        // CR 110.5h — a token created "tapped" enters the battlefield tapped.
        token.Tap();

        return token;
    }
}
