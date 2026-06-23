using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Dreamstone Hedron (Rise of the Eldrazi).
///
/// Artifact {6}. Oracle text (Scryfall-confirmed):
///   "{T}: Add {C}{C}{C}.
///    {3}, {T}, Sacrifice this artifact: Draw three cards."
///
/// Scryfall type line: Artifact (no subtype). Mana cost {6}.
///
/// ## Card identity + abilities come from JSON
///
/// Name / type / mana cost and both abilities are loaded from the embedded
/// JSON definition (<c>dreamstone-hedron.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. No code-side wiring is required — every
/// ability shape is already modelled by the JSON schema:
/// <list type="bullet">
///   <item><b>{T}: Add {C}{C}{C}</b> — a single
///     <see cref="Majik.Core.Abilities.ManaAbility"/> (CR 605.1 — mana
///     abilities don't use the stack). {C}{C}{C} folds into the generic bucket
///     via <see cref="Majik.Core.ValueObjects.ManaCost.Parse"/> (CR 107.4c) →
///     three colourless. Same tap-for-colourless body as Hedron Archive's
///     {C}{C}, scaled to three.</item>
///   <item><b>{3}, {T}, Sacrifice this artifact: Draw three cards</b> — an
///     <see cref="Majik.Core.Abilities.ActivatedAbility"/> (CR 602) whose cost
///     stack is a <see cref="Majik.Core.Costs.ManaCostCost"/>({3}) +
///     a <c>tap_self</c> + a <c>sacrifice_self</c> additional cost
///     (CR 701.16), resolving a <c>draw_card</c> effect of amount 3 (CR 120).
///     Same {mana},{T},Sacrifice: Draw cards mana-rock shape as Hedron Archive
///     (<see cref="HedronArchiveFactory"/>), scaled to {3} / draw three.
///     Empty library is a silent no-op for the unavailable draws; the loss
///     flag is handled by the draw path (CR 120.3 / 704.5b).</item>
/// </list>
/// </summary>
[CardName("Dreamstone Hedron")]
public static class DreamstoneHedronFactory
{
    public const string CardName = "Dreamstone Hedron";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("dreamstone-hedron");

    /// <summary>
    /// Construct Dreamstone Hedron owned and controlled by
    /// <paramref name="owner"/>. Identity, the {T}: Add {C}{C}{C} mana ability,
    /// and the {3}, {T}, Sacrifice: Draw three cards activated ability all come
    /// from JSON.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Artifact)CardDefinitionFactory.Build(Definition, owner);
    }
}
