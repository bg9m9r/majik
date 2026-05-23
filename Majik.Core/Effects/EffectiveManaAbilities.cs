using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.Effects;

/// <summary>
/// CR 305.6 / 305.7 — derives the effective mana abilities for a permanent
/// after the CR 613 layer system has run.
///
/// For lands whose effective (Layer 4) subtypes contain a basic-land
/// subtype NOT on the printed card, this returns one synthesized basic
/// mana ability per such acquired basic subtype, IN PLACE OF the
/// permanent's printed mana abilities — the land "loses any abilities
/// printed on the card and gains the appropriate mana ability for each
/// new basic land type." Otherwise returns the printed mana abilities
/// unchanged.
///
/// Non-land permanents and missing-layer-service callers fall through to
/// the printed-abilities path: the layer system here only reshapes the
/// land-mana set. Broader rewiring (Pithing Needle, Cursed Totem,
/// type-changing-to-creature land mana, etc.) is intentionally out of
/// scope.
/// </summary>
public static class EffectiveManaAbilities
{
    /// <summary>
    /// Effective mana abilities for <paramref name="permanent"/>. When
    /// <paramref name="layers"/> is null, returns the printed mana
    /// abilities (the override only fires when the layer system can
    /// actually compute new subtypes). When <paramref name="controller"/>
    /// is null, falls back to the permanent's current controller
    /// (CR 110.2) for synthesizing new basic mana abilities.
    /// </summary>
    public static IReadOnlyList<IManaAbility> For(
        Permanent permanent,
        ContinuousEffectsService? layers,
        Player? controller = null)
    {
        if (permanent == null) throw new ArgumentNullException(nameof(permanent));

        // No layer service available (e.g. agent has no path to it yet) —
        // null-fallback per the PR's scope. Behaviour matches the
        // pre-Blood-Moon enumeration: just the printed mana abilities.
        if (layers == null)
            return permanent.Abilities.OfType<IManaAbility>().ToList();

        // Only lands are subject to the CR 305.6 retyping override.
        // Non-land permanents keep their printed mana abilities until a
        // future PR widens scope (Pithing Needle, etc.).
        if (permanent is not Land land)
            return permanent.Abilities.OfType<IManaAbility>().ToList();

        var printedSubtypes = land.Subtypes.ToHashSet();
        var effective = layers.Compute(permanent).Subtypes;

        // Effective basic-land subtypes the printed card didn't have →
        // they came from a Layer 4 retyping effect (Blood Moon, Spreading
        // Seas, etc.). CR 305.6 says the printed abilities are lost and
        // basic mana abilities are gained instead.
        var newlyAcquiredBasics = effective
            .Where(BasicLandManaColors.IsBasicLandSubtype)
            .Where(st => !printedSubtypes.Contains(st))
            .ToList();

        if (newlyAcquiredBasics.Count == 0)
            return permanent.Abilities.OfType<IManaAbility>().ToList();

        // CR 305.6 — printed abilities lost, basic mana gained per new
        // basic land subtype. Synthesized on demand (not stored on the
        // card); the ability's controller is the land's current
        // controller, or an explicitly supplied override.
        var owner = controller ?? land.Controller
            ?? throw new InvalidOperationException(
                $"Cannot synthesize basic mana ability for {land.Name}: no controller set.");

        return newlyAcquiredBasics
            .Select(st => (IManaAbility)BuildBasicMana(land, owner, st))
            .ToList();
    }

    private static ManaAbility BuildBasicMana(Land source, Player controller, CardSubtype basic)
    {
        var color = BasicLandManaColors.Map[basic];
        return new ManaAbility(source, controller, ManaCost.Parse(color));
    }
}
