using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Murderous Redcap (Shadowmoor, {2}{B}{R}).
///
/// Creature — Goblin Assassin 2/2. Oracle text:
///   "When Murderous Redcap enters the battlefield, it deals 2 damage to any
///    target.
///    Persist (When this creature dies, if it had no -1/-1 counters on it,
///    return it to the battlefield under its owner's control with a -1/-1
///    counter on it.)"
///
/// ## Implemented (v1)
/// - 2/2 Creature — Goblin Assassin (CardSubtype.Goblin + CardSubtype.Assassin),
///   mana cost {2}{B}{R}.
/// - <b>ETB triggered ability (CR 603.6a)</b>: declares a 1..1 "any target"
///   <see cref="TargetRequest"/>. On resolution the chosen target receives
///   2 damage via <see cref="Fx.DealDamageAny"/> — same shape Pyrite
///   Spellbomb / Lightning Bolt / Lightning Helix use. Planeswalker targets
///   convert to loyalty removal (CR 306.7); player targets become life loss
///   (CR 120.3).
/// - <b>Persist (CR 702.79)</b>: wired via the shared
///   <see cref="PersistFactory.Build(Creature)"/> primitive. The ETB damage
///   trigger fires again on Persist-return when the return is routed through
///   <see cref="Services.ZoneService"/> — the canonical Persist+ETB damage
///   pattern that turned Murderous Redcap into a Shadowmoor staple.
///
/// ## Deferred (v1 gaps)
/// - The Persist primitive's raw graveyard → battlefield zone-move does not
///   republish <see cref="CardMovedEvent"/>, so the ETB damage trigger does
///   not auto-fire on the in-effect return (same posture as Kitchen Finks /
///   the Undying-shape primitives). Fully-wired game flow that routes Persist
///   returns through ZoneService would re-fire the ETB.
/// </summary>
[CardName("Murderous Redcap")]
public static class MurderousRedcapFactory
{
    public const string CardName = "Murderous Redcap";
    public const string PrintedManaCost = "{2}{B}{R}";
    public const int Power = 2;
    public const int Toughness = 2;
    public const int EtbDamage = 2;

    /// <summary>
    /// Construct Murderous Redcap owned and controlled by
    /// <paramref name="owner"/>. The ETB damage trigger + Persist trigger
    /// are attached to the card shape; call
    /// <see cref="Majik.Core.Services.TriggerManager.BindCard"/> on the
    /// returned creature to register them with the live trigger manager.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Goblin, CardSubtype.Assassin });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB damage trigger (CR 603.6a). Declares one "any target"
        // TargetRequest — the bot / agent picks at trigger-on-stack time
        // (CR 603.3d). Resolution reads ChosenTargets and routes through
        // Fx.DealDamageAny for Player / Creature / Planeswalker dispatch.
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;
        var etbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        var etbEffect = new Effect(
            $"{CardName}: deal {EtbDamage} damage to any target",
            () =>
            {
                if (etbTrigger == null) return;
                var chosen = etbTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                var target = chosen[0][0];
                Fx.DealDamageAny(target, EtbDamage);
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

        // ----------------------------------------------------------------
        // Persist (CR 702.79) — keyword marker + death trigger from the
        // shared primitive.
        // ----------------------------------------------------------------
        PersistFactory.Build(card);

        return card;
    }
}
