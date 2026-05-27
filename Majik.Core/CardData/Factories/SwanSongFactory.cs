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
/// Named-card factory for Swan Song ({U}).
///
/// Instant. Oracle text:
///   "Counter target enchantment, instant, or sorcery spell. Its controller
///    creates a 2/2 blue Bird creature token with flying."
///
/// ## Implemented (v1)
/// - Instant {U}, blue.
/// - <see cref="NegateFactory"/>-shape counter with a three-type filter
///   (enchantment / instant / sorcery). CR 608.2b: if the chosen target is
///   none of those at resolution, neither the counter nor the token happens
///   (applied defensively at resolve time).
/// - Compensation token: the COUNTERED spell's controller (the opponent)
///   gets a 2/2 blue Bird with flying via
///   <see cref="TokenFactory.CreateOnBattlefield"/>, mirroring Young
///   Pyromancer's token path. Created only when the counter actually happens.
/// </summary>
[CardName("Swan Song")]
public static class SwanSongFactory
{
    public const string CardName = "Swan Song";
    public const string PrintedManaCost = "{U}";

    public const int TokenPower = 2;
    public const int TokenToughness = 2;

    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "counter target enchantment/instant/sorcery spell; its
    /// controller creates a 2/2 blue flying Bird" SpellDefinition.
    /// </summary>
    /// <param name="targetResolver">Resolves the raw target token to a live engine object.</param>
    /// <param name="stack">Active stack; required to remove the countered spell.</param>
    /// <param name="zones">Optional ZoneService so the Bird's enters-the-battlefield
    /// event fires (Soul Warden etc.); null falls back to raw placement.</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack,
        ZoneService? zones = null)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target enchantment, instant, or sorcery spell", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect("Swan Song — counter target enchantment/instant/sorcery spell; its controller creates a 2/2 flying Bird", () =>
                    {
                        if (stack == null || resolved is not ISpell spell) return;

                        // CR 608.2b — only enchantment / instant / sorcery
                        // spells are legal targets.
                        var c = spell.Card;
                        if (!(c.HasType(CardType.Enchantment)
                              || c.HasType(CardType.Instant)
                              || c.HasType(CardType.Sorcery)))
                        {
                            return;
                        }

                        OracleSpellBinder.RemoveFromStack(stack, spell);
                        spell.Card.SetZone(ZoneType.Graveyard);

                        // "Its controller creates a 2/2 blue Bird with flying."
                        var controller = spell.Controller;
                        if (controller == null) return;

                        var spec = new TokenFactory.TokenSpec(
                            Name: "Bird",
                            Power: TokenPower,
                            Toughness: TokenToughness,
                            Subtypes: new[] { CardSubtype.Bird },
                            Keywords: new[] { "Flying" },
                            Colors: new[] { ManaColor.Blue });

                        TokenFactory.CreateOnBattlefield(spec, controller, zones);
                    }),
                };
            });
    }
}
