using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mox Amber (Dominaria).
///
/// Legendary Artifact — {0}.
/// Oracle text:
///   "{T}: Add one mana of any color among legendary creatures and
///    planeswalkers you control."
///
/// ## Implementation (v1)
/// - Card identity: Legendary Artifact with mana cost {0}.
/// - "Add one mana of any color among legendary creatures and planeswalkers
///   you control" is modelled as five <see cref="ManaAbility"/> instances
///   (one per WUBRG) — same shape as <see cref="MoxOpalFactory"/> /
///   <see cref="DelightedHalflingFactory"/>. Each ability's
///   <c>canActivateCheck</c> ANDs <c>!IsTapped</c> with a live scan of
///   the controller's battlefield: at least one Permanent must be (Legendary
///   AND (Creature OR Planeswalker)) AND its colour identity (derived from
///   <see cref="CardColors.GetColors"/> over the printed mana cost) must
///   include the ability's colour. Opponent legendaries do NOT count —
///   the scan is scoped to the controller's battlefield zone (CR 605.1).
///
/// ## Deferred (v1 gaps)
/// - <b>Single modal-colour ability</b>: the engine has no "pick a colour
///   at activation" mana-ability primitive yet — same gap as Delighted
///   Halfling / City of Brass / Mox Opal. Five separate abilities is the
///   established workaround; the bot's source-picker selects the right
///   colour at payment time.
/// - <b>Colour identity vs. printed colour</b>: this factory uses printed
///   mana-cost-derived colour (<see cref="CardColors"/>) rather than full
///   Commander colour-identity (CR 903.4). For Mox Amber's printed text
///   ("one mana of any color among"), the printed-colour reading is
///   correct — colour-identity would over-count (e.g. an Equipment with
///   coloured activated-ability pips). The two readings coincide for every
///   Dominaria-era legendary creature / planeswalker.
/// - <b>Type-changing effects</b>: a creature being granted (or losing)
///   the Legendary supertype mid-game is sampled live at activation, so
///   layer-4 type-changing effects feed through naturally (mirrors
///   <see cref="MoxOpalFactory"/>'s live artifact-count scan).
/// </summary>
[CardName("Mox Amber")]
public static class MoxAmberFactory
{
    public const string CardName = "Mox Amber";
    public const string PrintedManaCost = "{0}";

    private static readonly (string Code, ManaColor Color)[] Colors =
    {
        ("W", ManaColor.White),
        ("U", ManaColor.Blue),
        ("B", ManaColor.Black),
        ("R", ManaColor.Red),
        ("G", ManaColor.Green),
    };

    /// <summary>
    /// Construct Mox Amber owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var mox = new Artifact(
            CardName,
            PrintedManaCost,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: null);
        mox.SetOwner(owner);
        mox.SetController(owner);

        // --------------------------------------------------------------
        // {T}: Add one mana of any color among legendary creatures and
        // planeswalkers you control.
        // Five ManaAbility instances, each gated on:
        //   (1) Mox Amber is untapped, AND
        //   (2) controller controls at least one legendary creature or
        //       planeswalker whose colour set includes this ability's
        //       colour (CR 105 colour derivation via CardColors).
        // The gate is evaluated against the LIVE controller (mox.Controller)
        // so control-change effects are honoured.
        // --------------------------------------------------------------
        foreach (var (code, color) in Colors)
        {
            var capturedColor = color;
            mox.AddAbility(new ManaAbility(
                source: mox,
                controller: owner,
                manaGenerated: ManaCost.Parse(code),
                canActivateCheck: () => !mox.IsTapped
                                     && ColorAvailable(mox, capturedColor)));
        }

        return mox;
    }

    /// <summary>
    /// True if <paramref name="mox"/>'s controller controls at least one
    /// legendary creature or planeswalker whose colour set contains
    /// <paramref name="color"/>. Colour derived from printed mana cost
    /// via <see cref="CardColors.GetColors"/> (CR 105).
    /// </summary>
    private static bool ColorAvailable(Artifact mox, ManaColor color)
    {
        var controller = mox.Controller;
        if (controller == null) return false;

        foreach (var card in controller.Zones.Battlefield.GetCards())
        {
            if (!card.HasSupertype(CardSupertype.Legendary)) continue;
            if (!(card.HasType(CardType.Creature) || card.HasType(CardType.Planeswalker))) continue;

            var colors = CardColors.GetColors(card);
            if (colors.Contains(color)) return true;
        }

        return false;
    }
}
