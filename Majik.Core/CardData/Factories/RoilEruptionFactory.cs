using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Primitives;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Roil Eruption (Zendikar Rising, {1}{R}).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "Kicker {5} (You may pay an additional {5} as you cast this spell.)
///    Roil Eruption deals 3 damage to any target. If this spell was
///    kicked, it deals 5 damage instead."
///
/// <para>Direct analogue of <see cref="BurstLightningFactory"/> (kicker
/// burn — pay-additional → larger damage to any target). Differences:
/// this is a Sorcery (not an Instant), its printed cost is {1}{R} (not
/// {R}), its kicker is {5} (not {4}), and its damage is 3 / kicked 5
/// (not 2 / 4).</para>
///
/// <para>Kicker (CR 702.33) is a real <see cref="IAdditionalCost"/>
/// primitive — <see cref="KickerAdditionalCost"/>. The factory exposes
/// <see cref="BuildAdditionalCost"/> to construct the kicker rider for a
/// specific card instance, and the resolve body reads
/// <see cref="Card.WasKicked"/> at resolution time (CR 702.33b — "if
/// [this spell] was kicked" is checked when the spell resolves; the
/// cast-time payment locks in the sentinel during
/// <see cref="SpellCastFlow"/>'s additional-cost loop and the cleanup
/// effect appended by the cast flow clears the flag after resolution).</para>
/// </summary>
[CardName("Roil Eruption")]
public static class RoilEruptionFactory
{
    public const string CardName = "Roil Eruption";
    public const string PrintedManaCost = "{1}{R}";
    public const string KickerCostText = "{5}";

    public const int BaseDamage = 3;
    public const int KickedDamage = 5;

    /// <summary>CardDef DSL — card shape only. Kicker-conditional damage
    /// body is built via <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost);

    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Roil Eruption is
    /// cast. Single 1..1 "any target" request; on resolution reads
    /// <see cref="Card.WasKicked"/> on <paramref name="card"/> to choose
    /// between <see cref="BaseDamage"/> (3) and <see cref="KickedDamage"/>
    /// (5).
    ///
    /// <para>CR 702.33b — "if [this spell] was kicked" is checked at the
    /// moment the spell resolves; the kicker decision is locked in when
    /// the spell is cast (CR 601.2b). The runtime read off
    /// <c>card.WasKicked</c> captures that decision because
    /// <see cref="KickerAdditionalCost.Pay"/> stamps the flag at
    /// cast-announcement and <see cref="SpellCastFlow"/> appends a
    /// cleanup effect that clears it after resolution.</para>
    /// </summary>
    /// <param name="card">The cast card instance — the resolve body reads
    /// <see cref="Card.WasKicked"/> off this same reference so the kicker
    /// branch fires only when the cast actually paid the rider
    /// (CR 702.33b).</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(
        ICard card,
        Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(card);
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
                // CR 702.33b — branch on the cast-time kicker stamp.
                // Card.WasKicked is set by KickerAdditionalCost.Pay during
                // SpellCastFlow's additional-cost loop and cleared by the
                // post-resolve cleanup effect the cast flow appends.
                bool wasKicked = card is Card concrete && concrete.WasKicked;
                var amount = wasKicked ? KickedDamage : BaseDamage;
                return new IEffect[]
                {
                    Fx.Inline("Roil Eruption: kicker-conditional damage", () =>
                        Fx.DealDamage(target, amount)),
                };
            });
    }

    /// <summary>
    /// Construct Roil Eruption's kicker <see cref="IAdditionalCost"/> for
    /// the supplied <paramref name="card"/> instance. Convenience builder
    /// for callers (tests, bot decision layer) that have already decided to
    /// pay the kicker; layer the returned cost onto the cast via
    /// <see cref="SpellCastFlow.CastAsync"/>'s <c>additionalCosts</c>
    /// parameter.
    /// </summary>
    public static IAdditionalCost BuildAdditionalCost(ICard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return new KickerAdditionalCost(card, ManaCost.Parse(KickerCostText));
    }
}
