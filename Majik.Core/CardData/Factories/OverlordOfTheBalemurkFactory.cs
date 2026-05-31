using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Overlord of the Balemurk (Duskmourn: House of
/// Horror, {3}{B}{B}). Enchantment Creature — Avatar Horror 5/5. Oracle
/// text (verified against Scryfall):
///   "Impending 5—{1}{B} (If you cast this spell for its impending cost, it
///    enters with five time counters and isn't a creature until the last is
///    removed. At the beginning of your end step, remove a time counter from
///    it.)
///    Whenever this permanent enters or attacks, mill four cards, then you
///    may return a non-Avatar creature card or a planeswalker card from your
///    graveyard to your hand."
///
/// The card's base shape (name, Enchantment + Creature types, Avatar +
/// Horror subtypes, {3}{B}{B}, 5/5) is materialised from the embedded JSON
/// definition (<c>overlord-of-the-balemurk.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two printed behaviours
/// (the Impending marker keyword + the enters-or-attacks trigger) are
/// layered on top here — the JSON <c>AbilityDefinition</c> schema doesn't
/// express keyword markers or the whole-graveyard "you may return"
/// effect, so they live in the factory (same posture as
/// <see cref="StormscaleScionFactory"/> and the other JSON-backed cards
/// whose behaviour outgrows the schema).
///
/// ## Implemented (v1)
/// - <b>Enters-or-attacks trigger (CR 603.1 ETB + CR 508.1f attack)</b>:
///   two <see cref="TriggeredAbility"/> instances sharing one effect body —
///   one gated on <see cref="Triggers.OnEnterBattlefieldSelf"/>, one on
///   <see cref="Triggers.OnAttackSelf"/> (same dual-trigger shape as
///   <see cref="PrimevalTitanFactory"/>'s "enters or attacks"). On
///   resolution: mill four cards (CR 701.13, via
///   <see cref="Majik.Core.Keywords.MillAction.Apply"/>), then the
///   controller <i>may</i> return one non-Avatar creature card or a
///   planeswalker card from their <b>whole</b> graveyard to hand. The
///   candidate filter excludes Avatar-subtype creatures (the printed
///   "non-Avatar") and includes planeswalker cards. The "you may" /
///   which-card decision is the agent's
///   <see cref="IPlayerAgent.ChooseFromPileAsync"/> with
///   <see cref="BotIntent.Reanimate"/> (upside intent → default agent
///   accepts and picks the first candidate); returning <see langword="null"/>
///   is a legal decline (CR 117.x). Mirrors the graveyard→hand return
///   idiom shipped on <see cref="TasigurTheGoldenFangFactory"/>.
///
/// ## Impending — modelled as a marker keyword (deferred mechanic)
/// "Impending 5—{1}{B}" is an alternative-cost keyword (Duskmourn). The
/// engine does not yet have a first-class Impending alt-cost / "isn't a
/// creature until the last time counter is removed" path. Following the
/// established marker-keyword precedent (Delve on
/// <see cref="TasigurTheGoldenFangFactory"/>, Suspend on the suspend
/// family), Impending is wired as a <see cref="KeywordAbility"/> marker
/// with <c>Arg = 5</c> so introspection (UI, bots, the alt-cost probe
/// stream) can see the keyword + counter count on the card. The full
/// Impending mechanic — casting for {1}{B} with five Time counters
/// (CR 122.1), the Layer-4 "isn't a creature" type-strip while counters
/// remain (CR 613 / Layer4TypeStripEffect, same machinery Heliod uses),
/// and the end-step "remove a time counter" delayed trigger — is
/// deferred. The card's printed gameplay payload (the enters-or-attacks
/// trigger) is fully implemented; only the alternate way to pay for it is
/// the deferred part. When cast for its normal {3}{B}{B} cost the card
/// behaves completely.
/// </summary>
[CardName("Overlord of the Balemurk")]
public static class OverlordOfTheBalemurkFactory
{
    public const string CardName = "Overlord of the Balemurk";
    public const string Slug = "overlord-of-the-balemurk";

    /// <summary>Impending counter count — "Impending 5".</summary>
    public const int ImpendingCount = 5;

    /// <summary>Cards milled by the enters-or-attacks trigger.</summary>
    public const int MillCount = 4;

    /// <summary>
    /// Construct Overlord of the Balemurk with no live TriggerManager
    /// wiring and the default agent-driven return decision. The two
    /// enters-or-attacks triggers + the Impending marker are attached for
    /// shape inspection. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, returnSelector: null);

    /// <summary>
    /// Construct Overlord of the Balemurk with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the ETB + attack triggers are
    /// registered so the matching events land their abilities on the stack
    /// automatically.</param>
    /// <param name="returnSelector">Deterministic test override for the
    /// "you may return …" decision. Invoked with the pre-filtered list of
    /// eligible graveyard cards (non-Avatar creatures + planeswalkers); its
    /// return value is the card to return, or <see langword="null"/> to
    /// decline. When null, the registered <see cref="IPlayerAgent"/> is
    /// consulted via <see cref="IPlayerAgent.ChooseFromPileAsync"/>.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        Func<IReadOnlyList<ICard>, ICard?>? returnSelector)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature +
        // Enchantment types, Avatar + Horror subtypes, {3}{B}{B}, 5/5). The
        // JSON carries no abilities — the Impending marker + the
        // enters-or-attacks trigger are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // Impending 5 — marker keyword (mechanic deferred; see class
        // remarks). Arg carries the printed counter count.
        card.AddAbility(new KeywordAbility("Impending", card, owner, arg: ImpendingCount));

        // ----------------------------------------------------------------
        // Shared effect body: "mill four cards, then you may return a
        // non-Avatar creature card or a planeswalker card from your
        // graveyard to your hand." (CR 701.13 mill + CR 117.x "may".)
        // ----------------------------------------------------------------
        IEffect BuildTriggerEffect(string label) =>
            new Effect(label, ctx => MillFourThenMayReturnAsync(owner, returnSelector, ctx));

        // ETB trigger — CR 603.1.
        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new[] { BuildTriggerEffect($"{CardName}: enters — mill 4, may return creature/planeswalker") },
            activeZones: new[] { ZoneType.Battlefield });
        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // Attack trigger — CR 508.1f.
        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new[] { BuildTriggerEffect($"{CardName}: attacks — mill 4, may return creature/planeswalker") },
            activeZones: new[] { ZoneType.Battlefield });
        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return card;
    }

    /// <summary>
    /// Mill four cards from <paramref name="controller"/>'s library, then
    /// optionally return one eligible graveyard card (a non-Avatar creature
    /// card or a planeswalker card) to hand. The candidate set is the
    /// controller's <b>whole</b> graveyard after the mill (the milled cards
    /// are now in the graveyard and are themselves eligible). When
    /// <paramref name="returnSelector"/> is supplied it drives the decision;
    /// otherwise the registered agent is consulted. Returning
    /// <see langword="null"/> is a legal decline (CR 117.x).
    /// </summary>
    public static async ValueTask MillFourThenMayReturnAsync(
        Player controller,
        Func<IReadOnlyList<ICard>, ICard?>? returnSelector,
        ResolutionContext ctx)
    {
        ArgumentNullException.ThrowIfNull(controller);

        // CR 701.13 — mill four.
        Majik.Core.Keywords.MillAction.Apply(controller, MillCount);

        // Eligible: a non-Avatar creature card OR a planeswalker card, from
        // the whole graveyard. "non-Avatar" excludes creatures with the
        // Avatar subtype (CR 205.3m). Planeswalkers are eligible regardless
        // of subtype.
        var candidates = controller.Zones.Graveyard.GetCards()
            .Where(IsEligibleReturn)
            .ToList();
        if (candidates.Count == 0) return;

        // "you may" — selector override (tests) or the registered agent.
        ICard? pick;
        if (returnSelector != null)
        {
            pick = returnSelector(candidates);
        }
        else
        {
            var agent = ctx.Agent ?? AgentRegistry.Get(controller);
            pick = agent != null
                ? await agent.ChooseFromPileAsync(
                    chooser: controller,
                    candidates: candidates,
                    pileLabel: "a non-Avatar creature or planeswalker in your graveyard",
                    intent: BotIntent.Reanimate)
                    .ConfigureAwait(false)
                // No agent registered — deterministic accept-first (matches
                // every retrofitted "may" factory's pre-agent posture; the
                // upside Reanimate intent makes the default agent do this too).
                : candidates[0];
        }

        // Decline (null) or an out-of-set pick → no-op.
        if (pick == null) return;
        if (!candidates.Contains(pick)) return;

        // Move from graveyard to hand. Direct-zone mutation mirrors the
        // Tasigur graveyard→hand return idiom at this dispatcher path.
        controller.Zones.Graveyard.RemoveCard(pick);
        controller.Zones.Hand.AddCard(pick);
        pick.SetZone(ZoneType.Hand);
    }

    /// <summary>
    /// Eligibility filter for the "return … to your hand" clause: a
    /// non-Avatar creature card, or a planeswalker card.
    /// </summary>
    public static bool IsEligibleReturn(ICard card)
    {
        if (card == null) return false;
        if (card.HasType(CardType.Planeswalker)) return true;
        if (card.HasType(CardType.Creature) && !card.HasSubtype(CardSubtype.Avatar)) return true;
        return false;
    }
}
