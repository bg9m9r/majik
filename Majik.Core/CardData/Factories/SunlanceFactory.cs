using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sunlance (Coldsnap, {W}).
///
/// Sorcery. Oracle text:
///   "Sunlance deals 3 damage to target nonwhite creature."
///
/// ## Implementation
///
/// - <b>Sorcery</b> shape, mana cost {W} (single white mana).
/// - Single 1..1 "target nonwhite creature" request (CR 115.1 — creatures
///   only; players and planeswalkers are not legal targets). The "nonwhite"
///   restriction is a colour predicate (CR 105 — a card's colours are derived
///   from the mana pips in its cost; a creature with no white pip is nonwhite).
/// - On resolution deals <see cref="Damage"/> (3) damage to the chosen
///   target creature via <see cref="Fx.DealDamageAny"/> (CR 119.2).
/// - CR 608.2b — at resolution the effect re-checks legality: if the resolved
///   target is not a creature (e.g. removed/changed type after targeting) or
///   is now white, the effect is a silent no-op rather than redirecting
///   damage.
///
/// Mirrors <see cref="FlameSlashFactory"/> (deal N damage to target creature)
/// with the nonwhite colour filter used by <see cref="DoomBladeFactory"/>.
/// </summary>
[CardName("Sunlance")]
public static class SunlanceFactory
{
    public const string CardName = "Sunlance";
    public const string PrintedManaCost = "{W}";
    public const int Damage = 3;

    /// <summary>CardDef DSL — card shape only (Sorcery, {W}).
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
    /// Build the <see cref="SpellDefinition"/> used when Sunlance is cast.
    /// Single 1..1 "target nonwhite creature" request; on resolution deals
    /// <see cref="Damage"/> (3) damage to the chosen creature through
    /// <see cref="Fx.DealDamageAny"/>.
    ///
    /// CR 608.2b — if the resolved object is not a creature, or has become
    /// white (illegal target due to zone change / type change / colour change
    /// after targeting), the effect is silently skipped. CR 105 — nonwhite =
    /// no white pip in the creature's colours.
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
                new TargetRequest(
                    Description: "target nonwhite creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Agent-prompt MVP: live gather every nonwhite creature on
                    // any battlefield. Removal intent pushes the opponent's
                    // biggest nonwhite threat up the bot's ranker.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Where(c => !CardColors.GetColors(c).Contains(ManaColor.White))
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: chosen =>
            {
                var target = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    Fx.Inline("Sunlance: 3 damage to target nonwhite creature", () =>
                    {
                        // CR 608.2b — resolution-time legality re-check.
                        if (target is not Creature creature) return;
                        // CR 105 — nonwhite filter (no {W} pip in colours).
                        if (CardColors.GetColors(creature).Contains(ManaColor.White)) return;
                        Fx.DealDamageAny(creature, Damage);
                    }),
                };
            });
    }
}
