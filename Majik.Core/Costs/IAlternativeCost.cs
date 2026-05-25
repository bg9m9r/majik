using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// CR 118.9 — alternative cost. May be paid INSTEAD of the spell's
/// printed mana cost. Each alternative may also impose its own zone
/// restrictions (e.g. Flashback only legal from graveyard) and
/// post-resolution side-effects (Flashback exiles).
/// </summary>
public interface IAlternativeCost
{
    string Description { get; }
    ManaCost AlternativeManaCost { get; }
    bool CanCastFor(ICard card, Player caster);
    /// <summary>Called after the spell resolves to apply any side-effect
    /// the alternative cost imposes (e.g. exile the card).</summary>
    void OnResolved(ICard card, Player caster);

    /// <summary>
    /// Optional override of the spell's post-resolution destination zone.
    /// Used by alt-costs that need the card to land somewhere other than
    /// the printed-type default (CR 608.2: instants/sorceries → graveyard,
    /// permanents → battlefield). Adventure (CR 715.3d) overrides to
    /// <see cref="ZoneType.Exile"/> so a Creature card cast as an Adventure
    /// sorcery does not enter the battlefield as a permanent on resolve.
    /// Null = follow the printed-type default (the historical behaviour;
    /// every pre-existing alt-cost type still returns null).
    /// </summary>
    ZoneType? PostResolutionZone => null;

    /// <summary>
    /// Optional X-value override for X-cost spells cast via this alt cost.
    /// CR 107.3b — the alt-cost may fix X without an agent prompt; e.g.
    /// Disrupting Shoal's mv-matched pitch sets X = pitched card's mana value.
    /// When non-null, <see cref="Majik.Core.Game.SpellCastFlow"/> uses this
    /// instead of calling <see cref="Majik.Core.Players.Agents.IPlayerAgent.ChooseXAsync"/>
    /// and does NOT add X to the total mana cost (the pitch IS the entire
    /// cost — CR 118.9). Null = honour the printed X-cost path (the
    /// historical behaviour; every pre-existing alt-cost type returns null).
    /// </summary>
    int? OverrideX => null;
}
