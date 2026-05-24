using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Temur Battle Rage (Khans of Tarkir, {1}{R}).
///
/// Instant. Oracle text:
///   "Target creature gains double strike until end of turn.
///    Ferocious — That creature also gains trample until end of turn if
///    you control a creature with power 4 or greater."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {1}{R}.
/// - Resolve-time <see cref="SpellDefinition"/> (via
///   <see cref="BuildSpellDefinition"/>) declares one 1..1 "target
///   creature" request. On resolution the targeted creature gains Double
///   strike until end of turn, registered as a
///   <see cref="GrantKeywordUntilEndOfTurnEffect"/> on the target's
///   <see cref="Creature.ActiveEffects"/> (CR 514.2 — expires at cleanup).
/// - Ferocious (CR 702.105b analog — not a keyword, a conditional):
///   at resolve time, if the caster controls any creature with power ≥ 4
///   (sampled from <see cref="Player.Zones.Battlefield"/>), the target
///   also gains Trample until end of turn.
/// - The <paramref name="powerChecker"/> parameter in
///   <see cref="BuildSpellDefinition"/> is a caller-supplied
///   <see cref="Func{Boolean}"/> that encapsulates the ferocious check —
///   mirrors the pattern used by StubbornDenialFactory. The single-arg
///   dispatcher path passes null (no ferocious check, only double strike).
///
/// ## Deferred (v1 gaps)
/// - <b>Illegal-target fizzle</b>: handled by <see cref="SpellCastFlow"/>
///   at resolution-time target legality (CR 608.2b); the resolve closure
///   additionally guards against a non-Creature resolver result and a
///   missing <see cref="Creature.ActiveEffects"/> service.
/// </summary>
public static class TemurBattleRageFactory
{
    public const string CardName = "Temur Battle Rage";
    public const string PrintedManaCost = "{1}{R}";

    /// <summary>Granted keyword — CR 702.4 Double strike.</summary>
    public const string GrantedDoubleStrike = "Double strike";

    /// <summary>Ferocious bonus keyword — CR 702.19 Trample.</summary>
    public const string GrantedTrample = "Trample";

    /// <summary>Ferocious power threshold (CR 702.105b).</summary>
    public const int FerociousPowerThreshold = 4;

    /// <summary>
    /// Build a Temur Battle Rage instant owned by <paramref name="owner"/>.
    /// Card shape only; the resolve-time SpellDefinition is built via
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
    /// Default ferocious check: scan the caster's battlefield for any
    /// creature with base power ≥ 4. Use when a live
    /// <see cref="ContinuousEffectsService"/> is not available.
    /// </summary>
    public static Func<bool> BuildFerociousChecker(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return () =>
        {
            foreach (var card in caster.Zones.Battlefield.GetCards())
            {
                if (card is Creature c && c.BasePower >= FerociousPowerThreshold) return true;
            }
            return false;
        };
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Temur Battle Rage
    /// is cast. Single 1..1 "target creature" request; on resolution the
    /// targeted creature gains Double strike until end of turn. If
    /// <paramref name="powerChecker"/> is non-null and returns true, the
    /// target also gains Trample until end of turn (ferocious).
    /// </summary>
    /// <param name="resolver">Target resolver from the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    /// <param name="powerChecker">Optional ferocious callback. Returns true
    /// when the caster controls a creature with power ≥ 4 at resolve time.
    /// Pass null to skip the ferocious check (only double strike is granted);
    /// or supply <see cref="BuildFerociousChecker"/> for live behavior.</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> resolver,
        Func<bool>? powerChecker = null)
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
                var raw = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect("Temur Battle Rage: gains double strike until end of turn", () =>
                    {
                        // CR 608.2b — if the target is no longer a Creature
                        // (zone-change, type-loss, etc.) or has no live
                        // continuous-effects service wired, the spell is a no-op.
                        if (raw is not Creature target) return;
                        if (target.ActiveEffects == null) return;

                        // CR 613.1c Layer 6 — keyword grant: Double strike.
                        target.ActiveEffects.Register(
                            new GrantKeywordUntilEndOfTurnEffect(target, GrantedDoubleStrike));

                        // Ferocious (CR 702.105b): sample at resolution time.
                        // If the caster controls a power-4+ creature, also
                        // grant Trample until end of turn to the same target.
                        if (powerChecker?.Invoke() == true)
                        {
                            target.ActiveEffects.Register(
                                new GrantKeywordUntilEndOfTurnEffect(target, GrantedTrample));
                        }
                    }),
                };
            });
    }
}
