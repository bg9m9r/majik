using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Meteor Golem (Dominaria + reprints, {7}).
///
/// Artifact Creature — Golem 3/3 (colorless). Oracle text (verified against
/// Scryfall 2026-06-24):
///   "When this creature enters, destroy target nonland permanent an opponent
///    controls."
///
/// ## Shape source
/// Card identity (name, {7}, 3/3, Artifact Creature — Golem) is loaded from
/// <c>Majik.Core/CardData/Cards/meteor-golem.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The single ETB destroy trigger is
/// attached in code below — the JSON ability schema does not yet express a
/// "destroy target" effect (same posture as
/// <see cref="RavenousChupacabraFactory"/> and <see cref="AcidicSlimeFactory"/>).
///
/// Meteor Golem is the colorless, nonland-permanent generalisation of
/// <see cref="RavenousChupacabraFactory"/>: where Chupacabra destroys "target
/// creature an opponent controls", Meteor Golem destroys "target nonland
/// permanent an opponent controls" — any nonland permanent type (creature,
/// artifact, enchantment, planeswalker, battle) qualifies.
///
/// ## Implemented (v1)
/// - 3/3 Artifact Creature — Golem at {7} (colorless).
/// - <b>ETB triggered ability (CR 603.6a)</b> — wired via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/>; same trigger shape as
///   <see cref="RavenousChupacabraFactory"/>.
///   - Mandatory 1..1 <see cref="TargetRequest"/> for "target nonland
///     permanent an opponent controls" — NOT a "may" ability (printed oracle
///     has no optional clause). <see cref="TargetRequest.CandidateGatherer"/>
///     enumerates every nonland permanent (CR 305 — Land is a card type, so
///     the filter rejects lands incl. Dryad Arbor) controlled by a player
///     OTHER than Meteor Golem's controller (CR 109.1 — opponent = any other
///     player), tagged <see cref="BotIntent.Removal"/> so the bot's ranker
///     picks the highest-value opposing permanent.
///   - On resolution: re-checks the chosen card is still a <see cref="Permanent"/>
///     on the Battlefield (CR 608.2b — illegal target → clean no-op), still a
///     nonland (CR 305) and still controlled by an opponent of Meteor Golem's
///     controller (CR 109.1 — re-checked in case control changed between
///     trigger and resolve). If valid: destroy via
///     <see cref="OracleSpellBinder.MoveToGraveyard"/> with
///     <see cref="ZoneMoveReason.Destroy"/> (CR 701.7 — indestructible
///     CR 702.12 cancels; regeneration CR 701.15 shield is consumed).
///   - Empty-candidate / no-target path is a clean no-op (CR 608.2b).
///
/// ## Overloads
/// - <see cref="Create(Player)"/> — card shape + ETB trigger attached. No
///   TriggerManager wiring; suitable for shape / dispatcher tests.
/// - <see cref="Create(Player, IEventBus, TriggerManager)"/> — full wiring:
///   ETB registered for automatic firing on Meteor Golem's own
///   <see cref="Majik.Core.Domain.DomainEvents.CardMovedEvent"/> into
///   <see cref="ZoneType.Battlefield"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Target legality in ActionValidator</b>: the validator does not yet
///   enforce "nonland permanent an opponent controls" at trigger-target
///   declaration; the resolution-time guard handles illegal targets
///   (CR 608.2b). Same posture as <see cref="RavenousChupacabraFactory"/>.
/// - <b>Mandatory pick when no legal target</b>: if no opposing nonland
///   permanent exists, the ETB still triggers (no "if able" clause) and falls
///   through to a clean no-op at resolution (CR 603.6c).
/// </summary>
[CardName("Meteor Golem")]
public static class MeteorGolemFactory
{
    public const string CardName = "Meteor Golem";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "meteor-golem";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Meteor Golem with the ETB trigger attached for shape
    /// inspection. Trigger is NOT registered with a <see cref="TriggerManager"/>.
    /// Suitable for shape / dispatcher tests. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Meteor Golem with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Event bus — reserved for parity with other
    /// ETB factories. May be null.</param>
    /// <param name="triggers">TriggerManager to register the ETB trigger
    /// against. May be null — trigger remains attached to the card shape.</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Artifact
        // Creature — Golem, {7}, 3/3). The ETB destroy trigger is layered on
        // below.
        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability (CR 603.6a):
        //   "When this creature enters, destroy target nonland permanent an
        //    opponent controls."
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;

        var etbEffect = new Effect(
            $"{CardName}: destroy target nonland permanent an opponent controls",
            () =>
            {
                if (etbTrigger == null) return;
                if (etbTrigger.ChosenTargets.Count == 0
                    || etbTrigger.ChosenTargets[0].Count == 0)
                {
                    return;
                }

                if (etbTrigger.ChosenTargets[0][0] is not Permanent target) return;

                // CR 608.2b — resolution-time legality check: still a
                // permanent on the battlefield.
                if (target.Zone != ZoneType.Battlefield) return;

                // CR 305 — Land is a card type; a nonland permanent must not
                // have the Land type. Re-checked at resolution.
                if (target.HasType(CardType.Land)) return;

                // CR 109.1 — "opponent" = any player other than Meteor Golem's
                // controller. Re-check at resolution in case control of either
                // side changed between trigger and resolve.
                var myController = card.Controller ?? owner;
                if (ReferenceEquals(target.Controller, myController)) return;

                // CR 701.7 — destroy. Indestructible (CR 702.12) cancels;
                // regeneration (CR 701.15) shield is consumed.
                OracleSpellBinder.MoveToGraveyard(target, ZoneMoveReason.Destroy);
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
                    Description: "target nonland permanent an opponent controls",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // CR 109.1 — opponents only; CR 305 — exclude lands.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .Where(p => !ReferenceEquals(p, card.Controller ?? owner))
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Permanent>()
                        .Where(perm => !perm.HasType(CardType.Land))
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}
