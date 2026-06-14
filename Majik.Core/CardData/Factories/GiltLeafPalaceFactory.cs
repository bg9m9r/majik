using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Gilt-Leaf Palace (Lorwyn) — the B/G member of the
/// Lorwyn "reveal-tribal" land cycle (Ancient Amphitheater, Auntie's Hovel,
/// Secluded Glen, Wanderwine Hub). Oracle text (verified against Scryfall
/// 2026-06-14):
///   "As this land enters, you may reveal an Elf card from your hand. If you
///    don't, this land enters tapped.
///    {T}: Add {B} or {G}."
///
/// <para>
/// The Land shell — both mana abilities {B}/{G} (CR 605.1 — mana abilities
/// don't use the stack) — is declared declaratively in
/// <c>Majik.Core/CardData/Cards/gilt-leaf-palace.json</c> and materialized via
/// <see cref="CardDefinitionFactory"/>, the same posture as
/// <see cref="AuntiesHovelFactory"/>. Gilt-Leaf Palace is a nonbasic, non-typed
/// land (no printed land subtype), so the def carries only the two mana
/// abilities.
/// </para>
///
/// <para>
/// "As this land enters, you may reveal an Elf card from your hand. If you
/// don't, this land enters tapped." (CR 614.1c — a replacement effect modifying
/// how the permanent enters) is wired as a
/// <see cref="ConditionalEntersTappedReplacement"/> when a
/// <see cref="ReplacementBus"/> is supplied. The reveal is optional and gives
/// the controller no in-game cost or downside, so the engine resolves it under
/// the auto-reveal model: the land enters untapped iff the controller's hand
/// contains at least one Elf card (i.e. there is a card available to reveal).
/// "Elf card" is matched by subtype (<see cref="ICard.HasSubtype"/>) — CR 205.3i:
/// a card is an "Elf card" iff it has the Elf creature type, so any card carrying
/// the Elf subtype qualifies, matching the printed oracle. The entering land is
/// itself in the Hand at predicate time on a normal play; it is excluded by
/// reference equality so it cannot reveal itself (and it has no Elf subtype
/// anyway).
/// </para>
///
/// <para>
/// This mirrors <see cref="AuntiesHovelFactory"/>'s scaffold, swapping the
/// "a Goblin card in hand" reveal predicate for the "an Elf card in hand"
/// predicate, and {B}/{R} mana for the {B}/{G} produced here.
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
[CardName("Gilt-Leaf Palace")]
public static class GiltLeafPalaceFactory
{
    public const string Slug = "gilt-leaf-palace";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>Construct Gilt-Leaf Palace owned and controlled by
    /// <paramref name="owner"/> (shape-only path — no ETB-tapped replacement
    /// wired). This is the overload <see cref="NamedCardFactory"/> dispatches
    /// to.</summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>Construct Gilt-Leaf Palace with an optional
    /// <see cref="ReplacementBus"/> for full "you may reveal an Elf card from
    /// your hand; if you don't, it enters tapped" wiring (CR 614.1c).</summary>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // "As this land enters, you may reveal an Elf card from your hand.
        // If you don't, this land enters tapped." (CR 614.1c). The reveal is
        // a cost-free, downside-free choice, so the engine auto-reveals: the
        // land enters untapped iff the controller has an Elf card to reveal
        // in hand. Predicate returns true => untapped, false => tapped.
        // "Elf card" => a card with the Elf subtype (CR 205.3i); the entering
        // land is excluded from its own hand search by reference equality.
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
            .Any(c => !ReferenceEquals(c, self) && c.HasSubtype(CardSubtype.Elf));
}
