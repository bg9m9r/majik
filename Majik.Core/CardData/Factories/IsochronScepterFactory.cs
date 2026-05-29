using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Isochron Scepter (Mirrodin).
///
/// Artifact — {2}. Oracle text (verified against Scryfall 2026-05-29):
///   "Imprint — When this artifact enters, you may exile an instant card with
///    mana value 2 or less from your hand.
///    {2}, {T}: You may copy the exiled card. If you do, you may cast the copy
///    without paying its mana cost."
///
/// ## Why a hand-coded factory (not a JSON definition)
///
/// The data-driven definition schema models fixed costs / produced mana /
/// simple effects; it has no field for "imprint an instant from your hand on
/// ETB" (CR 702.49) nor for "copy the exiled card and cast the copy without
/// paying its mana cost" (CR 707.10). Both riders would be silently dropped.
/// So this follows the proven hand-coded analogues: the imprint-on-ETB half
/// mirrors <see cref="UginsLabyrinthFactory"/> (CardMovedEvent ETB trigger +
/// <see cref="Permanent.AddImprinted"/>), and the copy-and-cast-for-free half
/// mirrors <see cref="MizzixsMasteryFactory"/> (look up the chosen card's
/// <see cref="SpellDefinition"/> and execute its effects in place — the same
/// lossy-v1 spell-copy posture as <see cref="Majik.Core.Services.SpellCopier"/>).
///
/// ## Implemented (v1)
/// - Artifact identity, mana cost {2}, owner/controller.
/// - <b>Imprint ETB trigger (CR 603.1 / CR 702.49)</b> over
///   <see cref="CardMovedEvent"/> via <see cref="Triggers.OnEnterBattlefieldSelf"/>.
///   On resolve the controller "may exile an instant card with mana value 2 or
///   less from your hand". Routed through the registered <see cref="IPlayerAgent"/>:
///     - <see cref="IPlayerAgent.ChooseYesNoAsync"/> gates the "you may"
///       (CR 117.x); agent-less callers default to YES (imprinting an instant
///       is the card's whole purpose — same auto-accept posture as
///       <see cref="UginsLabyrinthFactory"/>).
///     - <see cref="IPlayerAgent.ChooseFromHandAsync"/> picks which eligible
///       card to exile; the candidate list is pre-filtered to instant cards
///       (CR 205.2a) with mana value ≤ 2 (CR 202.3).
///   The chosen card moves Hand → Exile and is recorded via
///   <see cref="Permanent.AddImprinted"/>.
/// - <b>{2}, {T}: You may copy the exiled card. If you do, you may cast the
///   copy without paying its mana cost.</b> — an <see cref="ActivatedAbility"/>
///   (CR 605 — not a mana ability; uses the stack) with a
///   <see cref="ManaCostCost"/>("{2}") plus <see cref="AdditionalCost.Tap"/>.
///   On resolve, the imprinted card's <see cref="SpellDefinition"/> is looked
///   up via the caller-supplied lookup (production wiring routes through
///   <see cref="Majik.Core.CardData.ScryfallCardFactory.LookupSpellDefinition"/>)
///   and its effects execute in place (CR 707.10 — the copy is cast without
///   paying mana). The imprinted card itself never leaves exile — it is reused
///   turn after turn, which is the whole point of the Scepter.
///
/// ## Deferred (v1 gaps)
/// - <b>"You may" on the activation</b>: both the "you may copy" and "you may
///   cast" riders are auto-accepted on resolve (same posture as Mizzix's
///   Mastery / Through the Breach). A copy-then-decline-cast prompt is deferred.
/// - <b>Real spell-copy stack object</b>: inherited from
///   <see cref="Majik.Core.Services.SpellCopier"/>'s v1 stub — the copy isn't a
///   distinct <see cref="Majik.Core.Stack.IStackObject"/>; observers counting
///   stack items won't see it, and the "choose new targets for the copy" rider
///   reuses whatever the lookup default-picks.
/// - <b>No SpellDefinition lookup supplied</b>: the activated ability is a
///   clean no-op (shape path); production callers always wire the lookup.
/// </summary>
[CardName("Isochron Scepter")]
public static class IsochronScepterFactory
{
    public const string CardName = "Isochron Scepter";
    public const string PrintedManaCost = "{2}";

    /// <summary>Activation mana cost — {2} (CR 605).</summary>
    public const string ActivationManaCost = "{2}";

    /// <summary>Imprint eligibility ceiling — instant cards with mana value
    /// ≤ this (CR 202.3).</summary>
    public const int MaxImprintManaValue = 2;

    /// <summary>
    /// Construct Isochron Scepter with no live event-bus / trigger wiring.
    /// The ETB trigger is attached but not registered. Suitable for identity /
    /// dispatcher / shape tests.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, spellDefinitionLookup: null);

    /// <summary>
    /// Construct Isochron Scepter with optional runtime services. When
    /// <paramref name="triggers"/> is supplied, the Imprint ETB trigger is
    /// registered so <see cref="CardMovedEvent"/>s published on the bus
    /// automatically route it to the stack.
    /// <para>
    /// <paramref name="spellDefinitionLookup"/> binds the imprinted card's
    /// oracle to a <see cref="SpellDefinition"/> for the activated ability's
    /// copy-and-cast resolution; production callers wire
    /// <see cref="Majik.Core.CardData.ScryfallCardFactory.LookupSpellDefinition"/>.
    /// When null the activated ability is a no-op (shape path).
    /// </para>
    /// </summary>
    public static Artifact Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        Func<ICard, SpellDefinition?>? spellDefinitionLookup = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var scepter = new Artifact(CardName, PrintedManaCost);
        scepter.SetOwner(owner);
        scepter.SetController(owner);

        // ----------------------------------------------------------------
        // {2}, {T}: You may copy the exiled card. If you do, you may cast
        //   the copy without paying its mana cost.
        //
        // CR 605 — NOT a mana ability; uses the stack. Cost is {2} +  {T}.
        // ----------------------------------------------------------------
        scepter.AddAbility(new ActivatedAbility(
            source: scepter,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ActivationManaCost),
                AdditionalCost.Tap(scepter),
            },
            effects: BuildActivatedEffects(scepter, owner, spellDefinitionLookup)));

        // ----------------------------------------------------------------
        // Imprint — When this artifact enters, you may exile an instant card
        //   with mana value 2 or less from your hand.
        //
        // CR 603.1 — ETB triggered ability over CardMovedEvent.
        // CR 702.49 — the exiled card is "exiled with" this artifact (imprint).
        // ----------------------------------------------------------------
        var etbCondition = Triggers.OnEnterBattlefieldSelf(scepter);

        var etbEffect = new Effect(
            $"{CardName}: you may exile an instant MV<=2 card from your hand",
            () =>
            {
                var controller = scepter.Controller ?? owner;
                var agent = AgentRegistry.Get(controller);

                // Eligible: instant cards (CR 205.2a) with mana value ≤ 2
                // (CR 202.3 — printed-cost total value).
                var candidates = controller.Zones.Hand.GetCards()
                    .Where(c => c.HasType(CardType.Instant)
                                && ManaValueOf(c) <= MaxImprintManaValue)
                    .ToList();

                if (candidates.Count == 0) return; // nothing legal to exile

                // "You may" — CR 117.x. Default YES when agent-less
                // (imprinting an instant is the card's whole purpose).
                bool wantsToExile = agent == null
                    ? true
                    : agent.ChooseYesNoAsync(
                        $"Exile an instant (MV 2 or less) from your hand to imprint on {CardName}?",
                        BotIntent.CheatIntoPlay).GetAwaiter().GetResult();

                if (!wantsToExile) return;

                ICard? pick = agent != null
                    ? agent.ChooseFromHandAsync(
                            controller, candidates, BotIntent.CheatIntoPlay)
                        .GetAwaiter().GetResult()
                    : candidates[0];

                if (pick == null) return;

                controller.Zones.Hand.RemoveCard(pick);
                controller.Zones.Exile.AddCard(pick);
                pick.SetZone(ZoneType.Exile);

                // CR 702.49 — record the card as "exiled with" the artifact.
                scepter.AddImprinted(pick);
            });

        var etbTrigger = new TriggeredAbility(
            source: scepter,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        scepter.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return scepter;
    }

    /// <summary>
    /// Build the effect list for the {2},{T} activated ability: "You may copy
    /// the exiled card. If you do, you may cast the copy without paying its
    /// mana cost." (CR 707.10).
    /// <para>
    /// On resolve, the first card imprinted on <paramref name="scepter"/> is
    /// copied by looking up its <see cref="SpellDefinition"/> via
    /// <paramref name="spellDefinitionLookup"/> and executing its effects in
    /// place — same lossy-v1 spell-copy semantics as
    /// <see cref="MizzixsMasteryFactory"/> /
    /// <see cref="Majik.Core.Services.SpellCopier"/>. The imprinted card itself
    /// stays in exile so it can be reused. v1 auto-accepts both "you may"
    /// riders. When no card is imprinted or no lookup is supplied, the ability
    /// is a clean no-op.
    /// </para>
    /// </summary>
    public static IEffect[] BuildActivatedEffects(
        Artifact scepter,
        Player controller,
        Func<ICard, SpellDefinition?>? spellDefinitionLookup)
    {
        ArgumentNullException.ThrowIfNull(scepter);
        ArgumentNullException.ThrowIfNull(controller);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: copy the exiled card and cast the copy for free",
                () =>
                {
                    if (spellDefinitionLookup == null) return;

                    // "the exiled card" — the card imprinted with this artifact
                    // (CR 702.49). v1 picks the first (Isochron Scepter only ever
                    // holds one).
                    var imprinted = scepter.ImprintedCards.FirstOrDefault();
                    if (imprinted == null) return; // nothing imprinted — no-op

                    var def = spellDefinitionLookup(imprinted);
                    if (def == null) return;

                    // CR 707.10 — copies don't pay mana. Default ChosenSpellParams:
                    // no mode, no X, no targets, empty mana (same posture as
                    // Mizzix's Mastery). The EffectFactory's resolve-time fallback
                    // picks first-legal targets.
                    var p = new ChosenSpellParams(
                        ModeIndex: null,
                        X: null,
                        Targets: Array.Empty<IReadOnlyList<object>>(),
                        Mana: ManaPayment.Empty);

                    foreach (var effect in def.EffectFactory(p))
                    {
                        effect.Execute();
                    }

                    // The imprinted card is NOT moved — a copy is cast, and the
                    // original stays "exiled with" the Scepter for reuse.
                }),
        };
    }

    /// <summary>
    /// CR 202.3 — mana value of a card derived from its printed mana cost.
    /// Empty cost ⇒ mana value 0. Same approach as
    /// <see cref="UginsLabyrinthFactory"/>.
    /// </summary>
    private static int ManaValueOf(ICard card)
    {
        var cost = card.ManaCost;
        return string.IsNullOrEmpty(cost) ? 0 : ManaCost.Parse(cost).TotalValue;
    }
}
