using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Volcanic Hammer (Ninth Edition / Tenth Edition,
/// {1}{R}).
///
/// Sorcery. Oracle text:
///   "Volcanic Hammer deals 3 damage to any target."
///
/// ## Implementation
///
/// - <b>Sorcery</b> shape, mana cost {1}{R}.
/// - Single 1..1 "any target" request: creature, player, planeswalker, or
///   battle are all legal targets (CR 115.3).
/// - On resolution deals <see cref="Damage"/> (3) damage to the chosen
///   target via <see cref="Fx.DealDamageAny"/> (CR 119.2).
/// - Differs from <see cref="LightningStrikeFactory"/> (same cost, same
///   damage) only in card type: Volcanic Hammer is a Sorcery, Lightning
///   Strike is an Instant (CR 307 vs. CR 304).
/// </summary>
[CardName("Volcanic Hammer")]
public static class VolcanicHammerFactory
{
    public const string CardName = "Volcanic Hammer";
    public const string PrintedManaCost = "{1}{R}";
    public const int Damage = 3;

    /// <summary>CardDef DSL — card shape only (Sorcery, {1}{R}).
    /// Damage body is supplied at cast time via
    /// <see cref="BuildSpellDefinition"/> (the runtime needs the caller's
    /// target resolver from the <see cref="GameContext"/>).</summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Volcanic Hammer is
    /// cast. Single 1..1 "any target" request; on resolution deals
    /// <see cref="Damage"/> (3) damage to the chosen target through
    /// <see cref="Fx.DealDamageAny"/>.
    /// </summary>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target token → live game
    /// object).</param>
    public static SpellDefinition BuildSpellDefinition(Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("any target", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var target = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    Fx.Inline("Volcanic Hammer: 3 damage to any target", () =>
                        Fx.DealDamageAny(target, Damage)),
                };
            });
    }
}
