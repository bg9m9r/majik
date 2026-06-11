using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Teferi, Hero of Dominaria (Dominaria, {3}{W}{U}).
///
/// Legendary Planeswalker — Teferi. Starting loyalty 4.
/// Oracle text (Scryfall, verified 2026-06-09 from the embedded seed):
///   "+1: Draw a card. At the beginning of the next end step, untap up to
///        two lands.
///    −3: Put target nonland permanent into its owner's library third from
///        the top.
///    −8: You get an emblem with 'Whenever you draw a card, exile target
///        permanent an opponent controls.'"
///
/// The card's base shape (name, Legendary Planeswalker — Teferi, {3}{W}{U},
/// loyalty 4) is materialised from the embedded JSON definition
/// (<c>teferi-hero-of-dominaria.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The three loyalty abilities are
/// layered on here — the JSON <c>AbilityDefinition</c> schema doesn't express
/// loyalty abilities, delayed triggers, targeted library-insertion, or
/// emblems, so they live in the factory (same posture as
/// <see cref="UginEyeOfTheStormsFactory"/> and
/// <see cref="LilianaTheLastHopeFactory"/>).
///
/// ## Agent-targeted on the prod loyalty path (#2517)
/// All three abilities now declare real <see cref="TargetRequest"/>s and read
/// the activating player's CHOSEN objects off the live
/// <see cref="ResolutionContext"/> — the deterministic build-time resolvers are
/// gone, so the −3 / +1-untap / emblem clauses are LIVE on the routed prod
/// build instead of inert (they previously read null resolvers on prod). The
/// loyalty-target collection path (<c>TurnDriver.DispatchLoyalty</c> →
/// <c>agent.ChooseTargetsAsync</c> → <c>SetChosenTargets</c>) prompts the
/// controller for each request before the loyalty ability resolves; mirrors
/// <see cref="KothOfTheHammerFactory"/> (+1 "untap target Mountain") and
/// <see cref="LilianaOfTheVeilFactory"/> (−2 "target player").
///
/// - <b>+1: Draw a card; at the beginning of the next end step, untap up to two
///   lands (CR 606 + CR 121 + CR 603.7)</b>: draws one card for the controller
///   (<see cref="Fx.DrawCards"/>). The "up to two lands" choice is a real
///   <see cref="TargetRequest"/> (MinTargets 0, MaxTargets 2, gatherer = lands
///   the controller controls) collected by the loyalty-target path at +1
///   resolution. The chosen lands are captured and untapped by a one-shot
///   <see cref="DelayedTriggeredAbility"/> on the next End-step
///   <see cref="StepStartedEvent"/> (CR 603.7 — "at the beginning of the next
///   end step"); the TriggerManager auto-unregisters the delayed ability after
///   it fires (CR 603.7c). v1 timing note: the LAND CHOICE is locked in at +1
///   resolution (when the agent is already being prompted) rather than at the
///   end step — the untap itself still happens at the end step. Without the
///   <paramref name="triggers"/> service the draw still happens and the untap
///   clause is a legal no-op.
/// - <b>−3: Put target nonland permanent into its owner's library third from
///   the top (CR 606 + CR 701 + CR 401 + CR 110.4a)</b>: a real
///   <see cref="TargetRequest"/> "target nonland permanent" (gatherer = every
///   battlefield nonland permanent). The effect reads the CHOSEN permanent off
///   <c>rc.ChosenTargets[0][0]</c>, removes it from its current battlefield and
///   inserts it into its <em>owner's</em> library at index 2 — "third from the
///   top" in a top-first library (<see cref="IZone.InsertCardAt"/>).
/// - <b>−8: emblem with "Whenever you draw a card, exile target permanent an
///   opponent controls" (CR 606 + CR 114 + CR 603.1 + CR 701.21)</b>: mints an
///   <see cref="Emblem"/> in the controller's command zone carrying a
///   <see cref="CardDrawnEvent"/> trigger (gated to the emblem's controller)
///   that declares a "target permanent an opponent controls"
///   <see cref="TargetRequest"/>. The exile effect reads the CHOSEN permanent
///   off <c>rc.ChosenTargets[0][0]</c>, falling back to the first opponent-
///   controlled permanent read off the live <see cref="ResolutionContext"/>
///   (<see cref="ContextOpponents"/>) when no target was collected — never a
///   build-time resolver. Structural-only (no trigger) without the
///   <paramref name="triggers"/> service.
///
/// ## v1 boundary
/// - <b>Emblem trigger agent-prompt</b>: the emblem's draw trigger declares a
///   real <see cref="TargetRequest"/>, but the LIVE priority loop drains
///   pending triggers synchronously
///   (<c>PriorityManager.PutPendingTriggersOnStack</c>) and does not yet collect
///   trigger targets through the agent — a pre-existing engine-wide gap shared
///   by every bound targeted trigger (see <see cref="OracleTriggeredAbilityBinder"/>).
///   Until that drain goes async the emblem exiles the first opponent-controlled
///   permanent read off the live context (deterministic-but-live fallback, NOT
///   inert). The −3 and +1 land choices DO reach the agent today (they ride the
///   already-async loyalty-target path).
/// </summary>
[CardName("Teferi, Hero of Dominaria")]
public static class TeferiHeroOfDominariaFactory
{
    public const string CardName = "Teferi, Hero of Dominaria";
    public const string Slug = "teferi-hero-of-dominaria";
    public const int StartingLoyalty = 4;
    public const int Plus1DrawCount = 1;
    public const int Plus1MaxUntap = 2;
    public const int Minus3LibraryIndex = 2; // "third from the top" (0-based, top-first)
    public const int UltimateLoyaltyCost = -8;

    /// <summary>
    /// Construct Teferi with no trigger service wired — +1 draws but the untap
    /// clause no-ops (no delayed trigger scheduled), and −8 mints a structural-
    /// only emblem. The −3 still resolves against the chosen target. Loyalty
    /// changes always apply. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Planeswalker Create(Player owner) =>
        Create(owner, triggers: null);

    /// <summary>
    /// Construct Teferi, Hero of Dominaria.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">TriggerManager used to register the +1 delayed
    /// end-step untap trigger and the −8 emblem's draw trigger. May be null —
    /// the +1 untap clause never schedules and the emblem is structural-only.</param>
    public static Planeswalker Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary
        // Planeswalker — Teferi, {3}{W}{U}, loyalty 4). The JSON carries no
        // abilities — the three loyalty abilities are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var teferi = (Planeswalker)CardDefinitionFactory.Build(definition, owner);

        AddPlus1(teferi, owner, triggers);
        AddMinus3(teferi, owner);
        AddUltimate(teferi, owner, triggers);

        return teferi;
    }

    // -- +1: Draw a card. At the beginning of the next end step, untap up to
    //    two lands. -------------------------------------------------------------
    // CR 606 (loyalty) + CR 121 (draw) + CR 603.7 (delayed trigger). The "up to
    // two lands" choice is a real TargetRequest collected by the loyalty path;
    // the untap is a one-shot delayed trigger on the next End step (auto-
    // unregistered after firing, CR 603.7c).
    private static void AddPlus1(Planeswalker teferi, Player owner, TriggerManager? triggers)
    {
        // "untap up to two lands" — the activating player chooses which lands
        // (CR 606.3). MinTargets 0 ("up to"), MaxTargets 2. The gatherer offers
        // the lands the controller controls at +1 resolution.
        var landRequest = new TargetRequest(
            Description: "Untap up to two lands you control",
            MinTargets: 0,
            MaxTargets: Plus1MaxUntap,
            LegalCandidates: Array.Empty<object>(),
            Intent: BotIntent.Ramp,
            CandidateGatherer: gameCtx =>
            {
                var controller = teferi.Controller ?? owner;
                return controller.Zones.Battlefield.GetCards()
                    .OfType<Land>()
                    .Cast<object>()
                    .ToList();
            });

        teferi.AddAbility(new LoyaltyAbility(
            teferi,
            +1,
            new[]
            {
                Fx.Inline(
                    $"{CardName} +1: draw a card; schedule next-end-step untap of up to two chosen lands",
                    rc =>
                    {
                        var controller = teferi.Controller ?? owner;
                        Fx.DrawCards(controller, Plus1DrawCount);

                        if (triggers == null) return default;

                        // The lands the agent chose (CR 606.3). Captured now and
                        // untapped at the next end step (CR 603.7). "Up to two".
                        var chosenLands = (rc.ChosenTargets.Count > 0
                                ? rc.ChosenTargets[0]
                                : (IReadOnlyList<object>)Array.Empty<object>())
                            .OfType<Land>()
                            .Take(Plus1MaxUntap)
                            .ToList();
                        if (chosenLands.Count == 0) return default;

                        var untapEffect = Fx.Inline(
                            $"{CardName}: untap up to two lands (CR 603.7)",
                            () =>
                            {
                                foreach (var land in chosenLands)
                                {
                                    if (land.Zone != ZoneType.Battlefield) continue;
                                    if (land.IsTapped) land.Untap();
                                }
                            });

                        // CR 603.7 — "at the beginning of the next end step".
                        // Fires once on the next End-step StepStartedEvent
                        // regardless of whose turn it is (the clause is
                        // unqualified — "the next end step").
                        var delayed = new DelayedTriggeredAbility(
                            source: teferi,
                            controller: controller,
                            condition: new EventTriggerCondition<StepStartedEvent>(
                                (e, _) => e.StepType == StepStateType.End),
                            effects: new[] { untapEffect });

                        teferi.AddAbility(delayed);
                        triggers.RegisterDelayed(delayed);
                        return default;
                    }),
            },
            targetRequests: new[] { landRequest }));
    }

    // -- −3: Put target nonland permanent into its owner's library third from
    //    the top. -----------------------------------------------------------------
    // CR 606 (loyalty) + CR 401 (library order) + CR 110.4a (nonland). "Third
    // from the top" = index 2 in a top-first library. The target is a real
    // TargetRequest chosen by the activating player's agent; the effect reads
    // the CHOSEN permanent off rc.ChosenTargets.
    private static void AddMinus3(Planeswalker teferi, Player owner)
    {
        var nonlandRequest = new TargetRequest(
            Description: "Target nonland permanent",
            MinTargets: 1,
            MaxTargets: 1,
            LegalCandidates: Array.Empty<object>(),
            Intent: BotIntent.Removal,
            CandidateGatherer: gameCtx => gameCtx.AllPlayers
                .SelectMany(p => p.Zones.Battlefield.GetCards())
                .OfType<Permanent>()
                .Where(p => !p.HasType(CardType.Land)) // "nonland permanent"
                .Cast<object>()
                .ToList());

        teferi.AddAbility(new LoyaltyAbility(
            teferi,
            -3,
            new[]
            {
                Fx.Inline(
                    "Put target nonland permanent into its owner's library third from the top",
                    rc =>
                    {
                        var target = (rc.ChosenTargets.Count > 0 && rc.ChosenTargets[0].Count > 0
                            ? rc.ChosenTargets[0][0] as Permanent
                            : null);
                        if (target == null) return default;
                        if (target.HasType(CardType.Land)) return default; // "nonland permanent"
                        if (target.Zone != ZoneType.Battlefield) return default;

                        var holder = target.Controller ?? target.Owner;
                        holder?.Zones.Battlefield.RemoveCard(target);

                        // "its owner's library" (CR 401). Insert third from the top.
                        var libOwner = target.Owner ?? owner;
                        libOwner.Zones.Library.InsertCardAt(Minus3LibraryIndex, target);
                        target.SetZone(ZoneType.Library);
                        return default;
                    }),
            },
            targetRequests: new[] { nonlandRequest }));
    }

    // -- −8: You get an emblem with "Whenever you draw a card, exile target
    //    permanent an opponent controls." ---------------------------------------
    // CR 606 (loyalty) + CR 114 (emblem) + CR 603.1 (whenever-trigger) +
    // CR 701.21 (exile). The emblem's draw trigger declares a real
    // TargetRequest; its effect reads the CHOSEN permanent off
    // rc.ChosenTargets, falling back to the first opponent-controlled permanent
    // read off the LIVE context (ContextOpponents) — never a build-time
    // resolver. Structural-only on the no-triggers path (matches Liliana −7).
    private static void AddUltimate(Planeswalker teferi, Player owner, TriggerManager? triggers)
    {
        teferi.AddAbility(new LoyaltyAbility(teferi, UltimateLoyaltyCost, () =>
        {
            var controller = teferi.Controller ?? owner;

            // Emblem snapshots its Abilities at construction (CR 114), so the
            // trigger must exist before the emblem is minted.
            var emblemAbilities = new List<IAbility>();

            if (triggers != null)
            {
                // "exile target permanent an opponent controls" — a real
                // TargetRequest gathered from the live context at resolution.
                var exileRequest = new TargetRequest(
                    Description: "Exile target permanent an opponent controls",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    CandidateGatherer: gameCtx => ContextOpponentsBattlefield(gameCtx, controller)
                        .Cast<object>()
                        .ToList());

                var exileEffect = Fx.Inline(
                    $"{CardName} emblem: exile target permanent an opponent controls",
                    rc =>
                    {
                        var target = (rc.ChosenTargets.Count > 0 && rc.ChosenTargets[0].Count > 0
                                ? rc.ChosenTargets[0][0] as Permanent
                                : null)
                            // Live-context fallback (NOT a build-time resolver):
                            // the prod trigger-drain doesn't yet prompt the agent,
                            // so read the first opponent-controlled permanent off
                            // the resolution context (ContextOpponents).
                            ?? ContextOpponents.Of(rc, controller)
                                .SelectMany(p => p.Zones.Battlefield.GetCards())
                                .OfType<Permanent>()
                                .FirstOrDefault();
                        if (target == null) return default;
                        if (target.Zone != ZoneType.Battlefield) return default;

                        var holder = target.Controller ?? target.Owner;
                        holder?.Zones.Battlefield.RemoveCard(target);
                        var exileOwner = target.Owner ?? holder;
                        exileOwner?.Zones.Exile.AddCard(target);
                        target.SetZone(ZoneType.Exile);
                        return default;
                    });

                // Source is Teferi (a card) but the ability is registered
                // explicitly with the manager, so its activeZones gate is
                // irrelevant — the emblem lives in the command zone for the rest
                // of the game (CR 114) and the trigger fires while registered.
                var drawAbility = new TriggeredAbility(
                    source: teferi,
                    controller: controller,
                    condition: new EventTriggerCondition<CardDrawnEvent>(
                        (e, _) => ReferenceEquals(e.Player, controller)),
                    effects: new IEffect[] { exileEffect },
                    targetRequests: new[] { exileRequest });

                emblemAbilities.Add(drawAbility);
                triggers.RegisterTriggeredAbility(drawAbility);
            }

            // Mint the emblem (CR 114) with its abilities now populated.
            var emblem = new Emblem(
                controller: controller,
                sourceName: $"{CardName} — draw-exile emblem",
                abilities: emblemAbilities);
            controller.AddEmblem(emblem);
        }));
    }

    /// <summary>
    /// Every battlefield permanent an opponent of <paramref name="controller"/>
    /// controls, read from the live <see cref="Game.GameContext"/>. Used by the
    /// emblem's exile <see cref="TargetRequest"/> gatherer.
    /// </summary>
    private static IEnumerable<Permanent> ContextOpponentsBattlefield(
        Game.GameContext gameCtx, Player controller)
    {
        foreach (var p in gameCtx.AllPlayers)
        {
            if (p == null) continue;
            if (ReferenceEquals(p, controller)) continue;
            if (p.HasLost) continue;
            foreach (var card in p.Zones.Battlefield.GetCards())
            {
                if (card is Permanent perm) yield return perm;
            }
        }
    }
}
