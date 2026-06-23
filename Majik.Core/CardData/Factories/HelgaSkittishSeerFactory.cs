using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Mana;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Helga, Skittish Seer (Bloomburrow — {G}{W}{U}).
///
/// Legendary Creature — Frog Druid 1/3. Oracle text (verified against Scryfall):
///   "Whenever you cast a creature spell with mana value 4 or greater, you draw
///    a card, gain 1 life, and put a +1/+1 counter on Helga.
///    {T}: Add X mana of any one color, where X is Helga's power. Spend this
///    mana only to cast creature spells with mana value 4 or greater or creature
///    spells with {X} in their mana costs."
///
/// The base shape (name, Legendary Creature — Frog Druid, {G}{W}{U}, 1/3) is
/// materialised from the embedded JSON definition (<c>helga-skittish-seer.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory.Build"/>. Both abilities are layered on top
/// here — the JSON <c>AbilityDefinition</c> schema doesn't express a
/// big-creature-cast trigger nor a power-scaled spend-restricted mana ability
/// (same posture as <see cref="MarwynTheNurturerFactory"/> /
/// <see cref="AncientZigguratFactory"/>).
///
/// ## Implemented (v1)
///
/// ### "Whenever you cast a creature spell with mana value 4 or greater, …" (CR 603.1)
/// A <see cref="TriggeredAbility"/> over <see cref="SpellCastEvent"/> firing when
/// THIS card's controller casts a spell whose card is a Creature (CR 110.4 /
/// CR 205.3) with mana value 4 or greater (CR 202.3 — mana value is the printed
/// cost's <see cref="ManaCost.TotalValue"/>). On resolution it runs the printed
/// three-part effect (CR 608.2 — resolved as one instruction in order):
///   * the controller draws a card (CR 120) via <see cref="Fx.DrawCards"/>;
///   * the controller gains 1 life (CR 119.3) via <see cref="Fx.GainLife"/>;
///   * a +1/+1 counter is placed on Helga (CR 122 / CR 121.2) via
///     <see cref="Fx.PlaceCounter"/>, routed through the replacement bus so
///     counter-doublers apply (CR 614.1c).
/// Helga's own cast does NOT self-trigger: her SpellCastEvent fires while she is
/// a creature spell on the stack, but her mana value is 3 (&lt; 4) so the
/// predicate fails.
///
/// ### "{T}: Add X mana of any one color, where X is Helga's power. …" (CR 605.1 / 107.1b)
/// Five <see cref="ManaAbility"/> instances (one per WUBRG), the "any one color"
/// modelling used by <see cref="AncientZigguratFactory"/> / Cavern of Souls. Each
/// is wired via the dynamic <c>Func&lt;ManaCost&gt;</c> generator overload so it
/// reads Helga's CURRENT power (<see cref="Creature.GetPower"/> — base 1 + +1/+1
/// counters + continuous P/T effects, CR 122.6 / CR 613) at activation (CR 605.1 —
/// mana abilities resolve atomically; X is counted then). The generator returns a
/// <see cref="ManaCost"/> of X pips of that one colour. If Helga's power is 0 or
/// less the ability produces no mana.
///
/// ## Spend-restriction (enforced)
/// Each "any colour" ability stamps a shared <see cref="SpendRestriction"/> with
/// the predicate
///   <c>spell =&gt; spell.Card.HasType(CardType.Creature)
///                 &amp;&amp; (spell.Card.ManaCostValue.TotalValue &gt;= 4
///                          || spell.Card.ManaCostValue.HasX)</c>
/// (CR 106.4 — mana with a spend restriction can only pay for objects matching
/// the restriction). The payment gate in
/// <see cref="Majik.Core.Costs.ManaPaymentResolver"/> is live: it rides the
/// restriction onto each produced colored unit (a
/// <see cref="Majik.Core.Mana.ManaProvenanceSlot"/>) and withholds those units
/// from any spend whose cast object the restriction doesn't satisfy — so Helga's
/// mana can only pay creature spells with MV ≥ 4 or creature spells with {X} in
/// their cost. Same gate as Ancient Ziggurat / Cavern of Souls.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — single-arg dispatcher path
///   (<see cref="NamedCardFactory"/>). The cast trigger is attached for shape
///   observability but NOT registered with any <see cref="TriggerManager"/>.
/// - <see cref="Create(Player, TriggerManager?)"/> — registers the cast trigger
///   so a qualifying creature-spell <see cref="SpellCastEvent"/> fires it.
///
/// ## Deferred (v1 gaps)
/// - <b>LTB unregister</b>: the cast trigger's <c>activeZones</c> gates it to the
///   battlefield so it no-ops once Helga leaves play (CR 603.6c). The mana
///   abilities' <c>canActivateCheck</c> short-circuits on tapped Helga; summoning
///   sickness gating happens upstream at activation validation (CR 302.6),
///   same posture as Marwyn / Ancient Ziggurat.
/// </summary>
[CardName("Helga, Skittish Seer")]
public static class HelgaSkittishSeerFactory
{
    public const string CardName = "Helga, Skittish Seer";
    public const string Slug = "helga-skittish-seer";

    /// <summary>Mana-value threshold for the cast trigger and the spend gate.</summary>
    public const int ManaValueThreshold = 4;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    // CR 106.4 — "Spend this mana only to cast creature spells with mana value 4
    // or greater or creature spells with {X} in their mana costs." Shared static
    // restriction so every "any colour" ManaAbility stamps the same by-reference
    // predicate (SpendRestriction equality is delegate-by-ref).
    private static readonly SpendRestriction BigOrXCreatureSpellOnly =
        new("creature spell with mana value 4+ or {X} in its cost",
            spell =>
            {
                if (!spell.Card.HasType(CardType.Creature)) return false;
                // CR 202.3 — mana value is the printed cost's total. ICard
                // exposes only the cost STRING, so parse it. CR 107.3 — {X}
                // counts as 0 toward mana value, surfaced via ManaCost.HasX.
                var cost = ManaCost.Parse(spell.Card.ManaCost);
                return cost.TotalValue >= ManaValueThreshold || cost.HasX;
            });

    /// <summary>
    /// Single-arg dispatcher path. The cast trigger is attached structurally so
    /// the card shape is correct, but it is NOT registered with any
    /// <see cref="TriggerManager"/>. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null);

    /// <summary>
    /// Construct a fully-wired Helga, Skittish Seer.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">TriggerManager the cast trigger registers with so a
    /// qualifying creature-spell <see cref="SpellCastEvent"/> fires it
    /// automatically. May be null — the trigger is still attached to the card
    /// shape.</param>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (Legendary Creature —
        // Frog Druid, {G}{W}{U}, 1/3). The JSON carries no abilities — both are
        // layered below.
        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);

        BuildCastTrigger(card, owner, triggers);
        BuildManaAbilities(card, owner);

        return card;
    }

    // --- "Whenever you cast a creature spell with mv 4+, draw/gain/counter" ---

    private static void BuildCastTrigger(Creature card, Player owner, TriggerManager? triggers)
    {
        // CR 603.1 — "Whenever you cast a creature spell with mana value 4 or
        // greater, …":
        //   * the spell's controller is this card's controller ("you cast"),
        //   * the spell's card is a Creature (CR 110.4 — a creature spell),
        //   * its mana value (CR 202.3) is 4 or greater.
        var condition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            var controller = card.Controller ?? owner;
            if (!ReferenceEquals(e.Spell.Controller, controller)) return false;
            if (!e.Spell.Card.HasType(CardType.Creature)) return false;
            // CR 202.3 — mana value from the printed cost string (ICard exposes
            // only the cost string).
            return ManaCost.Parse(e.Spell.Card.ManaCost).TotalValue >= ManaValueThreshold;
        });

        var effect = new Effect(
            $"{CardName} — draw a card, gain 1 life, and put a +1/+1 counter on Helga",
            () =>
            {
                var controller = card.Controller ?? owner;

                // CR 120 — you draw a card.
                Fx.DrawCards(controller, 1);

                // CR 119.3 — you gain 1 life.
                Fx.GainLife(controller, 1);

                // CR 122 / CR 121.2 — put a +1/+1 counter on Helga. Routed
                // through Fx.PlaceCounter so the replacement bus (Hardened
                // Scales / Doubling Season) can adjust the amount (CR 614.1c).
                Fx.PlaceCounter(card, CounterType.PlusOnePlusOne, 1);
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { effect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);
    }

    // --- "{T}: Add X mana of any one color, where X is Helga's power" ---

    private static void BuildManaAbilities(Creature card, Player owner)
    {
        // CR 605.1 — mana ability (no stack); CR 107.1b — X resolves when the
        // effect determines it. Five abilities (one per WUBRG) model "any one
        // color" (Ancient Ziggurat / Cavern shape). Each reads Helga's CURRENT
        // power at activation (base 1 + +1/+1 counters + continuous P/T effects,
        // CR 122.6 / CR 613) and produces that many pips of its one colour, all
        // carrying the big-or-{X}-creature-spell SpendRestriction (CR 106.4).
        foreach (var color in new[] { "W", "U", "B", "R", "G" })
        {
            var pip = color; // capture
            card.AddAbility(new ManaAbility(
                source: card,
                controller: owner,
                manaGenerator: () =>
                {
                    int power = card.GetPower();
                    if (power <= 0) return ManaCost.Zero;
                    return ManaCost.Parse(string.Concat(Enumerable.Repeat($"{{{pip}}}", power)));
                },
                canActivateCheck: () => !card.IsTapped,
                printedManaGenerated: null,
                spendRestriction: BigOrXCreatureSpellOnly));
        }
    }
}
