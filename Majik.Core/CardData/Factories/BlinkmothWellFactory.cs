using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Blinkmoth Well (Darksteel).
///
/// Land. Oracle text (verified against Scryfall):
///   "{T}: Add {C}.
///    {2}, {T}: Tap target noncreature artifact."
///
/// A thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/blinkmoth-well.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card — the same
/// declarative posture as <see cref="MasterDecoyFactory"/> (a {T}: Add {C}
/// land like the Blinkmoth utility lands, but the residual is the targeted
/// tap rather than the manland animate). Both abilities are fully
/// declarative JSON:
///
/// - <b>{T}: Add {C}</b> — a vanilla <c>mana</c> ability producing one
///   colorless (CR 605.1 — mana abilities don't use the stack). {C} is
///   bucketed as +1 generic in <see cref="Majik.Core.ValueObjects.ManaCost"/>
///   today, matching the Blinkmoth/Inkmoth utility lands.
/// - <b>{2}, {T}: Tap target noncreature artifact</b> — an <c>activated</c>
///   ability with a <c>mana</c> ({2}) cost + a <c>tap_self</c> cost and a
///   <c>tap_target</c> effect (CR 701.21a) over the <c>noncreature_artifact</c>
///   target filter (CR 109.5 — an artifact that is not also a creature). The
///   shared <see cref="Majik.Core.Targeting.TargetCollection"/> pipeline
///   prompts the activating player's agent (CR 602.2b) for a legal pick and
///   the effect taps it via
///   <see cref="Majik.Core.Primitives.Fx.Tap(Majik.Core.Cards.Permanent)"/>
///   (CR 608.2b — an illegal target at resolution fizzles cleanly; tapping an
///   already-tapped permanent is a no-op per CR 701.21b).
/// </summary>
[CardName("Blinkmoth Well")]
public static class BlinkmothWellFactory
{
    public const string CardName = "Blinkmoth Well";
    public const string Slug = "blinkmoth-well";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Blinkmoth Well owned and controlled by <paramref name="owner"/>.
    /// Both abilities (the {T}: Add {C} mana ability and the {2}, {T}: tap
    /// target noncreature artifact activated ability) are materialised from the
    /// embedded JSON definition. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Land)CardDefinitionFactory.Build(Definition, owner);
    }
}
