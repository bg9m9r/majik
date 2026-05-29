using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wild Slash (Fate Reforged, {R}).
///
/// Instant. Oracle text (verified against Scryfall 2026-05):
///   "Ferocious — If you control a creature with power 4 or greater,
///    damage can't be prevented this turn.
///    Wild Slash deals 2 damage to any target."
///
/// ## Implemented (v1)
/// - <b>Instant {R}</b> shape via <see cref="CardDef.Instant"/>, mirroring
///   <see cref="ShockFactory"/> — the simplest {R} → 2-damage "any target"
///   burn (CR 115.3 — "any target" = creature, player, planeswalker, or
///   battle), routed through <see cref="Fx.DealDamageAny"/> so all legal
///   target classes resolve correctly (CR 306.7 — damage to a planeswalker
///   becomes loyalty removal).
/// - <b>Ferocious check (CR 702.105b analog — not a keyword, a
///   conditional)</b>: <see cref="BuildFerociousChecker"/> scans the
///   caster's battlefield for any creature with power ≥ 4, mirroring
///   <see cref="TemurBattleRageFactory.BuildFerociousChecker"/>. The check
///   is sampled at resolution time (CR 608.2c — intervening-if-style
///   condition evaluated as the spell resolves).
///
/// ## Deferred (v1 gap — documented no-op, same as Skullcrack)
/// - <b>"Damage can't be prevented this turn"</b> — no global
///   damage-prevention suppression infrastructure exists in the engine
///   today. Prevention effects are per-shield objects checked at
///   <see cref="Effects.DamageIntent"/> application time; there is no
///   "prevention-suppressed" flag on the replacement bus or game state to
///   gate them on. <see cref="SkullcrackFactory"/> already ships the exact
///   same clause as a documented no-op and names this same future wiring
///   point. Wild Slash follows that precedent: when the ferocious condition
///   is met the rider would set that flag, but until the flag exists the
///   clause is a no-op. The base 2 damage is unaffected either way (the
///   only burn spell that interacts with damage prevention is the rider
///   itself, which suppresses prevention rather than dealing extra damage).
///
/// Mirrors the resolve shape used by <see cref="ShockFactory"/> (base
/// 2-damage-any-target) and the ferocious-checker shape used by
/// <see cref="TemurBattleRageFactory"/>.
/// </summary>
[CardName("Wild Slash")]
public static class WildSlashFactory
{
    public const string CardName = "Wild Slash";
    public const string PrintedManaCost = "{R}";
    public const int Damage = 2;

    /// <summary>Ferocious power threshold (CR 702.105b analog).</summary>
    public const int FerociousPowerThreshold = 4;

    /// <summary>CardDef DSL — card shape only. Damage body is supplied at
    /// cast time via <see cref="BuildSpellDefinition"/> (the runtime needs
    /// the caller's target resolver, which lives on the
    /// <see cref="GameContext"/>).</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Default ferocious check: scan the caster's battlefield for any
    /// creature with base power ≥ <see cref="FerociousPowerThreshold"/>.
    /// Mirrors <see cref="TemurBattleRageFactory.BuildFerociousChecker"/>.
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
    /// Build the <see cref="SpellDefinition"/> used when Wild Slash is cast.
    /// Single 1..1 "any target" request; on resolution deals
    /// <see cref="Damage"/> (2) damage to the chosen target through
    /// <see cref="Fx.DealDamageAny"/>.
    ///
    /// The ferocious clause ("damage can't be prevented this turn") is
    /// sampled via <paramref name="ferociousChecker"/> at resolution time
    /// but is a documented v1 no-op — see the type-level remarks and
    /// <see cref="SkullcrackFactory"/> for the future prevention-suppression
    /// wiring point. It never alters the base damage outcome.
    /// </summary>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    /// <param name="ferociousChecker">Optional ferocious callback. Returns
    /// true when the caster controls a creature with power ≥ 4 at resolve
    /// time. Pass null to skip the (no-op) ferocious branch; or supply
    /// <see cref="BuildFerociousChecker"/> for live behavior.</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> resolver,
        Func<bool>? ferociousChecker = null)
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
                    Fx.Inline("Wild Slash: ferocious (no-op v1) + 2 damage to any target", () =>
                    {
                        // Ferocious (CR 702.105b analog): sample at resolution
                        // time. When the caster controls a power-4+ creature
                        // the spell would set "damage can't be prevented this
                        // turn" — DEFERRED v1 no-op (no prevention-suppression
                        // infrastructure exists; see SkullcrackFactory). The
                        // condition is still evaluated so the rider is wired the
                        // moment the flag lands; today it has no effect.
                        _ = ferociousChecker?.Invoke() == true;

                        // Base effect: 2 damage to any target (CR 115.3 /
                        // 306.7 via Fx.DealDamageAny).
                        Fx.DealDamageAny(target, Damage);
                    }),
                };
            });
    }
}
