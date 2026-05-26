using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cloudshift (Avacyn Restored, {W}).
///
/// Instant. Oracle text:
///   "Exile target creature you control, then return that card to the
///    battlefield under its owner's control."
///
/// CR 701.21 + CR 614 — Cloudshift is the canonical one-mana flicker
/// spell. The exile-then-return resolves entirely within a single spell
/// resolution; the returned creature is a new object (CR 400.7) so any
/// "until end of turn" effects on the exiled creature are dropped, "enters
/// the battlefield" triggers fire again, summoning sickness re-applies,
/// counters / damage / attached auras-and-equipment all clear.
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {W}.
/// - <b>Cast body</b>: single 1..1 "target creature you control"
///   <see cref="TargetRequest"/> via <see cref="BuildSpellDefinition"/>.
///   Live <c>CandidateGatherer</c> walks the casting controller's
///   battlefield for Creature permanents (controller-scoped — CR 109.5
///   "you control" reads <see cref="Permanent.Controller"/>).
/// - <b>Resolve</b>: re-checks the target is still a controller-side
///   battlefield Creature (CR 608.2b — illegal target → no effect). Moves
///   the creature Battlefield → Exile (CR 701.21), then immediately moves
///   it Exile → Battlefield under its owner's control (CR 614). Both moves
///   are owner-routed so LTB / ETB events fire on each transition (same
///   posture as <see cref="TouchTheSpiritRealmFactory"/>'s exile half).
/// - <b>Bot intent</b>: <see cref="BotIntent.Protection"/> — the dominant
///   use of Cloudshift in real games is dodging a removal spell or
///   re-triggering a value ETB. Heuristic bot ranks controller-side
///   creature targets per <c>HeuristicBotAgent.Score</c>'s Protection
///   path (same as <see cref="TouchTheSpiritRealmFactory"/>'s Channel).
///
/// ## Notes
/// Cloudshift's return is "to the battlefield under its owner's control" —
/// distinct from Touch the Spirit Realm's Channel (which also reads
/// "owner's control" and resolves on a delayed end-step trigger). Both
/// land on the owner side; for Cloudshift the controller and owner are
/// identical at resolve time (the target was "you control" — opponent
/// theft via Threaten / Mark of Mutiny is a separate edge case where the
/// controller-pronoun guard at CR 608.2b would already have fizzled the
/// spell).
///
/// ## Deferred (v1 gaps)
/// - <b>Token blink</b>: tokens that get exiled cease to exist (CR 111.8).
///   v1 still attempts the return; the engine's token-cleanup path drops
///   the object before the return runs in production, but the resolve
///   guards on <c>Zone == Exile</c> at return time so a vanished token is
///   skipped (mirrors Touch the Spirit Realm's defensive check).
/// - <b>Synchronous ETB-trigger ordering</b>: both the exile and the
///   re-enter publish <see cref="Events.CardMovedEvent"/>; downstream ETB
///   triggers stack in registration order. v1 callers exercise this via
///   the live <see cref="TriggerManager"/> path (named-factory and
///   end-to-end tests under <c>SolitudeFactoryTests</c> model the same
///   shape).
/// </summary>
[CardName("Cloudshift")]
public static class CloudshiftFactory
{
    public const string CardName = "Cloudshift";
    public const string PrintedManaCost = "{W}";

    /// <summary>Construct Cloudshift as an Instant owned and controlled by
    /// <paramref name="owner"/>. Card shape only — the resolve closure is
    /// produced by <see cref="BuildSpellDefinition"/>.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> for Cloudshift. Single 1..1
    /// "target creature you control" request; on resolve, exile-then-
    /// immediate-return via owner-routed zone moves. <paramref name="caster"/>
    /// is the player casting the spell — controller-scope filter.
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature you control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Protection,
                    // Controller-scoped gather. CR 109.5 / CR 608.2b — "you
                    // control" reads Permanent.Controller at choose-time.
                    CandidateGatherer: ctx => caster.Zones.Battlefield.GetCards()
                        .OfType<Creature>()
                        .Where(c => ReferenceEquals(c.Controller, caster))
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: chosen =>
            {
                if (chosen.Targets.Count == 0 || chosen.Targets[0].Count == 0)
                {
                    return Array.Empty<IEffect>();
                }
                if (chosen.Targets[0][0] is not Creature target)
                {
                    return Array.Empty<IEffect>();
                }

                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: exile target creature you control, then return it",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check.
                            if (target.Zone != ZoneType.Battlefield) return;
                            if (!ReferenceEquals(target.Controller, caster)) return;

                            var targetOwner = target.Owner ?? caster;

                            // CR 701.21 — Exile via owner-routed zone moves.
                            targetOwner.Zones.Battlefield.RemoveCard(target);
                            targetOwner.Zones.Exile.AddCard(target);
                            target.SetZone(ZoneType.Exile);

                            // CR 614 — "return that card to the battlefield
                            // under its owner's control" resolves in the same
                            // spell resolution (no delayed trigger). The card
                            // re-enters as a new object (CR 400.7).
                            if (target.Zone != ZoneType.Exile) return; // defensive — token cleanup etc.

                            targetOwner.Zones.Exile.RemoveCard(target);
                            targetOwner.Zones.Battlefield.AddCard(target);
                            target.SetZone(ZoneType.Battlefield);
                            target.SetController(targetOwner);
                        }),
                };
            });
    }
}
