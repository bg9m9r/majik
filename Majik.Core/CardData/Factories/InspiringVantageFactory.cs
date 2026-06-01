using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Inspiring Vantage (Aether Revolt) — a member of the
/// allied "fast land" nonbasic dual cycle. Oracle text (verified against
/// Scryfall):
///   "This land enters tapped unless you control two or fewer other lands.
///    {T}: Add {R} or {W}."
///
/// <para>
/// The Land shell — name, Land type, and the two mana abilities {R}/{W}
/// (CR 605.1 — mana abilities don't use the stack) — is declared
/// declaratively in <c>Majik.Core/CardData/Cards/inspiring-vantage.json</c>
/// and materialized via <see cref="CardDefinitionFactory"/>, the same posture
/// as <see cref="CinderGladeFactory"/>. Fast lands are nonbasic and carry no
/// printed land subtype.
/// </para>
///
/// <para>
/// "Enters tapped unless you control two or fewer other lands" (CR 614.1c) is
/// wired as a <see cref="ConditionalEntersTappedReplacement"/> when a
/// <see cref="ReplacementBus"/> is supplied. The predicate counts the
/// controller's OTHER battlefield lands (any land — basic or nonbasic),
/// excluding this land itself by reference equality so the count is correct
/// whether the entering card is on the battlefield at predicate time or not.
/// The land enters untapped iff that count is &lt;= 2 (i.e. the land enters
/// untapped on turns 1–3 when you have at most two other lands, and tapped
/// thereafter). This is the same "N or fewer other lands" form the generic
/// <see cref="ConditionalEntersTappedBinder"/> recognizes; it is wired here
/// in the factory to keep the per-card behaviour self-contained, matching the
/// posture of the sibling conditional-ETB land factories
/// (<see cref="CinderGladeFactory"/>, <see cref="CheckLandCycleFactory"/>).
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
[CardName("Inspiring Vantage")]
public static class InspiringVantageFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("inspiring-vantage");

    /// <summary>Construct Inspiring Vantage owned and controlled by
    /// <paramref name="owner"/> (shape-only path — no ETB-tapped replacement
    /// wired).</summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>Construct Inspiring Vantage with an optional
    /// <see cref="ReplacementBus"/> for full "enters tapped unless you control
    /// two or fewer other lands" wiring (CR 614.1c).</summary>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // Enters tapped unless you control two or fewer other lands
        // (CR 614.1c). Predicate returns true => enters untapped, false =>
        // enters tapped. "other lands" => any land (basic or nonbasic) the
        // controller controls EXCEPT this one; the card itself is excluded
        // from the count via reference equality so the replacement is
        // correct whether the entering card is on the battlefield at
        // predicate time or not (CR 614.1 — "other" excludes the source).
        // Untapped iff count <= 2.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new ConditionalEntersTappedReplacement(
                land,
                entersUntappedIf: (controller, self) =>
                    CountControllerOtherLands(controller, self) <= 2));
        }

        return land;
    }

    private static int CountControllerOtherLands(Player controller, ICard self) =>
        controller.Zones.Battlefield.GetCards()
            .Count(c => !ReferenceEquals(c, self) && c.HasType(CardType.Land));
}
