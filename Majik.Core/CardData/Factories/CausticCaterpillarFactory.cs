using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Caustic Caterpillar (Magic Origins, {G}).
///
/// Creature — Insect 1/1. Oracle text:
///   "Sacrifice this creature: Destroy target artifact or enchantment."
///
/// ## Implemented (v1)
/// - Card identity: Creature — Insect, mana cost {G}, P/T 1/1, owner / controller.
/// - <b>Sacrifice self: Destroy target artifact or enchantment</b> — single
///   <see cref="ActivatedAbility"/> with <see cref="AdditionalCost.Sacrifice"/>
///   on the caterpillar itself (no mana component — the cost is pure
///   sacrifice). A 1..1 <see cref="TargetRequest"/> for "target artifact or
///   enchantment" is declared so the activating agent picks a permanent at
///   activation (CR 602.2b). On resolution:
///   <list type="number">
///     <item>Sacrifice the caterpillar (battlefield → owner's graveyard —
///       same closure shape as Aether Spellbomb / Mind Stone / Expedition
///       Map, the generic <see cref="AdditionalCost.Pay"/> sacrifice path
///       is a stub).</item>
///     <item>Target permanent is still on the battlefield (CR 608.2b).</item>
///     <item>Target is an artifact OR an enchantment (the printed
///       "artifact or enchantment" predicate).</item>
///     <item>If both pass: destroy via
///       <see cref="OracleSpellBinder.MoveToGraveyard"/> with
///       <see cref="ZoneMoveReason.Destroy"/> (CR 701.7 — indestructible
///       cancels per CR 702.12, active regeneration shield consumed per
///       CR 701.15).</item>
///     <item>If any fails: the sacrifice still happens (cost was paid) and
///       the destroy is a clean no-op (CR 608.2b — illegal target →
///       effect does nothing).</item>
///   </list>
///
/// ## Deferred (v1 gaps)
/// - <b>Target legality in ActionValidator</b>: the validator does not yet
///   filter activation targets to "artifact or enchantment" — resolution-
///   time guard handles illegal targets (CR 608.2b). Same posture as
///   Aether Spellbomb / Assassin's Trophy.
/// - <b>Sacrifice payment side effects</b>: the engine's generic
///   <see cref="AdditionalCost"/> sacrifice payment is currently a no-op
///   stub. The effect closure performs the zone move directly so behaviour
///   is observable — remove the explicit move-to-graveyard once
///   <see cref="AdditionalCost.Pay"/> performs the sacrifice itself.
/// - <b>BotIntent on activation target</b>: the <see cref="TargetRequest"/>
///   carries <see cref="BotIntent.Removal"/> so the bot's target picker
///   ranks artifact / enchantment removal correctly; agent-side activation
///   prompting still relies on the generic ActivatedAbility surface.
///
/// ## Agatha re-home (agatha-bespoke-source-migration-tail)
/// The sac-then-destroy ability is <see cref="ActivatedAbility.RebindSafe"/>:
/// its "Sacrifice THIS CREATURE" half reads
/// <see cref="Majik.Core.Abilities.ResolutionContext.Source"/> (the re-homed
/// bearer under Agatha's Soul Cauldron) instead of capturing <c>card</c>, and
/// the "destroy target artifact or enchantment" half reads
/// <see cref="Majik.Core.Abilities.ResolutionContext.ChosenTargets"/>. The
/// <see cref="AdditionalCost.Sacrifice"/> cost re-homes onto the new source via
/// <see cref="AdditionalCost.RebindSource"/> (Stage 1). So Agatha's Soul
/// Cauldron re-homes the REAL ability to a counter-bearing bearer via
/// <see cref="ActivatedAbility.RebindTo"/> (CR 707.2 / 613.1f): the BEARER is
/// sacrificed and the chosen artifact / enchantment is destroyed — never the
/// exiled Caterpillar. "Sacrifice self + destroy target artifact/enchantment"
/// is outside <see cref="Majik.Core.CardData.OracleActivatedAbilityBinder"/>'s
/// reconstructable set, so RebindTo of the real ability is the only sound
/// re-home.
/// </summary>
[CardName("Caustic Caterpillar")]
public static class CausticCaterpillarFactory
{
    public const string CardName = "Caustic Caterpillar";
    public const string PrintedManaCost = "{G}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Caustic Caterpillar owned and controlled by
    /// <paramref name="owner"/>. The single "sacrifice: destroy target
    /// artifact or enchantment" activated ability is attached structurally.
    /// No event bus ⇒ the self-sacrifice cost publishes nothing (legacy
    /// shape-only posture for dispatcher / shape tests).
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, eventBus: null);

    /// <summary>
    /// Effects-aware overload the <b>production</b> <c>GameFacade</c> routed
    /// build dispatches to (the source generator recognises only a two-param
    /// <c>Create(Player, ContinuousEffectsService)</c> as the effects-aware
    /// overload). Forwards <c>effects.EventBus</c> so paying the self-sacrifice
    /// cost publishes a <see cref="PermanentSacrificedEvent"/> (CR 701.16a) for
    /// "whenever a/an [player] sacrifices …" aristocrat payoffs. Mirrors the
    /// Festival-Crasher / Spellbomb seam.
    /// </summary>
    public static Creature Create(Player owner, ContinuousEffectsService? effects) =>
        Create(owner, effects?.EventBus);

    /// <summary>
    /// Construct Caustic Caterpillar. When <paramref name="eventBus"/> is
    /// supplied, the self-sacrifice activation cost publishes a
    /// <see cref="PermanentSacrificedEvent"/> (CR 701.16a) crediting the
    /// cost-payer; when null the move still happens but nothing is published.
    /// </summary>
    public static Creature Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Insect });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Sacrifice this creature: Destroy target artifact or enchantment.
        // CR 602 — activated ability. Cost = AdditionalCost.Sacrifice on
        // the caterpillar itself; no mana component (pure sacrifice).
        // CR 608.2b — resolution-time guard ensures the chosen target is
        // still a legal artifact / enchantment on the battlefield.
        // CR 701.7 — destroy via MoveToGraveyard(Destroy).
        //
        // RE-SOURCE-SAFE (agatha-bespoke-source-migration-tail): the
        // "Sacrifice THIS CREATURE" half is the ability's own source, so the
        // effect reads the live ResolutionContext.Source (the re-homed bearer
        // under Agatha's Soul Cauldron) rather than capturing `card`, falling
        // back to `card` only on the context-less legacy sync path
        // (ResolutionContext.Legacy, Source = null). The "destroy target
        // artifact or enchantment" half reads ctx.ChosenTargets (the chosen
        // permanent), not the source, so it re-homes unchanged. The sole cost
        // is an AdditionalCost.Sacrifice that RebindTo Stage 1 re-homes onto the
        // new source automatically (AdditionalCost.RebindSource). Marked
        // RebindSafe below so Agatha's Soul Cauldron re-homes this REAL
        // sac-then-destroy ability to a counter-bearing bearer via
        // ActivatedAbility.RebindTo (CR 707.2 / 613.1f): the BEARER (not the
        // exiled Caterpillar) is sacrificed, and the chosen artifact /
        // enchantment is destroyed. "Sacrifice self + destroy target
        // artifact/enchantment" is OUTSIDE the OracleActivatedAbilityBinder
        // reconstructable set, so RebindTo of the real ability is the only sound
        // re-home.
        // ----------------------------------------------------------------
        var sacEffect = new Effect(
            $"{CardName}: sacrifice self + destroy target artifact/enchantment",
            ctx =>
            {
                // The re-homed bearer under Agatha (ctx.Source), else this card
                // on the context-less legacy sync path.
                var self = (ctx.Source as Creature) ?? card;
                SacrificeSelf(self, owner, eventBus);

                if (ctx.ChosenTargets.Count == 0 || ctx.ChosenTargets[0].Count == 0)
                {
                    return ValueTask.CompletedTask;
                }

                if (ctx.ChosenTargets[0][0] is not Permanent target)
                {
                    return ValueTask.CompletedTask;
                }

                // CR 608.2b — illegal-target check at resolution.
                if (target.Zone != ZoneType.Battlefield) return ValueTask.CompletedTask;
                if (!target.HasType(CardType.Artifact)
                    && !target.HasType(CardType.Enchantment))
                {
                    return ValueTask.CompletedTask;
                }

                // CR 701.7 — destroy. Indestructible (CR 702.12) cancels;
                // active regeneration shield (CR 701.15) is consumed.
                OracleSpellBinder.MoveToGraveyard(target, ZoneMoveReason.Destroy);

                return ValueTask.CompletedTask;
            });

        var sacAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.Sacrifice(card, eventBus),
            },
            effects: new IEffect[] { sacEffect },
            rebindSafe: true,
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target artifact or enchantment",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal),
            });

        card.AddAbility(sacAbility);

        return card;
    }

    /// <summary>
    /// CR 701.16 — sacrifice <paramref name="card"/> (battlefield → owner's
    /// graveyard) from inside the RESOLVE closure. When
    /// <paramref name="eventBus"/> is supplied the sacrifice routes through the
    /// bus-aware <see cref="Fx.Sacrifice(Cards.ICard, Player, Events.IEventBus)"/>
    /// overload so a <see cref="PermanentSacrificedEvent"/> (CR 701.16a) fires
    /// crediting the controller (aristocrat payoffs — Mayhem Devil, Blood
    /// Artist, It That Betrays). When null the bare
    /// <see cref="Fx.Sacrifice(Cards.ICard)"/> overload moves it without
    /// publishing (legacy shape-only posture). Idempotent — the on-battlefield
    /// guard means a stale resolve re-entry (cost already paid the sac) is a
    /// no-op, so the cost-seam publish and this resolve publish never both fire.
    /// </summary>
    private static void SacrificeSelf(Creature card, Player owner, Events.IEventBus? eventBus)
    {
        if (card.Zone != ZoneType.Battlefield) return;
        var controller = card.Controller ?? owner;
        if (eventBus != null) Fx.Sacrifice(card, controller, eventBus);
        else Fx.Sacrifice(card);
    }
}
