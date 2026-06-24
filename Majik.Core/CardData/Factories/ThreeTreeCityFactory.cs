using System.Linq;
using System.Runtime.CompilerServices;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Three Tree City (Bloomburrow).
///
/// Legendary Land. Oracle text (verified against Scryfall 2026-06-24):
///   "As Three Tree City enters, choose a creature type.
///    {T}: Add {C}.
///    {2}, {T}: Choose a color. Add an amount of mana of that color equal to
///    the number of creatures you control of the chosen type."
///
/// ## Card shape
/// The Legendary Land identity plus the first mana ability ("{T}: Add {C}",
/// CR 605.1a) are declared in
/// <c>Majik.Core/CardData/Cards/three-tree-city.json</c> and materialised via
/// <see cref="CardDefinitionFactory"/>. The second ability needs a runtime
/// color choice and a runtime creature-count, neither of which the JSON
/// <see cref="AbilityDefinition"/> schema expresses, so it is wired here —
/// exactly as <see cref="NykthosShrineToNyxFactory"/> wires its devotion ability.
///
/// ## "As Three Tree City enters, choose a creature type." (CR 614.12)
/// A replacement-style ETB choice, identical in shape to
/// <see cref="CavernOfSoulsFactory"/>'s "choose a creature type". The engine has
/// no ChooseSubtype agent prompt yet, so the chosen type is captured eagerly at
/// factory-build time when the <see cref="Create(Player, CardSubtype)"/> overload
/// is used (mirrors Cavern of Souls / Pithing Needle's deferral posture). The
/// choice is stored per-card and exposed via <see cref="GetChosenType(Land)"/>.
/// The shape-only single-arg path leaves the choice unset (still legal — the
/// {2},{T} ability simply counts creatures of a null type = 0).
///
/// ## {2}, {T}: Choose a color. Add (creatures of chosen type) of that color.
/// CR 605.1a — a mana ability (produces mana, no target, doesn't use the stack).
/// Modelled as a dynamic <see cref="ManaAbility"/> using the
/// <c>Func&lt;ManaCost&gt; manaGenerator</c> + <c>additionalCostPayer</c>
/// overload, exactly as <see cref="NykthosShrineToNyxFactory"/> models its
/// "{2},{T}: Add devotion mana":
/// <list type="number">
///   <item><c>canActivateCheck</c> — land untapped AND the controller's pool can
///     pay {2} (read-only affordability probe).</item>
///   <item><c>manaGenerator</c> — counts the creatures the controller controls of
///     the chosen creature type (<see cref="CountCreaturesOfChosenType"/>) and
///     returns that many pips of the up-front-chosen color (CR 605.1c — a count
///     of 0 produces no mana but is still a legal activation).</item>
///   <item>Taps the land (standard {T} cost).</item>
///   <item><c>additionalCostPayer</c> — drains {2} from the pool, part of the same
///     atomic activation cost (CR 602.2a).</item>
/// </list>
///
/// ## Choose a color (CR 105.1 / 105.2a)
/// "Choose a color" is one of the five colors W/U/B/R/G — colorless is NOT a
/// color, so it is rejected. The color choice is supplied up front to the full
/// overload; a live agent prompt is deferred engine-wide (same posture as
/// <see cref="NykthosShrineToNyxFactory"/>).
/// </summary>
[CardName("Three Tree City")]
public static class ThreeTreeCityFactory
{
    public const string CardName = "Three Tree City";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("three-tree-city");

    private static readonly ManaCost TapAdditionalCost = ManaCost.Parse("2");

    // Per-card chosen creature type. CR 614.12-shaped ETB choice captured at
    // factory-build time (engine has no ChooseSubtype prompt yet — same posture
    // as Cavern of Souls). Stored off the public surface so the choice doesn't
    // leak as a mutable property on Land.
    private static readonly ConditionalWeakTable<Land, ChoiceBox> _chosenType = new();

    private sealed class ChoiceBox { public CardSubtype Value; }

    /// <summary>
    /// Construct Three Tree City owned and controlled by <paramref name="owner"/>
    /// with no ETB creature-type choice resolved (shape-only path — only the
    /// JSON-declared "{T}: Add {C}" mana ability is attached; the {2},{T} ability
    /// needs a chosen color and is wired by the full overload).
    /// <see cref="GetChosenType"/> returns null.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Land)CardDefinitionFactory.Build(Definition, owner);
    }

    /// <summary>
    /// Construct Three Tree City and resolve the printed ETB creature-type choice
    /// (CR 614.12) eagerly, but with no color chosen for the {2},{T} ability.
    /// Only the JSON "{T}: Add {C}" ability is wired; the chosen type is stored
    /// and retrievable via <see cref="GetChosenType"/>.
    /// </summary>
    public static Land Create(Player owner, CardSubtype chosenCreatureType)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var land = Create(owner);
        _chosenType.AddOrUpdate(land, new ChoiceBox { Value = chosenCreatureType });
        return land;
    }

    /// <summary>
    /// Construct a fully-wired Three Tree City: resolve the ETB creature-type
    /// choice (CR 614.12) AND wire the {2},{T} ability for the given color.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="chosenCreatureType">The creature type chosen as the land
    /// enters (CR 614.12). The {2},{T} ability counts creatures of this type.</param>
    /// <param name="chosenColor">The color chosen for the {2},{T} ability
    /// (CR 105.1). Must be one of W/U/B/R/G — colorless is not a color and is
    /// rejected.</param>
    public static Land Create(Player owner, CardSubtype chosenCreatureType, ManaColor chosenColor)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (chosenColor is not (ManaColor.White or ManaColor.Blue
            or ManaColor.Black or ManaColor.Red or ManaColor.Green))
        {
            throw new ArgumentOutOfRangeException(
                nameof(chosenColor), chosenColor,
                "Three Tree City's chosen color must be one of W/U/B/R/G — "
                + "colorless is not a color (CR 105.1 / 105.2a).");
        }

        var land = Create(owner, chosenCreatureType);

        // ----------------------------------------------------------------
        // {2}, {T}: Choose a color. Add an amount of mana of that color equal
        // to the number of creatures you control of the chosen type.
        // (CR 605.1a) Dynamic-mana ManaAbility — identical shape to Nykthos'
        // "{2},{T}: Add devotion mana": the {2} additional cost is declared via
        // additionalCostPayer so it composes cleanly with the count-counting
        // Func<ManaCost>.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(
            source: land,
            controller: owner,
            manaGenerator: () => BuildColorMana(
                chosenColor, CountCreaturesOfChosenType(owner, chosenCreatureType)),
            canActivateCheck: () =>
                !land.IsTapped
                && owner.ManaPool.CanPay(TapAdditionalCost),
            additionalCostPayer: controller => controller.PayMana(TapAdditionalCost)));

        return land;
    }

    /// <summary>
    /// Counts the creatures <paramref name="player"/> controls that have
    /// creature type <paramref name="type"/>. Returns 0 for a null player.
    /// Exposed publicly so tests / bots can read the live count.
    /// </summary>
    public static int CountCreaturesOfChosenType(Player player, CardSubtype type)
    {
        if (player == null) return 0;
        var count = 0;
        foreach (var perm in player.Zones.Battlefield.GetCards())
        {
            if (perm is Card card
                && card.HasType(CardType.Creature)
                && card.HasSubtype(type))
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// Build a <see cref="ManaCost"/> of <paramref name="n"/> pips of
    /// <paramref name="color"/>. Returns <see cref="ManaCost.Zero"/> when
    /// <paramref name="n"/> is ≤ 0 (CR 605.1c — zero-mana activation is legal).
    /// </summary>
    internal static ManaCost BuildColorMana(ManaColor color, int n)
    {
        if (n <= 0) return ManaCost.Zero;
        var pip = color switch
        {
            ManaColor.White => "{W}",
            ManaColor.Blue => "{U}",
            ManaColor.Black => "{B}",
            ManaColor.Red => "{R}",
            ManaColor.Green => "{G}",
            _ => throw new ArgumentOutOfRangeException(
                nameof(color), color,
                "Three Tree City's chosen color must be one of W/U/B/R/G (CR 105.1)."),
        };
        return ManaCost.Parse(string.Concat(Enumerable.Repeat(pip, n)));
    }

    /// <summary>
    /// Returns the chosen creature type if one was resolved at construction time,
    /// else null. The choice is per-card (not per-factory).
    /// </summary>
    public static CardSubtype? GetChosenType(Land threeTreeCity)
    {
        ArgumentNullException.ThrowIfNull(threeTreeCity);
        return _chosenType.TryGetValue(threeTreeCity, out var box) ? box.Value : null;
    }
}
