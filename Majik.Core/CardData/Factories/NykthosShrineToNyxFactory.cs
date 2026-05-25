using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Nykthos, Shrine to Nyx (Theros / reprints).
///
/// Legendary Land. Oracle text:
///   "{T}: Add {C}.
///    {2}, {T}: Choose a color. Add an amount of mana of that color equal
///    to your devotion to that color. (Your devotion to a color is the
///    number of mana symbols of that color among the mana costs of
///    permanents you control.)"
///
/// ## Implemented (v1)
/// - Legendary Land identity (no printed subtypes — Nykthos is a generic
///   <see cref="CardType.Land"/>; the Legendary supertype matters for the
///   legend rule, CR 704.5j).
/// - <b>{T}: Add {C}</b> — vanilla <see cref="ManaAbility"/> (CR 605.1,
///   never on the stack). {C} bucketed as Generic +1 per
///   <see cref="ManaCost.Parse"/> (same as Mutavault / Phyrexian Tower).
/// - <b>{2}, {T}: Choose a color. Add N mana of that color, N = devotion
///   to that color.</b> CR 605.1b — still a mana ability (could produce
///   mana, no target, doesn't trigger when activated), so it does NOT go
///   on the stack. Wired via the new dynamic-generator + additional-cost
///   overload of <see cref="ManaAbility"/>: the <c>manaGenerator</c>
///   samples <see cref="ComputeDevotion"/> at activation time (devotion
///   bumps mid-game ARE observed), the <c>additionalCostPayer</c>
///   deducts {2} from the controller's mana pool, the <c>{T}</c> tap is
///   the default tap-as-cost path. Five <see cref="ManaAbility"/> slots
///   (one per WUBRG) stand in for the "Choose a color" prompt — same
///   pattern Cavern of Souls uses. <c>canActivateCheck</c> gates on tap
///   state, {2}-affordability, and non-zero devotion-to-that-colour
///   (CR 605.5 lets the ability resolve at devotion=0 too — but the bot
///   path benefits from short-circuiting so the {2} isn't burned for
///   zero mana).
///
/// ## Devotion model (CR 700.5)
/// Devotion to colour C = the number of mana symbols of C among the
/// mana costs of permanents the player controls.
/// <see cref="ComputeDevotion(Player, ManaColor)"/> sums the per-colour
/// pip counts from <see cref="Card.ManaCostValue"/> across the
/// controller's battlefield, mirroring
/// <see cref="HeliodSunCrownedFactory.ComputeDevotionToWhite"/>.
///
/// ### v1 simplifications (devotion gaps)
/// - <b>Hybrid pips ({W/U}, {2/W}, …)</b> — CR 700.5a says any mana
///   symbol that includes colour C counts toward devotion to C. v1 reads
///   pure-{C} pips only via <see cref="ManaCost.White"/> et al.; hybrid
///   contributions are deferred (same gap as Heliod).
/// - <b>Phyrexian pips ({U/P})</b> — same story: CR 700.5a includes them,
///   v1 doesn't read <see cref="ManaCost.PhyrexianPips"/>.
/// - <b>Tokens</b> — count only if their token-spec carries colour pips.
///   Plain token specs with empty cost contribute zero.
/// - <b>Continuous-effect colour adjustments</b> — copy effects, colour-
///   stripping (Painter's Servant et al.) aren't observed; devotion
///   reads printed costs only.
///
/// ## Deferred (v1 gaps beyond devotion)
/// - <b>"Choose a color" prompt</b>: the engine has no ChooseColor agent
///   prompt yet. Five separate <see cref="ManaAbility"/> slots stand in
///   for the choice (same pattern Cavern of Souls uses). When a
///   ChooseColor prompt lands, this can collapse to one ability with a
///   deferred colour resolution.
/// </summary>
[CardName("Nykthos, Shrine to Nyx")]
public static class NykthosShrineToNyxFactory
{
    public const string CardName = "Nykthos, Shrine to Nyx";

    /// <summary>
    /// Construct Nykthos, Shrine to Nyx. Legendary Land with one
    /// vanilla {T}: Add {C} ability plus five devotion-driven
    /// "{2}, {T}: Add N {colour}" mana abilities (one per WUBRG).
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(
            CardName,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Add {C}
        // CR 605.1 — mana abilities don't use the stack. {C} bucketed as
        // +1 Generic (ManaCost.Parse; mirrors Mutavault, Phyrexian Tower).
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("C")));

        // ----------------------------------------------------------------
        // {2}, {T}: Choose a color. Add N mana of that color, N =
        //   controller's devotion to that color (CR 700.5).
        //
        // Five ManaAbility slots (one per WUBRG) stand in for the
        // "choose a color" prompt — same pattern as Cavern of Souls.
        // Each slot uses the dynamic-generator + additional-cost
        // overload so:
        //   - the N is re-sampled at activation time (devotion bumps
        //     mid-game are honoured),
        //   - the {2} extra cost is paid atomically with the {T} tap,
        //   - the slot is gated on tap state, {2}-affordability, AND
        //     non-zero devotion-to-that-colour (CR 605.5 lets a
        //     zero-mana activation resolve, but the bot benefits from
        //     not burning {2} for nothing).
        // ----------------------------------------------------------------
        var twoGeneric = ManaCost.Parse("2");

        foreach (var (color, pip) in DevotionColors)
        {
            var capturedColor = color;
            var capturedPip = pip;

            land.AddAbility(new ManaAbility(
                source: land,
                controller: owner,
                manaGenerator: () =>
                {
                    var n = ComputeDevotion(owner, capturedColor);
                    return n <= 0
                        ? ManaCost.Zero
                        : ManaCost.Parse(new string(capturedPip, n));
                },
                canActivateCheck: () =>
                    !land.IsTapped
                    && owner.ManaPool.CanPay(twoGeneric)
                    && ComputeDevotion(owner, capturedColor) > 0,
                additionalCostPayer: p => p.PayMana(twoGeneric)));
        }

        return land;
    }

    /// <summary>
    /// The five WUBRG colour slots Nykthos's devotion ability iterates
    /// over. Pip is the single-character mana symbol passed to
    /// <see cref="ManaCost.Parse"/> when synthesising the produced cost.
    /// </summary>
    private static readonly IReadOnlyList<(ManaColor Color, char Pip)> DevotionColors =
        new[]
        {
            (ManaColor.White, 'W'),
            (ManaColor.Blue,  'U'),
            (ManaColor.Black, 'B'),
            (ManaColor.Red,   'R'),
            (ManaColor.Green, 'G'),
        };

    /// <summary>
    /// CR 700.5 — devotion to colour <paramref name="color"/>. Sum of
    /// pure-<paramref name="color"/> mana symbols across the mana costs
    /// of permanents <paramref name="player"/> controls.
    ///
    /// Exposed publicly so tests / bots can read live devotion without
    /// activating the ability.
    ///
    /// v1 reads pure-colour pips via <see cref="ManaCost.White"/> et al.
    /// Hybrid + Phyrexian pip contributions (CR 700.5a) are deferred —
    /// same gap as
    /// <see cref="HeliodSunCrownedFactory.ComputeDevotionToWhite"/>.
    /// Colorless ({C}) is not a colour for devotion purposes
    /// (CR 700.5 — devotion is per *colour*, and colourless isn't one);
    /// the helper returns 0 for any non-WUBRG argument.
    /// </summary>
    public static int ComputeDevotion(Player player, ManaColor color)
    {
        if (player == null) return 0;
        var total = 0;
        foreach (var perm in player.Zones.Battlefield.GetCards())
        {
            if (perm is Card concrete)
            {
                total += color switch
                {
                    ManaColor.White => concrete.ManaCostValue.White,
                    ManaColor.Blue  => concrete.ManaCostValue.Blue,
                    ManaColor.Black => concrete.ManaCostValue.Black,
                    ManaColor.Red   => concrete.ManaCostValue.Red,
                    ManaColor.Green => concrete.ManaCostValue.Green,
                    _ => 0,
                };
            }
        }
        return total;
    }
}
