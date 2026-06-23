using System;
using System.Linq;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tomb of the Spirit Dragon (Modern Horizons 2, {0}).
///
/// Land. Oracle text (verified against Scryfall 2026-06-23):
///   "{T}: Add {C}.
///    {2}, {T}: You gain 1 life for each colorless creature you control."
///
/// Scryfall-confirmed type line: <c>Land</c> — no basic supertype, no
/// subtypes, empty mana cost, colourless (CR 105.2 — no coloured pips, no
/// colour indicator).
///
/// ## Card shape
/// The plain Land identity plus the first mana ability ("{T}: Add {C}",
/// CR 605.1a) are declared in
/// <c>Majik.Core/CardData/Cards/tomb-of-the-spirit-dragon.json</c> and
/// materialised via <see cref="CardDefinitionFactory"/> — the same shape-only
/// posture as <see cref="CabalStrongholdFactory"/>. The second ability needs
/// a runtime "colorless creature" count and gains <em>life</em> (not mana),
/// so it is wired as a regular <see cref="ActivatedAbility"/> in this factory
/// (the Claws-of-Gix posture: <see cref="ManaCostCost"/> + tap-self cost + a
/// closure <see cref="Effect"/>; the {T} cost comes from
/// <see cref="Majik.Core.Primitives.Costs.TapSelf"/>).
///
/// ## {2}, {T}: You gain 1 life for each colorless creature you control. (CR 602)
/// An ordinary activated ability (NOT a mana ability — it produces no mana and
/// uses the stack, CR 602.2). Two costs in declaration order:
///   1. <see cref="ManaCostCost"/> "{2}" — two generic mana.
///   2. tap-self ({T}) via <see cref="Primitives.Costs.TapSelf"/>.
/// The effect counts the colourless creatures the controller controls at
/// resolution and gains that much life (CR 119.3). N may be 0 — a legal
/// activation that gains no life (CR 608.2 — the effect simply does nothing).
///
/// ## "colorless creature" (CR 105.2 + CR 202.2)
/// A permanent counts when it is a Creature whose <em>effective</em> colour
/// set is empty (<see cref="Permanent.GetEffectiveColors"/> returns no colours
/// — CR 105.2a). This honours colour-changing effects (a creature made blue
/// stops counting; a Devoid creature with coloured pips still counts as
/// colourless). Tomb of the Spirit Dragon itself is a Land, not a creature, so
/// it never counts toward its own ability.
/// </summary>
[CardName(CardName)]
public static class TombOfTheSpiritDragonFactory
{
    public const string CardName = "Tomb of the Spirit Dragon";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("tomb-of-the-spirit-dragon");

    /// <summary>
    /// Construct a Tomb of the Spirit Dragon owned and controlled by
    /// <paramref name="owner"/>. Both abilities are wired: the JSON-declared
    /// "{T}: Add {C}" mana ability and the factory-wired
    /// "{2}, {T}: You gain 1 life for each colorless creature you control."
    /// activated ability.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var tomb = (Land)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // {2}, {T}: You gain 1 life for each colorless creature you control.
        // CR 602 — activated ability; costs paid in declaration order.
        // CR 119.3 — life gain (read dynamically at resolution).
        // ----------------------------------------------------------------
        var gainLifeEffect = new Effect(
            $"{CardName}: controller gains 1 life for each colorless creature they control",
            () =>
            {
                var controller = tomb.Controller ?? owner;
                var n = CountColorlessCreatures(controller);
                if (n > 0)
                {
                    controller.GainLife(n);
                }
            });

        tomb.AddAbility(new ActivatedAbility(
            source: tomb,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{2}"),
                Majik.Core.Primitives.Costs.TapSelf(tomb),
            },
            effects: new IEffect[] { gainLifeEffect }));

        return tomb;
    }

    /// <summary>
    /// Count how many <b>colorless creatures</b> <paramref name="controller"/>
    /// currently controls (CR 105.2a). A permanent counts only when it is a
    /// Creature whose effective colour set is empty — colour-changing effects
    /// are honoured via <see cref="Permanent.GetEffectiveColors"/>. Exposed as
    /// a public helper for tests and bot policies. Returns 0 for null input.
    /// </summary>
    public static int CountColorlessCreatures(Player controller)
    {
        if (controller == null) return 0;

        return controller.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Count(p => p.HasType(CardType.Creature)
                        && p.GetEffectiveColors().Count == 0);
    }
}
