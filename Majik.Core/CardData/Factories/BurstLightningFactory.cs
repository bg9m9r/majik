using Majik.Core.Abilities;
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
/// Named-card factory for Burst Lightning (Zendikar / Modern Masters,
/// {R}).
///
/// Instant. Oracle text:
///   "Kicker {4} (You may pay an additional {4} as you cast this spell.)
///    Burst Lightning deals 2 damage to any target.
///    If Burst Lightning was kicked, it deals 4 damage to that target
///    instead."
///
/// <para>Kicker (CR 702.33) is now a real <see cref="IAdditionalCost"/>
/// primitive — <see cref="KickerAdditionalCost"/>. The factory exposes
/// <see cref="BuildAdditionalCost"/> to construct the kicker rider for
/// a specific card instance, and the resolve body reads
/// <see cref="Card.WasKicked"/> at resolution time (CR 702.33b — "if
/// [spell] was kicked" is checked when the spell resolves; the cast-
/// time payment locks in the sentinel during
/// <see cref="SpellCastFlow"/>'s additional-cost loop and the cleanup
/// effect appended by the cast flow clears the flag after resolution).</para>
///
/// <para>Bot-side discovery flows through
/// <see cref="KickerAltCostProbe"/> (registered in
/// <see cref="AlternativeCostProbeRegistry.CreateDefault"/>); the
/// probe's <see cref="KickerAltCostProbe.DefaultLookup"/> recognises
/// Burst Lightning as a {4}-kicker card.</para>
/// </summary>
[CardName("Burst Lightning")]
public static class BurstLightningFactory
{
    public const string CardName = "Burst Lightning";
    public const string PrintedManaCost = "{R}";
    public const string KickerCostText = "{4}";

    public const int BaseDamage = 2;
    public const int KickedDamage = 4;

    /// <summary>
    /// Build a Burst Lightning instant owned by <paramref name="owner"/>.
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
    /// Build the <see cref="SpellDefinition"/> used when Burst Lightning
    /// is cast. Single 1..1 "any target" request; on resolution reads
    /// <see cref="Card.WasKicked"/> on <paramref name="caster"/>'s
    /// active-cast card (the same card supplied to
    /// <see cref="SpellCastFlow.CastAsync"/>) to choose between
    /// <see cref="BaseDamage"/> (2) and <see cref="KickedDamage"/> (4).
    ///
    /// <para>CR 702.33b — "if [spell] was kicked" is checked at the
    /// moment the spell resolves; the kicker decision is locked in when
    /// the spell is cast (CR 601.2b). The runtime read off
    /// <c>card.WasKicked</c> captures that decision because
    /// <see cref="KickerAdditionalCost.Pay"/> stamps the flag at
    /// cast-announcement and <see cref="SpellCastFlow"/> appends a
    /// cleanup effect that clears it after resolution.</para>
    /// </summary>
    /// <param name="card">The cast card instance — the resolve body
    /// reads <see cref="Card.WasKicked"/> off this same reference so
    /// the kicker branch fires only when the cast actually paid the
    /// rider (CR 702.33b).</param>
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
                // Card.WasKicked is set by KickerAdditionalCost.Pay
                // during SpellCastFlow's additional-cost loop and
                // cleared by the post-resolve cleanup effect the
                // cast flow appends.
                bool wasKicked = card is Card concrete && concrete.WasKicked;
                var amount = wasKicked ? KickedDamage : BaseDamage;
                return new IEffect[]
                {
                    Fx.Inline("Burst Lightning: kicker-conditional damage", () =>
                        Fx.DealDamage(target, amount)),
                };
            });
    }

    /// <summary>
    /// Construct Burst Lightning's kicker <see cref="IAdditionalCost"/>
    /// for the supplied <paramref name="card"/> instance. Convenience
    /// builder for callers (tests, bot decision layer) that have already
    /// decided to pay the kicker; layer the returned cost onto the cast
    /// via <see cref="SpellCastFlow.CastAsync"/>'s <c>additionalCosts</c>
    /// parameter.
    /// </summary>
    public static IAdditionalCost BuildAdditionalCost(ICard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return new KickerAdditionalCost(card, ManaCost.Parse(KickerCostText));
    }
}
