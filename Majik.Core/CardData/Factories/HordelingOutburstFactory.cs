using Majik.Core.Abilities;
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
/// Named-card factory for Hordeling Outburst ({1}{R}{R}).
///
/// Sorcery. Oracle text:
///   "Create three 1/1 red Goblin creature tokens."
///
/// Same token-creation primitive as <see cref="DragonFodderFactory"/> /
/// <see cref="KrenkosCommandFactory"/>, but at count three and cost {1}{R}{R}.
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {1}{R}{R}.
/// - No target requests — the effect resolves entirely on the caster.
/// - Resolve effect via <see cref="BuildResolveEffect"/>:
///     calls <see cref="TokenFactory.CreateOnBattlefield"/> three times, each with a
///     <see cref="TokenFactory.TokenSpec"/> for a 1/1 red Goblin creature token
///     (CR 111 / 111.4).
/// </summary>
[CardName("Hordeling Outburst")]
public static class HordelingOutburstFactory
{
    public const string CardName = "Hordeling Outburst";
    public const string PrintedManaCost = "{1}{R}{R}";

    public const int TokenPower = 1;
    public const int TokenToughness = 1;
    public const int TokenCount = 3;

    /// <summary>
    /// Construct Hordeling Outburst as a Sorcery owned by <paramref name="owner"/>.
    /// Card shape only — the resolve closure is produced by
    /// <see cref="BuildResolveEffect"/> / <see cref="BuildSpellDefinition"/>.
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
    /// Build the <see cref="SpellDefinition"/> for Hordeling Outburst. No modes,
    /// no X, no target requests — the body resolves entirely on the caster.
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        ZoneService? zones = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => BuildResolveEffect(caster, zones));
    }

    /// <summary>
    /// Build the resolve effect: create three 1/1 red Goblin creature tokens
    /// on the caster's battlefield (CR 111 / 111.4).
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        ZoneService? zones = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: create three 1/1 red Goblin creature tokens.",
                () =>
                {
                    var spec = new TokenFactory.TokenSpec(
                        Name: "Goblin",
                        Power: TokenPower,
                        Toughness: TokenToughness,
                        Subtypes: new[] { CardSubtype.Goblin },
                        Keywords: null,
                        // CR 111.4 — printed "1/1 red Goblin creature token".
                        Colors: new[] { ManaColor.Red });

                    // Create three tokens (CR 111 — one token per "create").
                    for (var i = 0; i < TokenCount; i++)
                    {
                        TokenFactory.CreateOnBattlefield(spec, caster, zones);
                    }
                }),
        };
    }
}
