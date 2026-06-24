using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData.Definitions;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Skullcap Snail (Modern Horizons 3, {1}{B}).
/// Creature — Fungus Snail 1/1.
///
/// ## Oracle text (Scryfall verified 2026-06)
///   "When this creature enters, target opponent exiles a card from their
///    hand."
///
/// ## Base shape
/// Name / Creature / Fungus Snail / {1}{B} / 1/1 are materialised from the
/// embedded JSON definition (<c>skullcap-snail.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same JSON-backed posture as
/// <see cref="TourachDreadCantorFactory"/>. The JSON carries no abilities;
/// the ETB rider below is layered on here.
///
/// ## Implemented (v1)
/// - <b>ETB trigger</b> (CR 603.1 / CR 603.6a): "When this creature enters,
///   target opponent exiles a card from their hand." Keyed on
///   <see cref="Triggers.OnEnterBattlefieldSelf"/>, one 1..1
///   <see cref="TargetRequest"/> for "target opponent". On resolution the
///   chosen opponent exiles a card of <b>their</b> choice from their hand
///   (CR 701.10a / CR 609.2 — the choice belongs to the affected player, not
///   the controller). Same opponent-chooses-from-own-hand shape as
///   <see cref="ArchonOfCrueltyFactory"/>'s discard step, but the card goes
///   to <see cref="ZoneType.Exile"/> (CR 406.3) rather than the graveyard.
///   Agent-driven when an <see cref="IPlayerAgent"/> is supplied for the
///   target; deterministic first-card fallback otherwise. An empty hand →
///   no-op (CR 701.10a — exile what you can).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — card shape + ETB trigger attached for
///   shape / dispatch tests; the trigger is NOT registered with a
///   <see cref="TriggerManager"/>. This is the overload the dispatcher uses.
/// - <see cref="Create(Player, TriggerManager?, IPlayerAgent?)"/> — fully
///   wired: the ETB trigger registers so the battlefield-entry event
///   auto-queues it.
///
/// CR references: 603.1 / 603.6a (ETB triggered abilities), 701.10a (exile),
/// 406.3 (exile zone), 609.2 / 102.1 (the affected opponent chooses).
/// </summary>
[CardName("Skullcap Snail")]
public static class SkullcapSnailFactory
{
    public const string CardName = "Skullcap Snail";
    public const string Slug = "skullcap-snail";

    /// <summary>
    /// Construct Skullcap Snail with the ETB trigger attached for shape
    /// inspection. The trigger is NOT registered with a
    /// <see cref="TriggerManager"/>. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, targetAgent: null);

    /// <summary>
    /// Construct Skullcap Snail with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">TriggerManager — when supplied, the ETB trigger
    /// registers so the battlefield-entry event lands it on the stack
    /// automatically.</param>
    /// <param name="targetAgent">Optional agent for the TARGET opponent's
    /// exile pick. When non-null the pick is agent-driven; null falls back to
    /// a deterministic first-card pick.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        IPlayerAgent? targetAgent = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (Creature — Fungus
        // Snail, {1}{B}, 1/1). The JSON carries no abilities.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // ETB trigger — CR 603.1 / CR 603.6a.
        //   "When this creature enters, target opponent exiles a card from
        //    their hand."
        // Fires on CardMovedEvent → Battlefield for this card. One 1..1
        // "target opponent" request; on resolution the chosen opponent exiles
        // a card of their own choice (CR 609.2 / 102.1).
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;

        var etbEffect = new Effect(
            $"{CardName}: target opponent exiles a card from their hand",
            () => ResolveEtb(etbTrigger, targetAgent));

        var targetRequest = new TargetRequest(
            Description: "target opponent",
            MinTargets: 1,
            MaxTargets: 1,
            LegalCandidates: Array.Empty<object>());

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            // CR 113.6 — the ability functions only while on the battlefield.
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[] { targetRequest });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }

    // --- ETB resolution (CR 701.10a — exile, opponent chooses) ------------

    /// <summary>
    /// Resolve the ETB trigger: the chosen target opponent exiles a card from
    /// their hand. CR 609.2 / 102.1 — the choice belongs to the affected
    /// opponent (agent-driven when available, deterministic first-card
    /// fallback otherwise). The card moves to the exile zone (CR 406.3 /
    /// 701.10a). An empty hand is a no-op.
    /// </summary>
    private static void ResolveEtb(TriggeredAbility? trigger, IPlayerAgent? targetAgent)
    {
        var opponent = ResolveTargetOpponent(trigger);
        if (opponent is null) return; // no legal target chosen → no-op.

        var hand = opponent.Zones.Hand.GetCards().ToList();
        if (hand.Count == 0) return; // empty hand → exile nothing (CR 701.10a).

        ICard exilePick;
        if (targetAgent != null)
        {
            // CR 609.2 — the opponent chooses. Discard intent is the closest
            // hand-disruption decision class (same posture as Archon's
            // discard step); the affected player picks what to part with.
            var pick = targetAgent
                .ChooseFromHandAsync(opponent, hand.Cast<ICard>().ToList(), BotIntent.Discard)
                .GetAwaiter().GetResult();
            exilePick = (pick != null && pick.Zone == ZoneType.Hand) ? pick : hand[0];
        }
        else
        {
            exilePick = hand[0];
        }

        // CR 406.3 / 701.10a — move the chosen card from hand to exile.
        opponent.Zones.Hand.RemoveCard(exilePick);
        opponent.Zones.Exile.AddCard(exilePick);
        exilePick.SetZone(ZoneType.Exile);
    }

    private static Player? ResolveTargetOpponent(TriggeredAbility? trigger)
    {
        if (trigger is null
            || trigger.ChosenTargets.Count == 0
            || trigger.ChosenTargets[0].Count == 0)
        {
            return null;
        }
        return trigger.ChosenTargets[0][0] as Player;
    }
}
