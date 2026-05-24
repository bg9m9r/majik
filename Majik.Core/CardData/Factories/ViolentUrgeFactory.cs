using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Violent Urge (Duskmourn: House of Horror, {R}).
///
/// Instant. Oracle text:
///   "Target creature gets +1/+0 and gains first strike until end of turn.
///    Delirium — If there are four or more card types among cards in your
///    graveyard, that creature gains double strike until end of turn."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {R}.
/// - Resolve-time <see cref="SpellDefinition"/> (via
///   <see cref="BuildSpellDefinition"/>) declares one 1..1 "target creature"
///   request. On resolution the targeted creature gets +1/+0 (Layer 7c via
///   <see cref="PumpUntilEndOfTurnEffect"/>) and gains First strike via
///   <see cref="GrantKeywordUntilEndOfTurnEffect"/> (Layer 6 keyword grant —
///   CR 613.1c). Both expire at the cleanup step (CR 514.2).
/// - Delirium (CR 702.105) sampled at resolution time: count distinct
///   <see cref="CardType"/> values across cards in the caster's graveyard
///   via <see cref="TarmogoyfFactory.CountDistinctCardTypes"/> (the shared
///   helper Unholy Heat already routes through). When the count is ≥ 4,
///   the target ALSO gains Double strike until end of turn. Per CR 702.4b
///   a creature with both First strike and Double strike just has Double
///   strike — but the spell registers both keyword-grant effects faithfully
///   so the combat reader is the single source of truth.
/// - Card-shape only at the dispatcher; the resolve-time SpellDefinition
///   needs a target resolver supplied by the caller's
///   <see cref="GameContext"/>. Same posture as Unholy Heat / Temur Battle
///   Rage / Reckless Charge.
///
/// ## Deferred (v1 gaps)
/// - <b>Illegal-target fizzle</b>: handled by <see cref="SpellCastFlow"/>
///   at resolution-time target legality (CR 608.2b); the resolve closure
///   additionally guards against a non-Creature resolver result and a
///   missing <see cref="Creature.ActiveEffects"/> service so the effect
///   is a clean no-op rather than a NRE.
/// - <b>Anaphoric "that creature"</b>: the delirium clause's "that
///   creature" binds to the same target as the pump/first-strike half —
///   there is no second target request (CR 700.2).
/// </summary>
[CardName("Violent Urge")]
public static class ViolentUrgeFactory
{
    public const string CardName = "Violent Urge";
    public const string PrintedManaCost = "{R}";

    /// <summary>+P pump magnitude. Violent Urge prints +1/+0.</summary>
    public const int PumpPower = 1;

    /// <summary>+T pump magnitude. Violent Urge prints +1/+0.</summary>
    public const int PumpToughness = 0;

    /// <summary>Base granted keyword — CR 702.7 First strike.</summary>
    public const string GrantedFirstStrike = "First strike";

    /// <summary>Delirium granted keyword — CR 702.4 Double strike.</summary>
    public const string GrantedDoubleStrike = "Double strike";

    /// <summary>Delirium threshold — CR 702.105.</summary>
    public const int DeliriumThreshold = 4;

    /// <summary>
    /// Build a Violent Urge instant owned by <paramref name="owner"/>.
    /// Card shape only — the resolve-time target request + pump/first-strike
    /// (+ delirium double-strike rider) is built on demand via
    /// <see cref="BuildSpellDefinition"/>.
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
    /// Build the <see cref="SpellDefinition"/> used when Violent Urge is
    /// cast. Single 1..1 "target creature" request; on resolution the
    /// targeted creature gets +1/+0 and gains First strike until end of
    /// turn, and additionally gains Double strike until end of turn if the
    /// caster's graveyard satisfies delirium at resolution time.
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
                new TargetRequest("target creature", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var raw = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect("Violent Urge: +1/+0 and first strike (delirium → double strike) until end of turn", () =>
                    {
                        // CR 608.2b — illegal-target defensive guard. If the
                        // resolver returns a non-Creature (zone-change /
                        // type-loss / wrong resolver), or the target has no
                        // live continuous-effects service wired (shape-only
                        // tests), the spell does nothing.
                        if (raw is not Creature target) return;
                        if (target.ActiveEffects == null) return;

                        // CR 613.1c Layer 7c — +1/+0 pump.
                        target.ActiveEffects.Register(
                            new PumpUntilEndOfTurnEffect(target, PumpPower, PumpToughness));

                        // CR 613.1c Layer 6 — keyword grant: First strike.
                        target.ActiveEffects.Register(
                            new GrantKeywordUntilEndOfTurnEffect(target, GrantedFirstStrike));

                        // CR 702.105 — delirium is a state check sampled at
                        // resolution time. Count distinct CardType values
                        // across cards in the caster's graveyard via the
                        // shared Tarmogoyf helper. If ≥ 4, the target also
                        // gains Double strike until end of turn.
                        if (IsDeliriumActive(controller))
                        {
                            target.ActiveEffects.Register(
                                new GrantKeywordUntilEndOfTurnEffect(target, GrantedDoubleStrike));
                        }
                    }),
                };
            });
    }

    /// <summary>
    /// Sample the controller's graveyard for delirium (CR 702.105):
    /// true iff there are 4+ distinct <see cref="CardType"/> values
    /// across cards in <paramref name="controller"/>'s graveyard.
    /// Reuses <see cref="TarmogoyfFactory.CountDistinctCardTypes"/>
    /// (the same helper Unholy Heat routes through).
    /// </summary>
    public static bool IsDeliriumActive(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        var count = TarmogoyfFactory.CountDistinctCardTypes(
            controller.Zones.Graveyard.GetCards());
        return count >= DeliriumThreshold;
    }
}
