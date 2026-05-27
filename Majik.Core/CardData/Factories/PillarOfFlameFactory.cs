using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Pillar of Flame (Avacyn Restored, {R}).
///
/// Sorcery. Oracle text:
///   "Pillar of Flame deals 2 damage to any target. If a creature dealt
///    damage this way would die this turn, exile it instead."
///
/// ## Implemented (v1)
/// - Sorcery {R}, red.
/// - Single 1..1 "any target" request; on resolution deals 2 via
///   <see cref="Fx.DealDamageAny"/> (creature / player / planeswalker / battle).
/// - <b>Exile rider</b>: when a <see cref="ReplacementBus"/> is supplied and
///   the target is a creature, register an EOT-expirable
///   <see cref="AngerOfTheGodsExileInsteadReplacement"/> (the shared
///   "damaged-this-way dies → exile" replacement, CR 700.3 / 514.2) scoped to
///   the single damaged creature. Null bus → damage only (shape tests).
/// </summary>
[CardName("Pillar of Flame")]
public static class PillarOfFlameFactory
{
    public const string CardName = "Pillar of Flame";
    public const string PrintedManaCost = "{R}";
    public const int Damage = 2;

    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the resolve <see cref="SpellDefinition"/>: deal 2 to any target;
    /// if the target is a creature and <paramref name="replacements"/> is
    /// supplied, redirect that creature's death to exile until end of turn.
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> resolver,
        ReplacementBus? replacements = null)
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
                    Fx.Inline("Pillar of Flame: 2 damage to any target; damaged creature dies → exile", () =>
                    {
                        Fx.DealDamageAny(target, Damage);

                        if (replacements != null && target is Creature creature)
                        {
                            replacements.Register<ZoneMoveIntent>(
                                new AngerOfTheGodsExileInsteadReplacement(
                                    new HashSet<Creature> { creature }));
                        }
                    }),
                };
            });
    }
}
