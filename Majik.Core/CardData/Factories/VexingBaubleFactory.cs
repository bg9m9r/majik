using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Vexing Bauble (Modern Horizons 3).
///
/// Artifact — {1}. Oracle text:
///   "Whenever a player casts a spell, if no mana was spent to cast it,
///    counter that spell.
///    {1}, {T}, Sacrifice this artifact: Draw a card."
///
/// ## Implemented (v1)
/// - {1}, {T}, Sacrifice: Draw a card — wired (same cost shape as Clue token
///   plus a tap cost: <see cref="ManaCostCost"/> + <see cref="AdditionalCost.Tap"/>
///   + <see cref="AdditionalCost.Sacrifice"/>).
///
/// ## Deferred (v1 gaps)
/// - <b>"Counter free spells" triggered ability</b>: "Whenever a player casts
///   a spell, if no mana was spent to cast it, counter that spell" requires
///   (a) tracking per-cast mana-spent metadata on the stack object, (b) a
///   triggered condition that inspects that metadata, and (c) a counter-spell
///   effect. Deferred until the stack carries cast-cost provenance.
/// </summary>
public static class VexingBaubleFactory
{
    /// <summary>
    /// Construct Vexing Bauble owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var bauble = new Artifact("Vexing Bauble", "{1}");
        bauble.SetOwner(owner);
        bauble.SetController(owner);

        // ----------------------------------------------------------------
        // {1}, {T}, Sacrifice this artifact: Draw a card.
        // CR 605: not a mana ability — goes on the stack.
        // Cost shape mirrors the Clue token (TokenFactory.BuildClueDrawAbility)
        // plus an added tap cost.
        // ----------------------------------------------------------------
        var drawEffect = new Effect(
            "Vexing Bauble: draw a card",
            () =>
            {
                var top = owner.Zones.Library.GetCards().FirstOrDefault();
                if (top == null) return; // empty library — SBAs handle loss, not here
                owner.Zones.Library.RemoveCard(top);
                owner.Zones.Hand.AddCard(top);
                top.SetZone(ZoneType.Hand);
            });

        var drawAbility = new ActivatedAbility(
            source: bauble,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ManaCost.Parse("1")),
                AdditionalCost.Tap(bauble),
                AdditionalCost.Sacrifice(bauble),
            },
            effects: new IEffect[] { drawEffect });

        bauble.AddAbility(drawAbility);

        return bauble;
    }
}
