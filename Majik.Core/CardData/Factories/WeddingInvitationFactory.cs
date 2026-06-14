using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wedding Invitation (Innistrad: Crimson Vow, {2}).
///
/// Artifact. Oracle text (verified against Scryfall 2026-06-14):
///   "When this artifact enters, draw a card.
///    {T}, Sacrifice this artifact: Target creature can't be blocked this
///    turn. If it's a Vampire, it also gains lifelink until end of turn."
///
/// The base shape (name, single Artifact card type, {2}) is materialised from
/// the embedded JSON definition (<c>wedding-invitation.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same posture as
/// <see cref="RenegadeMapFactory"/> / <see cref="NihilSpellbombFactory"/>. The
/// ETB cantrip trigger and the {T}, Sacrifice targeted unblockable + Vampire
/// lifelink rider are layered on here because the JSON
/// <c>AbilityDefinition</c> schema expresses neither.
///
/// Wedding Invitation composes three shapes already in the engine:
/// - <b>ETB cantrip — CR 603.6e / CR 121.1</b>: a
///   <see cref="TriggeredAbility"/> gated on
///   <see cref="Triggers.OnEnterBattlefieldSelf"/> whose effect routes the
///   draw through <see cref="Fx.DrawCards"/> (replacement-aware; empty-library
///   loss flagged via the SBA path, CR 704.5c). Same cantrip path as every
///   ETB-draw artifact (Bygone Bishop's Clue, Faerie Mastermind).
/// - <b>"Target creature can't be blocked this turn" — CR 509.1c</b>: a
///   single-target <see cref="CombatRestrictionEffect"/> with
///   <see cref="CombatRestriction.CannotBeBlocked"/>, EOT-scoped (CR 514.2) —
///   the same restriction Slip Through Space / Rogue's Passage install.
/// - <b>Vampire lifelink rider — CR 613.1c</b>: when the chosen target has the
///   <see cref="CardSubtype.Vampire"/> subtype, a Layer-6
///   <see cref="GrantKeywordUntilEndOfTurnEffect"/>("Lifelink") is also
///   registered on the target (mirrors Heliod, Sun-Crowned's lifelink grant).
///
/// ## Implemented (v1)
/// - Artifact {2} (mana value 2), owner / controller wiring.
/// - ETB cantrip trigger (registered with a <see cref="TriggerManager"/> when
///   one is supplied; attached to the card shape otherwise).
/// - {T}, Sacrifice activated ability with one 1..1 "target creature" request.
///   On resolution the artifact is sacrificed (self-sac stub, same posture as
///   Nihil Spellbomb / Renegade Map — the live cost path also publishes via
///   the bus-aware <see cref="AdditionalCost.Sacrifice"/>), the chosen creature
///   gets the CannotBeBlocked restriction (CR 608.2b illegal-target re-check),
///   and a Vampire target additionally gains Lifelink until end of turn.
///
/// ## Deferred (v1 gaps)
/// - <b>Target legality in ActionValidator</b>: the validator does not yet
///   filter the chosen object to "a creature on the battlefield" — the
///   resolution-time guard handles illegal targets (CR 608.2b), same posture
///   as Slip Through Space / Heliod's lifelink grant.
/// </summary>
[CardName("Wedding Invitation")]
public static class WeddingInvitationFactory
{
    public const string CardName = "Wedding Invitation";
    public const string Slug = "wedding-invitation";

    /// <summary>CR 702.15 — Lifelink keyword string (layer-system canonical).</summary>
    public const string LifelinkKeyword = "Lifelink";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Wedding Invitation owned and controlled by
    /// <paramref name="owner"/>. Shape-only — no event bus / TriggerManager,
    /// so the ETB trigger is attached to the card but not registered, and the
    /// self-sacrifice cost publishes nothing (dispatcher / structural tests).
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, triggers: null, eventBus: null);

    /// <summary>
    /// Effects-aware overload the <b>production</b> <c>GameFacade</c> routed
    /// build dispatches to (the source generator recognises
    /// <c>Create(Player, ContinuousEffectsService)</c> as the effects-aware
    /// overload — Renegade Map / Festival Crasher pattern). Threads
    /// <c>effects.EventBus</c> into the self-sacrifice cost so paying it
    /// publishes a <see cref="PermanentSacrificedEvent"/> (CR 701.16a). The
    /// ETB trigger auto-binds on the live manager's first zone crossing, so no
    /// TriggerManager is needed here.
    /// </summary>
    public static Artifact Create(Player owner, ContinuousEffectsService? effects) =>
        Create(owner, triggers: null, eventBus: effects?.EventBus);

    /// <summary>
    /// Construct Wedding Invitation with optional <see cref="TriggerManager"/>
    /// wiring. When <paramref name="triggers"/> is supplied the ETB cantrip
    /// trigger is registered so the artifact's Battlefield arrival queues the
    /// draw automatically.
    /// </summary>
    public static Artifact Create(Player owner, TriggerManager? triggers) =>
        Create(owner, triggers, eventBus: null);

    /// <summary>
    /// Canonical builder. <paramref name="eventBus"/> (when non-null) is
    /// threaded into the self-sacrifice <see cref="AdditionalCost"/> so the
    /// live activation cost path publishes a
    /// <see cref="PermanentSacrificedEvent"/> (CR 701.16a).
    /// </summary>
    public static Artifact Create(
        Player owner,
        TriggerManager? triggers,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name, Artifact, {2}) from the embedded JSON definition.
        var card = (Artifact)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB cantrip — CR 603.6e / CR 121.1.
        //   "When this artifact enters, draw a card."
        // Fires on a CardMovedEvent → Battlefield matching this card; the
        // draw routes through Fx.DrawCards (replacement-aware; empty-library
        // loss flagged by the SBA path, CR 704.5c).
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: draw a card on enter (CR 121.1)",
            () => Fx.DrawCards(card.Controller ?? owner, 1));

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // {T}, Sacrifice this artifact:
        //   Target creature can't be blocked this turn. If it's a Vampire,
        //   it also gains lifelink until end of turn.
        // CR 602 — activated ability; tap + self-sacrifice cost, no mana.
        // CR 509.1c — single-target CannotBeBlocked restriction, EOT-scoped.
        // CR 613.1c — Layer-6 lifelink keyword grant, Vampire-only rider.
        // ----------------------------------------------------------------
        ActivatedAbility? ability = null;
        var resolveEffect = new Effect(
            $"{CardName}: target creature unblockable (+ Vampire lifelink) + sac self",
            () =>
            {
                // Self-sacrifice stub (Battlefield → owner's graveyard). The
                // live cost path may have already moved the card; SacrificeSelf
                // is idempotent (same posture as Nihil Spellbomb / Renegade Map).
                SacrificeSelf(card, owner, eventBus);

                if (ability == null) return;
                var chosen = ability.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;
                if (chosen[0][0] is not Creature target) return;
                if (target.Zone != ZoneType.Battlefield) return; // CR 608.2b
                if (target.ActiveEffects == null) return;          // shape-only no-op

                // CR 509.1c — "can't be blocked this turn" (EOT default, CR 514.2).
                target.ActiveEffects.Register(
                    new CombatRestrictionEffect(
                        CombatRestriction.CannotBeBlocked, target));

                // CR 613.1c — "If it's a Vampire, it also gains lifelink until
                // end of turn." Read the target's live (computed) subtypes so a
                // type-changing effect that made it a Vampire still qualifies.
                if (target.HasSubtype(CardSubtype.Vampire))
                {
                    target.ActiveEffects.Register(
                        new GrantKeywordUntilEndOfTurnEffect(target, LifelinkKeyword));
                }
            });

        ability = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.Tap(card),
                // CR 701.16a — bus on the SAC COST so the live activation path
                // (CostPayment → cost.Pay) publishes PermanentSacrificedEvent;
                // the closure's SacrificeSelf is the bus-aware fallback for the
                // resolve-only dispatcher/test path.
                AdditionalCost.Sacrifice(card, eventBus),
            },
            effects: new IEffect[] { resolveEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.CombatTrick,
                    // Live gatherer: every creature on the battlefield across
                    // all players (no further restriction — any creature is a
                    // legal target).
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(ability);

        return card;
    }

    /// <summary>
    /// CR 701.16 — move <paramref name="card"/> from the battlefield to its
    /// owner's graveyard. Idempotent. When <paramref name="eventBus"/> is
    /// supplied the move routes through <see cref="Fx.Sacrifice(ICard, Player,
    /// IEventBus)"/>, publishing a <see cref="PermanentSacrificedEvent"/>
    /// (CR 701.16a). In the live activation path the cost already moved the
    /// card, so this closure no-ops (single publish either way).
    /// </summary>
    private static void SacrificeSelf(Artifact card, Player owner, IEventBus? eventBus)
    {
        if (card.Zone != ZoneType.Battlefield) return;

        if (eventBus != null)
        {
            Fx.Sacrifice(card, card.Controller ?? owner, eventBus);
            return;
        }

        var controller = card.Controller ?? owner;
        controller.Zones.Battlefield.RemoveCard(card);
        owner.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);
    }
}
