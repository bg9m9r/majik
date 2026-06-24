using Majik.Core.Abilities;
using Majik.Core.CardData.Classes;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Innkeeper's Talent (Bloomburrow, {1}{G}).
///
/// Enchantment — Class {1}{G}. Oracle text (verified against Scryfall):
///   "(Gain the next level as a sorcery to add its ability.)
///    At the beginning of combat on your turn, put a +1/+1 counter on target
///      creature you control.
///    {G}: Level 2
///    Permanents you control with counters on them have ward {1}.
///    {3}{G}: Level 3
///    If you would put one or more counters on a permanent or player, put twice
///      that many of each of those kinds of counters on that permanent or
///      player instead."
///
/// ## Shape source
/// Card identity (name, {1}{G}, Enchantment — Class, green) is loaded from
/// <c>Majik.Core/CardData/Cards/innkeepers-talent.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The <see cref="ClassState"/> binder +
/// the three level abilities are wired in code below.
///
/// ## Implementation (full Class leveling — CR 716)
/// Mirrors <see cref="BanditsTalentFactory"/> / <see cref="CaretakersTalentFactory"/>
/// (the Enchantment — Class shell + <see cref="ClassState"/> side-table +
/// sorcery-speed level-up activated abilities), with Innkeeper's Talent's three
/// abilities:
///
/// - <b>Level 1 — begin-combat +1/+1 counter on target creature you control</b>
///   (CR 508.1 begin-combat trigger): a <see cref="TriggeredAbility"/> over
///   <see cref="Triggers.OnStepBegin"/>(<see cref="StepStateType.BeginningOfCombat"/>)
///   restricted to the controller's own turn, carrying a 1..1
///   "target creature you control" <see cref="TargetRequest"/> — the same
///   shape as <see cref="RestlessBivouacFactory"/>'s targeted-counter trigger.
///   On resolution it rechecks target legality (CR 608.2b — still a Creature
///   on the battlefield this player controls) and places one +1/+1 counter via
///   <see cref="CountersService.Add"/> so the Level-3 doubler (and other
///   counter replacements) can rewrite the amount (CR 614). Level-1 abilities
///   are unconditional (a Class enters at level 1 with its level-1 ability
///   active, CR 716.2).
///
/// - <b>Level 2 — conditional ward grant</b> (CR 702.21 / 613.1f):
///   "Permanents you control with counters on them have ward {1}." A
///   <see cref="GrantAbilityToGroupLifecycle"/> Layer-6 ability-adding static
///   that materialises a "Ward" <see cref="KeywordAbility"/> marker (arg: {1})
///   on every permanent the controller controls that has at least one counter
///   (<see cref="CounterCollection.HasAny"/>), gated on
///   <see cref="ClassState.CurrentLevel"/> &gt;= 2. Live membership recomputes
///   as permanents gain/lose counters or enter/leave (CR 611.2c). This is the
///   counter-filtered analogue of <see cref="SvyelunOfSeaAndSkyFactory"/>'s
///   "Other Merfolk you control have ward {1}".
///
///   Ward enforcement posture: as on every printed-ward / ward-grant card in
///   this engine (Svyelun, Kappa Cannoneer, Aboleth Spawn), ward is a
///   keyword-surface marker — the grant flips
///   <see cref="Permanent.HasEffectiveKeyword"/>("Ward"); the spell-resolution
///   Ward TRIGGER (force the targeting opponent to pay {1}) is the shared
///   deferral across the ward family, NOT a per-card gap.
///
/// - <b>Level 3 — counter doubling on permanents AND players</b> (CR 614 /
///   CR 121.2): "If you would put one or more counters on a permanent or
///   player, put twice that many of each of those kinds of counters on that
///   permanent or player instead." Two CR 614 replacements registered against
///   the supplied <see cref="ReplacementBus"/>, gated on level &gt;= 3:
///     1. <see cref="CounterAddIntent"/> doubler — the permanent half. ANY
///        counter kind (contrast <see cref="BranchingEvolutionFactory"/>'s
///        +1/+1-only scope; Innkeeper's "each of those kinds" doubles all),
///        like the counter half of <see cref="DoublingSeasonFactory"/>.
///     2. <see cref="PlayerCounterAddIntent"/> doubler — the player half
///        (poison / energy / experience). Doubling Season is permanent-only;
///        Innkeeper's "or player" clause extends the same multiplier to player
///        counters routed through <see cref="PlayerCountersService.Add"/>.
///
///   Note the printed clause is "If YOU would put …" — it scopes to placements
///   the Class's controller causes, not the recipient's controller. Both
///   replacements gate on this Class being on the battlefield at level 3.
///
/// ## Deferred (v1 gaps — shared with the Class / ward / counter families)
/// - <b>Ward {1} spell-resolution enforcement</b>: keyword-surface marker only;
///   forcing the targeting opponent to pay {1} awaits the shared Ward-trigger
///   primitive (same deferral as Svyelun / Kappa Cannoneer).
/// - <b>CR 616 replacement ordering</b>: when the Level-3 doubler overlaps
///   other counter replacements (Hardened Scales, Doubling Season) the affected
///   player chooses the order (CR 616.1); the bus applies in registration order
///   today — the same affected-player ordering prompt gap noted on Doubling
///   Season / Branching Evolution.
/// </summary>
[CardName("Innkeeper's Talent")]
public static class InnkeepersTalentFactory
{
    public const string CardName = "Innkeeper's Talent";
    public const string Slug = "innkeepers-talent";
    public const string PrintedManaCost = "{1}{G}";
    public const string Level2Cost = "{G}";
    public const string Level3Cost = "{3}{G}";

    /// <summary>CR 702.21 — granted Ward cost at level 2: {1}.</summary>
    public const int WardAmount = 1;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Innkeeper's Talent with no live runtime services. The Level-1
    /// begin-combat counter trigger + the two level-up activated abilities are
    /// attached for shape inspection. The Level-2 ward grant needs a
    /// <see cref="ContinuousEffectsService"/> and the Level-3 counter doublers
    /// need a <see cref="ReplacementBus"/> — supply them via the wired overload.
    /// This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, triggers: null, eventBus: null, continuousEffects: null, replacements: null);

    /// <summary>
    /// Construct Innkeeper's Talent with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the Level-1 begin-combat counter
    /// trigger is registered for bus-driven firing.</param>
    /// <param name="eventBus">When supplied, level-up resolutions publish
    /// <see cref="ClassLevelUpEvent"/>, and the Level-2 ward grant re-syncs its
    /// membership on relevant board events.</param>
    /// <param name="continuousEffects">When supplied, the Level-2 ward grant is
    /// registered against the layers service (gated on level &gt;= 2).</param>
    /// <param name="replacements">When supplied, the two Level-3 counter
    /// doublers (permanent + player) are registered (gated on level &gt;= 3).</param>
    public static Enchantment Create(
        Player owner,
        TriggerManager? triggers,
        IEventBus? eventBus = null,
        ContinuousEffectsService? continuousEffects = null,
        ReplacementBus? replacements = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Enchantment)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Class state binder (CR 716). MaxLevel=3, per-level costs {G} / {3}{G}.
        // ----------------------------------------------------------------
        var classState = new ClassState(
            maxLevel: 3,
            levelUpCosts: new[]
            {
                ManaCost.Parse(Level2Cost),
                ManaCost.Parse(Level3Cost),
            });

        if (eventBus != null)
        {
            classState.OnLevelUp = (from, to) =>
                eventBus.Publish(new ClassLevelUpEvent(card, card.Controller ?? owner, from, to));
        }

        card.AttachClassState(classState);

        // ----------------------------------------------------------------
        // Level 1 — "At the beginning of combat on your turn, put a +1/+1
        // counter on target creature you control." (CR 508.1 begin-combat
        // trigger — active from level 1, no level gate.) Restricted to the
        // controller's own turn via Triggers.OnStepBegin(owner, BeginningOfCombat).
        // ----------------------------------------------------------------
        TriggeredAbility? combatTrigger = null;

        var counterEffect = new Effect(
            $"{CardName}: put a +1/+1 counter on target creature you control",
            () =>
            {
                if (combatTrigger == null) return;
                if (combatTrigger.ChosenTargets.Count == 0) return;
                if (combatTrigger.ChosenTargets[0].Count == 0) return;

                if (combatTrigger.ChosenTargets[0][0] is not Permanent target) return;

                // CR 608.2b — resolve-time legality recheck: the chosen target
                // must still be a Creature on the battlefield this trigger's
                // controller controls ("you control").
                if (target.Zone != ZoneType.Battlefield) return;
                if (!target.HasType(CardType.Creature)) return;
                if (!ReferenceEquals(target.Controller, card.Controller ?? owner)) return;

                // Route through CountersService so the Level-3 doubler (and any
                // other CR 614 counter replacement) can rewrite the amount.
                CountersService.Add(
                    target, CounterType.PlusOnePlusOne, 1, replacements, eventBus);
            });

        combatTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnStepBegin(owner, StepStateType.BeginningOfCombat),
            effects: new IEffect[] { counterEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature you control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    CandidateGatherer: _ =>
                    {
                        var ctrl = card.Controller ?? owner;
                        return ctrl.Zones.Battlefield.GetCards()
                            .OfType<Creature>()
                            .Where(c => c.Zone == ZoneType.Battlefield
                                        && ReferenceEquals(c.Controller, ctrl))
                            .Cast<object>()
                            .ToList();
                    },
                    Intent: BotIntent.Buff),
            });

        card.AddAbility(combatTrigger);
        triggers?.RegisterTriggeredAbility(combatTrigger);

        // ----------------------------------------------------------------
        // Level-up activated abilities — CR 716.4 (sequential), sorcery speed
        // (CR 716.3). Mirrors BanditsTalentFactory.
        // ----------------------------------------------------------------
        card.AddAbility(BuildLevelUpAbility(card, owner, classState, targetLevel: 2));
        card.AddAbility(BuildLevelUpAbility(card, owner, classState, targetLevel: 3));

        // ----------------------------------------------------------------
        // Level 2 — "Permanents you control with counters on them have ward
        // {1}." (CR 702.21 / 613.1f.) Granted via GrantAbilityToGroupLifecycle
        // gated on ClassState.CurrentLevel >= 2; membership = the controller's
        // permanents that currently bear at least one counter. Ward is a
        // keyword-surface marker (shared ward-enforcement deferral).
        // ----------------------------------------------------------------
        if (continuousEffects != null)
        {
            var wardGrant = new GrantAbilityToGroupLifecycle(
                source: card,
                layers: continuousEffects,
                eventBus: eventBus,
                scope: p => classState.CurrentLevel >= 2
                            && p.Counters.HasAny
                            && ReferenceEquals(p.Controller, card.Controller),
                abilityFactory: member => new IAbility[]
                {
                    // CR 702.21 — Ward {1} keyword marker (arg: 1).
                    new KeywordAbility(
                        "Ward", member, member.Controller ?? owner, arg: WardAmount),
                },
                membershipProvider: () => ControllerBattlefield(card));
            wardGrant.Attach();
        }

        // ----------------------------------------------------------------
        // Level 3 — "If you would put one or more counters on a permanent or
        // player, put twice that many of each of those kinds of counters on
        // that permanent or player instead." (CR 614 / 121.2.) ANY counter
        // kind; permanent half + player half; gated on level >= 3 AND this
        // Class on the battlefield.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            // Permanent half (CounterAddIntent) — any kind.
            replacements.Register<CounterAddIntent>(new LambdaReplacement<CounterAddIntent>(
                applies: (intent, _) =>
                    card.Zone == ZoneType.Battlefield
                    && classState.CurrentLevel >= 3
                    && intent.Amount > 0,
                replace: (intent, _) => intent with { Amount = intent.Amount * 2 },
                oneShot: false,
                tag: card));

            // Player half (PlayerCounterAddIntent) — poison / energy / etc.
            replacements.Register<PlayerCounterAddIntent>(new LambdaReplacement<PlayerCounterAddIntent>(
                applies: (intent, _) =>
                    card.Zone == ZoneType.Battlefield
                    && classState.CurrentLevel >= 3
                    && intent.Amount > 0,
                replace: (intent, _) => intent with { Amount = intent.Amount * 2 },
                oneShot: false,
                tag: card));
        }

        return card;
    }

    /// <summary>
    /// Build the "Level up to <paramref name="targetLevel"/>" sorcery-speed
    /// activated ability (CR 716.3 / 716.4). Mirrors
    /// <see cref="BanditsTalentFactory"/>.
    /// </summary>
    private static ActivatedAbility BuildLevelUpAbility(
        Enchantment card, Player owner, ClassState classState, int targetLevel)
    {
        var cost = classState.CostFor(targetLevel);

        var effect = new Effect(
            $"{CardName}: level up to {targetLevel}",
            () => classState.LevelUpTo(targetLevel));

        return new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(cost) },
            effects: new IEffect[] { effect },
            sorcerySpeed: true);
    }

    /// <summary>
    /// Live candidate set for the Level-2 ward grant: every permanent on the
    /// Class's controller's battlefield. The <c>scope</c> predicate further
    /// filters to those with counters at level &gt;= 2.
    /// </summary>
    private static IEnumerable<Permanent> ControllerBattlefield(Enchantment source)
    {
        var controller = source.Controller;
        if (controller == null) return Array.Empty<Permanent>();
        return controller.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Where(p => p.Zone == ZoneType.Battlefield);
    }
}
