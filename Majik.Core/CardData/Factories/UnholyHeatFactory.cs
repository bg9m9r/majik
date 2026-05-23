using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Unholy Heat (Modern Horizons 2, {R}).
///
/// Instant. Oracle text:
///   "Unholy Heat deals 2 damage to any target.
///    Delirium — Unholy Heat deals 4 damage to that target instead if
///    there are four or more card types among cards in your graveyard."
///
/// ## Implementation
///
/// CR 702.105 — Delirium is a state check; at the moment of the check
/// (here: spell resolution) count the distinct <see cref="CardType"/>
/// values across cards in the controller's graveyard. If that count is
/// ≥ 4, the higher damage value applies instead of the base damage.
///
/// The card-type counting helper is reused from
/// <see cref="TarmogoyfFactory.CountDistinctCardTypes"/> — same shape
/// of "count distinct types across a card collection", just scoped to
/// the spell controller's graveyard instead of all graveyards.
///
/// Card-shape only here; the resolve-time spell definition (target +
/// damage effect with delirium gate) is built on-demand via
/// <see cref="BuildSpellDefinition(Player, Func{object, object})"/>
/// because <see cref="SpellDefinition"/> needs a target resolver
/// supplied by the caller's <see cref="GameContext"/>.
/// </summary>
public static class UnholyHeatFactory
{
    public const string CardName = "Unholy Heat";
    public const string PrintedManaCost = "{R}";

    public const int BaseDamage = 2;
    public const int DeliriumDamage = 4;
    public const int DeliriumThreshold = 4;

    /// <summary>
    /// Build an Unholy Heat instant owned by <paramref name="owner"/>.
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
    /// Build the <see cref="SpellDefinition"/> used when Unholy Heat is
    /// cast. Single 1..1 "any target" request; on resolution the
    /// controller's graveyard is sampled and the damage amount picked
    /// based on whether delirium is satisfied (CR 702.105).
    /// </summary>
    /// <param name="controller">Spell controller — the graveyard whose
    /// distinct card-type count drives delirium.</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player controller,
        Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(controller);
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
                    new Effect("Unholy Heat: delirium-conditional damage", () =>
                    {
                        var amount = IsDeliriumActive(controller)
                            ? DeliriumDamage
                            : BaseDamage;
                        OracleSpellBinder.DealDamage(target, amount);
                    }),
                };
            });
    }

    /// <summary>
    /// Sample the controller's graveyard for delirium (CR 702.105):
    /// true iff there are 4+ distinct <see cref="CardType"/> values
    /// across cards in <paramref name="controller"/>'s graveyard.
    /// Reuses <see cref="TarmogoyfFactory.CountDistinctCardTypes"/>.
    /// </summary>
    public static bool IsDeliriumActive(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        var count = TarmogoyfFactory.CountDistinctCardTypes(
            controller.Zones.Graveyard.GetCards());
        return count >= DeliriumThreshold;
    }
}
