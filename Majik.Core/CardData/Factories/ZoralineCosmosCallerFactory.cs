using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Zoraline, Cosmos Caller (Edge of Eternities,
/// <c>{1}{W}{B}</c>). Legendary Creature — Bat Cleric. 3/3.
///
/// Oracle text (Scryfall-verified):
/// <list type="number">
///   <item>"Flying, vigilance"</item>
///   <item>"Whenever a Bat you control attacks, you gain 1 life."</item>
///   <item>"Whenever Zoraline enters or attacks, you may pay <c>{W}{B}</c>
///       and 2 life. When you do, return target nonland permanent card
///       with mana value 3 or less from your graveyard to the battlefield
///       with a finality counter on it."</item>
/// </list>
///
/// ## Shape source
/// Card identity (name, <c>{1}{W}{B}</c>, 3/3, Legendary Creature — Bat
/// Cleric, Flying + Vigilance) is loaded from
/// <c>Majik.Core/CardData/Cards/zoraline-cosmos-caller.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/> (the <c>keywords</c> array carries
/// Flying CR 702.9 + Vigilance CR 702.21). The two triggered abilities are
/// attached in code below.
///
/// ## Implemented (v1)
/// <list type="bullet">
///   <item><b>Ability 2 — Bat-attack lifegain (CR 508.1f / 603.6a)</b>:
///   wired over <see cref="CreatureAttacksEvent"/> matching any attacker
///   that (a) the owner controls and (b) has the <see cref="CardSubtype.Bat"/>
///   subtype. Zoraline herself is a Bat, so her own attack also fires this
///   (CR 508.1f — one event per declared attacker). Effect:
///   <see cref="Player.GainLife"/>(1).</item>
///
///   <item><b>Ability 3 — enter/attack reflexive reanimation</b>: a single
///   <see cref="TriggeredAbility"/> whose condition matches Zoraline's own
///   ETB (<see cref="Triggers.OnEnterBattlefieldSelf"/>) OR her own attack
///   (<see cref="Triggers.OnAttackSelf"/>). On resolution the controller's
///   agent is prompted to pay the combined optional cost <c>{W}{B}</c> + 2
///   life (CR 601.2b — an optional cost that can't be paid isn't paid; the
///   life requirement and mana payability are both gated up front). When
///   paid, the "When you do, …" reflexive sub-effect (CR 603.7c) returns a
///   target nonland permanent card with mana value 3 or less from the
///   controller's graveyard to the battlefield under their control, with a
///   <see cref="CounterType.Finality"/> counter on it (CR 122.1m). v1 picks
///   the target deterministically — first eligible card in the controller's
///   graveyard — mirroring <see cref="EmperorOfBonesFactory"/> /
///   <see cref="ReanimateFactory"/>'s deterministic v1 target selection. A
///   1..1 <see cref="TargetRequest"/> is still exposed so a live caller can
///   pre-set the chosen target.</item>
/// </list>
///
/// <para>
/// <b>Finality counter wiring</b>: the factory eagerly calls
/// <see cref="FinalityCounterReplacement.Register"/> on the supplied
/// <see cref="ReplacementBus"/> so the die-redirect is in place for any
/// finality-marked permanent Zoraline returns (CR 122.1m — a permanent with
/// a finality counter that would die is exiled instead). Idempotent.
/// </para>
///
/// ## Deferred (v1 gaps)
/// <list type="bullet">
///   <item><b>Agent-driven target prompt</b>: ability 3 honours a pre-set
///   <see cref="TriggeredAbility.ChosenTargets"/>; otherwise it
///   deterministically picks the first eligible graveyard card. The
///   choose-one-from-graveyard agent prompt is the standard v1 deferral
///   shared with Emperor of Bones / Reanimate.</item>
/// </list>
/// </summary>
[CardName("Zoraline, Cosmos Caller")]
public static class ZoralineCosmosCallerFactory
{
    public const string CardName = "Zoraline, Cosmos Caller";

    /// <summary>The combined-cost mana half — <c>{W}{B}</c> (CR 601.2b).</summary>
    public const string ReflexiveManaCost = "{W}{B}";

    /// <summary>The combined-cost life half — 2 life (CR 118.4).</summary>
    public const int ReflexiveLifeCost = 2;

    /// <summary>"Mana value 3 or less" reanimation cap (CR 202.3b).</summary>
    public const int MaxReanimateManaValue = 3;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("zoraline-cosmos-caller");

    /// <summary>
    /// Construct Zoraline for the dispatcher / shape-test path: no
    /// <see cref="TriggerManager"/>, <see cref="ZoneService"/>,
    /// <see cref="ReplacementBus"/>, or <see cref="IEventBus"/> wired.
    /// Identity + ability shape are fully populated; live bus-driven trigger
    /// firing is a no-op.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, zoneService: null,
            replacements: null, eventBus: null);

    /// <summary>
    /// Construct Zoraline with optional engine plumbing. Each dependency is
    /// independent:
    /// <list type="bullet">
    ///   <item><paramref name="triggers"/> registers both triggered
    ///   abilities so the trigger bus surfaces them.</item>
    ///   <item><paramref name="zoneService"/> routes graveyard → battlefield
    ///   for ability 3's reanimation so ETB triggers on the returning
    ///   permanent fire (CR 603.6a).</item>
    ///   <item><paramref name="replacements"/> is where
    ///   <see cref="FinalityCounterReplacement"/> registers and where the
    ///   finality counter placement routes.</item>
    ///   <item><paramref name="eventBus"/> publishes the
    ///   <see cref="CounterAddedEvent"/> for downstream counters-matter
    ///   triggers.</item>
    /// </list>
    /// </summary>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ZoneService? zoneService,
        ReplacementBus? replacements,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // Finality counter infrastructure — register the global die-redirect
        // (idempotent). Without this, the counter ability 3 stamps does
        // nothing on the returned permanent's eventual death (CR 122.1m).
        if (replacements != null)
        {
            FinalityCounterReplacement.Register(replacements);
        }

        // ----------------------------------------------------------------
        // Ability 2 — "Whenever a Bat you control attacks, you gain 1 life."
        // CR 508.1f / 603.6a. One CreatureAttacksEvent per declared
        // attacker; filter to Bat-subtyped attackers the owner controls.
        // Zoraline herself is a Bat, so her own attack fires this too.
        // ----------------------------------------------------------------
        var batLifegainEffect = new Effect(
            $"{CardName}: you gain 1 life (a Bat you control attacked)",
            () => owner.GainLife(1));

        var batAttackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CreatureAttacksEvent>((e, _) =>
                e.Attacker.HasSubtype(CardSubtype.Bat)
                && ReferenceEquals(e.Attacker.Controller, owner)),
            effects: new IEffect[] { batLifegainEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(batAttackTrigger);
        triggers?.RegisterTriggeredAbility(batAttackTrigger);

        // ----------------------------------------------------------------
        // Ability 3 — "Whenever Zoraline enters or attacks, you may pay
        // {W}{B} and 2 life. When you do, return target nonland permanent
        // card with mana value 3 or less from your graveyard to the
        // battlefield with a finality counter on it."
        //
        // "Enters or attacks" is two distinct trigger events (CR 603.6a ETB
        // + CR 508.1f attack) that share one effect. ITriggerCondition
        // subscribes per concrete event type, so this is modelled as TWO
        // TriggeredAbility instances sharing the same reflexive-reanimation
        // resolution (BuildReanimateAbility), matching how the engine
        // dispatches each event type separately.
        // ----------------------------------------------------------------
        var etbReanimate = BuildReanimateAbility(
            card, owner, Triggers.OnEnterBattlefieldSelf(card),
            zoneService, replacements, eventBus);
        card.AddAbility(etbReanimate);
        triggers?.RegisterTriggeredAbility(etbReanimate);

        var attackReanimate = BuildReanimateAbility(
            card, owner, Triggers.OnAttackSelf(card),
            zoneService, replacements, eventBus);
        card.AddAbility(attackReanimate);
        triggers?.RegisterTriggeredAbility(attackReanimate);

        return card;
    }

    /// <summary>
    /// Build one of ability 3's two triggered abilities (ETB or attack) over
    /// <paramref name="condition"/>. Both carry the same reflexive-cost
    /// reanimation effect and a 1..1 graveyard <see cref="TargetRequest"/>.
    /// </summary>
    private static TriggeredAbility BuildReanimateAbility(
        Creature zoraline, Player owner, ITriggerCondition condition,
        ZoneService? zoneService, ReplacementBus? replacements, IEventBus? eventBus)
    {
        TriggeredAbility? trigger = null;
        var effect = new Effect(
            $"{CardName}: may pay {ReflexiveManaCost} and {ReflexiveLifeCost} life; " +
            "if you do, return target nonland permanent card with mana value 3 or " +
            "less from your graveyard with a finality counter",
            async ctx => await ResolveReanimate(
                    zoraline, owner, trigger, zoneService, replacements, eventBus, ctx)
                .ConfigureAwait(false));

        trigger = new TriggeredAbility(
            source: zoraline,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { effect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target nonland permanent card with mana value 3 or less in your graveyard",
                    MinTargets: 0,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Reanimate | BotIntent.CardAdvantage),
            });

        return trigger;
    }

    /// <summary>
    /// Resolve ability 3. Prompt the controller's agent for the optional
    /// combined cost ({W}{B} + 2 life, CR 601.2b). On yes + payable, pay the
    /// mana and lose 2 life, then run the reflexive "When you do, …"
    /// reanimation (CR 603.7c): return the chosen / first-eligible nonland
    /// permanent card with mana value ≤ 3 from the controller's graveyard to
    /// the battlefield under their control, stamping a finality counter.
    /// </summary>
    private static async System.Threading.Tasks.ValueTask ResolveReanimate(
        Creature zoraline, Player controller, TriggeredAbility? trigger,
        ZoneService? zoneService, ReplacementBus? replacements,
        IEventBus? eventBus, ResolutionContext ctx)
    {
        // CR 601.2b — the optional reflexive cost. Determine the cost is even
        // payable BEFORE prompting is not required, but pay only on yes.
        var manaCost = ManaCost.Parse(ReflexiveManaCost);

        var agent = ctx.Agent ?? AgentRegistry.Get(controller);
        // A null agent (raw shape path) means no decision surface — treat as
        // "did not pay" so the reflexive block is skipped (clean no-op).
        if (agent == null) return;

        var wantsToPay = await agent
            .ChooseYesNoAsync(
                ctx.Game,
                $"Pay {ReflexiveManaCost} and {ReflexiveLifeCost} life?",
                CardName,
                ctx.Ct)
            .ConfigureAwait(false);
        if (!wantsToPay) return;

        // CR 601.2b — an optional cost that can't be paid isn't paid. Both
        // halves must be payable; the life requirement is the controller
        // having at least 2 life to lose as a cost (CR 118.4 / 119.4).
        if (!controller.ManaPool.CanPay(manaCost)) return;
        if (controller.LifeTotal < ReflexiveLifeCost) return;
        if (!controller.PayMana(manaCost)) return;
        controller.LoseLife(ReflexiveLifeCost);

        // "When you do, return target nonland permanent card with mana value
        // 3 or less from your graveyard …" — the reflexive sub-effect.
        var pick = SelectTarget(controller, trigger);
        if (pick == null) return;

        // CR 608.2b — the target card must still be in the controller's
        // graveyard at resolution.
        if (pick.Zone != ZoneType.Graveyard) return;
        if (!controller.Zones.Graveyard.GetCards().Contains(pick)) return;

        // CR 701.20 — graveyard → battlefield under the controller. Route
        // through ZoneService when wired so ETB triggers fire (CR 603.6a).
        Fx.ReturnFromGraveyardToBattlefield(pick, controller, zoneService);

        // "With a finality counter on it" — CR 122.1m. Route through
        // CountersService.Add so the placement publishes its post-commit
        // event and honours the replacement bus.
        if (pick is Permanent finalityTarget)
        {
            CountersService.Add(
                finalityTarget, CounterType.Finality, 1, replacements, eventBus);
        }
    }

    /// <summary>
    /// Pick ability 3's reanimation target. Honours a pre-set chosen target
    /// on the trigger when present and eligible; otherwise v1 deterministic
    /// fallback — the first nonland permanent card with mana value ≤ 3 in the
    /// controller's graveyard (CR 700.6 — a single "target" object).
    /// </summary>
    private static ICard? SelectTarget(Player controller, TriggeredAbility? trigger)
    {
        if (trigger != null
            && trigger.ChosenTargets.Count > 0
            && trigger.ChosenTargets[0].Count > 0
            && trigger.ChosenTargets[0][0] is ICard chosen
            && IsEligible(chosen))
        {
            return chosen;
        }

        return controller.Zones.Graveyard.GetCards()
            .FirstOrDefault(IsEligible);
    }

    /// <summary>
    /// "Nonland permanent card with mana value 3 or less" — CR 110.4a /
    /// CR 202.3b. A permanent card is one with a permanent card type
    /// (creature / artifact / enchantment / planeswalker / battle); "nonland"
    /// excludes lands; the mana value cap is 3.
    /// </summary>
    private static bool IsEligible(ICard card) =>
        !card.HasType(CardType.Land)
        && IsPermanentCard(card)
        && card is Card concrete
        && concrete.ManaCostValue.TotalValue <= MaxReanimateManaValue;

    /// <summary>
    /// A "permanent card" is a card with one of the permanent card types
    /// (CR 110.4a). Land is included here for completeness but the
    /// <see cref="IsEligible"/> caller has already excluded lands ("nonland").
    /// </summary>
    private static bool IsPermanentCard(ICard card) =>
        card.HasType(CardType.Creature)
        || card.HasType(CardType.Artifact)
        || card.HasType(CardType.Enchantment)
        || card.HasType(CardType.Planeswalker)
        || card.HasType(CardType.Land);
}
