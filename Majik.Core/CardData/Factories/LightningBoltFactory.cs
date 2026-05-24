using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Lightning Bolt (Alpha and many reprints, {R}).
///
/// Instant. Oracle text:
///   "Lightning Bolt deals 3 damage to any target."
///
/// ## Implementation
///
/// The simplest burn spell in Modern. Single 1..1 "any target" target
/// request — same shape as <see cref="BurstLightningFactory"/> (deals 2)
/// and <see cref="UnholyHeatFactory"/> (deals 2/4). On resolution the
/// spell deals exactly 3 damage to the chosen target via
/// <see cref="SearingBlazeFactory.DealDamageWithPlaneswalker"/> so
/// Player, Creature, and Planeswalker targets all work (CR 119 /
/// CR 306.7 loyalty removal).
///
/// Card-shape only here; the resolve-time spell definition (target +
/// damage effect) is built on demand via
/// <see cref="BuildSpellDefinition(Func{object, object})"/> because
/// <see cref="SpellDefinition"/> needs a target resolver supplied by
/// the caller's <see cref="GameContext"/>.
/// </summary>
public static class LightningBoltFactory
{
    public const string CardName = "Lightning Bolt";
    public const string PrintedManaCost = "{R}";
    public const int DamageAmount = 3;

    /// <summary>
    /// Build a Lightning Bolt instant owned by <paramref name="owner"/>.
    /// Card shape only — see <see cref="BuildSpellDefinition"/> for the
    /// resolve-time damage effect.
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
    /// Build the <see cref="SpellDefinition"/> used when Lightning Bolt is
    /// cast. Single 1..1 "any target" request; on resolution deals 3
    /// damage to the chosen target (CR 119).
    ///
    /// Routes through
    /// <see cref="SearingBlazeFactory.DealDamageWithPlaneswalker"/> so
    /// loyalty removal fires correctly on Planeswalker targets
    /// (CR 119.3 / 306.7).
    /// </summary>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> resolver)
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
                    new Effect("Lightning Bolt: 3 damage to any target", () =>
                        SearingBlazeFactory.DealDamageWithPlaneswalker(
                            target, DamageAmount)),
                };
            });
    }
}
