using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Parametric named-card factory for the 10-member Ravnica "shock land"
/// dual-land cycle:
///
/// <list type="bullet">
///   <item>Ravnica: City of Guilds (original): Sacred Foundry,
///     Hallowed Fountain, Watery Grave, Overgrown Tomb, Temple Garden.</item>
///   <item>Return to Ravnica / Gatecrash (reprint):
///     Blood Crypt, Godless Shrine, Breeding Pool, Stomping Ground,
///     Steam Vents.</item>
/// </list>
///
/// Each shock land is a dual-typed nonbasic land carrying both basic-land
/// subtypes (CR 305.6) with the same printed oracle (only the colour /
/// subtype pair differs):
/// <code>
/// ({T}: Add {A} or {B}.)
/// As [Card] enters, you may pay 2 life. If you don't, it enters tapped.
/// </code>
///
/// Args layout (forwarded by the source generator at dispatch time):
/// <c>[0] = printed card name</c>,
/// <c>[1] = first basic land subtype (e.g. "Mountain")</c>,
/// <c>[2] = second basic land subtype (e.g. "Plains")</c>,
/// <c>[3] = first produced colour (single-letter Scryfall code)</c>,
/// <c>[4] = second produced colour</c>.
///
/// ## Implemented (v1)
/// - <b>Land identity</b> — non-Basic <see cref="Land"/> carrying the two
///   printed basic land subtypes (CR 305.6 — Sacred Foundry IS a Mountain
///   AND a Plains for purposes of every "if you control a Mountain" check,
///   shocklands tapping for {R} or {W} etc.). No supertype.
/// - <b>{T}: Add {A} or {B}</b> — split into two <see cref="ManaAbility"/>
///   instances (one per produced colour), same fan-out shape as
///   <see cref="CheckLandCycleFactory"/> and <see cref="PainLandCycleFactory"/>.
///   The bot's source-picker iterates produced colours and picks the matching
///   ability per spell. The basic-land subtypes don't auto-attach intrinsic
///   mana abilities in this engine (see
///   <see cref="LavaclawReachesFactory"/> — printed oracle abilities are
///   the source of truth).
/// - <b>ETB "you may pay 2 life; if you don't, it enters tapped" (CR 614.1c)</b>
///   via <see cref="ConditionalEntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/>. Predicate:
///     - Consults the registered <see cref="IPlayerAgent"/> for the controller
///       via <see cref="IPlayerAgent.ChooseYesNoAsync"/> with intent
///       <c>BotIntent.LoseLife | BotIntent.CostToDecline</c>. Heuristic /
///       scripted bots see the LoseLife flag and decline by default
///       (untapped tempo isn't worth 2 life when life total is precarious);
///       remote-agent UIs surface the printed question verbatim.
///     - Honours CR 119.4 — "you can't pay life you don't have." If the
///       controller has fewer than 2 life remaining the agent prompt is
///       skipped and the land enters tapped. (Shocklands at exactly 2 life
///       can pay and drop to 0 — SBA-driven loss happens after the
///       replacement resolves; same approach the rules text takes.)
///     - On a "yes" the controller's life is reduced by 2 via
///       <see cref="Player.LoseLife"/> (CR 118.8 — payment of life)
///       and the land enters untapped (replacement leaves
///       <see cref="ZoneMoveIntent.EntersTapped"/> false).
///     - On a "no" / no-agent / can't-afford path the replacement flips
///       <see cref="ZoneMoveIntent.EntersTapped"/> to true.
/// - <b>Single-arg dispatcher path</b> — constructs without a
///   <see cref="ReplacementBus"/>. The ETB-tapped / pay-2 replacement is
///   omitted (shape-only posture matching every other ETB-replacement
///   factory's single-arg shape — see <see cref="CheckLandCycleFactory"/>).
///   Lands enter untapped on this path; the full overload wires the
///   replacement when the bus is supplied.
///
/// ## Deferred (v1 gaps)
/// - <b>Life-payment event provenance</b>: the 2-life payment runs through
///   <see cref="Player.LoseLife"/>, not a dedicated <c>LifePaidEvent</c>.
///   "Whenever a player pays life" triggers (Bolas's Citadel, Daxos of
///   Meletis, etc.) do not see this payment; same simplification the
///   Horizon Canopy / fetchland 1-life shapes take.
/// - <b>CR 119.4 floor at exactly 2 life</b>: the predicate allows payment
///   when life ≥ 2, dropping the controller to 0 (legal — see Rule 119.4
///   carve-out for life payments that bring you to 0). SBAs handle the
///   loss-of-game afterward. The "less than 2" gate matches the floor
///   precisely.
/// </summary>
[CardName("Sacred Foundry",   "Mountain", "Plains",   "R", "W")]
[CardName("Hallowed Fountain", "Plains",   "Island",   "W", "U")]
[CardName("Watery Grave",     "Island",   "Swamp",    "U", "B")]
[CardName("Overgrown Tomb",   "Swamp",    "Forest",   "B", "G")]
[CardName("Temple Garden",    "Forest",   "Plains",   "G", "W")]
[CardName("Blood Crypt",      "Swamp",    "Mountain", "B", "R")]
[CardName("Godless Shrine",   "Plains",   "Swamp",    "W", "B")]
[CardName("Breeding Pool",    "Forest",   "Island",   "G", "U")]
[CardName("Stomping Ground",  "Mountain", "Forest",   "R", "G")]
[CardName("Steam Vents",      "Island",   "Mountain", "U", "R")]
public static class ShockLandCycleFactory
{
    /// <summary>
    /// Fallback overload — only reachable when constructed by hand.
    /// Default-builds Sacred Foundry (R/W, Mountain/Plains).
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, new[] { "Sacred Foundry", "Mountain", "Plains", "R", "W" });

    /// <summary>
    /// Construct the shock land identified by <paramref name="args"/>.
    /// Single-arg dispatcher path — no <see cref="ReplacementBus"/> wired.
    /// The ETB "may pay 2 life or enter tapped" predicate is omitted
    /// (shape-only posture, matches <see cref="CheckLandCycleFactory"/>);
    /// the mana abilities + dual subtypes are still attached.
    /// </summary>
    public static Land Create(Player owner, string[] args) =>
        Create(owner, args, replacements: null);

    /// <summary>
    /// Construct the shock land identified by <paramref name="args"/> with
    /// an optional <see cref="ReplacementBus"/> for full ETB wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="args">See class xmldoc for layout.</param>
    /// <param name="replacements">When supplied, the
    /// "you may pay 2 life; if you don't, it enters tapped" replacement is
    /// registered (CR 614.1c).</param>
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
                $"ShockLandCycleFactory needs args = [name, subtypeA, subtypeB, colorA, colorB] (got {args.Length}).",
                nameof(args));
        }

        var cardName = args[0];
        var subtypeAName = args[1];
        var subtypeBName = args[2];
        var colorA = args[3];
        var colorB = args[4];

        var subtypeA = ParseBasicSubtype(subtypeAName)
            ?? throw new ArgumentException(
                $"ShockLandCycleFactory: unknown basic subtype '{subtypeAName}'.",
                nameof(args));
        var subtypeB = ParseBasicSubtype(subtypeBName)
            ?? throw new ArgumentException(
                $"ShockLandCycleFactory: unknown basic subtype '{subtypeBName}'.",
                nameof(args));

        // CR 305.6 — shocklands are nonbasic land with two basic-land
        // subtypes printed on them. They are NOT Basic (no supertype).
        var land = new Land(
            cardName,
            supertypes: null,
            subtypes: new[] { subtypeA, subtypeB });
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // ETB: "As [Card] enters, you may pay 2 life. If you don't, it
        // enters tapped." (CR 614.1c)
        //
        // Modelled as a single ConditionalEntersTappedReplacement: the
        // predicate returns true (untapped) iff the controller can pay
        // and elects to pay 2 life — and on a yes, deducts the life as a
        // side-effect inside the predicate. Returning false flips the
        // intent's EntersTapped to true.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new ConditionalEntersTappedReplacement(
                land,
                entersUntappedIf: (controller, self) =>
                    TryPayTwoLifeOrEnterTapped(controller, self)));
        }

        // ----------------------------------------------------------------
        // {T}: Add {A} or {B}
        // CR 605.1 — mana ability, no stack. Split into two ManaAbility
        // instances (one per produced colour). Same shape as Check / Pain
        // / Filter land cycles.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse(colorA)));
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse(colorB)));

        return land;
    }

    /// <summary>
    /// Predicate body for the ETB replacement. Consults the controller's
    /// registered agent for the pay-2-life optional, honours CR 119.4
    /// ("you can't pay life you don't have"), and deducts the 2 life as
    /// a side-effect on the yes path.
    /// </summary>
    /// <returns><c>true</c> ⇒ land enters untapped (life was paid).
    /// <c>false</c> ⇒ enters tapped (declined, no agent, or insufficient
    /// life).</returns>
    private static bool TryPayTwoLifeOrEnterTapped(Player controller, ICard self)
    {
        _ = self;
        // CR 119.4 — you can't pay life you don't have. With life total
        // at or above 2 the payment is legal (dropping to 0 is allowed
        // for a life payment — SBAs handle the loss afterward).
        if (controller.LifeTotal < 2) return false;

        var agent = AgentRegistry.Get(controller);
        if (agent == null)
        {
            // No agent registered — default to declining the optional
            // payment so the land enters tapped. Matches the
            // shape-only posture of the single-arg dispatcher path and
            // the legacy auto-tapped default.
            return false;
        }

        bool wantsToPay;
        try
        {
            wantsToPay = agent.ChooseYesNoAsync(
                question: "Pay 2 life so this land enters untapped?",
                intent: BotIntent.LoseLife | BotIntent.CostToDecline,
                ct: default)
                .GetAwaiter().GetResult();
        }
        catch
        {
            // Defensive: any agent failure → fall back to entering tapped.
            return false;
        }

        if (!wantsToPay) return false;

        // CR 118.8 — pay 2 life. Run-through Player.LoseLife so combat
        // / SBA listeners observe the life change.
        controller.LoseLife(2);
        return true;
    }

    private static CardSubtype? ParseBasicSubtype(string raw) =>
        Enum.TryParse<CardSubtype>(raw, ignoreCase: true, out var s) ? s : null;
}
