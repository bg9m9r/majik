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
/// Named-card factory for Raise the Alarm (Mirrodin, {1}{W}).
///
/// Instant. Oracle text:
///   "Create two 1/1 white Soldier creature tokens."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {1}{W}, white (CR 202.2a / 105.2).
///   Mana value 2 (CR 202.3).
/// - Resolve effect (<see cref="BuildResolveEffects"/>): create two 1/1
///   white Soldier creature tokens for the caster via
///   <see cref="TokenFactory.CreateOnBattlefield"/>, matching the pattern
///   established by <see cref="SpectralProcessionFactory"/> for looped
///   token creation. Explicit White colour stamp via
///   <see cref="TokenFactory.TokenSpec.Colors"/> (CR 105 / 111.4).
/// - No target requests (CR 115.1 — "Create two … tokens" names no targets).
/// - <see cref="BuildSpellDefinition"/> wraps the resolve effect into a
///   <see cref="SpellDefinition.Vanilla"/> for cast-flow integration.
///
/// ## Deferred (v1 gaps)
/// - None. Raise the Alarm has no riders (flash, kicker, etc.) beyond the
///   printed oracle.
/// </summary>
[CardName("Raise the Alarm")]
public static class RaiseTheAlarmFactory
{
    public const string CardName = "Raise the Alarm";
    public const string PrintedManaCost = "{1}{W}";

    public const int TokensCreated = 2;
    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    /// <summary>
    /// Construct the Raise the Alarm instant shape. Owner and controller are
    /// both set to <paramref name="owner"/>.
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
    /// Build a <see cref="SpellDefinition"/> for Raise the Alarm. No target
    /// requests (CR 115.1). The effect factory creates two Soldier tokens for
    /// <paramref name="caster"/> on resolution.
    /// </summary>
    /// <param name="caster">The player casting Raise the Alarm.</param>
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
    /// Build Raise the Alarm's resolve effects — create two 1/1 white
    /// Soldier creature tokens under <paramref name="caster"/>'s control.
    /// CR 111 / 111.4.
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
                $"{CardName}: create {TokensCreated} 1/1 white Soldier tokens",
                () =>
                {
                    for (var i = 0; i < TokensCreated; i++)
                    {
                        CreateSoldierToken(caster, zoneService);
                    }
                }),
        };
    }

    /// <summary>
    /// CR 111 / 111.4 — create one 1/1 white Soldier creature token under
    /// <paramref name="controller"/>'s control.
    /// </summary>
    public static Creature CreateSoldierToken(
        Player controller,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: "Soldier",
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Soldier },
            Keywords: null,
            // CR 105 / CR 111.4 — printed "1/1 white Soldier creature token".
            Colors: new[] { ManaColor.White });

        return TokenFactory.CreateOnBattlefield(spec, controller, zoneService);
    }
}
