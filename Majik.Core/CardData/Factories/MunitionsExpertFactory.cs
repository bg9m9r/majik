using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Munitions Expert (Mercadian Masques, {R}).
///
/// Creature — Goblin Warrior 1/1. Oracle text:
///   "When Munitions Expert enters, you may have it deal X damage to any
///    target, where X is the number of Goblins you control."
///
/// ## Implemented (v1)
/// - 1/1 Creature — Goblin Warrior, mana cost {R}, owner/controller wired.
/// - <b>ETB triggered ability (CR 603.6a)</b>: declares one 1..1 "any
///   target" <see cref="TargetRequest"/> — same shape as Murderous Redcap /
///   Pyrite Spellbomb. On resolution the effect:
///   <ol>
///     <li>Reads the chosen target off
///         <see cref="TriggeredAbility.ChosenTargets"/> (CR 603.3d — the
///         controller picks at trigger-on-stack time).</li>
///     <li>Counts Goblins on the controller's battlefield <em>including
///         Munitions Expert itself</em> — oracle reads "Goblins you
///         control" with no "other" qualifier. So a solo Munitions Expert
///         ETBs and pings for 1; with two friendly Goblins already out it
///         pings for 3 (1 Munitions Expert + 2 buddies).</li>
///     <li>Routes X damage to the chosen target via
///         <see cref="Fx.DealDamageAny"/> — Player → life loss
///         (CR 120.3), Creature → marked damage (CR 119.3), Planeswalker
///         → loyalty removal (CR 306.7). Same dispatch surface
///         Murderous Redcap / Pyrite Spellbomb / Lightning Bolt use.</li>
///   </ol>
///   The "you may" rider (CR 603.1 / CR 605.1) is modelled by simply
///   declining to choose a target — the engine's trigger-on-stack flow
///   surfaces the optional target request; a controller that picks no
///   target resolves to a no-op (CR 608.2b — illegal / absent targets
///   cause the relevant portion of the effect to do nothing).
///
/// ## "Mob math" parity with Krenko
/// X is read from the live battlefield snapshot at resolution time
/// (CR 608.2). Munitions Expert is a Goblin he controls, so the "Goblins
/// you control" count always includes himself unless he's left the
/// battlefield between trigger announce and resolution (e.g. blink, kill
/// spell on the ETB stack). The same self-counting rule applies to
/// <see cref="KrenkoMobBossFactory"/>'s tap ability — kept symmetric so
/// Goblin tribal stacking math is uniform across the engine.
///
/// ## Deferred (v1 gaps)
/// - <b>Optional ("you may") prompt</b>: the engine's
///   <see cref="TriggeredAbility"/> doesn't expose a "may decline"
///   posture at target-selection time today — callers / agents simply
///   omit the target to decline. A real prompt would surface the "you
///   may" branch as a yes/no choice before target picking (CR 603.6a /
///   CR 605.1). Same posture as Murderous Redcap (which is unconditional
///   "deals 2" — Munitions Expert's "may" rider is the only delta).
/// - <b>Damage prevention / replacement</b>: damage routing flows
///   through <see cref="Fx.DealDamageAny"/> directly; prevention
///   replacement effects (CR 615) are not wired at this call site —
///   matches Murderous Redcap / Burst Lightning posture.
/// </summary>
[CardName("Munitions Expert")]
public static class MunitionsExpertFactory
{
    public const string CardName = "Munitions Expert";
    public const string PrintedManaCost = "{R}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Munitions Expert owned and controlled by
    /// <paramref name="owner"/>. The ETB damage trigger is attached to the
    /// card shape; call <see cref="Majik.Core.Services.TriggerManager.BindCard"/>
    /// on the returned creature to register it with the live trigger
    /// manager so it fires off the bus.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Goblin, CardSubtype.Warrior });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB damage trigger — CR 603.6a + CR 107.1b (variable X).
        //   "When Munitions Expert enters, you may have it deal X damage
        //    to any target, where X is the number of Goblins you control."
        //
        // X-count semantics mirror Krenko: counts Goblins on controller's
        // battlefield INCLUDING Munitions Expert itself (oracle has no
        // "other" qualifier). Snapshot taken at resolution (CR 608.2).
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;
        var etbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        var etbEffect = new Effect(
            $"{CardName}: deal X damage to any target (X = Goblins you control)",
            () =>
            {
                if (etbTrigger == null) return;
                var chosen = etbTrigger.ChosenTargets;
                // CR 605.1 — declining the optional "may" rider, or no
                // target picked, resolves as a no-op.
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                var target = chosen[0][0];

                // CR 608.2 — X read at resolution. Includes Munitions
                // Expert itself ("Goblins you control" — no "other"
                // qualifier).
                var controller = card.Controller ?? owner;
                int x = controller.Zones.Battlefield.GetCards()
                    .Count(c => c.HasSubtype(CardSubtype.Goblin));

                if (x <= 0) return;
                Fx.DealDamageAny(target, x);
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "any target",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(etbTrigger);

        return card;
    }
}
