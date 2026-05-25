using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bone Shards (Modern Horizons 2, {B}).
///
/// Sorcery. Oracle text:
///   "As an additional cost to cast this spell, sacrifice a creature
///    or discard a card.
///    Destroy target creature or planeswalker."
///
/// ## Why it gets its own factory
/// Bone Shards is the close cousin of <see cref="BoneSplintersFactory"/>
/// (same printed cost {B}, same destroy clause's structural shape) with
/// two upgrades that make it Modern-staple-good and that the engine
/// already has primitives for: (1) the additional cost is a disjunction
/// — sacrifice a creature OR discard a card — and (2) the destroy
/// targets creatures AND planeswalkers (same shape as Swift End / Murderous
/// Rider Adventure). Discard mode pairs particularly well with the
/// Madness package (Anje's Ravager, Asylum Visitor, etc.); sacrifice
/// mode pairs with Hogaak / Vengevine reanimator. {B} for unconditional
/// edict-style removal is among the most efficient printed in Modern.
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {B}.
/// - Additional cost (CR 601.2f):
///   <see cref="SacrificeCreatureOrDiscardCardAdditionalCost"/> —
///   disjunctive payment that prefers sacrificing a creature when one
///   is available and falls back to discarding a card otherwise. The
///   cast flow's pre-check (<see cref="SpellCastFlow"/>) rejects the
///   cast when NEITHER mode is payable (CR 601.2g — additional cost
///   that can't be paid → cast is illegal). Same posture as
///   <see cref="BoneSplintersFactory"/>.
/// - <b>Destroy target creature or planeswalker</b> —
///   <see cref="BuildSpellDefinition"/> declares a single 1..1 "target
///   creature or planeswalker" <see cref="TargetRequest"/> (Intent:
///   <see cref="BotIntent.Removal"/>) with a live
///   <see cref="TargetRequest.CandidateGatherer"/> that enumerates
///   every creature + planeswalker on the battlefield across all
///   players. On resolution the targeted permanent is destroyed via
///   <see cref="OracleSpellBinder.MoveToGraveyard"/>
///   (CR 701.7) iff it is still a creature or planeswalker on the
///   battlefield (CR 608.2b — illegal target → no-op). Indestructible
///   (CR 702.12) cancels the destroy; active regeneration shields
///   (CR 701.15) are consumed via the Destroy reason, since Bone
///   Shards does NOT print "can't be regenerated".
///
/// ## Deferred (v1 gaps)
/// - <b>Mode prompt</b>. The agent doesn't choose between sacrifice
///   and discard at announcement; the cost defaults to sacrificing a
///   creature when one is available, otherwise discards. Same queue
///   as <see cref="DiscardACardCost"/>'s deferred discard-target
///   prompt and Bone Splinters' deferred sacrifice-target prompt.
/// - <b>Self-sacrifice loophole</b>: same as Bone Splinters — the
///   engine doesn't currently prevent the caster from picking the
///   same creature as both the sacrificed cost and the targeted
///   destroy. Resolution-time legality check (CR 608.2b) makes the
///   destroy a no-op if the target was the one sacrificed (it has
///   moved to the graveyard before the destroy resolves), so the rule
///   holds correctly without explicit ordering enforcement.
/// </summary>
[CardName("Bone Shards")]
public static class BoneShardsFactory
{
    public const string CardName = "Bone Shards";
    public const string PrintedManaCost = "{B}";

    /// <summary>
    /// Build a Bone Shards sorcery owned by <paramref name="owner"/>.
    /// Card shape only — the resolve-time target request + destroy effect
    /// is built on demand via <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Bone Shards is
    /// cast. Declares the disjunctive sacrifice-or-discard additional
    /// cost (CR 601.2f) alongside a single 1..1 "target creature or
    /// planeswalker" <see cref="TargetRequest"/>; on resolution the
    /// targeted permanent is destroyed (CR 701.7) iff it is still a
    /// creature or planeswalker on the battlefield at resolution
    /// (CR 608.2b).
    /// </summary>
    /// <param name="resolver">Resolves the raw target token to a live
    /// engine object (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target creature or planeswalker",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Live gatherer (agent-prompt MVP). All creatures +
                    // planeswalkers on the battlefield across every
                    // player — HeuristicBotAgent.Score handles the
                    // ownership flip so opponent permanents rank ahead
                    // of own permanents for Removal intent.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c.HasType(CardType.Creature)
                            || c.HasType(CardType.Planeswalker))
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: chosen =>
            {
                var raw = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: destroy target creature or planeswalker",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality check.
                            // Target must still be a creature or
                            // planeswalker permanent at resolution. If the
                            // chosen target was the same card the caster
                            // sacrificed for the additional cost, it has
                            // already moved to the graveyard and this
                            // guard makes the destroy a no-op.
                            if (raw is not Permanent permanent) return;
                            if (permanent.Zone != ZoneType.Battlefield) return;
                            if (!(permanent.HasType(CardType.Creature)
                                  || permanent.HasType(CardType.Planeswalker))) return;

                            // CR 701.7 — destroy. Indestructible
                            // (CR 702.12) cancels; active regeneration
                            // shield (CR 701.15) IS consumed (printed
                            // text does not say "can't be regenerated").
                            OracleSpellBinder.MoveToGraveyard(permanent, ZoneMoveReason.Destroy);
                        }),
                };
            },
            AdditionalCosts: new IAdditionalCost[]
            {
                new SacrificeCreatureOrDiscardCardAdditionalCost(),
            });
    }
}
