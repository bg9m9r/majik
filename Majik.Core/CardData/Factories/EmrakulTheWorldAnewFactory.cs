using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Emrakul, the World Anew (Modern Horizons 3, {12}).
///
/// Legendary Creature — Eldrazi 12/12. Oracle text (Scryfall, verified):
///   "When you cast this spell, gain control of all creatures target player
///    controls.
///    Flying, protection from spells and from permanents that were cast this
///    turn
///    When Emrakul leaves the battlefield, sacrifice all creatures you
///    control.
///    Madness—Pay six {C}."
///
/// ## Madness — the deferral this factory closes (CR 702.35)
/// "Madness—Pay six {C}" is a <em>pure colorless</em> mana cost
/// (<c>{C}{C}{C}{C}{C}{C}</c>, six colorless pips, mana value six — CR 107.4c),
/// NOT a non-mana {special} rider. It rides the same name → mana-cost shape as
/// every other entry in <see cref="Majik.Core.Keywords.MadnessCatalog"/>, which
/// now catalogues Emrakul, so the whole intrinsic discard → exile →
/// cast-for-madness funnel (<see cref="Majik.Core.Primitives.Fx.DiscardCard"/>)
/// works for it with no per-card wiring.
///
/// ## Implemented (v1)
/// - <b>12/12 Legendary Creature — Eldrazi at {12}</b> (mana value 12,
///   colourless — CR 105.2c, no coloured symbols). Built directly as a
///   <see cref="Creature"/> (same posture as
///   <see cref="EmrakulTheAeonsTornFactory"/>, no JSON definition needed).
/// - <b>Flying (CR 702.9)</b>: <see cref="KeywordAbility"/>("Flying") marker —
///   combat code reads via <see cref="Majik.Core.Combat.CombatAbilities"/>.
/// - <b>Cast trigger — "When you cast this spell, gain control of all
///   creatures target player controls." (CR 603.6a / CR 613.2)</b>: a
///   <see cref="TriggeredAbility"/> over Emrakul's own
///   <see cref="SpellCastEvent"/> while it is on the stack
///   (<c>activeZones = {Stack}</c>, same shape as
///   <see cref="EmrakulThePromisedEndFactory"/>'s cast trigger). A 1..1
///   "target player" request gathers every player live; on resolution every
///   creature the chosen player controls gets a Layer-2
///   <see cref="ControlChangeEffect"/> registered against the live
///   <see cref="ContinuousEffectsService"/> (resolved through
///   <see cref="ContinuousEffectsServiceProvider"/>, the same indirection
///   <see cref="EmrakulThePromisedEndFactory"/> uses for
///   <see cref="Majik.Core.Players.ControlPlayerRegistryProvider"/>). The
///   control change persists while each creature is on the battlefield
///   (CR 613.2) — there is no duration clause, so it is not tied to Emrakul.
/// - <b>LTB trigger — "When Emrakul leaves the battlefield, sacrifice all
///   creatures you control." (CR 603.6c / CR 603.6d)</b>: a
///   <see cref="TriggeredAbility"/> over <see cref="CardMovedEvent"/> filtered
///   to Emrakul moving <c>Battlefield → anywhere</c> (covers dies / bounce /
///   exile / flicker — the engine's "leaves the battlefield" signal, same
///   condition shape as the exile-enchantment LTB pair). On resolution every
///   creature Emrakul's controller controls is sacrificed via
///   <see cref="Majik.Core.Primitives.Fx.Sacrifice(ICard, Player, IEventBus)"/>.
///   Active in the Battlefield zone so the trigger fires on the leave event.
///
/// ## Deferred sub-caveat (documented, not modelled — see stillMissing)
/// - <b>Protection from spells and from permanents that were cast this turn
///   (CR 702.16)</b>: shipped as a <see cref="ProtectionAbility"/> marker
///   ("spells and permanents cast this turn") for discoverability + the
///   spell-side gate ("protection from spells" → every spell is being cast,
///   so the predicate matches all spells). The "permanents that were cast
///   this turn" leg needs a per-turn "was this permanent cast this turn"
///   tracker the engine does not yet expose (Card.WasCast is not turn-scoped),
///   so the blocking / damage gates against such permanents are not yet wired
///   — same marker-first posture as the other Emrakul protections
///   (<see cref="EmrakulTheAeonsTornFactory"/> / Promised End). This does not
///   regress any existing behaviour.
/// </summary>
[CardName("Emrakul, the World Anew")]
public static class EmrakulTheWorldAnewFactory
{
    public const string CardName = "Emrakul, the World Anew";
    public const string PrintedManaCost = "{12}";
    public const int Power = 12;
    public const int Toughness = 12;

    /// <summary>
    /// Construct Emrakul with no live wiring. All markers + the cast trigger
    /// + the LTB trigger + the protection predicate are attached; nothing
    /// registers with a trigger bus, and the control / sacrifice resolutions
    /// look up live services through their providers (no-op when none is
    /// installed). Suitable for dispatcher / structural tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Emrakul. When <paramref name="triggers"/> is supplied, both
    /// the cast trigger and the LTB trigger register with the bus so the
    /// events automatically place the abilities on the stack (CR 603.2).
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Eldrazi });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying marker.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // CR 702.16 — "protection from spells and from permanents that were
        // cast this turn." Marker + spell-side predicate. Every spell is, by
        // definition, being cast (it is on the stack as a spell), so the
        // predicate matches all spells — the "protection from spells" leg.
        // The "permanents cast this turn" leg is documented-deferred (no
        // turn-scoped per-permanent cast tracker yet — see class xmldoc).
        card.AddAbility(new ProtectionAbility(
            "spells and permanents cast this turn",
            spellPredicate: _ => true));

        card.AddAbility(BuildCastTrigger(card, owner, triggers));
        card.AddAbility(BuildLeavesBattlefieldTrigger(card, owner, triggers));

        return card;
    }

    /// <summary>
    /// CR 603.6a / CR 613.2 — "When you cast this spell, gain control of all
    /// creatures target player controls." Fires on Emrakul's own
    /// <see cref="SpellCastEvent"/> while it is on the stack. On resolution
    /// every creature the chosen target player controls gets a Layer-2
    /// <see cref="ControlChangeEffect"/> registered against the live
    /// <see cref="ContinuousEffectsService"/>.
    /// </summary>
    private static TriggeredAbility BuildCastTrigger(
        Creature card, Player owner, TriggerManager? triggers)
    {
        // CR 603.6a — "when you cast this spell": fires only for Emrakul's own
        // SpellCastEvent (reference identity against the card).
        var condition = new EventTriggerCondition<SpellCastEvent>(
            (e, _) => ReferenceEquals(e.Spell.Card, card));

        TriggeredAbility? trigger = null;
        var effect = new Effect(
            $"{CardName}: gain control of all creatures target player controls (CR 613.2)",
            () =>
            {
                if (trigger == null) return;
                if (trigger.ChosenTargets.Count == 0) return;
                if (trigger.ChosenTargets[0].Count == 0) return;
                // CR 608.2b — illegal / no target → no-op.
                if (trigger.ChosenTargets[0][0] is not Player targetPlayer) return;

                // CR 613.2 — Layer-2 control change for EACH creature the
                // target player controls at resolution. The CES is resolved
                // live via the provider (keyed by Emrakul's controller); null
                // in shape-only construction → no-op (trigger still attached
                // for shape inspection).
                var effects = ContinuousEffectsServiceProvider.Get(owner);
                if (effects == null) return;

                var creatures = targetPlayer.Zones.Battlefield.GetCards()
                    .OfType<Creature>()
                    .ToList();
                foreach (var creature in creatures)
                {
                    // CR 613.2 — gain control: register a control-changing
                    // effect granting Emrakul's controller control. No
                    // duration clause → persists while the creature is on the
                    // battlefield (ControlChangeEffect.IsActive).
                    effects.Register(new ControlChangeEffect(creature, owner));
                }
            });

        trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { effect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target player",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    // CR 109.5 — "target player": any player in the game
                    // (including Emrakul's controller — there is no "opponent"
                    // restriction in the printed text).
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .Cast<object>()
                        .ToList()),
            },
            // On-cast trigger — active while the spell is on the stack
            // (mirrors Emrakul, the Promised End / Bloodbraid Elf's cascade).
            activeZones: new[] { ZoneType.Stack });

        triggers?.RegisterTriggeredAbility(trigger);
        return trigger;
    }

    /// <summary>
    /// CR 603.6c / CR 603.6d — "When Emrakul leaves the battlefield, sacrifice
    /// all creatures you control." Fires on Emrakul moving Battlefield →
    /// anywhere (the engine's "leaves the battlefield" signal — covers dies /
    /// bounce / exile / flicker). On resolution every creature Emrakul's
    /// controller controls is sacrificed.
    /// </summary>
    private static TriggeredAbility BuildLeavesBattlefieldTrigger(
        Creature card, Player owner, TriggerManager? triggers)
    {
        var condition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card)
                && e.FromZone == ZoneType.Battlefield
                && e.ToZone != ZoneType.Battlefield);

        var effect = new Effect(
            $"{CardName}: sacrifice all creatures you control (CR 603.6d)",
            () =>
            {
                var controller = card.Controller ?? owner;
                if (controller == null) return;

                var bus = Majik.Core.Events.EventBusRegistry.Get(controller);

                // Snapshot first — Fx.Sacrifice mutates the battlefield.
                var creatures = controller.Zones.Battlefield.GetCards()
                    .OfType<Creature>()
                    .ToList();
                foreach (var creature in creatures)
                {
                    if (bus is not null)
                    {
                        Majik.Core.Primitives.Fx.Sacrifice(creature, controller, bus);
                    }
                    else
                    {
                        Majik.Core.Primitives.Fx.Sacrifice(creature);
                    }
                }
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { effect },
            // Leaves-the-battlefield triggers look back from the battlefield
            // (CR 603.6d / 603.10) — active in the Battlefield zone so the
            // trigger fires on the leave event.
            activeZones: new[] { ZoneType.Battlefield });

        triggers?.RegisterTriggeredAbility(trigger);
        return trigger;
    }
}
