using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Flame Slash (Rise of the Eldrazi, {R}).
///
/// Sorcery. Oracle text:
///   "Flame Slash deals 4 damage to target creature."
///
/// ## Implementation
///
/// - <b>Sorcery</b> shape, mana cost {R} (single red mana).
/// - Single 1..1 "target creature" request (CR 115.1 — creatures only;
///   players and planeswalkers are not legal targets).
/// - On resolution deals <see cref="Damage"/> (4) damage to the chosen
///   target creature via <see cref="Fx.DealDamageAny"/> (CR 119.2).
/// - CR 608.2b — if the resolved target is not a creature (e.g. it was
///   removed from the battlefield after targeting), the effect is a
///   no-op rather than redirecting damage to a player.
/// </summary>
[CardName("Flame Slash")]
public static class FlameSlashFactory
{
    public const string CardName = "Flame Slash";
    public const string PrintedManaCost = "{R}";
    public const int Damage = 4;

    /// <summary>CardDef DSL — card shape only (Sorcery, {R}).
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
    /// Build the <see cref="SpellDefinition"/> used when Flame Slash is
    /// cast. Single 1..1 "target creature" request; on resolution deals
    /// <see cref="Damage"/> (4) damage to the chosen creature through
    /// <see cref="Fx.DealDamageAny"/>.
    ///
    /// CR 608.2b — if the resolved object is not a creature (illegal
    /// target due to zone change or type change after targeting), the
    /// effect is silently skipped.
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
                    Fx.Inline("Flame Slash: 4 damage to target creature", () =>
                    {
                        // CR 608.2b — only creatures are legal targets.
                        if (target is not Creature creature) return;
                        Fx.DealDamageAny(creature, Damage);
                    }),
                };
            });
    }
}
