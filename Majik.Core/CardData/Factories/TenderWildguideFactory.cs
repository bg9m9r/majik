using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Keywords;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tender Wildguide (Bloomburrow, {1}{G}).
///
/// Creature — Possum Druid, 2/2. Oracle text (Scryfall, verified 2026-06-24):
///   "Offspring {2} (You may pay an additional {2} as you cast this spell. If
///    you do, when this creature enters, create a 1/1 token copy of it.)
///    {T}: Add one mana of any color.
///    {T}: Put a +1/+1 counter on this creature."
///
/// ## Base shape (CardDef DSL)
///
/// The body — name, Possum Druid subtypes, {1}{G} 2/2 — plus the two
/// {T} abilities are materialised data-driven via the <see cref="CardDef"/>
/// DSL + <see cref="CardDefRuntime.Build"/>:
/// <list type="bullet">
///   <item><b>"{T}: Add one mana of any color."</b> — modeled as five
///   <see cref="ManaAbility"/> instances (one per WUBRG, CR 605.1 — mana
///   abilities don't use the stack), the same any-colour shape Birds of
///   Paradise / Ornithopter of Paradise use; the mana picker can satisfy any
///   single colour pip via this creature.</item>
///   <item><b>"{T}: Put a +1/+1 counter on this creature."</b> — a
///   <see cref="TapSelfCostDef"/> ({T}) cost + a self-targeted
///   <see cref="PutCounterEffectDef"/> (<c>Target = "self"</c>, CR 122.1 —
///   the counter lands on the SOURCE permanent with no target slot reserved),
///   the same self-counter posture as the Stonecoil/Walking Ballista family's
///   enters-with-counters shape, here on an activated ability (CR 602.2).</item>
/// </list>
///
/// ## Offspring {2} (CR 702.169)
///
/// Layered on after the DSL build because neither the Offspring additional
/// cost nor its ETB token-copy trigger is expressible in the CardDef DSL yet
/// (same posture as Manifold Mouse / Pawpatch Recruit). Wired through the
/// generic Offspring subsystem: <see cref="OffspringAdditionalCost"/> (the
/// optional additional cast cost, CR 702.169a — drains {2} and stamps
/// <see cref="Card.WasOffspringPaid"/>) + <see cref="OffspringAbility.Attach"/>
/// (the ETB trigger, CR 702.169b — when this creature enters, if its Offspring
/// cost was paid, create a 1/1 token copy of it). The caller layers
/// <see cref="BuildOffspringCost"/> onto the cast via
/// <see cref="Majik.Core.Game.SpellCastFlow"/>'s <c>additionalCosts</c> when
/// the caster chooses to pay; declining simply omits it. The
/// <see cref="KeywordAbility"/> marker (<c>arg: 2</c>) keeps the keyword scan
/// surface uniform with the other Offspring creatures.
/// </summary>
[CardName(CardName)]
public static class TenderWildguideFactory
{
    public const string CardName = "Tender Wildguide";
    public const string PrintedManaCost = "{1}{G}";
    public const string OffspringCostText = "{2}";

    /// <summary>CR 702.169 — the Offspring additional cost ({2}). Exposed so
    /// callers build the cost without hard-coding the value.</summary>
    public static Majik.Core.ValueObjects.ManaCost OffspringCost =>
        Majik.Core.ValueObjects.ManaCost.Parse(OffspringCostText);

    /// <summary>The DSL base shape (name, Possum Druid 2/2, the five any-colour
    /// mana abilities, the self-counter activated ability, the Offspring
    /// keyword marker). The Offspring ETB trigger is layered on in
    /// <see cref="Create(Player, TriggerManager?)"/> — the DSL cannot express
    /// it.</summary>
    public static CardDef Define()
    {
        // "{T}: Put a +1/+1 counter on this creature." — CR 602.2 activated
        // ability, CR 122.1 self-counter (Target = "self" reserves no target
        // slot; the counter lands on the source permanent).
        var counterAbility = new ActivatedAbilityDefinition
        {
            Costs = { new TapSelfCostDef() },
            Effects =
            {
                new PutCounterEffectDef { Counter = "+1/+1", Amount = 1, Target = "self" },
            },
        };

        var builder = CardDef
            .Creature(CardName, PrintedManaCost, power: 2, toughness: 2)
            .WithSubtypes(CardSubtype.Possum, CardSubtype.Druid)
            // "{T}: Add one mana of any color." — five ManaAbility instances,
            // one per WUBRG (CR 605.1). Mirrors Birds of Paradise / Ornithopter
            // of Paradise.
            .ManaAbility("W")
            .ManaAbility("U")
            .ManaAbility("B")
            .ManaAbility("R")
            .ManaAbility("G")
            .WithAbility(counterAbility.ToCardDefAbility())
            // CR 702.169 — keyword marker (the "{cost}" rider rides on the
            // OffspringAdditionalCost the caller layers onto the cast).
            .WithKeyword("Offspring");

        return builder.Build();
    }

    /// <summary>Shape-only construction (no live trigger-manager wiring).
    /// Suitable for <see cref="NamedCardFactory"/> dispatch / shape tests.</summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Tender Wildguide. When <paramref name="triggers"/> is supplied
    /// the Offspring ETB trigger is registered so the centralised event pump
    /// queues it automatically in a real match.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var possum = (Creature)CardDefRuntime.Build(Define(), owner);

        // Offspring {2} ETB token-copy (CR 702.169b). Layered on after the DSL
        // build — the DSL keyword marker carries only the "Offspring" string;
        // the ETB token trigger is wired through the generic subsystem.
        OffspringAbility.Attach(possum, triggers);

        // CR 702.169 — keep the keyword marker's Offspring {cost} arg uniform
        // with the other Offspring creatures (the DSL marker carries no arg).
        // Replace the bare DSL marker with the arg-bearing one so the keyword
        // scan surface matches Manifold Mouse / Pawpatch Recruit.
        var bareMarker = possum.Abilities.OfType<KeywordAbility>()
            .FirstOrDefault(k => k.Keyword == "Offspring" && k.Arg == null);
        if (bareMarker != null)
        {
            possum.RemoveAbility(bareMarker);
        }
        possum.AddAbility(new KeywordAbility("Offspring", possum, owner, arg: 2));

        return possum;
    }

    /// <summary>Build the Offspring {2} additional cost for this spell. Layer it
    /// onto the cast via SpellCastFlow's <c>additionalCosts</c> when the caster
    /// chooses to pay Offspring; omit it to decline.</summary>
    public static IAdditionalCost BuildOffspringCost(ICard card) =>
        new OffspringAdditionalCost(card, OffspringCost);
}
