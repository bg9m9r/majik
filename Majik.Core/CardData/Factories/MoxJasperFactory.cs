using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mox Jasper (Modern Horizons 3 Commander).
///
/// Legendary Artifact — {0}.
/// Oracle text:
///   "{T}: Add one mana of any color. Activate only if you control a Dragon."
///
/// ## Implementation (v1)
/// - Card identity: Legendary Artifact with mana cost {0} (colourless).
/// - "Add one mana of any color" is modelled as five <see cref="ManaAbility"/>
///   instances (one per WUBRG) — the same shape as <see cref="MoxAmberFactory"/>
///   / <see cref="MoxOpalFactory"/> / City of Brass / Delighted Halfling.
///   Each ability's <c>canActivateCheck</c> ANDs <c>!IsTapped</c> with a live
///   scan of the controller's battlefield for a Dragon.
/// - "Activate only if you control a Dragon" (CR 602.5e — activation
///   restriction) is the gate: the controller must control at least one
///   permanent with the Dragon creature subtype (CR 205.3m). The scan is
///   scoped to the controller's own battlefield zone, so opponent Dragons
///   do NOT count ("you control" — CR 109.5 / 605.1). <c>Card.HasSubtype</c>
///   reflects both printed and layer-4-granted subtypes, so a permanent that
///   "becomes a Dragon" mid-game (e.g. Cave of the Frost Dragon) is honoured.
/// - The gate is evaluated against the LIVE controller (<c>mox.Controller</c>),
///   so control-change effects are honoured.
///
/// ## Deferred (v1 gaps)
/// - <b>Single modal-colour ability</b>: the engine has no "pick a colour at
///   activation" mana-ability primitive yet — same gap as Mox Amber / Mox Opal
///   / Delighted Halfling / City of Brass. Five separate abilities is the
///   established workaround; the bot's source-picker selects the right colour
///   at payment time. Unlike Mox Amber, all five colours unlock together
///   here ("any color" is unconditional once a Dragon is controlled).
/// </summary>
[CardName("Mox Jasper")]
public static class MoxJasperFactory
{
    public const string CardName = "Mox Jasper";
    public const string PrintedManaCost = "{0}";

    /// <summary>
    /// Construct Mox Jasper owned and controlled by <paramref name="owner"/>.
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
        // {T}: Add one mana of any color. Activate only if you control a Dragon.
        // Five ManaAbility instances ("any color"), each gated on:
        //   (1) Mox Jasper is untapped, AND
        //   (2) the controller controls at least one Dragon (CR 602.5e
        //       activation restriction; CR 205.3m Dragon creature type).
        // --------------------------------------------------------------
        foreach (var color in new[] { "W", "U", "B", "R", "G" })
        {
            mox.AddAbility(new ManaAbility(
                source: mox,
                controller: owner,
                manaGenerated: ManaCost.Parse(color),
                canActivateCheck: () => !mox.IsTapped && ControlsDragon(mox)));
        }

        return mox;
    }

    /// <summary>
    /// True if <paramref name="mox"/>'s controller controls at least one
    /// permanent with the Dragon subtype (CR 205.3m). Scoped to the
    /// controller's battlefield — opponent Dragons do not count
    /// ("you control", CR 109.5).
    /// </summary>
    private static bool ControlsDragon(Artifact mox)
    {
        var controller = mox.Controller;
        if (controller == null) return false;

        foreach (var card in controller.Zones.Battlefield.GetCards())
        {
            if (card.HasSubtype(CardSubtype.Dragon)) return true;
        }

        return false;
    }
}
