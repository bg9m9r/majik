using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Nykthos, Shrine to Nyx (Theros).
///
/// Legendary Land. Oracle text (verified against Scryfall 2026-06-02):
///   "{T}: Add {C}.
///    {2}, {T}: Choose a color. Add an amount of mana of that color equal
///    to your devotion to that color. (Your devotion to a color is the
///    number of mana symbols of that color in the mana costs of permanents
///    you control.)"
///
/// ## Card shape
/// The Legendary Land identity plus the first mana ability
/// ("{T}: Add {C}", CR 605.1a) are declared in
/// <c>Majik.Core/CardData/Cards/nykthos-shrine-to-nyx.json</c> and
/// materialised via <see cref="CardDefinitionFactory"/>. The second
/// ability needs both a runtime color choice and a runtime devotion count,
/// neither of which the JSON <see cref="AbilityDefinition"/> schema
/// expresses, so it is wired in the factory.
///
/// ## {2}, {T}: Choose a color. Add devotion-to-that-color mana.
/// CR 605.1a — a mana ability (produces mana, no target, doesn't use the
/// stack). Modelled as a dynamic <see cref="ManaAbility"/> using the
/// <c>Func&lt;ManaCost&gt; manaGenerator</c> + <c>additionalCostPayer</c>
/// overload, exactly as <see cref="CabalCoffersFactory"/> models
/// "{2},{T}: Add {B} for each Swamp you control":
/// <list type="number">
///   <item><c>canActivateCheck</c> — land untapped AND the controller's
///     pool can pay {2} (read-only affordability probe).</item>
///   <item><c>manaGenerator</c> — counts the controller's devotion to the
///     up-front-chosen color (<see cref="ComputeDevotionToColor"/>) and
///     returns that many pips of that color (CR 700.5).</item>
///   <item>Taps the land (standard {T} cost).</item>
///   <item><c>additionalCostPayer</c> — drains {2} from the pool, part of
///     the same atomic activation cost (CR 602.2a).</item>
/// </list>
/// Activating with devotion 0 is legal (CR 605.1c) — it produces no mana,
/// still pays {2} and taps the land.
///
/// ## Choose a color (CR 105.1 / 105.2a)
/// "Choose a color" is one of the five colors W/U/B/R/G — colorless is NOT
/// a color, so it is rejected. The choice is supplied up front to the full
/// overload; a live agent prompt for the choice is deferred engine-wide
/// (same posture as <see cref="ColdsteelHeartFactory"/> /
/// <see cref="TempleOfTheDragonQueenFactory"/> — callers / tests pass the
/// already-chosen color). The shape-only single-arg dispatcher path wires
/// only the JSON-declared "{T}: Add {C}" ability, matching every other
/// choice-on-activation factory's single-arg posture.
///
/// ## Devotion to a color (CR 700.5)
/// "Your devotion to a color is the number of mana symbols of that color in
/// the mana costs of permanents you control." Mirrors
/// <see cref="HeliodSunCrownedFactory.ComputeDevotionToWhite"/>, generalised
/// across all five colors via <see cref="ComputeDevotionToColor"/>.
///
/// ## Deferred (v1 gaps — shared with the rest of the devotion surface)
/// - <b>Hybrid / Phyrexian pips</b>: CR 700.5a counts every mana symbol that
///   includes the color toward devotion. v1 reads the pure-color pip fields
///   on <see cref="ManaCost"/> only (no hybrid / Phyrexian buckets yet) —
///   the same gap documented on
///   <see cref="HeliodSunCrownedFactory.ComputeDevotionToWhite"/>.
/// - <b>Agent-driven color prompt</b>: deferred engine-wide as above.
/// </summary>
[CardName("Nykthos, Shrine to Nyx")]
public static class NykthosShrineToNyxFactory
{
    public const string CardName = "Nykthos, Shrine to Nyx";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("nykthos-shrine-to-nyx");

    private static readonly ManaCost TapAdditionalCost = ManaCost.Parse("2");

    /// <summary>
    /// Construct Nykthos, Shrine to Nyx owned and controlled by
    /// <paramref name="owner"/> (shape-only path — only the JSON-declared
    /// "{T}: Add {C}" mana ability is attached; the {2},{T} devotion
    /// ability needs a chosen color and is wired by the full overload).
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Land)CardDefinitionFactory.Build(Definition, owner);
    }

    /// <summary>
    /// Construct a fully-wired Nykthos, Shrine to Nyx.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="chosenColor">The color chosen for the {2},{T} ability
    /// (CR 105.1). Must be one of W/U/B/R/G — colorless is not a color and is
    /// rejected. The ability adds devotion-to-that-color pips of that color.</param>
    public static Land Create(Player owner, ManaColor chosenColor)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (chosenColor is not (ManaColor.White or ManaColor.Blue
            or ManaColor.Black or ManaColor.Red or ManaColor.Green))
        {
            throw new ArgumentOutOfRangeException(
                nameof(chosenColor), chosenColor,
                "Nykthos' chosen color must be one of W/U/B/R/G — "
                + "colorless is not a color (CR 105.1 / 105.2a).");
        }

        var land = Create(owner);

        // ----------------------------------------------------------------
        // {2}, {T}: Choose a color. Add an amount of mana of that color
        // equal to your devotion to that color. (CR 605.1a + CR 700.5)
        // Dynamic-mana ManaAbility — identical shape to Cabal Coffers'
        // "{2},{T}: Add {B} for each Swamp you control" (deferral #2): the
        // {2} additional cost is declared via additionalCostPayer so it
        // composes cleanly with the devotion-counting Func<ManaCost>.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(
            source: land,
            controller: owner,
            manaGenerator: () => BuildColorMana(
                chosenColor, ComputeDevotionToColor(owner, chosenColor)),
            canActivateCheck: () =>
                !land.IsTapped
                && owner.ManaPool.CanPay(TapAdditionalCost),
            additionalCostPayer: controller => controller.PayMana(TapAdditionalCost)));

        return land;
    }

    /// <summary>
    /// CR 700.5 — devotion to <paramref name="color"/>. Sum of that color's
    /// mana symbols among the mana costs of permanents
    /// <paramref name="player"/> controls. Reads the pure-color pip fields on
    /// <see cref="ManaCost"/> (hybrid / Phyrexian contributions deferred —
    /// same gap as Heliod). Returns 0 for a null player or a non-color
    /// (colorless / generic) argument. Exposed publicly so tests / bots can
    /// read the live count.
    /// </summary>
    public static int ComputeDevotionToColor(Player player, ManaColor color)
    {
        if (player == null) return 0;
        var total = 0;
        foreach (var perm in player.Zones.Battlefield.GetCards())
        {
            if (perm is Card concrete)
            {
                var cost = concrete.ManaCostValue;
                total += color switch
                {
                    ManaColor.White => cost.White,
                    ManaColor.Blue => cost.Blue,
                    ManaColor.Black => cost.Black,
                    ManaColor.Red => cost.Red,
                    ManaColor.Green => cost.Green,
                    _ => 0,
                };
            }
        }
        return total;
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
                "Nykthos' chosen color must be one of W/U/B/R/G (CR 105.1)."),
        };
        return ManaCost.Parse(string.Concat(Enumerable.Repeat(pip, n)));
    }
}
