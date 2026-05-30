using System;
using System.Linq;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Zagoth Crystal (Commander Legends, {3}).
///
/// Artifact. Oracle text (verified against Scryfall):
///   "{T}: Add {B}, {G}, or {U}.
///    Cycling {2} ({2}, Discard this card: Draw a card.)"
///
/// ## Implementation
///
/// Card identity (Artifact, {3}) is loaded from
/// <c>Majik.Core/CardData/Cards/zagoth-crystal.json</c> through
/// <see cref="CardDefinitionFactory"/>, matching the JSON-driven card cycle.
///
/// "{T}: Add {B}, {G}, or {U}." is a fixed three-colour mana ability. As with
/// Star Compass / Springleaf Drum / Chromatic Star the "or" is modelled as
/// three colour-specific <see cref="ManaAbility"/> slots — the activator picks
/// a colour by picking the matching ability slot, so no separate colour prompt
/// is needed (CR 605.1 — mana abilities don't use the stack). Unlike Star
/// Compass there is no "could produce" gate: every slot is always available
/// while the artifact is untapped (the only intrinsic gate is the {T} cost,
/// CR 602.2 / 605.1).
///
/// "Cycling {2}" (CR 702.32) is wired through the shared
/// <see cref="CyclingFactory.Build"/> primitive with cycle cost
/// <see cref="ManaCostCost"/>(<c>{2}</c>). The primitive appends the
/// <see cref="DiscardSelfCost"/> hand-zone gate (CR 702.32a) and, when an
/// event bus is supplied, publishes <see cref="CardCycledEvent"/> on resolve
/// (CR 702.32d) so "Whenever a player cycles a card" triggers fire.
/// </summary>
[CardName("Zagoth Crystal")]
public static class ZagothCrystalFactory
{
    public const string CardName = "Zagoth Crystal";
    public const string PrintedManaCost = "{3}";
    public const string CyclingCost = "{2}";

    // Oracle: "Add {B}, {G}, or {U}." — three fixed colour slots.
    private static readonly string[] Colors = { "B", "G", "U" };

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("zagoth-crystal");

    /// <summary>
    /// Construct Zagoth Crystal with no event bus. The three colour mana
    /// abilities + the cycling activated ability are attached; the cycling
    /// resolve body does not publish <see cref="CardCycledEvent"/> on this
    /// shape-only path.
    /// </summary>
    public static Artifact Create(Player owner) => Create(owner, eventBus: null);

    /// <summary>
    /// Construct Zagoth Crystal owned and controlled by <paramref name="owner"/>.
    /// When <paramref name="eventBus"/> is supplied the cycling resolve body
    /// publishes <see cref="CardCycledEvent"/> (CR 702.32d).
    /// </summary>
    public static Artifact Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var crystal = (Artifact)CardDefinitionFactory.Build(Definition, owner);

        // {T}: Add {B}, {G}, or {U}. CR 605.1 — mana abilities don't use the
        // stack; the colour choice is made by activating the matching slot.
        foreach (var pip in Colors)
        {
            crystal.AddAbility(new ManaAbility(
                source: crystal,
                controller: owner,
                manaGenerated: ManaCost.Parse(pip),
                canActivateCheck: () => !crystal.IsTapped));
        }

        // Cycling {2}. CR 702.32 — "{2}, Discard this card: Draw a card."
        // The primitive appends the DiscardSelfCost hand-zone gate (CR
        // 702.32a) and the CardCycledEvent publish (CR 702.32d).
        CyclingFactory.Build(crystal, new ManaCostCost(CyclingCost), eventBus);

        return crystal;
    }

    /// <summary>
    /// Test/agent helper: return the colour slot for <paramref name="colorPip"/>
    /// (one of B/G/U) on <paramref name="crystal"/>.
    /// </summary>
    public static ManaAbility AbilityForColor(Artifact crystal, string colorPip)
    {
        ArgumentNullException.ThrowIfNull(crystal);

        var target = ManaCost.Parse(colorPip);
        return crystal.Abilities
            .OfType<ManaAbility>()
            .Single(ma => ma.ManaGenerated.Equals(target));
    }
}
