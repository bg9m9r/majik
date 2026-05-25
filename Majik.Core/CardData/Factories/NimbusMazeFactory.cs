using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Nimbus Maze (Future Sight).
///
/// Land. Oracle text:
///   "{T}: Add {C}.
///    {T}: Add {W}. Activate only if you control a Plains.
///    {T}: Add {U}. Activate only if you control an Island."
///
/// ## Implemented (v1)
/// - Plain Land identity (no printed subtype, no supertype).
/// - <b>{T}: Add {C}</b> — vanilla <see cref="ManaAbility"/> (CR 605.1).
/// - <b>{T}: Add {W} gated on controlling a Plains</b> — same shape, with
///   a <c>canActivateCheck</c> that ANDs the printed tap gate
///   (<c>!IsTapped</c>) with a live scan of the controller's battlefield
///   for a permanent with <see cref="CardSubtype.Plains"/>. CR 605.1 — the
///   ability remains a mana ability (no stack).
/// - <b>{T}: Add {U} gated on controlling an Island</b> — same posture as
///   the {W} ability with the subtype check pointed at
///   <see cref="CardSubtype.Island"/>.
///
/// ## Notes
/// - The subtype scan reads <c>card.Controller</c> live so control-change
///   effects (Threaten, etc.) re-point the gate (CR 109.5).
/// - "A Plains" / "an Island" matches any permanent with that subtype on
///   the controller's battlefield — basics, dual lands (Tundra), shocks
///   (Hallowed Fountain), and any Plains/Island-typed permanent all
///   qualify. This is the printed reading of "you control a Plains".
/// - Nimbus Maze itself is just "Land" (no Plains / Island subtype) so it
///   never satisfies its own gates by being the only permanent on the
///   battlefield — matches the printed colour-fixing intent.
/// </summary>
[CardName("Nimbus Maze")]
public static class NimbusMazeFactory
{
    public const string CardName = "Nimbus Maze";

    /// <summary>
    /// Construct Nimbus Maze owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Plain Land — no subtype, no supertype.
        var land = new Land(CardName);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Add {C} — vanilla mana ability (CR 605.1).
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("C")));

        // ----------------------------------------------------------------
        // {T}: Add {W}. Activate only if you control a Plains.
        // The canActivateCheck ANDs the printed-tap legality
        // (`!IsTapped`) with the controller-controls-a-Plains scan;
        // controller is read live so control-change retargets the scan.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(
            source: land,
            controller: owner,
            manaGenerated: ManaCost.Parse("W"),
            canActivateCheck: () =>
                !land.IsTapped && ControllerControlsSubtype(land, CardSubtype.Plains)));

        // ----------------------------------------------------------------
        // {T}: Add {U}. Activate only if you control an Island.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(
            source: land,
            controller: owner,
            manaGenerated: ManaCost.Parse("U"),
            canActivateCheck: () =>
                !land.IsTapped && ControllerControlsSubtype(land, CardSubtype.Island)));

        return land;
    }

    /// <summary>
    /// Does Nimbus Maze's live controller control at least one permanent
    /// with <paramref name="subtype"/>? Reads <see cref="Card.Controller"/>
    /// on every call so control-change effects (Threaten, etc.) re-point
    /// the scan automatically (CR 109.5). Returns false when the
    /// controller is not yet assigned (defensive — Nimbus Maze is wired
    /// with SetController on construction, but a mid-zone-move sample
    /// could otherwise NRE).
    /// </summary>
    public static bool ControllerControlsSubtype(Land maze, CardSubtype subtype)
    {
        ArgumentNullException.ThrowIfNull(maze);
        var controller = maze.Controller ?? maze.Owner;
        if (controller is null) return false;

        foreach (var card in controller.Zones.Battlefield.GetCards())
        {
            if (card.HasSubtype(subtype)) return true;
        }
        return false;
    }
}
