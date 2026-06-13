using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for The Goose Mother (Wilds of Eldraine,
/// <c>{X}{G}{U}</c>). Legendary Creature — Bird Hydra 2/2.
///
/// Oracle text (Scryfall-verified):
///   "Flying
///    The Goose Mother enters with X +1/+1 counters on it.
///    When The Goose Mother enters, create half X Food tokens, rounded up.
///    Whenever The Goose Mother attacks, you may sacrifice a Food. If you
///    do, draw a card."
///
/// The base shape (name, Legendary, Creature, Bird + Hydra subtypes,
/// <c>{X}{G}{U}</c>, 2/2) is materialised from the embedded JSON definition
/// (<c>the-goose-mother.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. Flying, the two ETB triggers,
/// and the attack trigger are layered on here — the JSON
/// <c>AbilityDefinition</c> schema doesn't express keyword markers,
/// variable-X ETB counters, Food-token creation, or attack triggers, so they
/// live in the factory (same posture as <see cref="GildedGooseFactory"/> /
/// <see cref="BaskingBroodscaleFactory"/>, whose behaviour outgrows the
/// schema).
///
/// ## Implemented (v1)
/// <list type="bullet">
///   <item><b>Flying (CR 702.9)</b> — attached as a <see cref="KeywordAbility"/>
///   marker so combat surfaces (<see cref="Majik.Core.Combat.CombatAbilities.HasFlying"/>)
///   observe it. Same shape as Gilded Goose's Flying.</item>
///
///   <item><b>ETB +1/+1 counters trigger (CR 603.6a / CR 122.1g)</b> — "The
///   Goose Mother enters with X +1/+1 counters on it." On entering the
///   battlefield, reads X from <see cref="Card.PendingCastX"/> (stamped by
///   <see cref="Majik.Core.Game.SpellCastFlow"/> right after the caster's
///   <c>ChooseXAsync</c>) and places that many +1/+1 counters via
///   <see cref="CountersService.Add"/> (so Hardened Scales / Doubling Season
///   rewrite the amount — CR 614), then clears the stamp so a non-cast
///   re-entry (blink / copy) doesn't reuse it. Identical pattern to
///   <see cref="HangarbackWalkerFactory"/>'s ETB-counter effect.</item>
///
///   <item><b>ETB Food trigger (CR 603.6a / CR 111.10)</b> — "When The Goose
///   Mother enters, create half X Food tokens, rounded up." A SECOND ETB
///   trigger (distinct from the counters trigger — they are two separate
///   printed abilities). On resolve, reads the same X that was on
///   <see cref="Card.PendingCastX"/> (snapshotted into a closure-local at
///   build time via a shared X-reader so trigger-ordering between the two
///   ETB abilities doesn't matter — see the <c>xAtEntry</c> note below) and
///   mints ceil(X / 2) Food tokens through <see cref="TokenFactory.CreateFood"/>.
///   "Half X rounded up" = <c>(X + 1) / 2</c> in integer arithmetic
///   (CR 107.16 — rounded up).</item>
///
///   <item><b>Attacks trigger (CR 508.1f)</b> — "Whenever The Goose Mother
///   attacks, you may sacrifice a Food. If you do, draw a card." A
///   <see cref="TriggeredAbility"/> over <see cref="Triggers.OnAttackSelf"/>.
///   On resolve the "you may" (CR 117.5) is offered to the controller's
///   <see cref="IPlayerAgent"/> via <see cref="IPlayerAgent.ChooseYesNoAsync"/>;
///   v1 auto-takes the upside (yes) when no agent is registered, since a card
///   off one Food is generally pure value (matching the auto-pay posture of
///   Basking Broodscale's token "may"). The optional sacrifice cost reuses
///   <see cref="GildedGooseFactory.SacrificeAFoodCost"/>. "If you do" (CR
///   120.2 / CR 701.16) gates the draw on the sacrifice actually being paid —
///   if the controller has no Food (cost can't be paid), the draw does not
///   happen.</item>
/// </list>
///
/// ## Wiring overloads
/// <list type="bullet">
///   <item><see cref="Create(Player)"/> — shape only; triggers are attached
///   for shape / dispatcher tests but not registered with any
///   <see cref="TriggerManager"/>, and counter placements / Food ETBs bypass
///   the replacement / event / zone buses.</item>
///   <item><see cref="Create(Player, ZoneService?, TriggerManager?, ReplacementBus?, IEventBus?)"/>
///   — fully wired; the three triggers register, the ETB counters route
///   through the replacement + event buses (Hardened Scales / Animation
///   Module), and every Food token's ETB routes through
///   <paramref name="zones"/> so its <see cref="CardMovedEvent"/> publishes.</item>
/// </list>
///
/// ## Notes
/// <list type="bullet">
///   <item><b>X read from PendingCastX, snapshotted at first-fire</b>: both
///   ETB abilities read the same cast-time X. The counters trigger consumes
///   (clears) <see cref="Card.PendingCastX"/>, so the Food trigger captures X
///   into a shared closure-local (<c>xAtEntry</c>) on whichever ETB ability
///   resolves first — independent of trigger ordering (CR 603.3b — the
///   controller orders simultaneous triggers, either order is correct here).</item>
///   <item><b>Food-sacrifice target prompt</b>:
///   <see cref="GildedGooseFactory.SacrificeAFoodCost"/> picks the first
///   eligible Food deterministically (agents can pre-set its Target). Full
///   agent-driven Food selection waits on the shared sacrifice-prompt surface
///   (same gap noted on Gilded Goose / Witch's Oven).</item>
/// </list>
/// </summary>
[CardName("The Goose Mother")]
public static class TheGooseMotherFactory
{
    public const string CardName = "The Goose Mother";
    public const string Slug = "the-goose-mother";

    private const string FlyingKeyword = "Flying";

    /// <summary>
    /// Construct The Goose Mother with no live wiring. The three triggers are
    /// attached for shape observability but not registered with any
    /// <see cref="TriggerManager"/>; counter placements / Food ETBs bypass the
    /// replacement / event / zone buses. Suitable for shape / dispatcher
    /// tests. This is the overload <see cref="NamedCardFactory"/> dispatches
    /// to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zones: null, triggers: null, replacements: null, eventBus: null);

    /// <summary>
    /// Construct The Goose Mother with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zones">When supplied, every Food token's ETB routes
    /// through <see cref="ZoneService.MoveCardTo"/> so
    /// <see cref="CardMovedEvent"/> publishes.</param>
    /// <param name="triggers">When supplied, the three triggers register so
    /// the matching event lands the ability on the stack automatically
    /// (CR 603.2).</param>
    /// <param name="replacements">Routed through
    /// <see cref="CountersService.Add"/> for the ETB counters (Hardened Scales
    /// / Doubling Season bumps — CR 614).</param>
    /// <param name="eventBus">Publishes the <see cref="CounterAddedEvent"/>
    /// for the ETB counters so "+1/+1 counters were put on …" triggers can
    /// chain.</param>
    public static Creature Create(
        Player owner,
        ZoneService? zones,
        TriggerManager? triggers,
        ReplacementBus? replacements,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary,
        // Creature, Bird + Hydra subtypes, {X}{G}{U}, 2/2). The JSON carries
        // no abilities — Flying + the three triggers are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner, replacements);

        card.SetController(owner);

        // ----------------------------------------------------------------
        // "The Goose Mother enters with X +1/+1 counters on it" (CR 614.1d /
        // CR 202.3b) is NOT wired by this factory. It is registered by the
        // generic EntersWithCountersBinder as a variable-X
        // EntersWithCountersReplacement. On the production deck-build
        // (DeckCardBuilder APPROACH B) the binder runs in OverlayAdditiveBinders
        // against the live ReplacementBus, matches the oracle text ("enters with
        // X +1/+1 counters on it"), reads the chosen X off Card.PendingCastX
        // (stamped by SpellCastFlow after ChooseXAsync), and stamps
        // ZoneMoveIntent.PlusOneCountersOnEnter so the permanent enters WITH the
        // counters (no transient 0/0 window). Hardened Scales / Doubling Season
        // compose on that same ETB intent channel (CR 614).
        //
        // The factory deliberately does NOT MarkSelfManagesEntersWithCounters()
        // and does NOT attach an ETB TriggeredAbility for the counters — that
        // was the bug (the same one Walking Ballista had, #2635): the prod
        // Approach-B route calls NamedCardFactory.Create with no TriggerManager,
        // so a self-managed ETB trigger is never registered and never fires, AND
        // the self-manage flag suppresses the binder — the one mechanism that
        // route DOES run — yielding ZERO counters in real play. Only the
        // counters wiring changes; Flying, the ETB Food clause, and the
        // attacks-sacrifice-a-Food-draw trigger below are unchanged.
        //
        // The Food ETB still needs the cast-time X. The binder reads
        // Card.PendingCastX but does NOT clear it, so the Food trigger reads the
        // same value off the stamp (and clears it once consumed, below).
        // ----------------------------------------------------------------

        // CR 702.9 — Flying. KeywordAbility marker only; consumed by
        // CombatAbilities.HasFlying so block-legality observes it.
        card.AddAbility(new KeywordAbility(FlyingKeyword, card, owner));

        // Cast-time X for the Food clause. The "enters with X +1/+1 counters"
        // clause is now owned by the binder (see note above) and reads
        // PendingCastX without clearing it, so the Food ETB reads the same
        // stamp here. Snapshot once (so a later non-cast re-entry that re-fires
        // the trigger doesn't pick up a stale value) and clear the stamp once
        // consumed.
        int? xAtEntry = null;
        int CaptureX()
        {
            xAtEntry ??= card.PendingCastX ?? 0;
            return xAtEntry.Value;
        }

        // ----------------------------------------------------------------
        // ETB Food trigger — CR 603.6a / CR 111.10.
        //   "When The Goose Mother enters, create half X Food tokens,
        //    rounded up."
        // ceil(X / 2) = (X + 1) / 2 in integer arithmetic (CR 107.16 — round
        // up). Reads the cast-time X off PendingCastX (the binder leaves it
        // intact), then clears the stamp so a non-cast re-entry doesn't reuse
        // it.
        // ----------------------------------------------------------------
        var foodEffect = new Effect(
            $"{CardName}: create half X Food tokens, rounded up (CR 111.10)",
            () =>
            {
                var x = CaptureX();
                var foodCount = (x + 1) / 2; // ceil(X / 2)
                var controller = card.Controller ?? owner;
                for (var i = 0; i < foodCount; i++)
                {
                    TokenFactory.CreateFood(controller, zones);
                }
                card.ClearPendingCastX();
            });

        var foodTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { foodEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(foodTrigger);
        triggers?.RegisterTriggeredAbility(foodTrigger);

        // ----------------------------------------------------------------
        // Attacks trigger — CR 508.1f.
        //   "Whenever The Goose Mother attacks, you may sacrifice a Food. If
        //    you do, draw a card."
        // "You may" (CR 117.5) → consult the agent; auto-take the upside when
        // no agent is registered (a card off one Food is value). "If you do"
        // (CR 120.2) gates the draw on the optional Food-sacrifice actually
        // being paid — no Food (cost can't be paid) → no draw.
        // ----------------------------------------------------------------
        var attackEffect = new Effect(
            $"{CardName}: you may sacrifice a Food; if you do, draw a card",
            async ctx =>
            {
                var controller = card.Controller ?? owner;
                var sacCost = new GildedGooseFactory.SacrificeAFoodCost();

                // "If you do" hinges on actually being able to sacrifice a
                // Food. No Food → nothing to pay → no draw (CR 120.2).
                if (!sacCost.CanPay(controller)) return;

                var agent = ctx.Agent ?? AgentRegistry.Get(controller);
                bool sac = agent == null
                    || await agent.ChooseYesNoAsync(
                        "Sacrifice a Food to draw a card?",
                        BotIntent.Draw | BotIntent.CardAdvantage).ConfigureAwait(false);

                if (!sac) return;

                // Pay the optional cost, then draw (CR 120.2 — "If you do").
                sacCost.Pay(controller);
                Fx.DrawCards(controller, 1);
            });

        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new IEffect[] { attackEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return card;
    }
}
