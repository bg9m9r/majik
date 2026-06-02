using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cabal Stronghold (Dominaria).
///
/// Land. Oracle text (verified against Scryfall 2026-06-02):
///   "{T}: Add {C}.
///    {3}, {T}: Add {B} for each basic Swamp you control."
///
/// Scryfall-confirmed type line: <c>Land</c> — no basic supertype, no
/// subtypes. Cabal Stronghold is NOT a Swamp itself and therefore never
/// counts toward its own ability (CR 305.6 — the Swamp subtype is a printed
/// land subtype; Cabal Stronghold has none).
///
/// ## Card shape
/// The plain Land identity plus the first mana ability ("{T}: Add {C}",
/// CR 605.1a) are declared in
/// <c>Majik.Core/CardData/Cards/cabal-stronghold.json</c> and materialised
/// via <see cref="CardDefinitionFactory"/> — the exact shape-only posture of
/// <see cref="NykthosShrineToNyxFactory"/>. The second ability needs a
/// runtime "basic Swamp" count that the JSON <see cref="AbilityDefinition"/>
/// schema does not express, so it is wired in this factory.
///
/// ## {3}, {T}: Add {B} for each basic Swamp you control. (CR 605.1a)
/// A mana ability (produces mana, no target, doesn't use the stack).
/// Modelled as a dynamic <see cref="ManaAbility"/> using the
/// <c>Func&lt;ManaCost&gt; manaGenerator</c> + <c>additionalCostPayer</c>
/// overload, exactly as <see cref="NykthosShrineToNyxFactory"/> /
/// <see cref="CabalCoffersFactory"/> model their "{N},{T}: add dynamic mana"
/// abilities (deferral #2 — the {3} additional cost is declared via
/// <c>additionalCostPayer</c> so it composes cleanly with the
/// Swamp-counting <c>Func&lt;ManaCost&gt;</c> rather than being inlined in
/// the generator lambda):
/// <list type="number">
///   <item><c>canActivateCheck</c> — land untapped AND the controller's pool
///     can pay {3} (read-only affordability probe).</item>
///   <item><c>manaGenerator</c> — counts the basic Swamps the controller
///     controls (<see cref="CountBasicSwamps"/>) and returns that many {B}
///     pips.</item>
///   <item>Taps the land (standard {T} cost).</item>
///   <item><c>additionalCostPayer</c> — drains {3} from the pool, part of the
///     same atomic activation cost (CR 602.2a).</item>
/// </list>
/// Activating with zero basic Swamps is legal (CR 605.1c) — it produces no
/// mana, still pays {3} and taps the land.
///
/// ## "basic Swamp" (CR 305.6 + CR 205.4a)
/// Unlike Cabal Coffers ("each Swamp you control"), Cabal Stronghold counts
/// only <b>basic</b> Swamps — a permanent the controller controls that has
/// BOTH the <see cref="CardSubtype.Swamp"/> subtype AND the
/// <see cref="CardSupertype.Basic"/> supertype. A non-basic land with the
/// Swamp subtype (e.g. a dual land that is a Swamp) does NOT count. Snow-
/// Covered Swamp is still a basic land (it has the Basic supertype) and so
/// counts.
///
/// ## Deferred (v1 gaps)
/// - <b>N × {B} as concatenated string</b>: <see cref="ManaCost"/> has no
///   native "N black pips" constructor; <see cref="BuildBlackMana"/> builds a
///   string <c>"{B}{B}…"</c> and parses it — the same posture as
///   <see cref="CabalCoffersFactory.BuildBlackMana"/>.
/// </summary>
[CardName("Cabal Stronghold")]
public static class CabalStrongholdFactory
{
    public const string CardName = "Cabal Stronghold";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("cabal-stronghold");

    private static readonly ManaCost TapAdditionalCost = ManaCost.Parse("3");

    /// <summary>
    /// Construct a Cabal Stronghold owned and controlled by
    /// <paramref name="owner"/>. Both abilities are wired: the JSON-declared
    /// "{T}: Add {C}" mana ability and the factory-wired
    /// "{3}, {T}: Add {B} for each basic Swamp you control" mana ability.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // {3}, {T}: Add {B} for each basic Swamp you control. (CR 605.1a)
        // Dynamic-mana ManaAbility — same shape as Nykthos' devotion ability
        // (deferral #2): the {3} additional cost is declared via
        // additionalCostPayer so it composes with the basic-Swamp-counting
        // Func<ManaCost>.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(
            source: land,
            controller: owner,
            manaGenerator: () => BuildBlackMana(CountBasicSwamps(owner)),
            canActivateCheck: () =>
                !land.IsTapped
                && owner.ManaPool.CanPay(TapAdditionalCost),
            additionalCostPayer: controller => controller.PayMana(TapAdditionalCost)));

        return land;
    }

    /// <summary>
    /// Count how many <b>basic</b> Swamps <paramref name="controller"/>
    /// currently controls (CR 305.6 + CR 205.4a). A permanent counts only
    /// when it is a Land with BOTH the <see cref="CardSubtype.Swamp"/> subtype
    /// AND the <see cref="CardSupertype.Basic"/> supertype — a non-basic land
    /// that happens to be a Swamp does not count. Exposed as a public helper
    /// for tests and bot policies. Returns 0 for null input.
    /// </summary>
    public static int CountBasicSwamps(Player controller)
    {
        if (controller == null) return 0;
        return controller.Zones.Battlefield.GetCards()
            .OfType<Card>()
            .Count(c => c.HasType(CardType.Land)
                && c.HasSubtype(CardSubtype.Swamp)
                && c.HasSupertype(CardSupertype.Basic));
    }

    /// <summary>
    /// Build a <see cref="ManaCost"/> representing <paramref name="n"/> black
    /// mana pips. Returns <see cref="ManaCost.Zero"/> when <paramref name="n"/>
    /// is ≤ 0 (CR 605.1c — zero-mana activation is legal).
    /// </summary>
    internal static ManaCost BuildBlackMana(int n)
    {
        if (n <= 0) return ManaCost.Zero;
        return ManaCost.Parse(string.Concat(Enumerable.Repeat("{B}", n)));
    }
}
