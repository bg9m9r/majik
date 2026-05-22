using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData;

/// <summary>
/// Builds the two shared ability shapes used by the Modern Horizons
/// "Horizon Canopy" painless dual cycle: <c>Horizon Canopy</c>, <c>Fiery
/// Islet</c>, <c>Nurturing Peatland</c>, <c>Silent Clearing</c>,
/// <c>Sunbaked Canyon</c>, <c>Waterlogged Grove</c>.
///
/// Two patterns:
/// 1. <b>"{T}, Pay 1 life: Add {C}."</b> — a mana ability with an
///    extra non-mana activation cost (life). Modelled with the
///    additional-cost overload of <see cref="ManaAbility"/>; the
///    activation gate enforces CR 119.4 ("you can't pay life you
///    don't have"). The bot's source-picker scans by produced colour
///    and uses the ability transparently.
/// 2. <b>"{1}, {T}, Sacrifice this land: Draw a card."</b> — a
///    sorcery-speed cycling-style draw activated by sacrificing the
///    land. Same shape as Vexing Bauble's wired ability.
///
/// Why a binder helper (not a binder hooked into the pipeline): the
/// horizon-land oracle text doesn't currently match any of the existing
/// dual-mana / activated-ability regexes, and a fully data-driven path
/// would need new <c>EffectDefinition</c> + <c>ManaAbilityDefinition</c>
/// variants. Centralising the two ability shapes here keeps the
/// per-card factories tiny while leaving the door open for a later
/// regex-bound binder that detects the oracle pattern automatically.
/// </summary>
public static class HorizonLandBinder
{
    /// <summary>
    /// Attach a "{T}, Pay 1 life: Add &lt;color&gt;" mana ability.
    /// <paramref name="color"/> is a Scryfall single-letter colour code
    /// (W/U/B/R/G/C), parsed via <see cref="ManaCost.Parse"/>.
    ///
    /// CR 119.4 — the activation check requires the controller's life
    /// total to be strictly greater than 1 (you can't reduce life to 0
    /// or below as part of a cost).
    /// </summary>
    public static void AttachPayLifeMana(Land land, Player controller, string color)
    {
        ArgumentNullException.ThrowIfNull(land);
        ArgumentNullException.ThrowIfNull(controller);
        if (string.IsNullOrWhiteSpace(color)) throw new ArgumentException("Color required", nameof(color));

        var mana = ManaCost.Parse(color);
        land.AddAbility(new ManaAbility(
            source: land,
            controller: controller,
            manaGenerated: mana,
            canActivateCheck: () => !land.IsTapped && controller.LifeTotal > 1,
            additionalCostPayer: p => p.LoseLife(1)));
    }

    /// <summary>
    /// Attach a "{1}, {T}, Sacrifice this land: Draw a card" activated
    /// ability. Costs are wired via <see cref="ManaCostCost"/> +
    /// <see cref="AdditionalCost.Tap"/> + <see cref="AdditionalCost.Sacrifice"/>;
    /// the effect closure removes the top of the controller's library
    /// and moves it to hand (same shape as Vexing Bauble + the JSON-
    /// driven <c>draw_card</c> effect).
    ///
    /// Sacrifice-cost limitation: <see cref="AdditionalCost.Sacrifice"/>
    /// currently records the intent but doesn't route the land to the
    /// graveyard yet (see <c>AdditionalCost.Pay</c> sacrifice TODO).
    /// When the zone-service plumbing lands, this ability will correctly
    /// sacrifice the land before resolution.
    /// </summary>
    public static void AttachSacDraw(Land land, Player controller)
    {
        ArgumentNullException.ThrowIfNull(land);
        ArgumentNullException.ThrowIfNull(controller);

        var costs = new ICost[]
        {
            new ManaCostCost("1"),
            AdditionalCost.Tap(land),
            AdditionalCost.Sacrifice(land),
        };

        var effect = new Effect(
            $"{land.Name}: draw a card",
            () =>
            {
                var top = controller.Zones.Library.GetCards().FirstOrDefault();
                if (top == null) return; // empty library — SBAs handle loss
                controller.Zones.Library.RemoveCard(top);
                controller.Zones.Hand.AddCard(top);
                top.SetZone(ZoneType.Hand);
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: controller,
            costs: costs,
            effects: new IEffect[] { effect }));
    }
}
