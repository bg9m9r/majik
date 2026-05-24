using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Treasure Cruise (Khans of Tarkir, {7}{U}).
///
/// Sorcery. Oracle text:
///   "Delve (Each card you exile from your graveyard while casting this
///    spell pays for {1}.)
///    Draw three cards."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {7}{U}.
/// - "Delve" marker keyword via <see cref="KeywordAbility"/> so downstream
///   code (UI, bot probes, action validator) can introspect the keyword.
///   The actual Delve mechanic (CR 702.66) lives in
///   <see cref="Majik.Core.Costs.DelveCost"/> + <see cref="Majik.Core.Game.SpellCastFlow"/>;
///   callers cast Treasure Cruise via the cast-flow's <c>delveCost</c>
///   parameter when they want to substitute graveyard exiles for generic
///   mana.
/// - On-resolve "Draw three cards" effect, exposed via
///   <see cref="BuildResolveEffect"/> so tests/integrations can pass it
///   to a <c>SpellDefinition</c>.
///
/// ## Bot-side discovery
/// - <see cref="Majik.Core.Players.Agents.DelveAltCostProbe"/> surfaces
///   Treasure Cruise to the heuristic bot's
///   <see cref="Majik.Core.Players.Agents.IAlternativeCostProbe"/> stream
///   via the Delve <see cref="KeywordAbility"/> marker. The probe yields a
///   <see cref="Majik.Core.Costs.DelveAlternativeCost"/> that reduces the
///   generic mana cost by the graveyard exiles the bot selects.
/// </summary>
[CardName("Treasure Cruise")]
public static class TreasureCruiseFactory
{
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(name: "Treasure Cruise", manaCost: "{7}{U}");
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.66 — Delve marker. The behavior itself is in DelveCost +
        // SpellCastFlow; the marker is here so introspection (UI, bots)
        // can see the keyword on the card.
        card.AddAbility(new KeywordAbility("Delve", card, owner));

        return card;
    }

    /// <summary>
    /// Build the "Draw three cards" resolution effect for Treasure Cruise.
    /// Returns an <see cref="IEffect"/> array suitable for passing as the
    /// <c>effects</c> argument to a <see cref="Majik.Core.Spells.Spell"/>
    /// or as a <c>SpellDefinition.EffectFactory</c> return value.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return new IEffect[]
        {
            new Effect("Treasure Cruise: draw three cards.", () =>
            {
                // Simple top-of-library draws. Three iterations because
                // CR 121.1 ("Draw a card") repeats; an empty library mid-
                // draw flags the player for state-based loss (CR 704.5b)
                // — handled by other systems, not this effect.
                for (var i = 0; i < 3; i++)
                {
                    var top = caster.Zones.Library.GetCards().FirstOrDefault();
                    if (top == null)
                    {
                        caster.MarkTriedToDrawFromEmptyLibrary();
                        return;
                    }
                    caster.Zones.Library.RemoveCard(top);
                    caster.Zones.Hand.AddCard(top);
                    top.SetZone(ZoneType.Hand);
                }
            }),
        };
    }
}
