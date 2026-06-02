using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Shriekmaw (Lorwyn, {5}{B}).
///
/// Creature — Elemental 3/2. Oracle text (verified against Scryfall):
///   "Fear (This creature can't be blocked except by artifact creatures
///    and/or black creatures.)
///    When this creature enters, destroy target nonartifact, nonblack
///    creature.
///    Evoke {1}{B} (You may cast this spell for its evoke cost. If you do,
///    it's sacrificed when it enters.)"
///
/// The card's base shape (name, Elemental subtype, {5}{B}, 3/2) is
/// materialised from the embedded JSON definition (<c>shriekmaw.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The three printed behaviours
/// (Fear, the ETB destroy trigger, and Evoke) are layered on top here —
/// the JSON <c>AbilityDefinition</c> schema doesn't express keyword
/// markers, targeted destroy triggers, or Evoke, so they live in the
/// factory (same posture as <see cref="SolitudeFactory"/> /
/// <see cref="StormscaleScionFactory"/>).
///
/// ## Implemented (v1)
/// - <b>Fear (CR 702.36)</b> — attached as a <see cref="KeywordAbility"/>
///   marker; the combat system consumes it for block-restriction checks
///   (only artifact and/or black creatures may block). Same shape as
///   <see cref="RazortoothRatsFactory"/>.
/// - <b>Evoke {1}{B} (CR 702.74)</b> — pure-mana evoke (no pitch), wired
///   via <see cref="BuildEvokeCost"/> returning an
///   <see cref="EvokeAlternativeCost"/> over {1}{B}; this replaces the
///   printed {5}{B} mana cost at cast time. Paired with the printed evoke
///   sacrifice trigger (<see cref="EvokeFactory"/>): "When this creature
///   enters, if its evoke cost was paid, sacrifice it" (CR 702.74b). The
///   intervening-if reads <see cref="Creature.EvokeWasPaid"/>, flipped by
///   <see cref="EvokeAlternativeCost.OnResolved"/>.
/// - <b>ETB destroy trigger (CR 603.6a)</b>: "When this creature enters,
///   destroy target nonartifact, nonblack creature." Declares a mandatory
///   1..1 <see cref="TargetRequest"/>. The candidate gatherer enumerates
///   every Creature on the battlefield that is NOT an artifact (CR 301)
///   and NOT black (CR 105.2 / CR 202.3 — color from mana cost + color
///   indicator, via <see cref="CardColors"/>), tagged
///   <see cref="BotIntent.Removal"/>. On resolution the effect re-checks
///   legality (CR 608.2b): still a Creature on the battlefield, still
///   nonartifact, still nonblack — then destroys via
///   <see cref="OracleSpellBinder.MoveToGraveyard"/> with
///   <see cref="ZoneMoveReason.Destroy"/> (indestructible CR 702.12
///   cancels; regeneration CR 701.15 shield is consumed). Note: unlike
///   Ravenous Chupacabra this is NOT restricted to opponents' creatures —
///   the printed text allows targeting any nonartifact, nonblack creature,
///   including your own.
///
/// ## Deferred (v1 gaps)
/// - <b>Target legality in ActionValidator</b>: the validator does not yet
///   enforce "nonartifact, nonblack creature" at trigger-target
///   declaration; the resolution-time guard handles illegal targets
///   (CR 608.2b). Same posture as <see cref="RavenousChupacabraFactory"/>.
/// - <b>Mandatory pick when no legal target</b>: if no nonartifact,
///   nonblack creature exists, the ETB still triggers (the printed ability
///   has no "if able" clause) and falls through to a clean no-op at
///   resolution — matches existing factory posture.
/// </summary>
[CardName("Shriekmaw")]
public static class ShriekmawFactory
{
    public const string CardName = "Shriekmaw";
    public const string Slug = "shriekmaw";

    /// <summary>CR 702.74 — printed Evoke mana cost: {1}{B}.</summary>
    public const string EvokeManaCost = "{1}{B}";

    /// <summary>
    /// CR 702.74 — build the (pure-mana) Evoke alt-cost {1}{B}. The cast
    /// pipeline replaces the printed {5}{B} mana cost with this when the
    /// caster chooses to evoke; <see cref="EvokeAlternativeCost.OnResolved"/>
    /// flips <see cref="Creature.EvokeWasPaid"/> so the sacrifice trigger
    /// fires (CR 702.74b).
    /// </summary>
    public static EvokeAlternativeCost BuildEvokeCost() =>
        new(ManaCost.Parse(EvokeManaCost));

    /// <summary>
    /// Construct Shriekmaw owned and controlled by <paramref name="owner"/>.
    /// Fear + Evoke sacrifice trigger + ETB destroy trigger are attached
    /// structurally; no <see cref="TriggerManager"/> wiring. This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Fully-wired construction. <paramref name="triggers"/> registers the
    /// ETB destroy trigger so a <see cref="CardMovedEvent"/>
    /// (Stack → Battlefield) on this card fires it automatically. The
    /// Evoke sacrifice trigger is registered as well when a manager is
    /// supplied.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Elemental subtype, {5}{B}, 3/2). The JSON carries no abilities —
        // Fear / Evoke / the ETB destroy trigger are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // Fear — CR 702.36. KeywordAbility marker; the combat system checks
        // this for block restriction (only artifact / black creatures may
        // block). The NamedCardFactory / direct-test path doesn't run
        // KeywordBinder, so attach the marker here.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Fear", card, owner));

        // ----------------------------------------------------------------
        // Evoke — CR 702.74. Keyword marker + the printed evoke sacrifice
        // trigger (CR 702.74b): "When this creature enters, if its evoke
        // cost was paid, sacrifice it."
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Evoke", card, owner));
        var sacTrigger = EvokeFactory.Build(card);
        card.AddAbility(sacTrigger);
        triggers?.RegisterTriggeredAbility(sacTrigger);

        // ----------------------------------------------------------------
        // ETB destroy trigger — CR 603.6a.
        //   "When this creature enters, destroy target nonartifact,
        //    nonblack creature."
        // Mandatory 1..1 target. Candidate gatherer enumerates every
        // battlefield Creature that is neither an artifact (CR 301) nor
        // black (CR 105.2 / CR 202.3). Not restricted to opponents — the
        // printed text lets you target any qualifying creature.
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;
        var etbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        var etbEffect = new Effect(
            $"{CardName}: destroy target nonartifact, nonblack creature",
            () =>
            {
                if (etbTrigger == null) return;
                var chosen = etbTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                if (chosen[0][0] is not Creature target) return;

                // CR 608.2b — resolution-time legality re-check: the chosen
                // target must still be a legal "nonartifact, nonblack
                // creature" on the battlefield. If control / characteristics
                // changed so it's now an artifact or black, the spell/ability
                // doesn't affect it (clean no-op).
                if (target.Zone != ZoneType.Battlefield) return;
                if (target.HasType(CardType.Artifact)) return;
                if (IsBlack(target)) return;

                // CR 701.7 — destroy. Indestructible (CR 702.12) cancels;
                // regeneration (CR 701.15) shield is consumed.
                OracleSpellBinder.MoveToGraveyard(target, ZoneMoveReason.Destroy);
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
