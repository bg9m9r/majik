using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Secluded Glen (Lorwyn) — a "tribal reveal" dual
/// land. Oracle text (verified Scryfall 2026-06-23):
///   "As this land enters, you may reveal a Faerie card from your hand. If you
///    don't, this land enters tapped.
///    {T}: Add {U} or {B}."
///
/// <para>
/// The Land shell — both mana abilities {U}/{B} (CR 605.1 — mana abilities
/// don't use the stack) — is declared declaratively in
/// <c>Majik.Core/CardData/Cards/secluded-glen.json</c> and materialized via
/// <see cref="CardDefinitionFactory"/>, the same posture as
/// <see cref="AncientAmphitheaterFactory"/>. Secluded Glen is a nonbasic,
/// non-typed land (no printed land subtype), so the def carries only the two
/// mana abilities.
/// </para>
///
/// <para>
/// "As this land enters, you may reveal a Faerie card from your hand. If you
/// don't, this land enters tapped." (CR 614.1c — a replacement effect
/// modifying how the permanent enters) is wired as a
/// <see cref="ConditionalEntersTappedReplacement"/> when a
/// <see cref="ReplacementBus"/> is supplied. The reveal is optional and gives
/// the controller no in-game cost or downside, so the engine resolves it under
/// the auto-reveal model (identical to the rest of the Lorwyn cycle in
/// <see cref="AncientAmphitheaterFactory"/>): the land enters untapped iff the
/// controller's hand contains at least one Faerie card (i.e. there is a card
/// available to reveal). "Faerie card" is matched by creature subtype
/// (<see cref="ICard.HasSubtype"/> with <see cref="CardSubtype.Faerie"/>) —
/// CR 205.3m: a card is a "Faerie card" iff it has the Faerie creature
/// subtype. The entering land is itself in the Hand at predicate time on a
/// normal play; it is excluded by reference equality so it cannot reveal
/// itself (and it has no creature subtype anyway).
/// </para>
///
/// <para>
/// This mirrors <see cref="AncientAmphitheaterFactory"/>'s scaffold, swapping
/// the tribal-reveal "a Giant card in hand" predicate for the "a Faerie card
/// in hand" predicate, and {R}/{W} mana for {U}/{B}.
/// </para>
///
/// <para>
/// Single-arg dispatcher path constructs without a
/// <see cref="ReplacementBus"/> — the ETB-tapped replacement is omitted
/// (shape-only posture matching every other ETB-replacement factory's
/// single-arg path); the mana abilities are still attached. The full overload
/// wires the predicate when the bus is supplied.
/// </para>
/// </summary>
[CardName("Secluded Glen")]
public static class SecludedGlenFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("secluded-glen");

    /// <summary>Construct Secluded Glen owned and controlled by
    /// <paramref name="owner"/> (shape-only path — no ETB-tapped replacement
    /// wired).</summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>Construct Secluded Glen with an optional
    /// <see cref="ReplacementBus"/> for full "you may reveal a Faerie card from
    /// your hand; if you don't, it enters tapped" wiring (CR 614.1c).</summary>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // "You may reveal a Faerie card from your hand. If you don't, this
        // land enters tapped." (CR 614.1c). The reveal is a cost-free,
        // downside-free choice, so the engine auto-reveals: the land enters
        // untapped iff the controller has a Faerie card to reveal in hand.
        // Predicate returns true => untapped, false => tapped. "Faerie card"
        // => a card with the Faerie creature subtype (CR 205.3m); the
        // entering land is excluded from its own hand search by reference
        // equality.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new ConditionalEntersTappedReplacement(
                land,
                entersUntappedIf: (controller, self) =>
                    ControllerHasRevealableFaerieInHand(controller, self)));
        }

        return land;
    }

    private static bool ControllerHasRevealableFaerieInHand(Player controller, ICard self) =>
        controller.Zones.Hand.GetCards()
            .Any(c => !ReferenceEquals(c, self) && c.HasSubtype(CardSubtype.Faerie));
}
