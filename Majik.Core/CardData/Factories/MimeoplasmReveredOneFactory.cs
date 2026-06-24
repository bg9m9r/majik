using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mimeoplasm, Revered One (Commander Masters,
/// <c>{X}{B}{G}{U}</c>). Legendary Creature — Ooze. Printed 0/0.
///
/// Oracle text (Scryfall-verified 2026-06-24):
///   "As Mimeoplasm enters, exile up to X creature cards from your graveyard.
///    It enters with three +1/+1 counters on it for each creature card exiled
///    this way.
///    {2}: Mimeoplasm becomes a copy of target creature card exiled with it,
///    except it's 0/0 and has this ability."
///
/// The base shape (name, Legendary, Creature, Ooze subtype, <c>{X}{B}{G}{U}</c>,
/// 0/0) is materialised from the embedded JSON definition
/// (<c>mimeoplasm-revered-one.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The as-enters exile-and-count
/// clause and the <c>{2}</c> copy ability are layered on here — neither is
/// expressible in the JSON <c>AbilityDefinition</c> schema (same posture as
/// <see cref="TheGooseMotherFactory"/> / <see cref="ThespiansStageFactory"/>).
///
/// ## Implemented (v1)
/// <list type="bullet">
///   <item><b>As-enters exile + counters (CR 614.1c / CR 122.1g)</b> — "As
///   Mimeoplasm enters, exile up to X creature cards from your graveyard. It
///   enters with three +1/+1 counters on it for each creature card exiled this
///   way." Modeled as a self ETB <see cref="TriggeredAbility"/> over
///   <see cref="Triggers.OnEnterBattlefieldSelf"/> whose resolution reads the
///   cast-time X off <see cref="Card.PendingCastX"/> (stamped by
///   <see cref="Majik.Core.Game.SpellCastFlow"/> after <c>ChooseXAsync</c>),
///   prompts the controller's <see cref="IPlayerAgent"/> to exile up to X
///   creature cards from their own graveyard (a
///   <see cref="ChoiceKind.PickN"/> 0..X choice), links each exiled card to
///   this Mimeoplasm via <see cref="ICard.SetExiledWith"/> (so the copy ability
///   below can find "a creature card exiled with it" — CR 400.7), and places
///   <c>3 × count</c> +1/+1 counters via <see cref="CountersService.Add"/> (so
///   Hardened Scales / Doubling Season rewrite the amount — CR 614). The
///   cast-time X stamp is cleared once consumed so a non-cast re-entry
///   (blink / copy) doesn't reuse it.
///   <para>
///   <b>As-enters vs ETB-trigger boundary</b>: the printed clause is an
///   "as enters" replacement (CR 614.1c) — the exile + counters happen as part
///   of entering, before any player gets priority. This engine's
///   <see cref="Triggers.OnEnterBattlefieldSelf"/> resolution runs in the same
///   ETB window before priority is passed, so the transient 0/0 (no counters
///   yet) never reaches a state-based-action check (CR 704.5f) — observationally
///   equivalent to the true as-enters timing. Same modeling boundary as the
///   counters-on-enter clause of other variable-X enter-with-counters creatures
///   on the no-binder factory path.
///   </para></item>
///
///   <item><b><c>{2}</c>: becomes a copy of a creature card exiled with it,
///   except 0/0 (CR 707.2 / CR 613.2)</b> — an <see cref="ActivatedAbility"/>
///   (CR 602, uses the stack) whose target is "a creature card exiled with
///   this permanent". The candidate pool is gathered live via
///   <see cref="TargetRequest.CandidateGatherer"/>: exile-zone cards whose
///   <see cref="ICard.ExiledWith"/> equals this Mimeoplasm's
///   <see cref="ICard.InstanceId"/> AND which are creature cards. Resolution
///   registers a permanent <see cref="CopyCharacteristicsEffect"/>
///   (<c>expiresAtEndOfTurn: false</c> — the copy lasts while Mimeoplasm stays
///   on the battlefield) seeding the full copiable characteristics (name,
///   types, subtypes, supertypes, colour, P/T, keyword markers) of the chosen
///   exiled creature card, then layers a <see cref="BecomesPTEffect"/>(0, 0)
///   (Layer 7b set-base, CR 613.7b) so the copy is 0/0 regardless of the copied
///   creature's printed P/T. The +1/+1 counters already on Mimeoplasm then add
///   on top at Layer 7d (CR 613.7d), giving the correct final P/T
///   (0/0 + 3·count counters). "Has this ability" needs no special handling —
///   <see cref="CopyCharacteristicsEffect"/> rewrites only the characteristics
///   row, never <see cref="Card.Abilities"/>, so the runtime copy-ability
///   instance survives the copy and Mimeoplasm can copy again.</item>
/// </list>
///
/// ## Wiring overloads
/// <list type="bullet">
///   <item><see cref="Create(Player)"/> — shape only; the ETB trigger is
///   attached for shape / dispatcher tests but not registered with any
///   <see cref="TriggerManager"/>, and the copy ability resolves to a no-op
///   (no <see cref="ContinuousEffectsService"/> to register the copy effect
///   on). This is the overload <see cref="NamedCardFactory"/> dispatches
///   to.</item>
///   <item><see cref="Create(Player, ContinuousEffectsService?, TriggerManager?, ReplacementBus?, IEventBus?)"/>
///   — fully wired; the ETB trigger registers, the as-enters counters route
///   through the replacement + event buses, and the copy ability registers its
///   characteristics + 0/0 effects on the continuous-effects service.</item>
/// </list>
/// </summary>
[CardName("Mimeoplasm, Revered One")]
public static class MimeoplasmReveredOneFactory
{
    public const string CardName = "Mimeoplasm, Revered One";
    public const string Slug = "mimeoplasm-revered-one";

    /// <summary>The copy ability's <c>{2}</c> activation cost.</summary>
    public const string CopyAbilityCost = "{2}";

    /// <summary>+1/+1 counters per creature card exiled as Mimeoplasm enters.</summary>
    public const int CountersPerExiled = 3;

    /// <summary>
    /// Construct Mimeoplasm with no live wiring. The ETB trigger + copy ability
    /// shapes are attached for shape / dispatcher observability but not
    /// registered with any <see cref="TriggerManager"/>; the copy ability
    /// resolves to a no-op (no <see cref="ContinuousEffectsService"/>). This is
    /// the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, effects: null, triggers: null, replacements: null, eventBus: null);

    /// <summary>
    /// Construct Mimeoplasm with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service the copy ability's
    /// <see cref="CopyCharacteristicsEffect"/> + <see cref="BecomesPTEffect"/>
    /// register on. May be null — the copy ability still resolves but records
    /// no effect.</param>
    /// <param name="triggers">When supplied, the ETB trigger registers so
    /// entering the battlefield lands it on the stack automatically (CR
    /// 603.2).</param>
    /// <param name="replacements">Routed through
    /// <see cref="CountersService.Add"/> for the as-enters counters (Hardened
    /// Scales / Doubling Season bumps — CR 614).</param>
    /// <param name="eventBus">Publishes the <see cref="CounterAddedEvent"/> for
    /// the as-enters counters so "+1/+1 counters were put on …" triggers can
    /// chain.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        TriggerManager? triggers,
        ReplacementBus? replacements,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary,
        // Creature, Ooze subtype, {X}{B}{G}{U}, 0/0). The JSON carries no
        // abilities — the as-enters clause + copy ability are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner, replacements);

        card.SetController(owner);

        // ----------------------------------------------------------------
        // As-enters exile + counters — CR 614.1c / CR 122.1g.
        //   "As Mimeoplasm enters, exile up to X creature cards from your
        //    graveyard. It enters with three +1/+1 counters on it for each
        //    creature card exiled this way."
        // Modeled as a self ETB trigger whose resolution runs in the ETB
        // window before priority (so the transient 0/0 never hits an SBA check
        // — CR 704.5f). Reads cast-time X off PendingCastX.
        // ----------------------------------------------------------------
        var enterEffect = new Effect(
            $"{CardName}: exile up to X creature cards from your graveyard; " +
            "enter with three +1/+1 counters per card exiled",
            async ctx => await ResolveEnterAsync(
                card, owner, replacements, eventBus, ctx).ConfigureAwait(false));

        var enterTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { enterEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(enterTrigger);
        triggers?.RegisterTriggeredAbility(enterTrigger);

        // ----------------------------------------------------------------
        // {2}: Mimeoplasm becomes a copy of target creature card exiled with
        // it, except it's 0/0 and has this ability. CR 602 / CR 707.2.
        // ----------------------------------------------------------------
        ActivatedAbility? copyAbility = null;
        var copyEffect = new Effect(
            $"{CardName}: becomes a copy of target creature card exiled with it, " +
            "except it's 0/0 and has this ability",
            () =>
            {
                if (copyAbility == null) return;
                if (effects == null) return; // shape-only path

                // CR 608.2b — read the chosen target; copy nothing if it's
                // gone / illegal. Must be a creature card still linked to this
                // Mimeoplasm in exile.
                if (copyAbility.ChosenTargets.Count == 0) return;
                if (copyAbility.ChosenTargets[0].Count == 0) return;
                if (copyAbility.ChosenTargets[0][0] is not Creature source) return;
                if (!IsExiledWithThis(card, source)) return;

                // CR 707.2 / 613.2 Layer 1 — becomes a copy in place, PERMANENT
                // (expiresAtEndOfTurn: false). "Has this ability" falls out for
                // free: the copy effect rewrites only the characteristics row,
                // never card.Abilities, so the copy ability instance survives.
                effects.Register(new CopyCharacteristicsEffect(
                    card, source, expiresAtEndOfTurn: false));

                // "except it's 0/0" — CR 613.7b Layer-7b set-base wipes the
                // copied P/T to 0/0. The +1/+1 counters already on Mimeoplasm
                // then add on top at Layer 7d, so the final P/T is
                // 0/0 + 3·count counters.
                effects.Register(new BecomesPTEffect(card, 0, 0));
            });

        copyAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(CopyAbilityCost) },
            effects: new IEffect[] { copyEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature card exiled with this permanent",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.None,
                    CandidateGatherer: _ => ExiledWithThis(card)),
            });

        card.AddAbility(copyAbility);

        return card;
    }

    // ------------------------------------------------------------------------
    // Resolution helpers
    // ------------------------------------------------------------------------

    /// <summary>
    /// Resolve the as-enters clause: read cast-time X, prompt to exile up to X
    /// creature cards from the controller's graveyard, link each to this
    /// Mimeoplasm, and place 3 counters per card exiled.
    /// </summary>
    private static async ValueTask ResolveEnterAsync(
        Creature card, Player owner,
        ReplacementBus? replacements, IEventBus? eventBus, ResolutionContext ctx)
    {
        if (card.Zone != ZoneType.Battlefield) return;

        var controller = card.Controller ?? owner;

        // Cast-time X (CR 614.1c reads the value chosen as the spell was cast).
        // Snapshot then clear so a non-cast re-entry doesn't reuse a stale X.
        var x = card.PendingCastX ?? 0;
        card.ClearPendingCastX();

        var chosen = await ChooseCreatureCardsToExileAsync(ctx, controller, x)
            .ConfigureAwait(false);

        // Exile each chosen creature card and link it to this Mimeoplasm so the
        // {2} copy ability's candidate gatherer can find it (CR 400.7 — the
        // exile zone's "exiled with" relationship is game-tracked bookkeeping).
        var exiledCount = 0;
        foreach (var pick in chosen)
        {
            if (pick.Zone != ZoneType.Graveyard) continue;
            var pickOwner = pick.Owner ?? controller;
            pickOwner.Zones.Graveyard.RemoveCard(pick);
            pickOwner.Zones.Exile.AddCard(pick);
            pick.SetZone(ZoneType.Exile);
            pick.SetExiledWith(card.InstanceId);
            exiledCount++;
        }

        // "Three +1/+1 counters for each creature card exiled this way."
        // Route through CountersService.Add so Hardened Scales / Doubling
        // Season rewrite the amount (CR 614) and the CounterAddedEvent fires.
        if (exiledCount > 0)
        {
            CountersService.Add(
                card, CounterType.PlusOnePlusOne,
                CountersPerExiled * exiledCount, replacements, eventBus);
        }
    }

    /// <summary>
    /// Prompt the controller to exile up to <paramref name="max"/> creature
    /// cards from their own graveyard ("up to X" — a 0..X choice). No agent /
    /// decline ⇒ exile NONE (the safe default; the upside of exiling is
    /// situational — the copy ability is optional and costs mana, so the
    /// no-agent posture mirrors the conservative "decline optional" default).
    /// </summary>
    private static async ValueTask<IReadOnlyList<Creature>> ChooseCreatureCardsToExileAsync(
        ResolutionContext ctx, Player controller, int max)
    {
        if (max <= 0) return Array.Empty<Creature>();

        var pool = controller.Zones.Graveyard.GetCards()
            .OfType<Creature>()
            .ToList();
        if (pool.Count == 0) return Array.Empty<Creature>();

        var cap = Math.Min(max, pool.Count);

        var agent = ctx.Agent ?? AgentRegistry.Get(controller);
        if (agent == null) return Array.Empty<Creature>();

        var req = new ChoiceRequest(
            Kind: ChoiceKind.PickN,
            Description: $"{CardName}: exile up to {max} creature cards from your graveyard",
            Min: 0,
            Max: cap,
            Candidates: pool.Cast<object>().ToList(),
            Intent: BotIntent.None,
            Optional: true);

        var picked = await agent.ChooseAsync(ctx.Game!, req, ctx.Ct).ConfigureAwait(false);

        // Sanitise: distinct creature cards drawn from the graveyard pool only,
        // capped at X.
        var allowed = new HashSet<Creature>(pool);
        var seen = new HashSet<Creature>();
        var result = new List<Creature>(cap);
        foreach (var o in picked)
        {
            if (result.Count >= cap) break;
            if (o is Creature c && allowed.Contains(c) && seen.Add(c)) result.Add(c);
        }
        return result;
    }

    /// <summary>
    /// CR 400.7 — the creature cards currently exiled with this Mimeoplasm
    /// (exile-zone cards whose <see cref="ICard.ExiledWith"/> matches this
    /// permanent's <see cref="ICard.InstanceId"/>). Exposed for the copy
    /// ability's target-candidate gatherer and tests.
    /// </summary>
    public static IReadOnlyList<object> ExiledWithThis(Creature mimeoplasm)
    {
        ArgumentNullException.ThrowIfNull(mimeoplasm);
        var owner = mimeoplasm.Owner;
        if (owner == null) return Array.Empty<object>();

        // The exiled cards may be owned by any player — scan every reachable
        // exile zone. In the no-Game shape posture only the owner's exile is
        // visible (no Game reference is threaded through the factory); a card
        // Mimeoplasm exiled is removed from its OWNER's graveyard into that
        // owner's exile, so the owner's exile holds them in the common case.
        return owner.Zones.Exile.GetCards()
            .OfType<Creature>()
            .Where(c => IsExiledWithThis(mimeoplasm, c))
            .Cast<object>()
            .ToList();
    }

    /// <summary>
    /// True iff <paramref name="candidate"/> is a creature card in exile linked
    /// to <paramref name="mimeoplasm"/> via the exiled-with relationship.
    /// </summary>
    private static bool IsExiledWithThis(Creature mimeoplasm, Creature candidate) =>
        candidate.Zone == ZoneType.Exile
        && candidate.ExiledWith == mimeoplasm.InstanceId;
}
