using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Midnight Haunting (Innistrad, {2}{W}).
///
/// Instant. Oracle text:
///   "Create two 1/1 white Spirit creature tokens with flying."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {2}{W}, white (CR 202.2a / 105.2).
///   Mana value 3 (CR 202.3).
/// - Resolve effect (<see cref="BuildResolveEffects"/>): create two 1/1
///   white Spirit creature tokens with Flying for the caster via
///   <see cref="TokenFactory.CreateOnBattlefield"/>. Mirrors
///   <see cref="SpectralProcessionFactory.CreateSpiritToken"/> — same Spirit
///   token shape (CR 111 / 111.4). Flying granted via the TokenSpec's
///   Keywords list (CR 702.9).
/// - No target requests (CR 115.1 — "Create two … tokens" names no targets).
/// - <see cref="BuildSpellDefinition"/> wraps the resolve effect into a
///   <see cref="SpellDefinition.Vanilla"/> for cast-flow integration.
///
/// ## Deferred (v1 gaps)
/// - None. Midnight Haunting has no riders (kicker, flashback, etc.) beyond
///   the printed oracle.
/// </summary>
[CardName("Midnight Haunting")]
public static class MidnightHauntingFactory
{
    public const string CardName = "Midnight Haunting";
    public const string PrintedManaCost = "{2}{W}";

    public const int TokensCreated = 2;
    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    /// <summary>
    /// Construct the Midnight Haunting instant shape. Owner and controller
    /// are both set to <paramref name="owner"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build a <see cref="SpellDefinition"/> for Midnight Haunting. No target
    /// requests (CR 115.1). The effect factory creates two Spirit tokens with
    /// Flying for <paramref name="caster"/> on resolution.
    /// </summary>
    /// <param name="caster">The player casting Midnight Haunting.</param>
    /// <param name="zoneService">Optional zone service so spawned tokens
    /// publish <see cref="Majik.Core.Events.CardMovedEvent"/> on ETB.</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return SpellDefinition.Vanilla(_ => BuildResolveEffects(caster, zoneService));
    }

    /// <summary>
    /// Build Midnight Haunting's resolve effects — create two 1/1 white
    /// Spirit creature tokens with Flying under <paramref name="caster"/>'s
    /// control. CR 111 / 111.4 / CR 702.9.
    /// </summary>
    /// <param name="caster">The resolving caster — token controller.</param>
    /// <param name="zoneService">Optional zone service for ETB events.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffects(
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
    /// CR 111 / 111.4 — create one 1/1 white Spirit creature token with
    /// Flying under <paramref name="controller"/>'s control. Mirrors
    /// <see cref="SpectralProcessionFactory.CreateSpiritToken"/> so Spirit-
    /// token minting stays uniform across white sources.
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
            // CR 105 / CR 111.4 — printed "1/1 white Spirit creature token with flying".
            Colors: new[] { ManaColor.White });

        return TokenFactory.CreateOnBattlefield(spec, controller, zoneService);
    }
}
