using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Alchemist's Greeting (Eldritch Moon, {4}{R}).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "Alchemist's Greeting deals 4 damage to target creature.
///    Madness {1}{R}"
///
/// ## Implementation
///
/// - <b>Sorcery</b> shape, mana cost {4}{R}. Card shape comes from the
///   embedded JSON (<c>alchemists-greeting.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - Single 1..1 "target creature" request (CR 115.1 — creatures only;
///   players and planeswalkers are not legal targets).
/// - On resolution deals <see cref="Damage"/> (4) damage to the chosen
///   target creature via <see cref="Fx.DealDamageAny"/> (CR 119.2).
/// - CR 608.2b — if the resolved target is not a creature (e.g. it was
///   removed from the battlefield after targeting), the effect is a no-op
///   rather than redirecting damage.
///
/// ## Madness {1}{R}
///
/// Madness (CR 702.35) is handled intrinsically by the engine — the
/// name→cost entry in <see cref="Keywords.MadnessCatalog"/> is consulted by
/// the central discard funnel (<see cref="Fx.DiscardCard"/>), which routes a
/// discarded madness card to exile and offers it for its madness cost. No
/// per-card wiring is required here; this factory implements only the
/// printed damage body. Mirrors the damage shape of
/// <see cref="FlameSlashFactory"/> ("deals 4 damage to target creature").
/// </summary>
[CardName("Alchemist's Greeting")]
public static class AlchemistsGreetingFactory
{
    public const string CardName = "Alchemist's Greeting";
    public const string Slug = "alchemists-greeting";
    public const string PrintedManaCost = "{4}{R}";
    public const int Damage = 4;

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Alchemist's Greeting
    /// is cast. Single 1..1 "target creature" request; on resolution deals
    /// <see cref="Damage"/> (4) damage to the chosen creature through
    /// <see cref="Fx.DealDamageAny"/>.
    ///
    /// CR 608.2b — if the resolved object is not a creature (illegal target
    /// due to zone change or type change after targeting), the effect is
    /// silently skipped.
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
                new TargetRequest("target creature", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var target = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    Fx.Inline("Alchemist's Greeting: 4 damage to target creature", () =>
                    {
                        // CR 608.2b — only creatures are legal targets.
                        if (target is not Creature creature) return;
                        Fx.DealDamageAny(creature, Damage);
                    }),
                };
            });
    }
}
