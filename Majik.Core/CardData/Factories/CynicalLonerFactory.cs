using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cynical Loner (Aetherdrift, {1}{B}).
///
/// Creature — Human Survivor 3/1. Oracle text (verified against Scryfall
/// 2026-06-24):
///   "This creature can't be blocked by Glimmers.
///    Survival — At the beginning of your second main phase, if this creature
///    is tapped, you may search your library for a card, put it into your
///    graveyard, then shuffle."
///
/// The card's base shape (name, Creature, Human + Survivor subtypes, {1}{B},
/// 3/1) is materialised from the embedded JSON definition
/// (<c>cynical-loner.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two text abilities are
/// layered on here because the JSON <c>AbilityDefinition</c> schema expresses
/// neither a typed "can't be blocked by a subtype" evasion static nor a
/// second-main-phase intervening-if Survival trigger.
///
/// ## Implemented (v1)
/// - 3/1 Creature — Human Survivor at printed cost {1}{B}, owner / controller
///   wired.
/// - <b>"This creature can't be blocked by Glimmers" (CR 509.1b / 702.x
///   evasion)</b> — a <see cref="CantBeBlockedExceptByEffect"/> registered on
///   the supplied <see cref="ContinuousEffectsService"/>, with the source's
///   <see cref="Creature.ActiveEffects"/> wired to that same service so
///   <see cref="Majik.Core.Combat.BlockLegality.CanBlock"/> consults it. The
///   allowed-blocker predicate accepts a would-be blocker iff it is NOT a
///   Glimmer (<see cref="CardSubtype.Glimmer"/>) — the complement of "can't be
///   blocked by Glimmers". Same effect shape as
///   <see cref="SungoldSentinelFactory"/> / <see cref="SteelLeafChampionFactory"/>.
///   Without a live service (the shape-only single-arg overload) the evasion is
///   structural only — same posture as every continuous-effect-dependent
///   factory.
/// - <b>Survival — second-main-phase tutor-to-graveyard (CR 603.1 / 603.4 /
///   701.18 / 701.20a)</b> — a <see cref="TriggeredAbility"/> on
///   <see cref="Triggers.OnStepBegin"/> with
///   <see cref="StepStateType.PostCombatMain"/> (the controller's own second /
///   post-combat main phase). "Survival" is reminder-text flavour for the
///   CR 603.4 <b>intervening-if</b> "if this creature is tapped": the trigger's
///   <see cref="TriggeredAbility.InterveningIf"/> re-checks
///   <see cref="Permanent.IsTapped"/> both when it would be put on the stack
///   AND at resolution, so a creature untapped in response does nothing. On
///   resolution (and only when the controller's agent takes the "you may"):
///   search the controller's library for ANY card, move it library →
///   graveyard, then shuffle (CR 701.18 / 701.20a). Empty library / decline =
///   no card moved; the library is still shuffled (CR 701.20a).
///
/// ## Single-arg dispatcher path
/// The <see cref="Create(Player)"/> overload attaches the evasion effect and
/// the Survival trigger structurally (correct card shape for factory-shape /
/// dispatch tests). The evasion effect is NOT registered against a live
/// <see cref="ContinuousEffectsService"/> (so blockers aren't actually
/// restricted) and the trigger is NOT registered with a
/// <see cref="TriggerManager"/>. Production callers use the full overload.
/// </summary>
[CardName("Cynical Loner")]
public static class CynicalLonerFactory
{
    public const string CardName = "Cynical Loner";
    public const string Slug = "cynical-loner";
    public const int Power = 3;
    public const int Toughness = 1;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Cynical Loner with no live wiring. The "can't be blocked by
    /// Glimmers" effect and the Survival trigger attach structurally; the
    /// evasion effect is NOT registered against a live
    /// <see cref="ContinuousEffectsService"/> (no block restriction is
    /// enforced) and the trigger is NOT enrolled with a
    /// <see cref="TriggerManager"/>. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, continuousEffects: null, triggers: null, zoneService: null);

    /// <summary>
    /// Construct a fully-wired Cynical Loner.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service the "can't be blocked by
    /// Glimmers" effect is registered against, and which the source's
    /// <see cref="Creature.ActiveEffects"/> is wired to so
    /// <see cref="Majik.Core.Combat.BlockLegality.CanBlock"/> reads the
    /// restriction (CR 509.1b). May be null — the evasion is then structural
    /// only.</param>
    /// <param name="triggers">Trigger manager for the Survival trigger. May be
    /// null — the trigger attaches structurally but isn't enrolled.</param>
    /// <param name="zoneService">Threaded into the library → graveyard move so a
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> fires (graveyard-arrival
    /// triggers see the milled card — CR 603.6a). May be null (raw-zone move
    /// fallback).</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        TriggerManager? triggers,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Human +
        // Survivor subtypes, {1}{B}, 3/1). The JSON carries no abilities — the
        // two text abilities are layered on below.
        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // "This creature can't be blocked by Glimmers." — CR 509.1b.
        // BlockLegality.CanBlock walks the ATTACKER's Creature.ActiveEffects
        // for every active CantBeBlockedExceptByEffect, so the source must read
        // the same service the effect is registered against. The allowed-blocker
        // predicate accepts a would-be blocker iff it is NOT a Glimmer (the
        // complement of "can't be blocked by Glimmers").
        // ----------------------------------------------------------------
        if (continuousEffects != null)
        {
            card.ActiveEffects = continuousEffects;
            continuousEffects.Register(new CantBeBlockedExceptByEffect(
                source: card,
                predicate: blocker => !BlockerIsGlimmer(blocker)));
        }

        // ----------------------------------------------------------------
        // Survival — second-main-phase tutor-to-graveyard. CR 603.1 (turn-based
        // trigger) / CR 603.4 (intervening-if) / CR 701.18 (search) / CR 701.20a
        // (shuffle).
        //   "Survival — At the beginning of your second main phase, if this
        //    creature is tapped, you may search your library for a card, put it
        //    into your graveyard, then shuffle."
        // "Survival" is reminder-text flavour for the intervening-if. The second
        // main phase is StepStateType.PostCombatMain.
        // ----------------------------------------------------------------
        var survivalEffect = new Effect(
            $"{CardName}: Survival — may search library for a card → graveyard, then shuffle",
            ctx => ResolveSurvivalAsync(card, owner, zoneService, ctx));

        var survivalTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnStepBegin(owner, StepStateType.PostCombatMain),
            effects: new IEffect[] { survivalEffect },
            // CR 603.4 — intervening-if "if this creature is tapped", re-checked
            // both when the trigger would be put on the stack (CanBePutOnStack)
            // and on resolution. A creature untapped in response does nothing.
            interveningIf: () => card.Zone == ZoneType.Battlefield && card.IsTapped,
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(survivalTrigger);
        triggers?.RegisterTriggeredAbility(survivalTrigger);

        return card;
    }

    /// <summary>
    /// CR 603.5 — the optional Survival instruction at resolution. Re-checks the
    /// intervening-if (CR 603.4) defensively, prompts the controller's agent for
    /// the "you may", and on a yes searches the controller's library for ANY
    /// card, moves it library → graveyard, then shuffles (CR 701.18 / 701.20a).
    /// A decline / no agent / empty library moves no card; the library is still
    /// shuffled. Public so tests / bots can drive resolution directly.
    /// </summary>
    public static async ValueTask ResolveSurvivalAsync(
        Creature card, Player owner, ZoneService? zoneService, ResolutionContext ctx)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(owner);

        // CR 603.4 — the intervening-if is re-checked on resolution too.
        if (card.Zone != ZoneType.Battlefield || !card.IsTapped) return;

        var controller = card.Controller ?? owner;
        var effectiveZones = zoneService ?? ZoneServiceRegistry.Get(controller);

        var agent = ctx.Agent ?? Majik.Core.Players.Agents.AgentRegistry.Get(controller);

        // CR 117.x / 605.1 — "you may". With no decision-maker the optional
        // action defaults to being declined, but the library is still shuffled
        // only if the search is actually performed. Here a declined "may" does
        // NOT search and does NOT shuffle (no search effect happened at all —
        // CR 701.18 / 701.20a only fire when the search instruction is taken).
        if (agent != null)
        {
            var wantsTo = await agent
                .ChooseYesNoAsync(ctx.Game, "Survival — search your library for a card to mill?", CardName, ctx.Ct)
                .ConfigureAwait(false);
            if (!wantsTo) return;
        }

        // CR 701.18 — search the controller's library for ANY card; move it to
        // the graveyard. CR 701.20a — shuffle whether or not a card was found.
        var candidates = controller.Zones.Library.GetCards().ToList();
        var pick = await Majik.Core.Zones.LibrarySearch.PromptOnlyAsync(
            ctx, controller, candidates, "card").ConfigureAwait(false);

        if (pick != null)
        {
            if (effectiveZones != null)
            {
                effectiveZones.MoveCard(pick, ZoneType.Library, ZoneType.Graveyard, controller);
            }
            else
            {
                controller.Zones.Library.RemoveCard(pick);
                controller.Zones.Graveyard.AddCard(pick);
                pick.SetZone(ZoneType.Graveyard);
            }
        }

        // CR 701.20a — shuffle after the search resolves, even if nothing moved.
        Majik.Core.Zones.LibraryShuffle.ShuffleLibrary(controller, "cynical-loner-survival");
    }

    /// <summary>
    /// True iff <paramref name="blocker"/> is (layer-computed) a Glimmer
    /// (CR 205.3 — subtype check). "Can't be blocked by Glimmers" is the
    /// complement: a legal blocker is one that is NOT a Glimmer.
    /// </summary>
    private static bool BlockerIsGlimmer(ICard blocker) =>
        blocker is Permanent p
            ? p.GetEffectiveSubtypes().Contains(CardSubtype.Glimmer)
            : blocker.HasSubtype(CardSubtype.Glimmer);
}
