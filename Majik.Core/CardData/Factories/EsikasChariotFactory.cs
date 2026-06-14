using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Esika's Chariot (Kaldheim, {3}{G}).
///
/// Legendary Artifact — Vehicle 4/4. Oracle text:
///   "When Esika's Chariot enters, create two 2/2 green Cat creature tokens."
///   "Whenever Esika's Chariot attacks, create a token that's a copy of
///    target token you control."
///   "Crew 4."
///
/// ## Implemented (v1)
/// - Shell: <see cref="Creature"/> with <see cref="CardType.Artifact"/>
///   additively stamped (CR 301.1 / 302.1 — the "Artifact Vehicle"
///   multi-type pattern; vehicles are modelled as Creature shells with
///   <c>BasePower</c>/<c>BaseToughness</c> = 4/4 so <see cref="CardData.Vehicles.CrewAction"/>
///   can register a one-turn <see cref="Majik.Core.Effects.VehicleCrewEffect"/>
///   that ships the 4/4 into the working <see cref="Majik.Core.Effects.CreatureCharacteristics"/>).
///   Legendary supertype + Vehicle subtype attached.
/// - <b>ETB trigger</b> (CR 603.1 / 603.6a): "When Esika's Chariot enters,
///   create two 2/2 green Cat creature tokens." Wired via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/>. Each token is built via
///   <see cref="TokenFactory.CreateOnBattlefield"/> with <see cref="CardSubtype.Cat"/>
///   and routes through <see cref="ZoneService"/> when supplied so
///   <see cref="CardMovedEvent"/> fires for downstream ETB listeners.
/// - <b>Attack trigger</b> (CR 508.1f / 706 — copy effects):
///   "Whenever Esika's Chariot attacks, create a token that's a copy of
///    target token you control." Wired via <see cref="Triggers.OnAttackSelf"/>.
///   The copy snapshots the target token's copiable values (name, P/T,
///   subtypes, keyword names) into a fresh <see cref="TokenFactory.CreateOnBattlefield"/>
///   token, mirroring the v1 lossy <see cref="Majik.Core.Effects.CopyEffect"/>
///   semantics already used by Splinter Twin. Targeting is selector-driven
///   (deterministic auto-pick on the single-arg dispatcher path; production
///   callers supply a <c>Func&lt;Player, Creature?&gt;</c> picker to scope
///   to "target token you control").
/// - <b>Crew 4</b> (CR 702.122): surfaced via <see cref="CrewCost"/> so
///   callers route through <see cref="CardData.Vehicles.CrewAction.Crew"/>.
///   Crew is structural data on this factory (no activated ability surface
///   yet — the engine's <c>CrewAction</c> is invoked directly by tests /
///   bots, same shape as the rest of the vehicle MVP).
///
/// ## Targeting (CR 115.1)
/// - <b>"copy of TARGET token you control"</b>: the attack trigger declares
///   a 1..1 <see cref="Majik.Core.Players.Agents.TargetRequest"/> with a live
///   CandidateGatherer scoping to the controller's token permanents, so the
///   choice routes through the shared prod targeting seam
///   (<see cref="TriggerManager.PutPendingTriggersOnStackAsync"/> →
///   <see cref="Majik.Core.Targeting.TargetCollection.CollectAsync"/> →
///   <c>agent.ChooseTargetsAsync</c>). The ACTIVATING PLAYER picks the copy
///   target; resolution re-checks legality (CR 608.2b) and falls back to the
///   deterministic-first picker only when no agent recorded a choice
///   (shape/dispatcher tests).
///
/// ## Deferred (v1 gaps)
/// - <b>Layer 1 copy effect on the spawned attack-copy token</b>: the
///   token's P/T + keywords are snapshotted at resolve time. If the
///   targeted token's characteristics change later (counters, lord
///   anthems), the copy does NOT track them — aligns with existing
///   <see cref="Majik.Core.Effects.CopyEffect"/> v1 lossiness.
/// - <b>Vehicle-as-non-creature off the battlefield</b>: the shell is a
///   <see cref="Creature"/>, so non-battlefield zone inspections see a
///   "creature card" type that the printed face doesn't have until
///   crewed. This is the same v1 simplification used by every other
///   Vehicle modelled today (see <see cref="CardData.Vehicles.CrewActionTests"/>).
/// </summary>
[CardName("Esika's Chariot")]
public static class EsikasChariotFactory
{
    public const string CardName = "Esika's Chariot";
    public const string PrintedManaCost = "{3}{G}";
    public const int CrewCost = 4;
    public const int VehiclePower = 4;
    public const int VehicleToughness = 4;

    /// <summary>
    /// Construct Esika's Chariot with no live ZoneService / TriggerManager
    /// wiring. ETB + attack triggers are attached for shape inspection;
    /// tests fire them by invoking the effects directly. Token-copy
    /// targeting falls back to a deterministic first-token-creature pick
    /// scoped to the chariot's controller. Suitable for dispatcher / shape
    /// tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zoneService: null, eventBus: null, triggers: null, copyTargetPicker: null);

    /// <summary>
    /// Construct Esika's Chariot with optional runtime services. When
    /// <paramref name="zoneService"/> is supplied, spawned tokens route
    /// through <see cref="TokenFactory.CreateOnBattlefield"/> using the
    /// service so each token publishes <see cref="CardMovedEvent"/> on
    /// battlefield entry (downstream ETB listeners — Soul Warden, etc. —
    /// fire). When <paramref name="triggers"/> is supplied, both ETB and
    /// attack triggers are registered for bus-driven firing. When
    /// <paramref name="copyTargetPicker"/> is supplied, it is invoked at
    /// attack-trigger resolution to choose which token-creature the
    /// chariot's controller controls becomes the copy target.
    /// </summary>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        IEventBus? eventBus,
        TriggerManager? triggers,
        Func<Player, Creature?>? copyTargetPicker)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: VehiclePower,
            toughness: VehicleToughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Vehicle });

        // CR 301.1 / 302.1 — Esika's Chariot is an Artifact (Vehicle). The
        // base Creature constructor only registers CardType.Creature, so
        // additively flag the Artifact type for HasType-based lookups
        // (mirrors Wurmcoil Engine's multi-type shape).
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB trigger — CR 603.1.
        //   "When Esika's Chariot enters, create two 2/2 green Cat creature
        //    tokens."
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: create two 2/2 Cat creature tokens",
            () => CreateTwoCatTokens(card.Controller ?? owner, zoneService));

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Attack trigger — CR 508.1f / 706 (copy effects).
        //   "Whenever Esika's Chariot attacks, create a token that's a
        //    copy of target token you control."
        //
        // CR 115.1 — "target token you control" is a true TARGET. Declaring
        // a 1..1 <see cref="TargetRequest"/> (with a live CandidateGatherer
        // enumerating the controller's token creatures) routes the choice
        // through the shared prod targeting seam
        // (TriggerManager.PutPendingTriggersOnStackAsync →
        // TargetCollection.CollectAsync → agent.ChooseTargetsAsync), so the
        // ACTIVATING PLAYER picks the copy target instead of the engine
        // auto-picking the first token. Resolution re-checks legality and
        // falls back to the deterministic-first picker only when no agent
        // recorded a choice (CR 608.2b) — preserving shape/dispatcher tests.
        // ----------------------------------------------------------------
        TriggeredAbility? attackTrigger = null;

        var attackEffect = new Effect(
            $"{CardName}: create a token that's a copy of target token you control",
            () => CreateCopyOfTargetToken(
                controller: card.Controller ?? owner,
                trigger: attackTrigger,
                picker: copyTargetPicker,
                zones: zoneService));

        var copyTargetRequest = BuildCopyTargetRequest(card, owner);

        attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new IEffect[] { attackEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[] { copyTargetRequest });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return card;
    }

    /// <summary>
    /// CR 603.1 ETB effect — create two 2/2 green Cat creature tokens under
    /// <paramref name="controller"/>'s control. CR 105 / CR 111.4 — green is
    /// stamped on each token via <see cref="TokenFactory.TokenSpec.Colors"/>.
    /// </summary>
    private static IReadOnlyList<Creature> CreateTwoCatTokens(
        Player controller, ZoneService? zones)
    {
        var spec = new TokenFactory.TokenSpec(
            Name: "Cat",
            Power: 2,
            Toughness: 2,
            Subtypes: new[] { CardSubtype.Cat },
            Colors: new[] { Majik.Core.ValueObjects.ManaColor.Green });

        var first = TokenFactory.CreateOnBattlefield(spec, controller, zones);
        var second = TokenFactory.CreateOnBattlefield(spec, controller, zones);
        return new[] { first, second };
    }

    /// <summary>
    /// CR 115.1 — the "target token you control" request for the attack
    /// trigger. Candidates are enumerated live at agent-prompt time from the
    /// chariot's CURRENT controller's battlefield (so a controller change
    /// still scopes "you control" correctly) and restricted to token
    /// permanents (CR 111.10). MinTargets/MaxTargets = 1.
    /// </summary>
    private static TargetRequest BuildCopyTargetRequest(Creature card, Player owner)
    {
        return new TargetRequest(
            Description: "target token you control",
            MinTargets: 1,
            MaxTargets: 1,
            LegalCandidates: Array.Empty<object>(),
            CandidateGatherer: _ =>
            {
                var ctrl = card.Controller ?? owner;
                if (ctrl == null) return Array.Empty<object>();
                return ctrl.Zones.Battlefield.GetCards()
                    .OfType<Creature>()
                    .Where(c => c.IsToken && ReferenceEquals(c.Controller, ctrl))
                    .Cast<object>()
                    .ToList();
            });
    }

    /// <summary>
    /// CR 508.1f attack-trigger effect — create a token that's a copy of
    /// the chosen token <paramref name="controller"/> controls.
    ///
    /// <para>
    /// Target resolution order (CR 608.2b — re-check legality at resolution):
    /// </para>
    /// <list type="number">
    ///   <item><description>The agent-chosen target recorded on
    ///   <paramref name="trigger"/> (<see cref="TriggeredAbility.ChosenTargets"/>),
    ///   stamped by the prod TriggerManager seam after prompting the
    ///   activating player — the real "target token you control" choice.</description></item>
    ///   <item><description>The explicit <paramref name="picker"/> closure
    ///   (used by shape/dispatcher tests that call <c>effect.Execute()</c>
    ///   directly without an agent loop).</description></item>
    ///   <item><description>The deterministic-first token-creature fallback
    ///   (no agent, no picker — keeps the single-arg dispatcher path
    ///   behaviour-preserving).</description></item>
    /// </list>
    ///
    /// V1 copies are lossy snapshots (CR 706.2 copiable values approximated
    /// to name + base P/T + subtypes + keyword names), mirroring
    /// <see cref="Majik.Core.Effects.CopyEffect"/>.
    /// </summary>
    private static Creature? CreateCopyOfTargetToken(
        Player controller,
        TriggeredAbility? trigger,
        Func<Player, Creature?>? picker,
        ZoneService? zones)
    {
        var target = ResolveChosenTarget(trigger, controller)
            ?? picker?.Invoke(controller)
            ?? DefaultPickTokenCreature(controller);
        if (target == null) return null;

        // CR 706.2 copiable values snapshot — name, base P/T, subtypes,
        // keyword names. Lossy w.r.t. later characteristic changes; see
        // class xmldoc.
        var keywords = target.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();

        // CR 706.2 — copy effects snapshot the source's colour identity
        // alongside its other copiable values. Read it via the same
        // <see cref="CardColors.GetColors"/> surface that powers
        // protection / lord-style triggers so the snapshot matches the
        // game-state observation surface (including any explicit token
        // colour the source itself was stamped with).
        var colours = CardColors.GetColors(target).ToList();

        var spec = new TokenFactory.TokenSpec(
            Name: target.Name,
            Power: target.BasePower,
            Toughness: target.BaseToughness,
            Subtypes: target.Subtypes.ToList(),
            Keywords: keywords,
            Colors: colours);

        var copy = TokenFactory.CreateOnBattlefield(spec, controller, zones);
        return copy;
    }

    /// <summary>
    /// CR 608.2b — read the agent-chosen target recorded on the trigger
    /// (stamped by the prod TriggerManager seam after prompting the
    /// activating player), re-checking legality at resolution: it must still
    /// be a token <see cref="Creature"/> the controller still controls.
    /// Returns null when no legal choice was recorded so the caller falls
    /// through to the explicit picker / deterministic fallback.
    /// </summary>
    private static Creature? ResolveChosenTarget(
        TriggeredAbility? trigger, Player controller)
    {
        if (trigger is null
            || trigger.ChosenTargets.Count == 0
            || trigger.ChosenTargets[0].Count == 0)
        {
            return null;
        }

        return trigger.ChosenTargets[0][0] is Creature chosen
            && chosen.IsToken
            && ReferenceEquals(chosen.Controller, controller)
            ? chosen
            : null;
    }

    /// <summary>
    /// Deterministic v1 fallback picker — first token Creature on
    /// <paramref name="controller"/>'s battlefield. "Target token you
    /// control" is scoped here to creature-token permanents (the typical
    /// game-state shape for this card: the two Cat tokens it just made).
    /// </summary>
    private static Creature? DefaultPickTokenCreature(Player controller)
    {
        return controller.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .FirstOrDefault(c => c.IsToken);
    }
}
