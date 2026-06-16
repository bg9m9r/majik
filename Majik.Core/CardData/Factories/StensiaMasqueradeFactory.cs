using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Stensia Masquerade (Shadows over Innistrad, {2}{R}).
///
/// Enchantment. Oracle text (verified against Scryfall):
///   "Attacking creatures you control have first strike.
///    Whenever a Vampire you control deals combat damage to a player, put a
///    +1/+1 counter on it.
///    Madness {2}{R} (If you discard this card, discard it into exile. When
///    you do, cast it for its madness cost or put it into your graveyard.)"
///
/// ## Implemented (v1)
/// - <b>Enchantment {2}{R}</b> — vanilla <see cref="Enchantment"/> shell.
/// - <b>"Whenever a Vampire you control deals combat damage to a player, put
///   a +1/+1 counter on it." (CR 510 / CR 603.1)</b> — a global
///   <see cref="TriggeredAbility"/> over <see cref="CombatDamageDealtEvent"/>,
///   gated on the damage SOURCE being (a) a <see cref="Creature"/> with the
///   <see cref="CardSubtype.Vampire"/> subtype, (b) controlled by Stensia
///   Masquerade's controller ("you control" — CR 109.4), and (c) dealing the
///   damage to a player (<see cref="CombatDamageDealtEvent.TargetPlayer"/> is
///   non-null — CR 510.1c). On resolution a single
///   <see cref="CounterType.PlusOnePlusOne"/> counter is added to <em>that</em>
///   Vampire — the damage source, NOT the enchantment ("put a +1/+1 counter on
///   it", where "it" is the Vampire — CR 122.1). The triggering Vampire is
///   captured off the event by the predicate and re-checked on the battlefield
///   at resolution. The live <see cref="TriggerManager"/> auto-binds this
///   ability when Stensia enters the battlefield (it scans
///   <c>card.Abilities.OfType&lt;ITriggeredAbility&gt;()</c> on the first
///   <see cref="Events.CardMovedEvent"/>), so the trigger fires in real games
///   without an explicit register call.
/// - <b>"Attacking creatures you control have first strike." (CR 702.7 /
///   CR 613.1f)</b> — a <see cref="AttackingControlledKeywordStaticEffect"/>
///   granting <c>"First strike"</c> (Layer 6) to every attacking creature the
///   controller controls. Registered against the per-match
///   <see cref="ContinuousEffectsService"/>; <see cref="CombatAbilities"/>
///   reads the effective keyword set, so an attacking creature gains first
///   strike for the combat-damage steps (CR 702.7c). Gated on the live combat
///   membership via <see cref="Combat.CombatMembershipRegistryProvider"/>.
///
/// ## Lifecycle
/// The single-arg <see cref="Create(Player)"/> overload produces a shape-only
/// card with the trigger attached (auto-bound on ETB) but no continuous
/// first-strike static (no layers service). The
/// <see cref="Create(Player, ContinuousEffectsService)"/> overload — the one
/// the production <c>GameFacade</c> routed build dispatches to (CR 613.7c) —
/// additionally registers the first-strike static.
///
/// ## Deferred (v1 gaps)
/// - <b>Madness {2}{R}</b> — the discard-into-exile alternative cost is
///   handled engine-wide by the <c>MadnessCatalog</c> + the
///   <c>Fx.DiscardCard</c> replacement funnel (CR 702.35); no per-card body is
///   required here. The enchantment's printed cost / type carry it.
/// </summary>
[CardName("Stensia Masquerade")]
public static class StensiaMasqueradeFactory
{
    public const string CardName = "Stensia Masquerade";
    public const string PrintedManaCost = "{2}{R}";

    /// <summary>
    /// Shape-only constructor — the combat-damage trigger is attached (and
    /// auto-binds when the card enters the battlefield) but the first-strike
    /// static is NOT registered (no layers service). Suitable for
    /// factory-shape / dispatcher tests.
    /// </summary>
    public static Enchantment Create(Player owner) => Create(owner, continuousEffects: null);

    /// <summary>
    /// Construct Stensia Masquerade. When <paramref name="continuousEffects"/>
    /// is supplied the "attacking creatures you control have first strike"
    /// static is registered against it (CR 613.7c). The combat-damage
    /// +1/+1-counter trigger is always attached and auto-binds to the live
    /// <see cref="TriggerManager"/> on the enchantment's first zone crossing.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service to register the
    /// attacking-creatures first-strike static against. May be null — no live
    /// first-strike grant (the trigger still works).</param>
    public static Enchantment Create(Player owner, ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(name: CardName, manaCost: PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // "Whenever a Vampire you control deals combat damage to a player,
        //  put a +1/+1 counter on it." — CR 510 / CR 603.1 / CR 122.1.
        //
        // The predicate captures the triggering Vampire (the damage SOURCE)
        // so resolution puts the counter on THAT creature, not the
        // enchantment. "You control" reads the LIVE controller of Stensia
        // Masquerade so a control change (CR 720) re-scopes correctly.
        // ----------------------------------------------------------------
        Creature? lastDamagingVampire = null;

        var counterEffect = new Effect(
            $"{CardName}: put a +1/+1 counter on the Vampire that dealt combat damage",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;
                var vampire = lastDamagingVampire;
                if (vampire == null) return;
                if (vampire.Zone != ZoneType.Battlefield) return; // gone since the trigger fired
                vampire.Counters.Add(CounterType.PlusOnePlusOne, 1);
            });

        var counterTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CombatDamageDealtEvent>((e, _) =>
            {
                var vampire = TryGetControlledVampireToPlayer(e, card, owner);
                if (vampire == null) return false;
                lastDamagingVampire = vampire;
                return true;
            }),
            effects: new IEffect[] { counterEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(counterTrigger);

        // ----------------------------------------------------------------
        // "Attacking creatures you control have first strike." — CR 702.7 /
        // CR 613.1f. A continuous Layer-6 keyword grant scoped to the
        // controller's attacking creatures (read off the live combat
        // membership registry). Only registered when a layers service is
        // available (production GameFacade build path).
        // ----------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(
                new AttackingControlledKeywordStaticEffect(card, "First strike"));
        }

        return card;
    }

    /// <summary>
    /// CR 510.1c / CR 109.4 — returns the damage source iff it is a Vampire
    /// creature the source enchantment's controller controls and the combat
    /// damage was dealt to a PLAYER (not a creature / planeswalker). Returns
    /// null otherwise.
    /// </summary>
    private static Creature? TryGetControlledVampireToPlayer(
        CombatDamageDealtEvent e,
        Enchantment card,
        Player owner)
    {
        // "to a player" — CombatDamageDealtEvent.TargetPlayer is non-null only
        // for the player-target overload (creature / planeswalker damage
        // leaves it null).
        if (e.TargetPlayer == null) return null;
        if (e.Source is not Creature vampire) return null;
        if (!vampire.HasSubtype(CardSubtype.Vampire)) return null;
        // "you control" — the LIVE controller of Stensia Masquerade (CR 720).
        var ctrl = card.Controller ?? owner;
        if (!ReferenceEquals(vampire.Controller, ctrl)) return null;
        return vampire;
    }
}
