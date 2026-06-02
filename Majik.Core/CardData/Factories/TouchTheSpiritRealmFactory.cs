using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Touch the Spirit Realm (Kamigawa: Neon Dynasty,
/// {2}{W}).
///
/// Enchantment. Oracle text (Scryfall, verified 2026-06-02):
///   "When this enchantment enters, exile up to one target artifact or
///    creature until this enchantment leaves the battlefield.
///    Channel — {1}{W}, Discard this card: Exile target artifact or creature.
///    Return it to the battlefield under its owner's control at the beginning
///    of the next end step."
///
/// Two distinct removal modes:
///   1. <b>Cast as the enchantment</b> — O-Ring: the ETB exiles up to one
///      artifact/creature UNTIL THIS LEAVES the battlefield (returns when the
///      enchantment dies/bounces). Same exile-on-ETB / return-on-LTB closure
///      shape as <see cref="BanishingLightFactory"/>, but the target is
///      optional ("up to one") and any controller's (not opponent-only), and
///      the filter is artifact-OR-creature rather than nonland-permanent.
///   2. <b>Channel from hand</b> — a TEMPORARY blink: discard the card to
///      exile a target artifact/creature and return it at the next end step
///      (CR 603.7 delayed trigger). NOT linked to the enchantment (the card
///      is in the graveyard, never on the battlefield).
///
/// ## Implemented (v1)
/// - <b>Enchantment {2}{W}</b>. Owner / controller wired.
/// - <b>ETB "exile up to one ... until this leaves"</b> (CR 603.6a / 701.21)
///   + <b>LTB return</b> (CR 603.6c / 110.2) — per-instance closure captures
///   the exiled card between the two triggered abilities (Banishing Light
///   shape). "Up to one" → <c>MinTargets: 0</c>; an empty choice is a clean
///   no-op.
/// - <b>Channel — {1}{W}, Discard this card</b> (CR 702.74): activated-from-
///   hand ability (<see cref="ManaCostCost"/> + <see cref="DiscardSelfCost"/>,
///   the discard gating activation to <see cref="ZoneType.Hand"/> per
///   CR 702.74a). On resolve exile the target then, when a
///   <see cref="TriggerManager"/> is supplied, register a one-shot
///   <see cref="DelayedTriggeredAbility"/> (CR 603.7) returning it to its
///   OWNER's control at the next end step.
///
/// ## Deferred (v1 gaps)
/// - <b>Replacement effects on return</b>: raw-zone return fallback skips
///   ETB/replacement events when no <see cref="ZoneService"/> is threaded in
///   (Banishing Light / Yorion posture).
/// - <b>Tokens</b>: an exiled token ceases to exist (CR 111.8); both return
///   paths defensively check <c>Zone == Exile</c> before moving it back.
/// </summary>
[CardName("Touch the Spirit Realm")]
public static class TouchTheSpiritRealmFactory
{
    public const string CardName = "Touch the Spirit Realm";
    public const string PrintedManaCost = "{2}{W}";
    public const string ChannelManaCost = "{1}{W}";

    /// <summary>Single-arg dispatcher entry — shape only (abilities attached
    /// but not registered with a <see cref="TriggerManager"/>).</summary>
    public static Enchantment Create(Player owner) => Create(owner, triggers: null, zones: null);

    /// <summary>
    /// Build Touch the Spirit Realm. When <paramref name="triggers"/> is
    /// supplied, the ETB / LTB O-Ring pair and the Channel's delayed end-step
    /// return are registered so the bus drives them.
    /// </summary>
    public static Enchantment Create(Player owner, TriggerManager? triggers, ZoneService? zones)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(CardName, PrintedManaCost, supertypes: null, subtypes: null);
        card.SetOwner(owner);
        card.SetController(owner);

        WireEtbExileUntilLeaves(card, owner, triggers);
        AttachChannelAbility(card, owner, triggers, zones);
        return card;
    }

    /// <summary>
    /// ETB "exile up to one target artifact or creature until this leaves" +
    /// the matching LTB return. Mirrors
    /// <see cref="BanishingLightFactory.WireExileEnchantmentTriggers"/> but
    /// with an optional ("up to one") target and an artifact-or-creature
    /// filter with no controller restriction.
    /// </summary>
    private static void WireEtbExileUntilLeaves(Enchantment card, Player owner, TriggerManager? triggers)
    {
        // Shared closure: ETB writes, LTB reads.
        ICard? exiled = null;
        Player? exiledOwner = null;

        TriggeredAbility? etbTrigger = null;
        var etbEffect = new Effect(
            $"{CardName}: exile up to one target artifact or creature until this leaves (CR 701.21)",
            () =>
            {
                if (etbTrigger == null) return;
                var chosen = etbTrigger.ChosenTargets;
                // "Up to one" — an empty choice is a legal no-op (CR 115.1b).
                if (chosen.Count == 0 || chosen[0].Count == 0) return;
                if (chosen[0][0] is not Permanent target) return;

                // CR 608.2b — resolution-time legality re-check.
                if (target.Zone != ZoneType.Battlefield) return;
                if (!target.HasType(CardType.Artifact) && !target.HasType(CardType.Creature)) return;

                // CR 701.21 — exile via the target's owner's zones.
                var targetOwner = target.Owner;
                if (targetOwner != null)
                {
                    targetOwner.Zones.Battlefield.RemoveCard(target);
                    targetOwner.Zones.Exile.AddCard(target);
                }
                target.SetZone(ZoneType.Exile);

                exiled = target;
                exiledOwner = targetOwner;
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "up to one target artifact or creature",
                    MinTargets: 0,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c.HasType(CardType.Artifact) || c.HasType(CardType.Creature))
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // LTB — return the exiled card to its owner's control (CR 603.6c / 110.2).
        var ltbEffect = new Effect(
            $"{CardName}: return the exiled card to the battlefield under its owner's control",
            () =>
            {
                if (exiled == null || exiledOwner == null) return;
                if (exiled.Zone != ZoneType.Exile) return; // CR 400.7 / 111.8

                exiledOwner.Zones.Exile.RemoveCard(exiled);
                exiledOwner.Zones.Battlefield.AddCard(exiled);
                exiled.SetZone(ZoneType.Battlefield);
                if (exiled is Card returned) returned.ChangeController(exiledOwner);
            });

        var ltbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CardMovedEvent>(
                (e, _) => ReferenceEquals(e.Card, card) && e.FromZone == ZoneType.Battlefield),
            effects: new IEffect[] { ltbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(ltbTrigger);
        triggers?.RegisterTriggeredAbility(ltbTrigger);
    }

    /// <summary>
    /// Channel — {1}{W}, Discard this card: exile target artifact or creature,
    /// returning it to its owner's control at the beginning of the next end
    /// step (CR 603.7 delayed trigger). Unlike the ETB this is a temporary
    /// blink — the card itself is in the graveyard, not linked to the exile.
    /// </summary>
    private static void AttachChannelAbility(
        Enchantment card, Player controller, TriggerManager? triggers, ZoneService? zones)
    {
        ActivatedAbility? channel = null;

        var channelEffect = new Effect(
            $"{CardName} (Channel): exile target artifact/creature; return at next end step",
            () =>
            {
                var ability = channel!;
                if (ability.ChosenTargets.Count == 0 || ability.ChosenTargets[0].Count == 0) return;
                if (ability.ChosenTargets[0][0] is not Permanent target) return;

                // CR 608.2b — resolution-time legality re-check.
                if (target.Zone != ZoneType.Battlefield) return;
                if (!target.HasType(CardType.Artifact) && !target.HasType(CardType.Creature)) return;

                // CR 701.21 — exile. Prefer ZoneService so LTB events fire.
                if (zones != null)
                {
                    zones.MoveCard(target, ZoneType.Battlefield, ZoneType.Exile);
                }
                else
                {
                    var fromOwner = target.Owner;
                    if (fromOwner != null)
                    {
                        fromOwner.Zones.Battlefield.RemoveCard(target);
                        fromOwner.Zones.Exile.AddCard(target);
                    }
                    target.SetZone(ZoneType.Exile);
                }

                // CR 603.7 — delayed end-step return (only with a live TriggerManager).
                if (triggers == null) return;

                var resolvedAt = Majik.Core.Game.LogicalClockScope.Current.NextTimestamp();
                var returnEffect = new Effect(
                    $"{CardName} (Channel): return exiled card at next end step (CR 603.7)",
                    () =>
                    {
                        if (target.Zone != ZoneType.Exile) return; // CR 111.8
                        var returnOwner = target.Owner ?? controller;
                        if (zones != null)
                        {
                            zones.MoveCard(target, ZoneType.Exile, ZoneType.Battlefield, returnOwner);
                        }
                        else
                        {
                            returnOwner.Zones.Exile.RemoveCard(target);
                            returnOwner.Zones.Battlefield.AddCard(target);
                            target.SetZone(ZoneType.Battlefield);
                            target.SetController(returnOwner);
                        }
                    });

                triggers.RegisterDelayed(new DelayedTriggeredAbility(
                    source: card,
                    controller: controller,
                    condition: new EventTriggerCondition<StepStartedEvent>(
                        (e, _) => e.StepType == PhaseStateType.End && e.Timestamp > resolvedAt),
                    effects: new IEffect[] { returnEffect }));
            });

        channel = new ActivatedAbility(
            source: card,
            controller: controller,
            costs: new ICost[]
            {
                new ManaCostCost(ChannelManaCost),
                new DiscardSelfCost(card),
            },
            effects: new IEffect[] { channelEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target artifact or creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c.HasType(CardType.Artifact) || c.HasType(CardType.Creature))
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(channel);
    }
}
