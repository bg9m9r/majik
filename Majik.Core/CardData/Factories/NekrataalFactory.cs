using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Nekrataal (Visions, {2}{B}{B}).
///
/// Creature — Human Assassin 2/1. Oracle text (verified against Scryfall):
///   "First strike
///    When this creature enters, destroy target nonartifact, nonblack
///    creature. That creature can't be regenerated."
///
/// The card's base shape (name, Human Assassin subtypes, {2}{B}{B}, 2/1) is
/// materialised from the embedded JSON definition (<c>nekrataal.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two printed behaviours
/// (First strike + the ETB destroy trigger) are layered on top here — the
/// JSON <c>AbilityDefinition</c> schema doesn't express keyword markers or
/// targeted destroy triggers, so they live in the factory (same posture as
/// <see cref="ShriekmawFactory"/>).
///
/// ## Implemented (v1)
/// - <b>First strike (CR 702.7)</b> — attached as a <see cref="KeywordAbility"/>
///   marker; the combat-damage step consumes it for first-strike damage.
///   Same shape as <see cref="YouthfulKnightFactory"/>.
/// - <b>ETB destroy trigger (CR 603.6a)</b>: "When this creature enters,
///   destroy target nonartifact, nonblack creature. That creature can't be
///   regenerated." Declares a mandatory 1..1 <see cref="TargetRequest"/>.
///   The candidate gatherer enumerates every Creature on the battlefield
///   that is NOT an artifact (CR 301) and NOT black (CR 105.2 / CR 202.3 —
///   color from mana cost + color indicator, via <see cref="CardColors"/>),
///   tagged <see cref="BotIntent.Removal"/>. On resolution the effect
///   re-checks legality (CR 608.2b): still a Creature on the battlefield,
///   still nonartifact, still nonblack — then destroys via
///   <see cref="OracleSpellBinder.MoveToGraveyard"/> with
///   <see cref="ZoneMoveReason.DestroyNoRegeneration"/>. The
///   "can't be regenerated" rider (CR 701.15) is honoured by that move
///   reason: indestructible (CR 702.12) still cancels the destroy, but any
///   active regeneration shield is BYPASSED rather than consumed — this is
///   the one behavioural difference from <see cref="ShriekmawFactory"/>,
///   which prints no such rider and uses <see cref="ZoneMoveReason.Destroy"/>.
///   Note: like Shriekmaw and unlike Ravenous Chupacabra this is NOT
///   restricted to opponents' creatures — the printed text allows targeting
///   any nonartifact, nonblack creature, including your own.
///
/// ## Deferred (v1 gaps)
/// - <b>Target legality in ActionValidator</b>: the validator does not yet
///   enforce "nonartifact, nonblack creature" at trigger-target
///   declaration; the resolution-time guard handles illegal targets
///   (CR 608.2b). Same posture as <see cref="ShriekmawFactory"/>.
/// - <b>Mandatory pick when no legal target</b>: if no nonartifact,
///   nonblack creature exists, the ETB still triggers (the printed ability
///   has no "if able" clause) and falls through to a clean no-op at
///   resolution — matches existing factory posture.
/// </summary>
[CardName("Nekrataal")]
public static class NekrataalFactory
{
    public const string CardName = "Nekrataal";
    public const string Slug = "nekrataal";

    /// <summary>
    /// Construct Nekrataal owned and controlled by <paramref name="owner"/>.
    /// First strike + ETB destroy trigger are attached structurally; no
    /// <see cref="TriggerManager"/> wiring. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Fully-wired construction. <paramref name="triggers"/> registers the
    /// ETB destroy trigger so a <see cref="CardMovedEvent"/>
    /// (Stack → Battlefield) on this card fires it automatically.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Human Assassin subtypes, {2}{B}{B}, 2/1). The JSON carries no
        // abilities — First strike / the ETB destroy trigger are layered
        // on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // First strike — CR 702.7. KeywordAbility marker; the combat-damage
        // step checks this to assign first-strike damage. The
        // NamedCardFactory / direct-test path doesn't run KeywordBinder, so
        // attach the marker here.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("First strike", card, owner));

        // ----------------------------------------------------------------
        // ETB destroy trigger — CR 603.6a.
        //   "When this creature enters, destroy target nonartifact,
        //    nonblack creature. That creature can't be regenerated."
        // Mandatory 1..1 target. Candidate gatherer enumerates every
        // battlefield Creature that is neither an artifact (CR 301) nor
        // black (CR 105.2 / CR 202.3). Not restricted to opponents — the
        // printed text lets you target any qualifying creature.
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;
        var etbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        var etbEffect = new Effect(
            $"{CardName}: destroy target nonartifact, nonblack creature (no regeneration)",
            () =>
            {
                if (etbTrigger == null) return;
                var chosen = etbTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                if (chosen[0][0] is not Creature target) return;

                // CR 608.2b — resolution-time legality re-check: the chosen
                // target must still be a legal "nonartifact, nonblack
                // creature" on the battlefield. If control / characteristics
                // changed so it's now an artifact or black, the ability
                // doesn't affect it (clean no-op).
                if (target.Zone != ZoneType.Battlefield) return;
                if (target.HasType(CardType.Artifact)) return;
                if (IsBlack(target)) return;

                // CR 701.7 — destroy. "That creature can't be regenerated"
                // (CR 701.15) honoured via DestroyNoRegeneration:
                // indestructible (CR 702.12) still cancels the destroy, but
                // any active regeneration shield is BYPASSED rather than
                // consumed. (This is the only behavioural difference from
                // Shriekmaw, which uses ZoneMoveReason.Destroy.)
                OracleSpellBinder.MoveToGraveyard(target, ZoneMoveReason.DestroyNoRegeneration);
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target nonartifact, nonblack creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Live candidate gatherer: every battlefield Creature
                    // that is neither an artifact (CR 301) nor black
                    // (CR 105.2 / CR 202.3 via CardColors). Engine resolves
                    // this at prompt time against the live board.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Where(c => !c.HasType(CardType.Artifact) && !IsBlack(c))
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }

    /// <summary>
    /// CR 105.2 / CR 202.3 — a card is black iff black is among its colors
    /// (mana-cost pips + color indicator, surfaced by
    /// <see cref="CardColors.GetColors"/>).
    /// </summary>
    private static bool IsBlack(ICard card) =>
        CardColors.GetColors(card).Contains(ManaColor.Black);
}
