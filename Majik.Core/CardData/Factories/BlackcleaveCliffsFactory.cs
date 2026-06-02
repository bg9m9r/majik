using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Blackcleave Cliffs (Scars of Mirrodin) — the B/R
/// member of the "fast land" cycle. Oracle (verified against Scryfall):
/// <code>
/// This land enters tapped unless you control two or fewer other lands.
/// {T}: Add {B} or {R}.
/// </code>
///
/// <para>
/// The Land shell — plain nonbasic <see cref="Land"/> (no supertype, no
/// printed subtype) plus the two mana abilities {B}/{R} (CR 605.1 — mana
/// abilities don't use the stack) — is declared declaratively in
/// <c>Majik.Core/CardData/Cards/blackcleave-cliffs.json</c> and materialized
/// via <see cref="CardDefinitionFactory"/>, the same JSON-driven posture as
/// <see cref="SeachromeCoastFactory"/>.
/// </para>
///
/// <para>
/// "Enters tapped unless you control two or fewer other lands" (CR 614.1c)
/// is the fast-land ETB condition. On the production load path it is wired
/// automatically from the printed oracle text by
/// <see cref="ConditionalEntersTappedBinder"/> (its
/// "N or [more|fewer] other lands" regex matches this exact wording, building
/// the predicate <c>CountOtherLands(controller, self) &lt;= 2</c>). This
/// factory mirrors that predicate on the optional
/// <see cref="ReplacementBus"/> overload so the behaviour is exercisable in
/// isolation (same shape as <see cref="SeachromeCoastFactory"/>):
/// the land enters untapped iff the controller controls at most two OTHER
/// lands ("other" excludes this fast land via reference equality; "you
/// control" reads the controller's battlefield only).
/// </para>
///
/// <para>
/// Single-arg dispatcher path constructs without a
/// <see cref="ReplacementBus"/> — the ETB-tapped replacement is omitted
/// (shape-only posture matching every other ETB-replacement factory's
/// single-arg path). Lands enter untapped on that code path; the full
/// overload wires the predicate when the bus is supplied, and prod load
/// wires it from oracle text via the binder.
/// </para>
/// </summary>
[CardName("Blackcleave Cliffs")]
public static class BlackcleaveCliffsFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("blackcleave-cliffs");

    /// <summary>
    /// CR 614.1c threshold — "two or fewer other lands". The fast land
    /// enters untapped iff the controller controls at most this many other
    /// lands.
    /// </summary>
    private const int OtherLandThreshold = 2;

    /// <summary>
    /// Construct Blackcleave Cliffs owned and controlled by
    /// <paramref name="owner"/>. Shape-only path — no
    /// <see cref="ReplacementBus"/>, so the ETB-tapped predicate is not
    /// wired here.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>
    /// Construct Blackcleave Cliffs with an optional <see cref="ReplacementBus"/>
    /// for full ETB-tapped wiring (CR 614.1c).
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">When supplied, the "enters tapped unless
    /// you control two or fewer other lands" replacement is registered.</param>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // Enters tapped unless you control two or fewer other lands
        // (CR 614.1c). Predicate returns true ⇒ enters untapped, false ⇒
        // enters tapped. The card itself is excluded from the count via
        // reference equality, so the replacement is correct whether the
        // entering card is on the battlefield at predicate time or not.
        // Same shape ConditionalEntersTappedBinder emits from oracle text
        // on the production load path.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new ConditionalEntersTappedReplacement(
                land,
                entersUntappedIf: (controller, self) =>
                    CountOtherLands(controller, self) <= OtherLandThreshold));
        }

        return land;
    }

    private static int CountOtherLands(Player controller, ICard self) =>
        controller.Zones.Battlefield.GetCards()
            .Count(c => !ReferenceEquals(c, self) && c.HasType(CardType.Land));
}
