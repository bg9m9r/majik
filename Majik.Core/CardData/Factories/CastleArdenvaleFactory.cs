using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Castle Ardenvale (Throne of Eldraine / reprints).
///
/// Land. Oracle text (Scryfall-confirmed):
///   "This land enters tapped unless you control a Plains.
///    {T}: Add {W}.
///    {2}{W}{W}, {T}: Create a 1/1 white Human creature token."
///
/// Scryfall type line: Land (no basic supertype, no subtypes).
/// Castle Ardenvale is NOT itself a Plains.
///
/// Mirrors <see cref="CastleLocthwainFactory"/> (the white twin of the
/// Eldraine Castle cycle) — the only differences are the gating subtype
/// (Plains vs Swamp), the produced colour ({W} vs {B}), and the second
/// activated ability (token creation vs draw-and-lose-life).
///
/// ## Implemented (v1)
/// - <b>Land identity</b> — plain nonbasic Land, no supertype, no subtype.
/// - <b>ETB tapped unless you control a Plains (CR 614.1c)</b> — registered
///   as a <see cref="ConditionalEntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/>. The predicate checks whether the
///   controller controls at least one other permanent with the
///   <see cref="CardSubtype.Plains"/> subtype (shocklands with Plains
///   subtype, snow-covered Plains, etc. all qualify). The card itself is
///   excluded via reference equality (same shape as
///   <see cref="CastleLocthwainFactory"/>). Single-arg dispatcher path omits
///   the replacement (shape-only posture).
/// - <b>{T}: Add {W}</b> — vanilla <see cref="ManaAbility"/> (CR 605.1).
/// - <b>{2}{W}{W}, {T}: Create a 1/1 white Human creature token.</b>
///   Modelled as an <see cref="ActivatedAbility"/> with cost stack
///   <c>[ManaCostCost("{2}{W}{W}"), AdditionalCost.Tap(self)]</c>.
///   Resolution mints one 1/1 white Human token via
///   <see cref="TokenFactory.CreateOnBattlefield(TokenFactory.TokenSpec, Player, Majik.Core.Services.ZoneService?)"/>
///   under the live controller (CR 111 / 111.4). The token's colour is
///   stamped explicitly white per CR 111.4.
///
/// ## Notes
/// - The token effect captures <c>land</c> (not <c>owner</c>) so live
///   controller tracking via <see cref="Card.Controller"/> picks up
///   control-change effects at resolution time (same posture as
///   <see cref="CastleLocthwainFactory"/>).
/// - Token-doubling replacements (Doubling Season / Parallel Lives) are not
///   threaded here — the count is a fixed one and no ReplacementBus is
///   available on the activated-ability effect closure in the shape-only
///   factory. The single-token mint matches the analogue token-creating
///   factories (e.g. <see cref="AkroanCrusaderFactory"/>'s heroic path).
/// </summary>
[CardName("Castle Ardenvale")]
public static class CastleArdenvaleFactory
{
    public const string CardName = "Castle Ardenvale";

    /// <summary>
    /// Construct Castle Ardenvale without a <see cref="ReplacementBus"/>
    /// wired. The ETB-tapped-unless-Plains predicate is omitted (shape-only
    /// posture); the mana ability and token ability are still attached.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, replacements: null);

    /// <summary>
    /// Construct Castle Ardenvale.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">When supplied, the
    /// "enters tapped unless you control a Plains" replacement is registered
    /// (CR 614.1c). May be null.</param>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Non-basic Land — no supertype, no subtype.
        var land = new Land(CardName, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // ETB tapped unless you control a Plains (CR 614.1c).
        //
        // Predicate: entersUntappedIf returns true ⟺ the controller
        // controls at least one land (other than this card) with the
        // CardSubtype.Plains subtype. Reference-equality exclusion of self
        // mirrors CastleLocthwainFactory's single-type predicate shape.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new ConditionalEntersTappedReplacement(
                land,
                entersUntappedIf: (controller, self) =>
                    controller.Zones.Battlefield.GetCards()
                        .Any(c => !ReferenceEquals(c, self) && c.HasSubtype(CardSubtype.Plains))));
        }

        // ----------------------------------------------------------------
        // {T}: Add {W} — vanilla mana ability (CR 605.1).
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("W")));

        // ----------------------------------------------------------------
        // {2}{W}{W}, {T}: Create a 1/1 white Human creature token.
        //
        // CR 602 — ordinary activated ability. Cost = {2}{W}{W} mana + tap
        // self. Resolution mints one 1/1 white Human token onto the
        // controller's battlefield (CR 111 / 111.4).
        //
        // The effect lambda captures `land` (not `owner`) so live
        // controller tracking via land.Controller picks up control-change
        // effects at resolution time.
        // ----------------------------------------------------------------
        var createTokenEffect = new Effect(
            $"{CardName}: create a 1/1 white Human creature token",
            () =>
            {
                var controller = land.Controller ?? owner;
                // CR 111.4 — a 1/1 white Human creature token. Colour is
                // stamped explicitly white (tokens have no mana cost to
                // derive colour from). No ZoneService is wired in the
                // shape-only factory, so CreateOnBattlefield falls back to a
                // direct battlefield add.
                var spec = new TokenFactory.TokenSpec(
                    Name: "Human",
                    Power: 1,
                    Toughness: 1,
                    Subtypes: new[] { CardSubtype.Human },
                    Keywords: null,
                    Colors: new[] { ManaColor.White });
                TokenFactory.CreateOnBattlefield(spec, controller, zones: null);
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{2}{W}{W}"),
                AdditionalCost.Tap(land),
            },
            effects: new IEffect[] { createTokenEffect }));

        return land;
    }
}
