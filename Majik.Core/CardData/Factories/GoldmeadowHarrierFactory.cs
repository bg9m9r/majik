using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Goldmeadow Harrier (Lorwyn / 10th Edition, {W}).
///
/// Creature — Kithkin Soldier 1/1. Oracle text (verified against Scryfall):
///   "{W}, {T}: Tap target creature."
///
/// Now a thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/goldmeadow-harrier.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card — the same
/// posture as <see cref="KarakasFactory"/> / <see cref="BoseijuFactory"/>.
/// The single ability is fully declarative JSON:
///
/// - <b>{W}, {T}: Tap target creature</b> — an <c>activated</c> ability with a
///   <c>mana</c> ({W}) cost + a <c>tap_self</c> cost and a <c>tap_target</c>
///   effect (CR 701.21a) over the <c>creature</c> target filter. The shared
///   <see cref="Majik.Core.Targeting.TargetCollection"/> pipeline prompts the
///   activating player's agent (CR 602.2b) for a creature pick, and the effect
///   taps the chosen creature via
///   <see cref="Majik.Core.Primitives.Fx.Tap(Majik.Core.Cards.Permanent)"/>
///   (CR 608.2b — an illegal target at resolution fizzles cleanly; tapping an
///   already-tapped permanent is a no-op per CR 701.21b).
///
/// This card previously hand-rolled the tap ability because the declarative
/// JSON schema lacked a "tap target …" effect verb. That verb
/// (<see cref="TapTargetEffectDef"/>) now exists, so the factory collapses to
/// the standard JSON-loading shell.
/// </summary>
[CardName("Goldmeadow Harrier")]
public static class GoldmeadowHarrierFactory
{
    public const string CardName = "Goldmeadow Harrier";
    public const string Slug = "goldmeadow-harrier";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Goldmeadow Harrier owned and controlled by
    /// <paramref name="owner"/>. The single tap-target-creature activated
    /// ability is materialised from the embedded JSON definition. This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Creature)CardDefinitionFactory.Build(Definition, owner);
    }
}
