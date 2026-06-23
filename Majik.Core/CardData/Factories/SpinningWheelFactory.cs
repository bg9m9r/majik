using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Spinning Wheel (Time Spiral / Mirage, {3}).
///
/// Artifact. Oracle text (verified against Scryfall):
///   "{T}: Add one mana of any color.
///    {5}, {T}: Tap target creature."
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/spinning-wheel.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card — the same
/// posture as <see cref="ManalithFactory"/> / <see cref="GoldmeadowHarrierFactory"/>.
/// Both abilities are fully declarative JSON; no engine mechanic is new here:
///
/// - <b>{T}: Add one mana of any color</b> — five
///   <see cref="Majik.Core.Abilities.ManaAbility"/> instances (one per WUBRG),
///   carried as five <c>{ "kind": "mana" }</c> entries. This is the standard
///   "any color" modelling (CR 605.1a — "any color" resolves to five distinct
///   single-colour mana abilities), identical to Manalith. The implicit {T}
///   self-tap is baked into each mana ability's cost.
/// - <b>{5}, {T}: Tap target creature</b> — an <c>activated</c> ability with a
///   <c>mana</c> ({5}) cost + a <c>tap_self</c> cost and a <c>tap_target</c>
///   effect (CR 701.21a) over the <c>creature</c> target filter, exactly as
///   Goldmeadow Harrier's <c>{W}, {T}: Tap target creature</c> (only the mana
///   cost differs). The shared
///   <see cref="Majik.Core.Targeting.TargetCollection"/> pipeline prompts the
///   activating player's agent (CR 602.2b) for a creature pick, and the effect
///   taps the chosen creature via
///   <see cref="Majik.Core.Primitives.Fx.Tap(Majik.Core.Cards.Permanent)"/>
///   (CR 608.2b — an illegal target at resolution fizzles cleanly; tapping an
///   already-tapped permanent is a no-op per CR 701.21b).
/// </summary>
[CardName("Spinning Wheel")]
public static class SpinningWheelFactory
{
    public const string CardName = "Spinning Wheel";
    public const string Slug = "spinning-wheel";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Spinning Wheel owned and controlled by <paramref name="owner"/>.
    /// The five any-colour mana abilities and the {5},{T} tap-target-creature
    /// activated ability are materialised from the embedded JSON definition.
    /// This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Artifact)CardDefinitionFactory.Build(Definition, owner);
    }
}
