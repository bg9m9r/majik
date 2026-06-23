using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Niko, Light of Hope (Kaldheim, {2}{W}{U}).
///
/// Legendary Creature — Human Wizard 3/4. Oracle text
/// (verified against Scryfall 2026-06-23):
///   "When Niko enters, create two Shard tokens. (They're enchantments with
///    "{2}, Sacrifice this token: Scry 1, then draw a card.")
///    {2}, {T}: Exile target nonlegendary creature you control. Shards you
///    control become copies of it until the next end step. Return it to the
///    battlefield under its owner's control at the beginning of the next end
///    step."
///
/// ## Implemented (v1)
/// - 3/4 Legendary Creature — Human Wizard at {2}{W}{U}; colour identity WU
///   (CR 105.2 / 202.2c, from the {W}{U} pips). Base shape materialised from
///   the embedded JSON definition (<c>niko-light-of-hope.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory"/>, same posture as
///   <see cref="ShiftingWoodlandFactory"/>.
///
/// - <b>ETB trigger</b> (CR 603.1 / 603.6a): "When Niko enters, create two
///   Shard tokens." Wired via <see cref="Triggers.OnEnterBattlefieldSelf"/>.
///   Each Shard is a bespoke colourless Enchantment token (CR 111.4) carrying
///   the activated ability "{2}, Sacrifice this token: Scry 1, then draw a
///   card." — built by <see cref="CreateShard"/> (the enchantment-token
///   analogue of <see cref="TokenFactory.CreateClue"/>'s {2}+sac+draw shape,
///   with a Scry 1 step (CR 701.20) sequenced before the draw).
///
/// - <b>Activated ability</b> (CR 602): "{2}, {T}: Exile target nonlegendary
///   creature you control. Shards you control become copies of it until the
///   next end step. Return it to the battlefield under its owner's control at
///   the beginning of the next end step." Modelled as an
///   <see cref="ActivatedAbility"/> with a {2} mana cost + a {T} tap cost
///   (CR 602.1) and a 1..1 "target nonlegendary creature you control" request.
///   On resolution (CR 608.2b legality re-check):
///   <list type="number">
///     <item>The chosen creature is exiled (CR 701.21) routed through
///     <see cref="ZoneService"/> when supplied so <see cref="CardMovedEvent"/>
///     fires.</item>
///     <item>Each Shard the activating player controls "becomes a copy of it
///     until the next end step" — a <see cref="CopyCharacteristicsEffect"/>
///     registered with <c>expiresAtEndOfTurn: true</c> (CR 707.2 / 613.2
///     Layer 1; dropped at the cleanup step CR 514.2 by
///     <see cref="ContinuousEffectsService.ExpireEndOfTurn"/>). The copy source
///     is the just-exiled creature (its copiable values are read off the
///     last-known battlefield object; CR 707.3).</item>
///     <item>A delayed end-step return is registered (CR 603.7): at the
///     beginning of the next end step the exiled card returns to the
///     battlefield under its OWNER's control (CR 108.3 / 614), the same delayed
///     pattern as <see cref="FlickerwispFactory"/>.</item>
///   </list>
///
/// ## Targeting (CR 115.1)
/// "target nonlegendary creature you control" — a 1..1
/// <see cref="TargetRequest"/> whose live <c>CandidateGatherer</c> scopes to
/// the activating player's battlefield creatures that are NOT legendary
/// (<see cref="Permanent.HasEffectiveSupertype"/> so a granted Legendary is
/// respected; CR 205.4) and are not Niko itself in the rare case Niko were
/// somehow noncreature — Niko is Legendary so the nonlegendary filter already
/// excludes it.
///
/// ## Scry 1, then draw (Shard token)
/// The Shard's sac ability runs the standard <see cref="ScryAction"/> pipeline
/// for N=1 (CR 701.20) — consulting the resolving agent's
/// <see cref="IPlayerAgent.ChooseScryDecisionAsync"/>, defaulting to
/// "keep on top" when no agent is wired — then draws one card (CR 121.1; empty
/// library flags the SBA loss via
/// <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/>), mirroring the
/// Clue/Charming-Prince shapes.
///
/// ## Deferred (v1 gaps)
/// - <b>Lossy copy snapshot</b>: the Shards' copy of the exiled creature
///   inherits the same v1 lossiness documented on
///   <see cref="CopyCharacteristicsEffect"/> (name / mana cost / supertypes /
///   colour / non-keyword abilities recorded; type line + subtypes + keyword
///   markers + P/T surface through <see cref="ContinuousEffectsService.Compute"/>).
///   The Shard is an Enchantment runtime instance, so a P/T row is not seeded
///   until Layer-1/4 grants Creature type — same known manland-on-a-noncreature
///   gap as Shifting Woodland.
/// - <b>True new-object semantics on return</b> (CR 400.7): the exiled creature
///   is returned as the same runtime instance — tracked alongside the shared
///   flicker "new-object" primitive deferred (Flickerwisp / Cloudshift).
/// </summary>
[CardName("Niko, Light of Hope")]
public static class NikoLightOfHopeFactory
{
    public const string CardName = "Niko, Light of Hope";
    public const string Slug = "niko-light-of-hope";

    /// <summary>The exile/copy/return activated ability's {2} mana cost.</summary>
    public const string AbilityManaCost = "{2}";

    /// <summary>The Shard token's sac-to-scry-draw {2} mana cost.</summary>
    public const string ShardSacCost = "{2}";

    /// <summary>The Shard token's Scry amount (CR 701.20).</summary>
    public const int ShardScryAmount = 1;

    /// <summary>
    /// Construct Niko with no runtime services wired. The ETB trigger + the
    /// {2},{T} exile/copy/return ability are attached so the card surface is
    /// complete; the ability resolves to a no-op copy/return (no
    /// <see cref="ContinuousEffectsService"/> / <see cref="TriggerManager"/>
    /// to register on). This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, effects: null, triggers: null, zones: null);

    /// <summary>
    /// Construct Niko with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service the
    /// "Shards become copies until the next end step"
    /// <see cref="CopyCharacteristicsEffect"/> is registered on. May be null —
    /// the ability still exiles + returns, but no copy effect is recorded.</param>
    /// <param name="triggers">Manager that registers the ETB trigger + the
    /// delayed end-step return (CR 603.7). May be null — shape-only posture; the
    /// delayed return is omitted.</param>
    /// <param name="zones">Used to route the Shard creation + the exile move so
    /// <see cref="CardMovedEvent"/> fires for downstream listeners. Null →
    /// direct owner-routed zone mutation.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        TriggerManager? triggers,
        ZoneService? zones)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary Creature
        // — Human Wizard 3/4, {2}{W}{U}). The ETB trigger + the {2},{T} ability
        // are layered on below (not expressible in the current JSON
        // AbilityDefinition schema — same posture as ShiftingWoodlandFactory).
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var niko = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // ETB trigger — CR 603.1.
        //   "When Niko enters, create two Shard tokens."
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: create two Shard tokens",
            () =>
            {
                var controller = niko.Controller ?? owner;
                CreateShard(controller, zones);
                CreateShard(controller, zones);
            });

        var etbTrigger = new TriggeredAbility(
            source: niko,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(niko),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        niko.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Activated ability — CR 602.
        //   "{2}, {T}: Exile target nonlegendary creature you control. Shards
        //    you control become copies of it until the next end step. Return it
        //    to the battlefield under its owner's control at the beginning of
        //    the next end step."
        // ----------------------------------------------------------------
        ActivatedAbility? exileAbility = null;
        var exileEffect = new Effect(
            $"{CardName}: exile target nonlegendary creature, Shards copy it, return at next end step",
            () =>
            {
                if (exileAbility == null) return;

                var controller = niko.Controller ?? owner;

                // CR 608.2b — read the chosen target; do nothing if it's gone /
                // illegal. Must still be a nonlegendary creature the controller
                // still controls on the battlefield.
                if (exileAbility.ChosenTargets.Count == 0) return;
                if (exileAbility.ChosenTargets[0].Count == 0) return;
                if (exileAbility.ChosenTargets[0][0] is not Creature target) return;
                if (target.Zone != ZoneType.Battlefield) return;
                if (!ReferenceEquals(target.Controller, controller)) return;
                if (target.HasEffectiveSupertype(CardSupertype.Legendary)) return;

                ExecuteExileCopyReturn(target, controller, effects, triggers, zones);
            });

        exileAbility = new ActivatedAbility(
            source: niko,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(AbilityManaCost),
                AdditionalCost.Tap(niko),
            },
            effects: new IEffect[] { exileEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target nonlegendary creature you control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Protection,
                    CandidateGatherer: _ => NonlegendaryCreaturesYouControl(niko.Controller ?? owner)),
            });

        niko.AddAbility(exileAbility);

        return niko;
    }

    /// <summary>
    /// CR 115.1 — the "target nonlegendary creature you control" candidate
    /// pool: <paramref name="controller"/>'s battlefield creatures that are not
    /// Legendary (CR 205.4 — read through <see cref="Permanent.HasEffectiveSupertype"/>
    /// so a granted Legendary is respected). Exposed for the gatherer + tests.
    /// </summary>
    public static IReadOnlyList<object> NonlegendaryCreaturesYouControl(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return controller.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => ReferenceEquals(c.Controller, controller))
            .Where(c => !c.HasEffectiveSupertype(CardSupertype.Legendary))
            .Cast<object>()
            .ToList();
    }

    /// <summary>
    /// CR 602 resolution body — exile <paramref name="target"/>, make every
    /// Shard the controller controls a copy of it until the next end step, and
    /// register the delayed end-step return.
    /// </summary>
    private static void ExecuteExileCopyReturn(
        Creature target,
        Player controller,
        ContinuousEffectsService? effects,
        TriggerManager? triggers,
        ZoneService? zones)
    {
        // CR 707.3 — snapshot the Shards' copy SOURCE from the live battlefield
        // object before it leaves: the copy is "of it", read off last-known
        // information. Done before the exile move so the copiable values are
        // read while target is still on the battlefield.
        var shards = controller.Zones.Battlefield.GetCards()
            .OfType<Enchantment>()
            .Where(e => e.IsToken && e.Name == ShardName
                        && ReferenceEquals(e.Controller, controller))
            .ToList();

        // CR 707.2 / 613.2 Layer 1 — "Shards you control become copies of it
        // until the next end step." Registered with expiresAtEndOfTurn: true so
        // the cleanup step (CR 514.2) lifts the copy. Skipped when no
        // continuous-effects service is wired (shape-only path).
        if (effects != null)
        {
            foreach (var shard in shards)
            {
                CopyCharacteristicsEffect.RegisterCopy(
                    effects,
                    shard,
                    target,
                    abilityRebind: CopyCharacteristicsEffect.DefaultAbilityRebind,
                    expiresAtEndOfTurn: true);
            }
        }

        var targetOwner = target.Owner ?? controller;

        // CR 701.21 — Exile. Prefer ZoneService when supplied so CardMovedEvent
        // fires; owner-routed so a control-swapped creature still leaves the
        // controller's battlefield correctly.
        if (zones != null)
        {
            zones.MoveCard(target, ZoneType.Battlefield, ZoneType.Exile);
        }
        else
        {
            target.Controller?.Zones.Battlefield.RemoveCard(target);
            targetOwner.Zones.Exile.AddCard(target);
            target.SetZone(ZoneType.Exile);
        }

        // CR 603.7 — register the delayed end-step return. Skipped when no
        // TriggerManager is wired (shape-only tests).
        if (triggers == null) return;

        var resolvedAt = Majik.Core.Game.LogicalClockScope.Current.NextTimestamp();
        var returnEffect = new Effect(
            $"{CardName}: return exiled creature to owner's battlefield at next end step (CR 603.7)",
            () =>
            {
                // CR 111.8 — tokens cease to exist when they leave the
                // battlefield; guard defensively so a token target no-ops.
                if (target.Zone != ZoneType.Exile) return;

                // "under its owner's control" (CR 108.3 / 614) — route through
                // the owner's zones; the owner regains control.
                if (zones != null)
                {
                    zones.MoveCard(target, ZoneType.Exile, ZoneType.Battlefield, targetOwner);
                }
                else
                {
                    targetOwner.Zones.Exile.RemoveCard(target);
                    targetOwner.Zones.Battlefield.AddCard(target);
                    target.SetZone(ZoneType.Battlefield);
                    target.SetController(targetOwner);
                }
            });

        var delayed = new DelayedTriggeredAbility(
            source: target,
            controller: controller,
            condition: new EventTriggerCondition<Events.StepStartedEvent>(
                (e, _) => e.StepType == StepStateType.End
                          && e.Timestamp > resolvedAt),
            effects: new IEffect[] { returnEffect });

        triggers.RegisterDelayed(delayed);
    }

    /// <summary>The printed name of the Shard token (CR 111.4).</summary>
    public const string ShardName = "Shard";

    /// <summary>
    /// CR 111.4 — build a Shard token: a colourless Enchantment token carrying
    /// "{2}, Sacrifice this token: Scry 1, then draw a card." and put it onto
    /// <paramref name="controller"/>'s battlefield. The Shard is bespoke (not in
    /// <see cref="TokenFactory"/>) because it is an Enchantment token with a
    /// scry-then-draw activated ability — a shape unique to Niko. Returns the
    /// minted Shard.
    /// </summary>
    public static Enchantment CreateShard(Player controller, ZoneService? zones = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var shard = new Enchantment(ShardName, manaCost: "")
        {
            Owner = controller,
            Controller = controller,
            IsToken = true,
        };

        // CR 111.4 — Shard tokens are colourless (no colour indicator).
        shard.SetTokenColors(Array.Empty<ManaColor>());

        // "{2}, Sacrifice this token: Scry 1, then draw a card."
        shard.AddAbility(BuildShardSacAbility(shard, controller));

        // Tokens enter the battlefield directly (CR 111.6). Sentinel-library
        // pattern so CardMovedEvent fires when a ZoneService is wired
        // (mirrors TokenFactory.PutOnBattlefield).
        shard.SetZone(ZoneType.Library);
        controller.Zones.Library.AddCard(shard);
        if (zones != null)
        {
            zones.MoveCardTo(shard, ZoneType.Battlefield, controller);
        }
        else
        {
            controller.Zones.Library.RemoveCard(shard);
            shard.SetZone(ZoneType.Battlefield);
            controller.Zones.Battlefield.AddCard(shard);
        }

        return shard;
    }

    /// <summary>
    /// "{2}, Sacrifice this token: Scry 1, then draw a card." — the Shard's sac
    /// ability (CR 701.20 + CR 121.1). Two costs ({2} mana + sacrifice-self) and
    /// a single effect that scries 1 (consulting the resolving agent; default
    /// keep-on-top) then draws one card. The sacrifice move is performed inside
    /// the effect closure because <see cref="AdditionalCost.Sacrifice"/> is a
    /// no-op payment stub on the cost side (Clue / Food / Blood precedent).
    /// </summary>
    private static ActivatedAbility BuildShardSacAbility(Enchantment shard, Player controller)
    {
        var costs = new ICost[]
        {
            new ManaCostCost(ShardSacCost),
            AdditionalCost.Sacrifice(shard),
        };

        var effect = new Effect(
            "Shard: sacrifice self, scry 1, then draw a card",
            async ctx =>
            {
                // CR 701.16 — sacrifice payment (AdditionalCost.Sacrifice is a
                // no-op stub; Clue/Food/Blood precedent). Idempotent re-entry
                // guard. Credit the cost-payer on the ambient bus so "whenever
                // you sacrifice …" payoffs fire (best-effort).
                if (shard.Zone == ZoneType.Battlefield)
                {
                    var wasToken = shard.IsToken; // CR 111.7 — snapshot pre-move.
                    controller.Zones.Battlefield.RemoveCard(shard);
                    controller.Zones.Graveyard.AddCard(shard);
                    shard.SetZone(ZoneType.Graveyard);

                    Majik.Core.Events.EventBusRegistry.Get(controller)
                        ?.Publish(new Majik.Core.Events.PermanentSacrificedEvent(
                            shard, controller, wasToken));
                }

                // CR 701.20 — Scry 1: consult the resolving agent; default to
                // keep-on-top (empty ToBottom) when no agent is wired.
                var peeked = ScryAction.Peek(controller, ShardScryAmount);
                if (peeked.Count > 0)
                {
                    var agent = ctx.Agent ?? AgentRegistry.Get(controller);
                    ScryAction.ScryDecision decision;
                    if (agent != null)
                    {
                        decision = await agent.ChooseScryDecisionAsync(ctx.Game, peeked)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        decision = new ScryAction.ScryDecision(
                            ToBottom: Array.Empty<ICard>(),
                            TopOrder: peeked.ToList());
                    }

                    ScryAction.Apply(controller, peeked.Count, decision);
                }

                // CR 121.1 — "then draw a card". Empty library flags the SBA
                // loss via the standard helper (Clue / Blood parity).
                var top = controller.Zones.Library.GetCards().FirstOrDefault();
                if (top == null)
                {
                    controller.MarkTriedToDrawFromEmptyLibrary();
                    return;
                }
                controller.Zones.Library.RemoveCard(top);
                controller.Zones.Hand.AddCard(top);
                top.SetZone(ZoneType.Hand);
            });

        return new ActivatedAbility(shard, controller, costs: costs, effects: new IEffect[] { effect });
    }
}
