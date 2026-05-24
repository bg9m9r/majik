using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Primitives;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

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
/// ## Implementation (v1 — kicker primitive deferred)
///
/// CR 702.33 — Kicker is an additional cost (not an alternative cost)
/// that modifies the spell's effect when paid. There is no Kicker
/// primitive in the engine yet (see <c>Majik.Core/Costs/</c> — Buyback,
/// Delve, Suspend, Convoke, etc. are present; Kicker is not). Wiring
/// Kicker properly requires:
///   - An <see cref="IAdditionalCost"/> shape callers can layer onto a
///     cast (Buyback is the closest existing analogue).
///   - A "was kicked?" state bit plumbed from cast-time decision through
///     <see cref="SpellCastFlow"/> to the resolving stack object so the
///     EffectFactory can branch on it (CR 702.33b).
///   - <see cref="OracleSpellBinder"/> / <see cref="KeywordAnalyzer"/>
///     awareness so data-driven cards (Goblin Bushwhacker, Kicker
///     Apocalypse, etc.) discover the additional cost from oracle text.
///
/// Until that infra lands, Burst Lightning ships with default-not-kicked
/// behavior: cast resolves for 2 damage. The kicked branch is structural
/// — callers can opt in by passing <c>wasKicked: true</c> to
/// <see cref="BuildSpellDefinition"/>, which yields 4 damage. Bots
/// driving the cost-payment side won't choose to kick today (no
/// additional-cost probe), so the practical effect at the table is "{R}
/// Instant: 2 damage to any target".
///
/// Card-shape only here; the resolve-time spell definition (target +
/// damage effect with the kicked branch) is built on-demand via
/// <see cref="BuildSpellDefinition(Func{object, object}, bool)"/>
/// because <see cref="SpellDefinition"/> needs a target resolver
/// supplied by the caller's <see cref="GameContext"/>.
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
    /// is cast. Single 1..1 "any target" request; on resolution deals
    /// <see cref="BaseDamage"/> (2) or <see cref="KickedDamage"/> (4)
    /// based on <paramref name="wasKicked"/>.
    ///
    /// CR 702.33b — "if [spell] was kicked" is checked at the moment
    /// the spell resolves; the kicker decision is locked in when the
    /// spell is cast (CR 601.2b). Until Kicker is a real primitive, the
    /// flag is supplied by the caller.
    /// </summary>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    /// <param name="wasKicked">Whether the kicker cost was paid at cast
    /// time. Defaults to <c>false</c> — kicker is not yet wired through
    /// <see cref="SpellCastFlow"/>, so production casts ship as
    /// not-kicked.</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> resolver,
        bool wasKicked = false)
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
                var amount = wasKicked ? KickedDamage : BaseDamage;
                return new IEffect[]
                {
                    Fx.Inline("Burst Lightning: kicker-conditional damage", () =>
                        Fx.DealDamage(target, amount)),
                };
            });
    }
}
