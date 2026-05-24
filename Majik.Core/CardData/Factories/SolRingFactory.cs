using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sol Ring (Limited Edition Alpha, {1}).
///
/// Artifact. Oracle text:
///   "{T}: Add {C}{C}."
///
/// ## Implementation
///
/// Single <see cref="ManaAbility"/> using the simple static-amount overload
/// — taps Sol Ring and adds two colourless. CR 605 covers the mana ability
/// itself; CR 107.4c routes {C} through the generic bucket via
/// <see cref="ManaCost.Parse"/> (so <c>Parse("CC")</c> yields a cost with
/// <c>Generic == 2</c>). Sister artifact to Mishra's Workshop / Urza's Mine
/// for the colourless surface; differs only in printed amount + no land-
/// type or spend-restriction baggage.
///
/// ## Types
/// - Plain <see cref="Artifact"/>. No supertypes (not legendary on the
///   modern reprint — the Commander Legends / 30A printings re-add the
///   Legendary supertype, but the canonical Modern-legal Limited Edition
///   line is plain Artifact, matching every other oracle reference in the
///   engine).
/// </summary>
[CardName("Sol Ring")]
public static class SolRingFactory
{
    public const string CardName = "Sol Ring";
    public const string PrintedManaCost = "{1}";

    /// <summary>
    /// Construct Sol Ring owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // {T}: Add {C}{C}.  ManaCost.Parse("CC") buckets two {C} into
        // Generic = 2 (CR 107.4c — engine collapses colourless to generic).
        card.AddAbility(new ManaAbility(
            source: card,
            controller: owner,
            manaGenerated: ManaCost.Parse("CC")));

        return card;
    }
}
