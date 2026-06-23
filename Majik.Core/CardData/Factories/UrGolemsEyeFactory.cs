using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ur-Golem's Eye (Time Spiral, {4}).
///
/// Artifact. Oracle text (Scryfall, verified 2026-06-23):
///   "{T}: Add {C}{C}."
///
/// A vanilla {4} colourless-fixing mana rock — the strictly-simpler cousin of
/// <see cref="SphereOfTheSunsFactory"/> / Worn Powerstone: no "enters tapped"
/// clause (so CR 614.1c never applies — it always enters untapped) and no
/// counters, just the single tap-for-{C}{C} mana ability.
///
/// ## Implementation
///
/// Card identity (Artifact, {4}) AND the <b>{T}: Add {C}{C}</b> mana ability
/// are both carried declaratively in
/// <c>Majik.Core/CardData/Cards/ur-golems-eye.json</c> (the <c>"kind": "mana",
/// "produces": "CC"</c> ability union) and materialized in one shot by
/// <see cref="CardDefinitionFactory"/> → <see cref="CardDefRuntime"/>. {C}{C}
/// folds into the generic bucket via
/// <see cref="Majik.Core.ValueObjects.ManaCost.Parse"/> (CR 107.4c) → two
/// colourless. CR 605.1 — the mana ability doesn't use the stack.
///
/// This factory is therefore a thin JSON pass-through (same posture as the
/// other JSON-driven rocks); it adds no bespoke abilities of its own.
/// </summary>
[CardName("Ur-Golem's Eye")]
public static class UrGolemsEyeFactory
{
    public const string CardName = "Ur-Golem's Eye";
    public const string PrintedManaCost = "{4}";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("ur-golems-eye");

    /// <summary>
    /// Construct Ur-Golem's Eye owned and controlled by <paramref name="owner"/>.
    /// The single {T}: Add {C}{C} mana ability (CR 605.1) is attached by
    /// <see cref="CardDefinitionFactory.Build(CardDefinition, Player)"/> from the
    /// JSON definition's <c>mana</c> ability — no bespoke wiring needed.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Artifact)CardDefinitionFactory.Build(Definition, owner);
    }
}
