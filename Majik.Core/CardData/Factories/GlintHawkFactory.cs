using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Glint Hawk (Scars of Mirrodin, Creature — Bird
/// {W} 2/2).
///
/// Oracle text (verified against Scryfall):
///   "Flying
///    When this creature enters, sacrifice it unless you return an artifact
///    you control to its owner's hand."
///
/// The base shape (name, Creature, Bird subtype, {W}, 2/2) is materialised
/// from the embedded JSON definition (<c>glint-hawk.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. Flying + the ETB
/// sacrifice-unless-return trigger are layered on top here (the JSON
/// <c>AbilityDefinition</c> schema doesn't yet express keyword markers or
/// this trigger shape — same posture as <see cref="KorSkyfisherFactory"/>).
///
/// ## Implemented
/// - 2/2 Creature — Bird, mana cost {W}, owner/controller wired.
/// - <b>Flying (CR 702.9)</b> — wired as a <see cref="KeywordAbility"/> marker
///   so <see cref="Majik.Core.Combat.CombatAbilities.HasFlying"/> surfaces the
///   evasion / block-legality properties.
/// - ETB triggered ability (CR 603.6a) fired when Glint Hawk enters the
///   battlefield.
///   - <b>"sacrifice it unless you return an artifact you control to its
///     owner's hand."</b> This is a "do X unless you pay an alternative cost"
///     consequence (CR 603.6 / CR 701.16 / CR 701.10). The "return an artifact
///     you control" half is the controller's CHOICE — modelled as an
///     <i>optional</i> 0..1 <see cref="TargetRequest"/>: the controller may
///     return one artifact they control to do nothing, or may decline (return
///     nothing) and let Glint Hawk be sacrificed.
///   - On resolution: if the controller chose an artifact (and it is still on
///     the battlefield, CR 608.2b), that artifact is returned to its owner's
///     hand (CR 701.10) via <see cref="ZoneService.MoveCard"/> when a
///     ZoneService is supplied, or via a raw zone move as fallback. If no
///     artifact was returned, Glint Hawk is sacrificed (CR 701.16 — bypasses
///     Indestructible / regeneration).
///   - Unlike <see cref="KorSkyfisherFactory"/> (returns "a permanent you
///     control", mandatory), Glint Hawk restricts the bounce to an ARTIFACT
///     you control and the return is OPTIONAL with a self-sacrifice fallback.
///     The CandidateGatherer therefore enumerates only the controller's own
///     battlefield <i>artifacts</i> (Glint Hawk itself is NOT an artifact, so
///     it is never a return candidate).
///
/// ## Overloads
/// - <see cref="Create(Player)"/> — card shape + Flying + ETB trigger attached
///   for shape inspection; no ZoneService / eventBus wiring (raw zone-move +
///   bus-less sacrifice fallback). Suitable for shape tests and the
///   <see cref="NamedCardFactory"/> dispatcher.
/// - <see cref="Create(Player, ZoneService, IEventBus, TriggerManager)"/>
///   — full wiring: ZoneService routes the bounce, eventBus carries the
///   <see cref="PermanentSacrificedEvent"/> on the self-sac path, and
///   TriggerManager evaluates the ETB trigger so it fires automatically when
///   the card enters the battlefield.
/// </summary>
[CardName("Glint Hawk")]
public static class GlintHawkFactory
{
    public const string CardName = "Glint Hawk";
    public const string Slug = "glint-hawk";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Glint Hawk with Flying + the ETB trigger attached for shape
    /// inspection. No ZoneService / eventBus wiring — the bounce uses a raw
    /// zone move and the self-sacrifice publishes no event.
    /// Suitable for shape tests and the <see cref="NamedCardFactory"/> dispatcher.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, zoneService: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct a fully-wired Glint Hawk.
    ///
    /// When <paramref name="zoneService"/> is supplied, the artifact return is
    /// routed through <see cref="ZoneService.MoveCard"/> so the replacement bus
    /// fires and a <see cref="CardMovedEvent"/> is published. When
    /// <paramref name="eventBus"/> is supplied, the self-sacrifice fallback
    /// routes through <see cref="Fx.Sacrifice(ICard, Player, IEventBus)"/> so a
    /// <see cref="PermanentSacrificedEvent"/> fires (CR 701.16a). When
    /// <paramref name="triggers"/> is supplied, the ETB TriggeredAbility is
    /// registered with the TriggerManager so it fires automatically.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zoneService">Zone service for replacement-bus-aware moves.
    /// May be null — raw zone move is used as fallback.</param>
    /// <param name="eventBus">Event bus for the self-sacrifice
    /// <see cref="PermanentSacrificedEvent"/>. May be null.</param>
    /// <param name="triggers">TriggerManager to register the ETB trigger
    /// against. May be null — trigger is attached to the card shape only.</param>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Bird,
        // {W}, 2/2). The JSON carries no abilities — Flying + the ETB
        // sacrifice-unless-return are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.9 — Flying. KeywordAbility marker so CombatAbilities surfaces
        // evasion / block-legality.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // --------------------------------------------------------------------
        // ETB triggered ability (CR 603.6a):
        //   "sacrifice it unless you return an artifact you control to its
        //    owner's hand."
        // The "return an artifact you control" half is optional (a 0..1 target
        // request); declining (no artifact returned) sacrifices Glint Hawk.
        // --------------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;

        var etbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        var etbEffect = new Effect(
            "Glint Hawk — sacrifice it unless you return an artifact you control to its owner's hand",
            () =>
            {
                if (etbTrigger == null) return;

                var chosen = etbTrigger.ChosenTargets;
                var raw = (chosen.Count > 0 && chosen[0].Count > 0) ? chosen[0][0] : null;

                // CR 701.10 — return the chosen artifact to its owner's hand,
                // satisfying the "unless" clause, so Glint Hawk is NOT
                // sacrificed.
                if (raw is Permanent target
                    && target.HasType(CardType.Artifact)
                    // CR 608.2b — if the chosen artifact is no longer on the
                    // battlefield at resolution it is an illegal target; the
                    // "unless" cost is not paid → fall through to sacrifice.
                    && target.Zone == ZoneType.Battlefield)
                {
                    var targetOwner = target.Owner;
                    if (targetOwner != null)
                    {
                        if (zoneService != null)
                        {
                            zoneService.MoveCard(target, ZoneType.Battlefield, ZoneType.Hand);
                        }
                        else
                        {
                            var fromController = target.Controller ?? targetOwner;
                            fromController.Zones.Battlefield.RemoveCard(target);
                            targetOwner.Zones.Hand.AddCard(target);
                            target.SetZone(ZoneType.Hand);
                            target.SetController(targetOwner);
                        }

                        return; // "unless" cost paid — no sacrifice.
                    }
                }

                // No artifact returned → CR 701.16: sacrifice Glint Hawk.
                // Sacrifice bypasses Indestructible (CR 702.12b) / regeneration
                // (CR 701.15c). Only sacrifice if it's still on the battlefield.
                if (card.Zone != ZoneType.Battlefield) return;

                if (eventBus != null)
                {
                    Fx.Sacrifice(card, card.Controller ?? owner, eventBus);
                }
                else
                {
                    Fx.Sacrifice(card);
                }
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    // OPTIONAL (0..1): the controller MAY return an artifact
                    // they control to avoid the sacrifice ("unless you return
                    // an artifact you control"). MinTargets=0 lets the
                    // controller decline → Glint Hawk is sacrificed.
                    Description: "an artifact you control",
                    MinTargets: 0,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Bounce,
                    // CandidateGatherer: any ARTIFACT on the CONTROLLER's own
                    // battlefield. Glint Hawk itself is a Bird, not an artifact,
                    // so it is never a candidate (CR 109.5 / 608).
                    CandidateGatherer: ctx => (ctx.AllPlayers
                            .FirstOrDefault(p => ReferenceEquals(p, card.Controller ?? owner))
                            ?.Zones.Battlefield.GetCards() ?? Enumerable.Empty<Card>())
                        .OfType<Permanent>()
                        .Where(c => c.HasType(CardType.Artifact))
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(etbTrigger);

        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}
