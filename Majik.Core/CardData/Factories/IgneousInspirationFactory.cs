using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using DomainRule = Majik.Core.Rules.Domain;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Igneous Inspiration (Dominaria United, {2}{R}).
///
/// Sorcery. Oracle text:
///   "Kicker {3}"
///   "Domain — Igneous Inspiration deals N damage to any target, where N
///    is one plus the number of basic land types among lands you control."
///   "If this spell was kicked, exile the top card of your library. Until
///    the end of your next turn, you may play that card."
///
/// ## Implemented (v1)
///
/// - Sorcery {2}{R} shape.
/// - Single 1..1 "any target" request.
/// - <b>Domain damage</b> (CR 702.16): <c>N = 1 + Domain</c> distinct
///   basic land types among lands the controller controls. Routes
///   through the canonical <see cref="DomainRule.CountTypes(Player, ContinuousEffectsService?)"/>
///   primitive — Blood Moon / Spreading Seas / Urborg / Yavimaya retypes
///   feed through when a live continuous-effects service is supplied
///   (CR 613 layer pipeline). Damage is dealt via
///   <see cref="Fx.DealDamageAny"/>.
/// - <b>Kicker {3}</b> (CR 702.33) wired as a real
///   <see cref="KickerAdditionalCost"/> primitive — same shape as
///   <see cref="BurstLightningFactory"/>. The factory exposes
///   <see cref="BuildAdditionalCost"/> for callers that have already
///   decided to pay the kicker; the resolve body reads
///   <see cref="Card.WasKicked"/> at resolution.
/// - <b>Kicked rider — exile top + "may play this turn / next turn"
///   grant</b> (CR 702.33b / CR 514.2): on a kicked resolution, the
///   caster's top library card is moved to exile and stamped with a
///   runtime exile-cast grant via
///   <see cref="Card.GrantRuntimeExileCast"/>. When an
///   <see cref="IEventBus"/> is supplied the grant clears at the start
///   of the caster's second Cleanup step (CR 514.2 — sorcery resolves
///   during caster's own turn → first cleanup = this turn, second =
///   the caster's next turn). Mirrors
///   <see cref="LightUpTheStageFactory.BuildResolveEffect"/>'s
///   subscription pattern.
///
/// ## Deferred (v1 gaps)
///
/// - <b>"May play that card" includes lands</b>: the printed "play"
///   covers both casting and playing a land (CR 305.2). The runtime
///   exile-cast grant authorises casting; playing an exiled land would
///   need a parallel "play this land from exile" grant. Same v1 gap as
///   <see cref="LightUpTheStageFactory"/> / Wrenn cycle factories — most
///   Dominaria United Limited play patterns don't exercise this corner.
/// - <b>Empty-library on kicked resolve</b>: when the library is empty,
///   the exile + grant are skipped silently (no SBA flag for the exile
///   move — same shape as Light Up the Stage / Necrodominance).
/// - <b>Granted-cost defaults to printed mana cost</b>: "you may play
///   that card" with no alt-cost rider, so the grant uses the exiled
///   card's printed mana cost (CR 118.9). Lands have no mana cost; same
///   gap as above.
/// </summary>
[CardName("Igneous Inspiration")]
public static class IgneousInspirationFactory
{
    public const string CardName = "Igneous Inspiration";
    public const string PrintedManaCost = "{2}{R}";
    public const string KickerCostText = "{3}";

    /// <summary>CardDef DSL — card shape only. Domain-driven damage body
    /// is built via <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost);

    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Igneous
    /// Inspiration is cast. Single 1..1 "any target" request; resolution
    /// deals <c>1 + Domain</c> damage (CR 702.16) to the chosen target.
    /// On a kicked cast (<see cref="Card.WasKicked"/> stamped by
    /// <see cref="KickerAdditionalCost.Pay"/>) the resolve body
    /// additionally exiles the top of the caster's library and grants a
    /// runtime exile-cast permission cleared on the caster's NEXT
    /// Cleanup step (CR 514.2 — "until the end of your next turn").
    /// </summary>
    /// <param name="card">The cast card instance — the resolve body
    /// reads <see cref="Card.WasKicked"/> off this same reference so
    /// the kicker branch fires only when the cast actually paid the
    /// rider (CR 702.33b).</param>
    /// <param name="controller">Spell controller — the lands whose
    /// distinct basic-type count drives N, and whose library is exiled
    /// on the kicker branch.</param>
    /// <param name="effects">Live continuous-effects service for the
    /// domain count. When null, printed subtypes are used.</param>
    /// <param name="resolver">Target resolver (chosen target → live
    /// game object).</param>
    /// <param name="eventBus">Optional event bus. When supplied, the
    /// kicker's "until the end of your next turn" grant is scheduled
    /// to clear on the caster's second Cleanup step (CR 514.2).</param>
    public static SpellDefinition BuildSpellDefinition(
        ICard card,
        Player controller,
        ContinuousEffectsService? effects,
        Func<object, object> resolver,
        IEventBus? eventBus = null)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(controller);
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

                bool wasKicked = card is Card concrete && concrete.WasKicked;

                return new IEffect[]
                {
                    Fx.Inline(
                        $"{CardName}: Domain damage (+ kicker exile/may-play branch)",
                        () =>
                        {
                            // CR 702.16 — N = 1 + distinct basic land
                            // types among lands the controller controls.
                            var domain = DomainRule.CountTypes(controller, effects);
                            var amount = 1 + domain;
                            Fx.DealDamageAny(target, amount);

                            if (!wasKicked) return;

                            // CR 702.33b — kicker rider. Exile top of
                            // library and grant cast-from-exile until
                            // end of the caster's NEXT turn (CR 514.2).
                            ExileTopAndGrantMayPlay(controller, eventBus);
                        }),
                };
            });
    }

    /// <summary>
    /// Construct Igneous Inspiration's kicker <see cref="IAdditionalCost"/>
    /// for the supplied <paramref name="card"/> instance. Convenience
    /// builder for callers (tests, bot decision layer) that have already
    /// decided to pay the kicker; layer the returned cost onto the cast
    /// via <see cref="SpellCastFlow.CastAsync"/>'s <c>additionalCosts</c>
    /// parameter.
    /// </summary>
    public static IAdditionalCost BuildAdditionalCost(ICard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return new KickerAdditionalCost(card, ManaCost.Parse(KickerCostText));
    }

    /// <summary>
    /// Kicker rider — exile the top card of the caster's library and
    /// stamp a runtime exile-cast grant on it. When an event bus is
    /// supplied, schedule the grant to clear on the caster's SECOND
    /// Cleanup step (CR 514.2 — sorcery resolves during caster's own
    /// turn so the first cleanup is this turn; the grant survives until
    /// the second). Mirrors <see cref="LightUpTheStageFactory"/>'s
    /// cleanup-counting pattern.
    /// </summary>
    private static void ExileTopAndGrantMayPlay(Player caster, IEventBus? eventBus)
    {
        var top = caster.Zones.Library.GetCards().FirstOrDefault();
        if (top == null) return; // empty library — no card to exile

        caster.Zones.Library.RemoveCard(top);
        caster.Zones.Exile.AddCard(top);
        top.SetZone(ZoneType.Exile);

        if (top is not Card concrete) return;

        // CR 118.9 — grant matches ExileCastAlternativeCost. Cost = the
        // exiled card's printed mana cost ("you may play that card"
        // with no alt-cost rider).
        concrete.GrantRuntimeExileCast(caster, concrete.ManaCostValue);

        if (eventBus == null) return;

        // CR 514.2 — schedule the "end of your next turn" clear.
        // Count Cleanup steps owned by the caster: the FIRST cleanup
        // belongs to this turn (sorcery resolved on caster's own turn),
        // the SECOND belongs to the caster's NEXT turn.
        var cleanupsSeen = 0;
        Action<StepStartedEvent>? handler = null;
        handler = (e) =>
        {
            if (e.StepType != PhaseStateType.Cleanup) return;
            if (!ReferenceEquals(e.Player, caster)) return;
            cleanupsSeen++;
            if (cleanupsSeen < 2) return;

            concrete.ClearRuntimeExileCast();
            if (handler != null) eventBus.Unsubscribe(handler);
        };
        eventBus.Subscribe(handler);
    }
}
