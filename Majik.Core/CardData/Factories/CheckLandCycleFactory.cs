using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Parametric named-card factory for the 10-member "check land" cycle —
/// Magic 2010 allied + Innistrad enemy:
///
/// <list type="bullet">
///   <item>M10 (allied):   Glacial Fortress, Drowned Catacomb,
///     Dragonskull Summit, Rootbound Crag, Sunpetal Grove.</item>
///   <item>Innistrad (enemy): Isolated Chapel, Clifftop Retreat,
///     Hinterland Harbor, Sulfur Falls, Woodland Cemetery.</item>
/// </list>
///
/// Each member shares the same printed oracle — only the produced
/// colour pair and the two land subtypes consulted by the ETB-tapped
/// predicate differ — so one factory class handles all ten:
/// <code>
/// This land enters tapped unless you control a [Basic A] or a [Basic B].
/// {T}: Add {A} or {B}.
/// </code>
///
/// Args layout (forwarded by the source generator at dispatch time):
/// <c>[0] = printed card name</c>,
/// <c>[1] = first basic land subtype (e.g. "Plains")</c>,
/// <c>[2] = second basic land subtype (e.g. "Island")</c>,
/// <c>[3] = first produced colour (single-letter Scryfall code)</c>,
/// <c>[4] = second produced colour (single-letter Scryfall code)</c>.
///
/// ## Implemented (v1)
/// - <b>Land identity</b> — plain <see cref="Land"/>, no supertype, no
///   printed subtype (check lands are nonbasic, non-typed).
/// - <b>ETB tapped unless basic-type match (CR 614.1c)</b> — registered as
///   a <see cref="ConditionalEntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/>. Predicate: the land enters untapped iff
///   the controller controls another land (excluding this one) with the
///   first OR second basic subtype. Mirrors the predicate
///   <see cref="SubtypeEntersTappedBinder"/> emits for the two-subtype
///   "unless you control an X or a Y" oracle shape. Predicate intentionally
///   uses <c>HasSubtype</c> (not <c>HasSupertype(Basic)</c>) so any land
///   with the named subtype qualifies — shocklands, M10 dual subtypes,
///   etc. all light up the check, matching the printed oracle (the word
///   "basic" appears in many printings but the engine canonicalises on
///   subtype matching, same as the binder).
/// - <b>{T}: Add {A} or {B}</b> — split into two <see cref="ManaAbility"/>
///   instances (one per produced colour), same fan-out shape used by
///   <see cref="PainLandCycleFactory"/>'s coloured modes and the bot's
///   source-picker iterates by produced colour to pick the matching
///   ability per spell.
///
/// ## Deferred (v1 gaps)
/// - Single-arg dispatcher path constructs without a
///   <see cref="ReplacementBus"/> — the ETB-tapped replacement is omitted
///   (shape-only posture matching every other ETB-replacement factory's
///   single-arg path). Lands enter untapped on this code path; the full
///   overload wires the predicate when the bus is supplied.
/// </summary>
[CardName("Glacial Fortress",    "Plains",   "Island",   "W", "U")]
[CardName("Drowned Catacomb",    "Island",   "Swamp",    "U", "B")]
[CardName("Dragonskull Summit",  "Swamp",    "Mountain", "B", "R")]
[CardName("Rootbound Crag",      "Mountain", "Forest",   "R", "G")]
[CardName("Sunpetal Grove",      "Forest",   "Plains",   "G", "W")]
[CardName("Isolated Chapel",     "Plains",   "Swamp",    "W", "B")]
[CardName("Clifftop Retreat",    "Mountain", "Plains",   "R", "W")]
[CardName("Hinterland Harbor",   "Forest",   "Island",   "G", "U")]
[CardName("Sulfur Falls",        "Island",   "Mountain", "U", "R")]
[CardName("Woodland Cemetery",   "Swamp",    "Forest",   "B", "G")]
public static class CheckLandCycleFactory
{
    /// <summary>
    /// Fallback overload — only reachable when constructed by hand.
    /// Default-builds Glacial Fortress (W/U, Plains/Island).
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, new[] { "Glacial Fortress", "Plains", "Island", "W", "U" });

    /// <summary>
    /// Construct the check land identified by <paramref name="args"/>.
    /// Single-arg dispatcher path — no <see cref="ReplacementBus"/> wired.
    /// The ETB-tapped-unless-basic predicate is omitted (matches every
    /// other ETB-replacement factory's shape-only posture); the mana
    /// abilities are still attached.
    /// </summary>
    public static Land Create(Player owner, string[] args) =>
        Create(owner, args, replacements: null);

    /// <summary>
    /// Construct the check land identified by <paramref name="args"/> with
    /// an optional <see cref="ReplacementBus"/> for full ETB-tapped wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="args">See class xmldoc for layout.</param>
    /// <param name="replacements">When supplied, the
    /// "enters tapped unless you control a [BasicA] or [BasicB]"
    /// replacement is registered (CR 614.1c).</param>
    public static Land Create(
        Player owner,
        string[] args,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length < 5)
        {
            throw new ArgumentException(
                $"CheckLandCycleFactory needs args = [name, basicA, basicB, colorA, colorB] (got {args.Length}).",
                nameof(args));
        }

        var cardName = args[0];
        var basicAName = args[1];
        var basicBName = args[2];
        var colorA = args[3];
        var colorB = args[4];

        var basicA = ParseBasicSubtype(basicAName)
            ?? throw new ArgumentException(
                $"CheckLandCycleFactory: unknown basic subtype '{basicAName}'.",
                nameof(args));
        var basicB = ParseBasicSubtype(basicBName)
            ?? throw new ArgumentException(
                $"CheckLandCycleFactory: unknown basic subtype '{basicBName}'.",
                nameof(args));

        // Non-basic land, no supertype, no printed subtype.
        var land = new Land(cardName, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // Enters tapped unless you control a [BasicA] or a [BasicB]
        // (CR 614.1c). Predicate returns true ⇒ enters untapped, false ⇒
        // enters tapped. The card itself is excluded from the count via
        // reference equality so the replacement is correct whether the
        // entering card is on the battlefield at predicate time or not.
        // Same shape as SubtypeEntersTappedBinder's two-subtype branch.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new ConditionalEntersTappedReplacement(
                land,
                entersUntappedIf: (controller, self) =>
                    ControllerHasSubtype(controller, self, basicA)
                    || ControllerHasSubtype(controller, self, basicB)));
        }

        // ----------------------------------------------------------------
        // {T}: Add {A} or {B}
        // CR 605.1 — mana ability, no stack. Split into two ManaAbility
        // instances (one per produced colour) so the bot's source-picker
        // can iterate produced colours; same fan-out shape as the pain
        // land cycle's coloured modes.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse(colorA)));
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse(colorB)));

        return land;
    }

    private static bool ControllerHasSubtype(
        Player controller,
        ICard self,
        CardSubtype subtype) =>
        controller.Zones.Battlefield.GetCards()
            .Any(c => !ReferenceEquals(c, self) && c.HasSubtype(subtype));

    private static CardSubtype? ParseBasicSubtype(string raw) =>
        Enum.TryParse<CardSubtype>(raw, ignoreCase: true, out var s) ? s : null;
}
