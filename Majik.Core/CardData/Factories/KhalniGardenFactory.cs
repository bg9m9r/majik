using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Khalni Garden (Worldwake).
///
/// Land. Oracle text (Scryfall-confirmed):
///   "This land enters tapped.
///    When this land enters, create a 0/1 green Plant creature token.
///    {T}: Add {G}."
///
/// Scryfall type line: Land (no basic supertype, no subtypes).
///
/// Mirrors the suggested analogues <see cref="DenOfTheBugbearFactory"/> /
/// <see cref="CastleArdenvaleFactory"/> — an enters-tapped land with a
/// vanilla {T}: Add {G} mana ability, plus (unlike those) an ETB triggered
/// ability that mints a token via <see cref="TokenFactory"/>. Here the
/// enters-tapped clause is <i>unconditional</i> (no "unless you control…"
/// rider), so it uses the plain <see cref="EntersTappedReplacement"/>
/// (CR 614.1c) — same posture as the Tranquil Cove / gain-land tap-land
/// cycle.
///
/// ## Implemented (v1)
/// - <b>Land identity</b> — plain nonbasic Land, no supertype, no subtype.
/// - <b>ETB tapped (CR 614.1c)</b> — registered as an
///   <see cref="EntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/>. Unconditional. The single-arg dispatcher
///   path omits the replacement (shape-only posture, matching the
///   tap-land-cycle factories whose unconditional ETB-tapped is applied on
///   the production load path by <see cref="EntersTappedBinder"/>).
/// - <b>When this land enters, create a 0/1 green Plant creature token.</b>
///   Modelled as a <see cref="TriggeredAbility"/> whose condition is
///   <see cref="Triggers.OnEnterBattlefieldSelf(ICard)"/> (CR 603.6e). On
///   resolution it mints one 0/1 green Plant token under the live controller
///   via <see cref="TokenFactory.CreateOnBattlefield(TokenFactory.TokenSpec, Player, Majik.Core.Services.ZoneService?)"/>
///   (CR 111 / 111.4). The token's colour is stamped explicitly green per
///   CR 111.4 (tokens have no mana cost to derive colour from). The effect
///   lambda captures <c>land</c> (not <c>owner</c>) so live controller
///   tracking via <see cref="Card.Controller"/> picks up control-change
///   effects at resolution time (same posture as the analogues).
/// - <b>{T}: Add {G}</b> — vanilla <see cref="ManaAbility"/> (CR 605.1).
///
/// ## Notes
/// - Token-doubling replacements (Doubling Season / Parallel Lives) are not
///   threaded here — the count is a fixed one and no ReplacementBus is
///   available on the trigger's effect closure in the shape-only factory.
///   The single-token mint matches the analogue token-creating factories
///   (e.g. <see cref="DenOfTheBugbearFactory"/>'s attack-trigger path).
/// </summary>
[CardName("Khalni Garden")]
public static class KhalniGardenFactory
{
    public const string CardName = "Khalni Garden";
    public const int TokenPower = 0;
    public const int TokenToughness = 1;

    /// <summary>
    /// Construct Khalni Garden without a <see cref="ReplacementBus"/> wired.
    /// The ETB-tapped replacement is omitted (shape-only posture); the mana
    /// ability and the ETB Plant-token trigger are still attached.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, replacements: null);

    /// <summary>
    /// Construct Khalni Garden.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">When supplied, the unconditional
    /// "enters tapped" replacement is registered (CR 614.1c). May be
    /// null.</param>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Non-basic Land — no supertype, no subtype.
        var land = new Land(CardName, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // ETB tapped (CR 614.1c) — unconditional "This land enters tapped."
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        // ----------------------------------------------------------------
        // {T}: Add {G} — vanilla mana ability (CR 605.1).
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("G")));

        // ----------------------------------------------------------------
        // When this land enters, create a 0/1 green Plant creature token.
        //
        // CR 603.6e — an ETB triggered ability. On resolution it mints one
        // 0/1 green Plant token onto the controller's battlefield
        // (CR 111 / 111.4). The effect captures `land` (not `owner`) so live
        // controller tracking via land.Controller picks up control-change
        // effects at resolution time.
        // ----------------------------------------------------------------
        var createTokenEffect = new Effect(
            $"{CardName}: create a 0/1 green Plant creature token",
            () =>
            {
                var controller = land.Controller ?? owner;
                CreatePlantToken(controller);
            });

        var etbTrigger = new TriggeredAbility(
            source: land,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(land),
            effects: new IEffect[] { createTokenEffect },
            activeZones: new[] { ZoneType.Battlefield });

        land.AddAbility(etbTrigger);

        return land;
    }

    /// <summary>
    /// CR 111 / CR 111.4 — create one 0/1 green Plant creature token under
    /// <paramref name="controller"/>'s control. Colour is stamped explicitly
    /// green (tokens have no mana cost to derive colour from).
    /// </summary>
    public static Creature CreatePlantToken(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: "Plant",
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Plant },
            Keywords: null,
            Colors: new[] { ManaColor.Green });

        return TokenFactory.CreateOnBattlefield(spec, controller);
    }
}
