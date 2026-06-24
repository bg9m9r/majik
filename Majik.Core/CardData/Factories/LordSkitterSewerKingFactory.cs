using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Lord Skitter, Sewer King (Bloomburrow, {2}{B}).
/// Legendary Creature — Rat Noble, 3/3.
///
/// Oracle text (verified against Scryfall 2026-06-24):
///   "Whenever another Rat you control enters, exile up to one target card
///    from an opponent's graveyard.
///    At the beginning of combat on your turn, create a 1/1 black Rat creature
///    token with "This token can't block.""
///
/// ## Base shape
/// Name / Creature / Legendary / Rat + Noble subtypes / {2}{B} / 3/3 are
/// materialised from the embedded JSON definition
/// (<c>lord-skitter-sewer-king.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same JSON-backed posture as
/// <see cref="LegionWarbossFactory"/> / <see cref="KroxaTitanFactory"/>. The
/// two printed triggers are layered on here because the JSON ability schema
/// doesn't yet express subtype-narrowed-enters or begin-combat triggers.
///
/// ## Implemented (v1)
/// - <b>Another-Rat-enters trigger (CR 603.6a / CR 603.6e)</b> — "Whenever
///   another Rat you control enters, exile up to one target card from an
///   opponent's graveyard." Wired as a <see cref="TriggeredAbility"/> over
///   <see cref="CardMovedEvent"/> → Battlefield for a creature OTHER than Lord
///   Skitter, controlled by Lord Skitter's controller, with the Rat subtype
///   (same predicate shape as <see cref="GlaringFleshrakerFactory"/>'s
///   another-colorless-creature trigger, narrowed by subtype instead of
///   colour). The trigger carries an "up to one" (0..1) graveyard
///   <see cref="TargetRequest"/> (CR 115.1a — optional target). On resolution
///   the chosen card's graveyard residency is rechecked (CR 608.2b) and it is
///   moved to its owner's Exile zone — same chosen-target graveyard-exile
///   resolution as <see cref="SoulGuideLanternFactory"/>'s ETB exile. Choosing
///   no target (the "up to one" zero case) is a clean no-op.
/// - <b>Begin-combat token (CR 508.1 — "At the beginning of combat on your
///   turn")</b> — wired as a <see cref="TriggeredAbility"/> over
///   <see cref="StepStartedEvent"/> for
///   <see cref="StepStateType.BeginningOfCombat"/> restricted to the
///   controller's own turns (<see cref="Triggers.OnStepBegin"/>). On
///   resolution it creates one 1/1 black Rat creature token under Lord
///   Skitter's controller (CR 111 / CR 111.4) via
///   <see cref="TokenFactory.CreateOnBattlefield"/> carrying a
///   <c>"CantBlock"</c> <see cref="KeywordAbility"/> marker — the quoted
///   "This token can't block." static, ENFORCED at block declaration by
///   <see cref="Majik.Core.Combat.CombatValidator"/> via
///   <see cref="Majik.Core.Combat.CombatAbilities.HasCantBlock"/> (CR 509.1a).
///   Same token-with-CantBlock posture as <see cref="MirrexFactory"/>'s
///   Phyrexian Mite token.
///
/// ## Single-arg dispatcher path
/// The <see cref="Create(Player)"/> overload attaches both triggers
/// structurally (correct card shape for factory-shape / dispatch tests).
/// Neither trigger is registered with a <see cref="TriggerManager"/>; the
/// token half mints with a raw zone move (null <see cref="ZoneService"/>).
/// Production callers use the full overload.
///
/// ## Deferred (v1 gaps)
/// - <b>Graveyard target prompt</b>: the chosen card is read from
///   <see cref="TriggeredAbility.ChosenTargets"/> set by the prompt pipeline /
///   tests; v1 has no bespoke agent-targeting beyond that (same posture as
///   <see cref="SoulGuideLanternFactory"/>).
/// </summary>
[CardName("Lord Skitter, Sewer King")]
public static class LordSkitterSewerKingFactory
{
    public const string CardName = "Lord Skitter, Sewer King";
    public const string Slug = "lord-skitter-sewer-king";
    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    /// <summary>
    /// Construct Lord Skitter with no live wiring. Both triggers are attached
    /// for shape inspection (not registered with a
    /// <see cref="TriggerManager"/>); the token half uses a raw zone move (null
    /// <see cref="ZoneService"/>). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null, zoneService: null);

    /// <summary>
    /// Construct a fully-wired Lord Skitter.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">TriggerManager to register both triggers
    /// against. May be null — both triggers are still attached to the card
    /// shape.</param>
    /// <param name="zoneService">Optional zone service so the Rat token's ETB
    /// <see cref="CardMovedEvent"/> fires (notably feeding Lord Skitter's own
    /// another-Rat-enters trigger). May be null — raw zone move performed
    /// instead.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ZoneService? zoneService)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Legendary, Rat + Noble subtypes, {2}{B}, 3/3). The JSON carries no
        // abilities — both triggers are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // Trigger 1 — another Rat you control enters (CR 603.6a / CR 603.6e).
        //   "Whenever another Rat you control enters, exile up to one target
        //    card from an opponent's graveyard."
        // Predicate: a creature OTHER than Lord Skitter, controlled by Lord
        // Skitter's controller, with the Rat subtype, entering the
        // battlefield. (Predicate shape mirrors Glaring Fleshraker's
        // another-colorless-creature trigger, narrowed by subtype.) The
        // graveyard exile is an "up to one" optional target (CR 115.1a).
        // ----------------------------------------------------------------
        var ratEntersCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
            e.ToZone == ZoneType.Battlefield
            && e.Card.HasType(CardType.Creature)
            && !ReferenceEquals(e.Card, card)
            && ReferenceEquals(e.Card.Controller, card.Controller ?? owner)
            && e.Card.HasSubtype(CardSubtype.Rat));

        TriggeredAbility? ratEntersTrigger = null;
        var exileEffect = new Effect(
            $"{CardName}: exile up to one target card from an opponent's graveyard",
            () => ResolveExileTarget(ratEntersTrigger));

        ratEntersTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: ratEntersCondition,
            effects: new IEffect[] { exileEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    // CR 115.1a — "up to one target" is an optional target:
                    // MinTargets 0, MaxTargets 1.
                    Description: "up to one target card in an opponent's graveyard",
                    MinTargets: 0,
                    MaxTargets: 1,
                    LegalCandidates: System.Array.Empty<object>(),
                    Intent: BotIntent.Removal),
            });

        card.AddAbility(ratEntersTrigger);
        triggers?.RegisterTriggeredAbility(ratEntersTrigger);

        // ----------------------------------------------------------------
        // Trigger 2 — begin-combat token (CR 508.1).
        //   "At the beginning of combat on your turn, create a 1/1 black Rat
        //    creature token with "This token can't block.""
        // Restricted to the controller's own turns via
        // Triggers.OnStepBegin(owner, BeginningOfCombat).
        // ----------------------------------------------------------------
        var beginCombatEffect = new Effect(
            $"{CardName}: at beginning of combat, create a 1/1 black Rat token that can't block",
            () => CreateRatToken(card.Controller ?? owner, zoneService));

        var beginCombatTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnStepBegin(owner, StepStateType.BeginningOfCombat),
            effects: new IEffect[] { beginCombatEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(beginCombatTrigger);
        triggers?.RegisterTriggeredAbility(beginCombatTrigger);

        return card;
    }

    /// <summary>
    /// CR 608.2b — resolve the "exile up to one target card from an opponent's
    /// graveyard" effect. The chosen card (when one was chosen — "up to one"
    /// permits zero) must still be in a graveyard; it is moved to its owner's
    /// Exile zone. Mirrors <see cref="SoulGuideLanternFactory"/>'s
    /// chosen-target graveyard exile.
    /// </summary>
    private static void ResolveExileTarget(TriggeredAbility? trigger)
    {
        if (trigger == null) return;
        if (trigger.ChosenTargets.Count == 0) return;
        // "up to one" — zero chosen targets is a legal no-op (CR 115.1a).
        if (trigger.ChosenTargets[0].Count == 0) return;

        if (trigger.ChosenTargets[0][0] is not ICard targetCard) return;

        // CR 608.2b — the target card must still be in a graveyard.
        if (targetCard.Zone != ZoneType.Graveyard) return;

        var targetOwner = targetCard.Owner;
        if (targetOwner == null) return;

        targetOwner.Zones.Graveyard.RemoveCard(targetCard);
        targetOwner.Zones.Exile.AddCard(targetCard);
        targetCard.SetZone(ZoneType.Exile);
    }

    /// <summary>
    /// CR 111 / CR 111.4 — create one 1/1 black Rat creature token under
    /// <paramref name="controller"/>'s control carrying the quoted "This token
    /// can't block." restriction (CR 509.1a), recorded as a
    /// <c>"CantBlock"</c> <see cref="KeywordAbility"/> marker and enforced at
    /// block declaration by
    /// <see cref="Majik.Core.Combat.CombatValidator"/> (same posture as
    /// <see cref="MirrexFactory"/>'s Phyrexian Mite token).
    /// </summary>
    public static Creature CreateRatToken(
        Player controller,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: "Rat",
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Rat },
            Keywords: null,
            // CR 105 / CR 111.4 — printed "1/1 black Rat creature token".
            Colors: new[] { ManaColor.Black });

        var token = TokenFactory.CreateOnBattlefield(spec, controller, zoneService);

        // "This token can't block." — recorded as a "CantBlock" marker
        // (CR 509.1a), enforced at block declaration by CombatValidator via
        // CombatAbilities.HasCantBlock.
        token.AddAbility(new KeywordAbility("CantBlock", token, controller));

        return token;
    }
}
