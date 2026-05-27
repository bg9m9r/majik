using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Lava Coil (Guilds of Ravnica, {1}{R}).
///
/// Sorcery. Oracle text:
///   "Lava Coil deals 4 damage to target creature. If that creature would die
///    this turn, exile it instead."
///
/// ## Implemented (v1)
/// - Sorcery {1}{R}, red.
/// - Single 1..1 "target creature" request; on resolution deals 4 via
///   <see cref="Fx.DealDamageAny"/>. CR 608.2b — only acts when the resolved
///   target is a creature.
/// - <b>Exile rider</b>: when a <see cref="ReplacementBus"/> is supplied,
///   register an EOT-expirable <see cref="AngerOfTheGodsExileInsteadReplacement"/>
///   (the shared "damaged-this-way dies → exile" replacement, CR 700.3 /
///   514.2) scoped to the damaged creature. Null bus → damage only.
/// </summary>
[CardName("Lava Coil")]
public static class LavaCoilFactory
{
    public const string CardName = "Lava Coil";
    public const string PrintedManaCost = "{1}{R}";
    public const int Damage = 4;

    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the resolve <see cref="SpellDefinition"/>: deal 4 to the target
    /// creature; if <paramref name="replacements"/> is supplied, redirect that
    /// creature's death to exile until end of turn.
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
                new TargetRequest("target creature", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var target = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    Fx.Inline("Lava Coil: 4 damage to target creature; would die → exile", () =>
                    {
                        // CR 608.2b — only creatures are legal targets.
                        if (target is not Creature creature) return;

                        Fx.DealDamageAny(creature, Damage);

                        if (replacements != null)
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
