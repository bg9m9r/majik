using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Marwyn, the Nurturer (Dominaria — {2}{G}).
///
/// Legendary Creature — Elf Druid 1/1. Oracle text (verified against Scryfall):
///   "Whenever another Elf you control enters, put a +1/+1 counter on Marwyn.
///    {T}: Add an amount of {G} equal to Marwyn's power."
///
/// A self-growing Elf mana engine: every OTHER Elf that enters pumps Marwyn
/// with a +1/+1 counter, and her {T} mana ability scales with her (counter-fed)
/// power. The base shape (name, Legendary Creature — Elf Druid, {2}{G}, 1/1) is
/// materialised from the embedded JSON definition (<c>marwyn-the-nurturer.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two abilities are layered on
/// top here — the JSON <c>AbilityDefinition</c> schema doesn't express an
/// other-Elf-enters counter trigger nor a power-scaled mana ability (same
/// posture as <see cref="ElvishWarmasterFactory"/> /
/// <see cref="ElvishArchdruidFactory"/>).
///
/// ## Implemented (v1)
///
/// ### "Whenever another Elf you control enters, put a +1/+1 counter on Marwyn." (CR 603.1)
/// A <see cref="TriggeredAbility"/> over <see cref="CardMovedEvent"/> fires when
/// another permanent enters the controller's battlefield that is an Elf
/// (ToZone == Battlefield, controller-owned, has the Elf subtype, and is NOT
/// Marwyn herself — "another"). On resolution it puts one +1/+1 counter on
/// Marwyn (CR 122 / CR 121.2) via <see cref="Majik.Core.Primitives.Fx.PlaceCounter"/>,
/// routed through the replacement bus so counter-doublers (Hardened Scales /
/// Doubling Season) apply. Marwyn herself is an Elf, but the "another"
/// qualifier excludes her own ETB (mirrors <see cref="ElvishWarmasterFactory"/>'s
/// "other Elves" predicate). Unlike the Warmaster there is no once-per-turn
/// lock — every other-Elf-enter adds a counter (CR 603.1).
///
/// ### "{T}: Add an amount of {G} equal to Marwyn's power." (CR 605.1 / 107.1b)
/// A <see cref="ManaAbility"/> wired via the <c>Func&lt;ManaCost&gt;</c>
/// generator overload (Nykthos / Elvish-Archdruid shape). The generator reads
/// Marwyn's CURRENT power (<see cref="Creature.GetPower"/>) — which includes the
/// +1/+1 counters fed by the trigger (CR 122.6 — a +1/+1 counter raises power)
/// as well as any other continuous effects (CR 613) — and returns a
/// <see cref="ManaCost"/> of that many green pips.
///
/// ## X-count semantics
/// - Counted at activation (CR 605.1 — mana abilities don't use the stack; the
///   generator runs atomically). Same snapshot posture as Elvish Archdruid's
///   {T} ability.
/// - Reads Marwyn's effective power, so +1/+1 counters AND continuous P/T
///   effects feed the amount. Marwyn alone (1 power) produces {G}.
/// - If Marwyn's power is 0 or less the ability produces no mana.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — single-arg dispatcher path
///   (<see cref="NamedCardFactory"/>). The counter trigger is attached for shape
///   observability but NOT registered with any <see cref="TriggerManager"/>.
/// - <see cref="Create(Player, TriggerManager?)"/> — registers the counter
///   trigger so an Elf-enter <see cref="CardMovedEvent"/> fires it automatically.
///
/// ## Deferred (v1 gaps)
/// - <b>LTB unregister</b>: the counter trigger's <c>activeZones</c> gates it to
///   the battlefield so it no-ops once Marwyn leaves play (CR 603.6c). The mana
///   ability's <c>canActivateCheck</c> short-circuits on tapped Marwyn;
///   summoning-sickness gating happens upstream at activation validation (CR
///   302.1 / 302.6), same posture as Elvish Archdruid.
/// </summary>
[CardName("Marwyn, the Nurturer")]
public static class MarwynTheNurturerFactory
{
    public const string CardName = "Marwyn, the Nurturer";
    public const string Slug = "marwyn-the-nurturer";

    /// <summary>
    /// Single-arg dispatcher path. The counter trigger is attached structurally
    /// so the card shape is correct, but it is NOT registered with any
    /// <see cref="TriggerManager"/>. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null);

    /// <summary>
    /// Construct a fully-wired Marwyn, the Nurturer.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">TriggerManager the counter trigger registers with
    /// so an other-Elf-enter <see cref="CardMovedEvent"/> fires it automatically.
    /// May be null — the trigger is still attached to the card shape.</param>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (Legendary Creature —
        // Elf Druid, {2}{G}, 1/1). The JSON carries no abilities — both are
        // layered below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        BuildCounterTrigger(card, owner, triggers);
        BuildManaAbility(card, owner);

        return card;
    }

    // --- "Whenever another Elf you control enters, +1/+1 counter" (CR 603.1) ---

    private static void BuildCounterTrigger(Creature card, Player owner, TriggerManager? triggers)
    {
        // CR 603.1 — "Whenever ANOTHER Elf YOU control enters, put a +1/+1
        // counter on Marwyn."
        //   * ToZone == Battlefield (something entered the battlefield),
        //   * the entering card is an Elf creature,
        //   * its controller is this card's controller ("you control"),
        //   * it is NOT Marwyn herself ("another").
        var condition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            if (e.ToZone != ZoneType.Battlefield) return false;
            if (ReferenceEquals(e.Card, card)) return false; // "another"
            if (e.Card is not Creature entered) return false;
            if (!entered.HasSubtype(CardSubtype.Elf)) return false;

            var controller = card.Controller ?? owner;
            return ReferenceEquals(entered.Controller, controller);
        });

        var counterEffect = new Effect(
            $"{CardName} — put a +1/+1 counter on Marwyn",
            // CR 122 / CR 121.2 — put a +1/+1 counter on Marwyn. Routed through
            // Fx.PlaceCounter so the replacement bus (Hardened Scales / Doubling
            // Season) can adjust the amount (CR 614.1c).
            () => Majik.Core.Primitives.Fx.PlaceCounter(card, CounterType.PlusOnePlusOne, 1));

        var counterTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { counterEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(counterTrigger);
        triggers?.RegisterTriggeredAbility(counterTrigger);
    }

    // --- "{T}: Add an amount of {G} equal to Marwyn's power" (CR 605.1) ---

    private static void BuildManaAbility(Creature card, Player owner)
    {
        // CR 605.1 — mana ability (no stack); CR 107.1b — X resolves at the
        // moment the effect determines it. Wired via the Func<ManaCost>
        // generator overload (Elvish Archdruid / Nykthos shape) so Marwyn's
        // CURRENT power (base 1 + +1/+1 counters + any continuous P/T effects,
        // CR 122.6 / CR 613) is read at each activation.
        card.AddAbility(new ManaAbility(
            source: card,
            controller: owner,
            manaGenerator: () =>
            {
                int power = card.GetPower();
                if (power <= 0) return ManaCost.Zero;

                // Build "{G}{G}...{G}" with `power` green pips.
                return ManaCost.Parse(string.Concat(Enumerable.Repeat("{G}", power)));
            },
            canActivateCheck: () => !card.IsTapped));
    }
}
