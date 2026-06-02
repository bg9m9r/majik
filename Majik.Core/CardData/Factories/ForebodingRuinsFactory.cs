using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Foreboding Ruins (Shadows over Innistrad) — a member
/// of the SOI/EMN "slow land" / "buddy land" dual cycle. Oracle text:
///   "As this land enters, you may reveal a Swamp or Mountain card from your
///    hand. If you don't, this land enters tapped.
///    {T}: Add {B} or {R}."
///
/// <para>
/// The Land shell — both mana abilities {B}/{R} (CR 605.1 — mana abilities
/// don't use the stack) — is declared declaratively in
/// <c>Majik.Core/CardData/Cards/foreboding-ruins.json</c> and materialized via
/// <see cref="CardDefinitionFactory"/>, the same posture as
/// <see cref="PortTownFactory"/>. Foreboding Ruins is a nonbasic, non-typed
/// land (no printed land subtype), so the def carries only the two mana
/// abilities.
/// </para>
///
/// <para>
/// "As this land enters, you may reveal a Swamp or Mountain card from your
/// hand. If you don't, this land enters tapped." (CR 614.1c — a replacement
/// effect modifying how the permanent enters) is wired as a
/// <see cref="ConditionalEntersTappedReplacement"/> when a
/// <see cref="ReplacementBus"/> is supplied. The reveal is optional and gives
/// the controller no in-game cost or downside, so the engine resolves it under
/// the auto-reveal model: the land enters untapped iff the controller's hand
/// contains at least one Swamp-or-Mountain card (i.e. there is a card available
/// to reveal). "Swamp or Mountain card" is matched by land subtype
/// (<see cref="ICard.HasSubtype"/>) — CR 205.3i / 305.6: a card is a "Swamp
/// card" iff it has the Swamp land subtype — so dual-typed and nonbasic lands
/// carrying the Swamp/Mountain subtype qualify, matching the printed oracle.
/// The entering land is itself in the Hand at predicate time on a normal play;
/// it is excluded by reference equality so it cannot reveal itself (and it
/// has no land subtype anyway).
/// </para>
///
/// <para>
/// This mirrors <see cref="PortTownFactory"/>'s scaffold, swapping the W/U
/// "Plains-or-Island card in hand" predicate for the B/R "Swamp-or-Mountain
/// card in hand" predicate.
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
[CardName("Foreboding Ruins")]
public static class ForebodingRuinsFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("foreboding-ruins");

    /// <summary>Construct Foreboding Ruins owned and controlled by
    /// <paramref name="owner"/> (shape-only path — no ETB-tapped
    /// replacement wired).</summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>Construct Foreboding Ruins with an optional
    /// <see cref="ReplacementBus"/> for full "you may reveal a Swamp or
    /// Mountain card from your hand; if you don't, it enters tapped" wiring
    /// (CR 614.1c).</summary>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // "You may reveal a Swamp or Mountain card from your hand. If you
        // don't, this land enters tapped." (CR 614.1c). The reveal is a
        // cost-free, downside-free choice, so the engine auto-reveals: the
        // land enters untapped iff the controller has a Swamp-or-Mountain
        // card to reveal in hand. Predicate returns true => untapped,
        // false => tapped. "Swamp or Mountain card" => a card with the
        // Swamp or Mountain land subtype (CR 205.3i); the entering land is
        // excluded from its own hand search by reference equality.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new ConditionalEntersTappedReplacement(
                land,
                entersUntappedIf: (controller, self) =>
                    ControllerHasRevealableCardInHand(controller, self)));
        }

        return land;
    }

    private static bool ControllerHasRevealableCardInHand(Player controller, ICard self) =>
        controller.Zones.Hand.GetCards()
            .Any(c => !ReferenceEquals(c, self)
                && (c.HasSubtype(CardSubtype.Swamp) || c.HasSubtype(CardSubtype.Mountain)));
}
