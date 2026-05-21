using Majik.Core.Abilities;
using Majik.Core.Players;

namespace Majik.Core.Cards;

/// <summary>
/// CR 114 — an emblem has no characteristics (no name, mana cost, types, or
/// power/toughness) other than the abilities granted at creation. Emblems live
/// in the command zone for the rest of the game.
///
/// Emblems are created by planeswalker ultimate abilities (and a handful of
/// other effects). They are not cards and have no card types; they exist solely
/// as a persistent ability container in the command zone.
///
/// For trigger-manager integration, any <see cref="ITriggeredAbility"/> in
/// <see cref="Abilities"/> should be registered via
/// <c>TriggerManager.RegisterTriggeredAbility()</c> at emblem-creation time.
/// The emblem itself acts as the <c>Source</c> of those abilities, and because
/// it never moves zones the zone-based auto-unregister path in TriggerManager
/// does not apply — callers must manage unregistration explicitly if needed
/// (in practice, "never" — emblems last for the rest of the game).
/// </summary>
public sealed class Emblem
{
    /// <summary>Stable identifier for this emblem instance.</summary>
    public Guid Id { get; }

    /// <summary>The player who controls this emblem (the player whose
    /// planeswalker created it).</summary>
    public Player Controller { get; }

    /// <summary>Human-readable description of the source, e.g.
    /// "Grist, the Hunger Tide emblem". Not a game characteristic.</summary>
    public string SourceName { get; }

    /// <summary>The abilities this emblem grants. Typically one triggered or
    /// static ability copied verbatim from the planeswalker's oracle text.</summary>
    public IReadOnlyList<IAbility> Abilities { get; }

    public Emblem(Player controller, string sourceName, IEnumerable<IAbility> abilities)
    {
        Controller = controller ?? throw new ArgumentNullException(nameof(controller));
        SourceName = sourceName ?? string.Empty;
        Abilities = abilities?.ToArray() ?? Array.Empty<IAbility>();
        Id = Guid.NewGuid();
    }

    public override string ToString() => $"Emblem — {SourceName} (ctrl: {Controller.Name})";
}
