using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Captain's Call (Magic 2013, {3}{W}).
///
/// Sorcery. Oracle text:
///   "Create three 1/1 white Soldier creature tokens."
///
/// ## Implemented (v1)
/// - Sorcery shape with printed mana cost {3}{W} (mana value 4).
/// - Resolve effect (<see cref="BuildResolveEffect"/>): create three 1/1
///   white Soldier creature tokens via
///   <see cref="TokenFactory.CreateOnBattlefield"/>. White colour stamp
///   applied via <see cref="TokenFactory.TokenSpec.Colors"/>
///   (CR 105 / 111.4). No keywords (plain Soldier token).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape-only path for tests / binders.
/// </summary>
[CardName("Captain's Call")]
public static class CaptainsCallFactory
{
    public const string CardName = "Captain's Call";

    /// <summary>Printed mana cost — {3}{W} (mana value 4).</summary>
    public const string PrintedManaCost = "{3}{W}";

    public const int TokensCreated = 3;
    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    /// <summary>Oracle text reference.</summary>
    public const string OracleText =
        "Create three 1/1 white Soldier creature tokens.";

    /// <summary>
    /// Construct the Captain's Call sorcery shape with no resolve effect
    /// bound. Use <see cref="BuildResolveEffect"/> to compose the
    /// create-three-Soldiers body.
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
    /// Build Captain's Call's resolve effect — create three 1/1 white
    /// Soldier creature tokens under <paramref name="caster"/> (CR 111 / 111.4).
    /// </summary>
    /// <param name="caster">The resolving caster — token controller.</param>
    /// <param name="zoneService">Optional zone service so each spawned
    /// Soldier token publishes <see cref="Majik.Core.Events.CardMovedEvent"/>
    /// on ETB. When null, tokens use raw zone moves.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
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
    /// <paramref name="controller"/>.
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
            Keywords: Array.Empty<string>(),
            Colors: new[] { ManaColor.White });

        return TokenFactory.CreateOnBattlefield(spec, controller, zoneService);
    }
}
