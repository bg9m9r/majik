using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Parametric named-card factory for the Scars of Mirrodin allied half of
/// the "fast land" cycle:
///
/// <list type="bullet">
///   <item>Blackcleave Cliffs (B/R)</item>
///   <item>Copperline Gorge (R/G)</item>
///   <item>Darkslick Shores (U/B)</item>
///   <item>Razorverge Thicket (G/W)</item>
///   <item>Seachrome Coast (W/U)</item>
/// </list>
///
/// The Kaladesh enemy half (Blooming Marsh, Botanical Sanctum, Concealed
/// Courtyard, Inspiring Vantage, Spirebluff Canal) was shipped earlier as
/// thin JSON-backed per-card factories and is intentionally NOT routed
/// through this class — those factories already own their <c>[CardName]</c>
/// dispatch entries. Combining the two halves into one factory would
/// require deleting / migrating those five wrappers, which is out of scope
/// for this PR.
///
/// Each Scars member shares the same printed oracle — only the produced
/// colour pair differs — so one factory class handles all five:
/// <code>
/// This land enters tapped unless you control two or fewer other lands.
/// {T}: Add {A} or {B}.
/// </code>
///
/// Args layout (forwarded by the source generator at dispatch time):
/// <c>[0] = printed card name</c>,
/// <c>[1] = first produced colour (single-letter Scryfall code)</c>,
/// <c>[2] = second produced colour (single-letter Scryfall code)</c>.
///
/// ## Implemented (v1)
/// - <b>Land identity</b> — plain <see cref="Land"/>, no supertype, no
///   printed subtype (fastlands are typeless / nonbasic duals).
/// - <b>ETB tapped unless two-or-fewer other lands (CR 614.1c)</b> —
///   registered as a <see cref="ConditionalEntersTappedReplacement"/> on
///   the supplied <see cref="ReplacementBus"/>. Predicate: the land enters
///   untapped iff the controller currently controls two or fewer other
///   lands (the entering card itself is excluded via reference equality).
///   Mirrors the predicate <see cref="ConditionalEntersTappedBinder"/>
///   emits for the "N or fewer other lands" oracle shape — same shape used
///   by the Kamigawa channel lands' production wire-up.
/// - <b>{T}: Add {A} or {B}</b> — split into two <see cref="ManaAbility"/>
///   instances (one per produced colour), same fan-out shape used by the
///   pain land cycle and the check land cycle so the bot's source-picker
///   can iterate produced colours.
///
/// ## Deferred (v1 gaps)
/// - Single-arg dispatcher path constructs without a
///   <see cref="ReplacementBus"/> — the ETB-tapped replacement is omitted
///   (shape-only posture matching every other ETB-replacement factory's
///   single-arg path; see <see cref="CheckLandCycleFactory"/>). Lands enter
///   untapped on this code path; the full overload wires the predicate
///   when the bus is supplied. Production card-load goes through
///   <see cref="ScryfallCardFactory"/>, which constructs with a bus and
///   then layers <see cref="ConditionalEntersTappedBinder"/> on top to
///   read the live oracle text — both paths converge on the same predicate.
/// </summary>
[CardName("Blackcleave Cliffs",  "B", "R")]
[CardName("Copperline Gorge",    "R", "G")]
[CardName("Darkslick Shores",    "U", "B")]
[CardName("Razorverge Thicket",  "G", "W")]
[CardName("Seachrome Coast",     "W", "U")]
public static class FastLandCycleFactory
{
    /// <summary>
    /// Fallback overload — only reachable when constructed by hand.
    /// Default-builds Blackcleave Cliffs (B/R).
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, new[] { "Blackcleave Cliffs", "B", "R" });

    /// <summary>
    /// Construct the fast land identified by <paramref name="args"/>.
    /// Single-arg dispatcher path — no <see cref="ReplacementBus"/> wired.
    /// The ETB-tapped-unless-two-or-fewer-other-lands predicate is omitted
    /// (matches every other ETB-replacement factory's shape-only posture);
    /// the mana abilities are still attached.
    /// </summary>
    public static Land Create(Player owner, string[] args) =>
        Create(owner, args, replacements: null);

    /// <summary>
    /// Construct the fast land identified by <paramref name="args"/> with
    /// an optional <see cref="ReplacementBus"/> for full ETB-tapped wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="args">See class xmldoc for layout.</param>
    /// <param name="replacements">When supplied, the
    /// "enters tapped unless you control two or fewer other lands"
    /// replacement is registered (CR 614.1c).</param>
    public static Land Create(
        Player owner,
        string[] args,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length < 3)
        {
            throw new ArgumentException(
                $"FastLandCycleFactory needs args = [name, colorA, colorB] (got {args.Length}).",
                nameof(args));
        }

        var cardName = args[0];
        var colorA = args[1];
        var colorB = args[2];

        // Non-basic land, no supertype, no printed subtype (Scars fastlands
        // are typeless duals — they don't share basic subtypes with shocks
        // or duals).
        var land = new Land(cardName, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // Enters tapped unless you control two or fewer other lands
        // (CR 614.1c). Predicate returns true ⇒ enters untapped, false ⇒
        // enters tapped. The card itself is excluded from the count via
        // reference equality so the replacement is correct whether the
        // entering card is on the battlefield at predicate time or not
        // (ZoneService runs replacements before the move commits, so the
        // card isn't on the battlefield yet — but future call sites may
        // differ). Same shape as ConditionalEntersTappedBinder's "fewer"
        // branch (the production wire-up).
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new ConditionalEntersTappedReplacement(
                land,
                entersUntappedIf: (controller, self) =>
                    CountOtherLands(controller, self) <= 2));
        }

        // ----------------------------------------------------------------
        // {T}: Add {A} or {B}
        // CR 605.1 — mana ability, no stack. Split into two ManaAbility
        // instances (one per produced colour) so the bot's source-picker
        // can iterate produced colours; same fan-out shape as the check
        // land cycle's coloured modes and the pain land cycle.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse(colorA)));
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse(colorB)));

        return land;
    }

    private static int CountOtherLands(Player controller, ICard self) =>
        controller.Zones.Battlefield.GetCards()
            .Count(c => !ReferenceEquals(c, self) && c.HasType(CardType.Land));
}
