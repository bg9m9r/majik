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
/// Named-card factory for Defibrillating Current (Murders at Karlov Manor,
/// {2/R}{2/W}{2/B}).
///
/// Sorcery. Oracle text (verified against the embedded Scryfall seed):
///   "({2/R} can be paid with any two mana or with {R}, and so on.)
///    Defibrillating Current deals 4 damage to target creature or planeswalker
///    and you gain 2 life."
///
/// ## Implementation
///
/// Combines three already-supported shapes:
/// - <b>Single "target creature or planeswalker" request</b> — same target
///   shape + live candidate gatherer as <see cref="BitterTriumphFactory"/> /
///   <see cref="CatharticPyreFactory"/>'s damage mode (every creature +
///   planeswalker on every battlefield; the bot ranks opponent permanents
///   highest via <see cref="BotIntent.Removal"/>).
/// - <b>4 damage to the resolved target</b> via <see cref="Fx.DealDamageAny"/>
///   (CR 120.1a — creature damage; CR 306.7 — planeswalker damage becomes
///   loyalty removal).
/// - <b>"and you gain 2 life" rider</b> — a non-conditional second clause
///   (CR 608.2e, left-to-right) routed through <see cref="Fx.GainLife"/>
///   (CR 119.3) so "whenever you gain life" observers + LifeGainedThisTurn
///   trackers see it. The lifegain happens regardless of whether the damage
///   clause finds a legal target (CR 608.2c — only the targeted clause is
///   skipped when the target is illegal; the untargeted lifegain still
///   resolves).
/// - <b>Three-color twobrid cost</b> {2/R}{2/W}{2/B} (CR 107.4e / CR 202.3f),
///   the multicolored analogue of <see cref="FlameJavelinFactory"/>'s
///   {2/R}{2/R}{2/R}. Each pip pays as one colored mana or {2}; mana value is
///   6. The engine's existing hybrid cost-payer handles payment (same path as
///   Flame Javelin / Spectral Procession).
///
/// Card shape comes from the embedded JSON (<c>defibrillating-current.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/>. The resolve-time body lives in
/// <see cref="BuildSpellDefinition"/> because a <see cref="SpellDefinition"/>
/// needs a target resolver supplied by the caller's
/// <see cref="GameContext"/> (not expressible in the data-only JSON schema).
/// </summary>
[CardName("Defibrillating Current")]
public static class DefibrillatingCurrentFactory
{
    public const string CardName = "Defibrillating Current";
    public const string Slug = "defibrillating-current";

    /// <summary>
    /// Printed mana cost — three twobrid pips {2/R}{2/W}{2/B} (CR 107.4e).
    /// Each pip pays as one colored mana or {2}; mana value is 6 (CR 202.3f).
    /// </summary>
    public const string PrintedManaCost = "{2/R}{2/W}{2/B}";

    /// <summary>CR 119 — fixed 4 damage to the target.</summary>
    public const int Damage = 4;

    /// <summary>CR 119.3 — fixed 2 life gained by the caster.</summary>
    public const int LifeGain = 2;

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Defibrillating Current
    /// is cast. Single 1..1 "target creature or planeswalker" request, no X; on
    /// resolution deals <see cref="Damage"/> (4) to the chosen target (if still
    /// legal, CR 608.2b) and the caster gains <see cref="LifeGain"/> (2) life
    /// (CR 119.3 — unconditional, resolves even if the damage clause's target
    /// is illegal).
    /// </summary>
    /// <param name="caster">The player who cast Defibrillating Current; gains
    /// 2 life on resolution.</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object). Pass
    /// <c>o =&gt; o</c> for tests that hand engine objects directly.</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature or planeswalker",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Live gatherer (CR 301 / 306.7): every creature +
                    // planeswalker on every battlefield. HeuristicBotAgent.Score
                    // flips ownership so opponent permanents rank ahead of own
                    // permanents for Removal intent (mirrors BitterTriumph).
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c.HasType(CardType.Creature)
                            || c.HasType(CardType.Planeswalker))
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: chosen =>
            {
                var raw = chosen.Targets[0][0];
                var resolved = resolver(raw);
                return new IEffect[]
                {
                    Fx.Inline(
                        "Defibrillating Current: 4 damage to target creature or planeswalker; you gain 2 life",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check; the
                            // target must still be a creature or planeswalker on
                            // the battlefield. CR 608.2c — an illegal target
                            // only skips this clause; the lifegain still resolves.
                            if (resolved is Permanent target
                                && target.Zone == Majik.Core.Zones.ZoneType.Battlefield
                                && (target.HasType(CardType.Creature)
                                    || target.HasType(CardType.Planeswalker)))
                            {
                                // CR 120.1a (creature) / CR 306.7 (planeswalker
                                // → loyalty removal) — Fx.DealDamageAny routes both.
                                Fx.DealDamageAny(target, Damage);
                            }

                            // CR 119.3 / CR 608.2e — "and you gain 2 life",
                            // unconditional second clause. Routed through
                            // Fx.GainLife so "whenever you gain life" observers
                            // + LifeGainedThisTurn trackers see it.
                            Fx.GainLife(caster, LifeGain);
                        }),
                };
            });
    }
}
