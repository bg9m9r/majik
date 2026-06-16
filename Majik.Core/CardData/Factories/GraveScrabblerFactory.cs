using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Grave Scrabbler (Time Spiral, {3}{B}).
///
/// Creature — Zombie 2/2. Oracle text (verified against Scryfall 2026-06-16):
///   "Madness {1}{B} (If you discard this card, discard it into exile. When you
///    do, cast it for its madness cost or put it into your graveyard.)
///    When this creature enters, <b>if its madness cost was paid</b>, you may
///    return target creature card from a graveyard to its owner's hand."
///
/// ## The pay-down — madness-paid resolution-flag seam
/// The whole point of this card (and the deferral it closes) is the
/// "<b>if its madness cost was paid</b>" ETB gate (CR 702.35c). The cast path
/// stamps <see cref="Card.WasCastForMadnessCost"/> at madness-cost PAY time
/// (TurnDriver's <c>PayCastMana</c>, when the cast pays a
/// <see cref="Card.RuntimeExileCastIsMadness"/> exile-cast grant the
/// <see cref="Majik.Core.Keywords.MadnessCatalog"/> opened via
/// <see cref="Fx.DiscardCard"/>). Because the same <see cref="Card"/> instance
/// is the spell and the resolving permanent, the flag survives onto the
/// battlefield permanent, so the ETB trigger's intervening-if (CR 603.4) reads
/// it at resolution — and a normal-cost cast (flag false) makes the ETB do
/// nothing.
///
/// ## Card identity comes from JSON
/// Name / type / printed cost / P/T are loaded from the embedded JSON
/// definition (<c>grave-scrabbler.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same posture as
/// <see cref="NightshadeAssassinFactory"/>.
///
/// ## Implemented (v1)
/// - 2/2 Creature — Zombie at {3}{B} (from JSON).
/// - ETB trigger (CR 603.6a) with an intervening-if gating on
///   <see cref="Card.WasCastForMadnessCost"/> (CR 603.4 — re-checked at trigger
///   and again at resolution). Targets a creature CARD in ANY graveyard ("from
///   a graveyard" — not just yours; CR 109.5). Resolution returns it to its
///   OWNER's hand (CR 701.20 / <see cref="Fx.ReturnFromGraveyardToHand"/>), then
///   clears the madness flag so a later blink / token entry never reuses it.
/// - "you may" (CR 603.3c) — modelled as an always-take optional in v1 (the
///   single-arg dispatcher / bot posture: a free return-to-hand is strictly
///   value-positive; declining is never correct). The chosen target rides the
///   agent-set <see cref="TriggeredAbility.ChosenTargets"/>.
///
/// ## Madness (intrinsic, NOT wired here)
/// Madness {1}{B} works for every catalogued card via
/// <see cref="Majik.Core.Keywords.MadnessCatalog"/> consulted by the central
/// discard funnel <see cref="Fx.DiscardCard"/>; "Grave Scrabbler" is catalogued
/// at {1}{B}, so the madness line itself needs no factory code — only the
/// "if its madness cost was paid" GATE (the seam above) is bespoke.
/// </summary>
[CardName("Grave Scrabbler")]
public static class GraveScrabblerFactory
{
    public const string CardName = "Grave Scrabbler";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "grave-scrabbler";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>Construct Grave Scrabbler with its ETB trigger attached to the
    /// card shape but NOT registered with a <see cref="TriggerManager"/>.
    /// Suitable for shape / dispatcher tests.</summary>
    public static Creature Create(Player owner) => Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Effects-aware overload the source-gen dispatches on the PRODUCTION
    /// <see cref="DeckCardBuilder"/> build path
    /// (<c>NamedCardFactory.Create(name, owner, effects)</c>). Resolves the live
    /// per-game <see cref="TriggerManager"/> from
    /// <see cref="TriggerManagerRegistry"/> so the ETB trigger is registered and
    /// fires in real games (CR 603.6a), and forwards
    /// <c>continuousEffects?.EventBus</c>. Mirrors
    /// <see cref="MirariWakeFactory"/>'s ambient-registry trigger wiring.
    /// </summary>
    public static Creature Create(Player owner, ContinuousEffectsService? continuousEffects) =>
        Create(owner, continuousEffects?.EventBus, TriggerManagerRegistry.Get());

    /// <summary>
    /// Construct Grave Scrabbler with optional runtime services. When
    /// <paramref name="triggers"/> is supplied the ETB trigger is registered so
    /// the relevant ETB event places it on the stack automatically (CR 603.3).
    /// </summary>
    public static Creature Create(Player owner, IEventBus? eventBus, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a + CR 603.4 (intervening-if).
        //   "When this creature enters, if its madness cost was paid, you may
        //    return target creature card from a graveyard to its owner's hand."
        // The intervening-if reads the per-cast madness stamp off the resolving
        // permanent (the same Card instance). The target is a creature CARD in
        // any graveyard.
        // ----------------------------------------------------------------
        var zones = ZoneServiceRegistry.Get(owner);

        TriggeredAbility? etb = null;

        var etbEffect = new Effect(
            $"{CardName}: if its madness cost was paid, you may return target creature card to its owner's hand",
            async rc =>
            {
                var controller = (rc.Source as Permanent)?.Controller ?? rc.Controller ?? owner;
                ResolveEtb(card, controller, etb, zones);
                await System.Threading.Tasks.Task.CompletedTask.ConfigureAwait(false);
            });

        etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            // CR 603.4 — intervening-if: only triggers / resolves while the
            // madness cost was paid for this creature's cast.
            interveningIf: () => card.WasCastForMadnessCost,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature card in a graveyard",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: GatherTargets(owner).ToList(),
                    Intent: BotIntent.Reanimate,
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Graveyard.GetCards())
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        return card;
    }

    /// <summary>Snapshot the creature-card-in-any-graveyard target set at
    /// trigger-creation time (production refreshes via the
    /// <see cref="TargetRequest.CandidateGatherer"/> at resolution).</summary>
    private static IEnumerable<object> GatherTargets(Player owner) =>
        owner.Zones.Graveyard.GetCards().OfType<Creature>().Cast<object>();

    /// <summary>
    /// Resolve the ETB. CR 603.4 — re-check the intervening-if (madness cost
    /// paid) at resolution; if it no longer holds, do nothing. Otherwise return
    /// the chosen creature card from its graveyard to its OWNER's hand
    /// (CR 701.20), then clear the per-cast madness stamp so a later non-cast
    /// entry never reuses it. Exposed for unit tests that drive the resolve
    /// directly (mirrors <see cref="NightshadeAssassinFactory"/>).
    /// </summary>
    public static void ResolveEtb(
        Creature scrabbler, Player controller, TriggeredAbility? etb, ZoneService? zones)
    {
        ArgumentNullException.ThrowIfNull(scrabbler);

        // CR 603.4 — the "if its madness cost was paid" gate is re-checked on
        // resolution. The flag is consumed (cleared) below regardless of
        // whether a target is returned, so a later blink / token copy never
        // re-reads a stale flag.
        var paid = scrabbler.WasCastForMadnessCost;
        scrabbler.ClearCastForMadness();
        if (!paid) return;

        var target = PickTarget(controller, etb);
        if (target == null) return;

        // CR 608.2b — illegal-on-resolution check: must still be a creature card
        // in a graveyard. Return it to its OWNER's hand (CR 701.20). Owner-routed
        // — Fx.ReturnFromGraveyardToHand moves from the card OWNER's graveyard to
        // the OWNER's hand (Grave Scrabbler returns to its owner's hand, not the
        // Scrabbler controller's hand).
        if (target.Zone != ZoneType.Graveyard) return;

        Fx.ReturnFromGraveyardToHand(target, zones);
    }

    /// <summary>Pick the return target — honours an agent-set
    /// <see cref="TriggeredAbility.ChosenTargets"/> (production); otherwise the
    /// first creature card in any graveyard (deterministic single-arg
    /// dispatcher posture).</summary>
    private static Creature? PickTarget(Player controller, TriggeredAbility? etb)
    {
        if (etb != null
            && etb.ChosenTargets.Count > 0
            && etb.ChosenTargets[0].Count > 0
            && etb.ChosenTargets[0][0] is Creature chosen)
        {
            return chosen;
        }

        // No agent-set target — fall back to the first creature card in any
        // graveyard reachable from the controller's game view.
        return controller.Zones.Graveyard.GetCards().OfType<Creature>().FirstOrDefault();
    }
}
