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
/// Two shapes of land subtype change must be distinguished here:
///
///   * REPLACEMENT (CR 305.6) — a Layer 4 effect overwrites the land
///     subtype slot (Blood Moon, Spreading Seas, Conversion). The land
///     "loses any abilities printed on the card and gains the
///     appropriate mana ability for each new basic land type." Return
///     ONLY the synthesized basic mana abilities for the newly acquired
///     basic subtypes — the printed abilities are dropped.
///
///   * ADDITIVE (CR 305.7) — a Layer 4 effect grants a basic land
///     subtype IN ADDITION to existing subtypes (Urborg, Yavimaya). The
///     printed abilities stay; the land additionally gains the mana
///     ability for each newly acquired basic land subtype.
///
/// Detection: if every printed subtype of the land is still present in
/// the effective subtype set, the Layer 4 effect was additive — keep
/// printed AND add new. If any printed subtype has been removed, the
/// Layer 4 effect was a replacement — return new only.
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
        Player? controller = null) =>
        For(permanent, layers, controller, allPlayers: null);

    /// <summary>
    /// Layer-aware mana-ability list with optional Damping Sphere awareness.
    /// When <paramref name="allPlayers"/> is supplied and any of those
    /// players controls a Damping Sphere on the battlefield, each returned
    /// ability sourced from a <see cref="Land"/> is wrapped in a
    /// <see cref="DampingSphereCappedManaAbility"/> so any activation
    /// producing two or more mana resolves into a single {C} instead.
    /// Pass null (default) to skip the scan — preserves pre-Damping-Sphere
    /// behaviour for callers that don't have a game-graph handle.
    /// </summary>
    public static IReadOnlyList<IManaAbility> For(
        Permanent permanent,
        ContinuousEffectsService? layers,
        Player? controller,
        IEnumerable<Player>? allPlayers)
    {
        if (permanent == null) throw new ArgumentNullException(nameof(permanent));

        var baseAbilities = ComputeLayered(permanent, layers, controller);
        return DampingSphereCappedManaAbility.WrapIfPresent(baseAbilities, allPlayers);
    }

    private static IReadOnlyList<IManaAbility> ComputeLayered(
        Permanent permanent,
        ContinuousEffectsService? layers,
        Player? controller)
    {
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

        // CR 305.6 vs 305.7 — additive (Urborg/Yavimaya) keeps printed
        // abilities and adds new basic mana; replacement (Blood Moon /
        // Spreading Seas) drops printed and returns only new basic mana.
        // Detection: if every printed subtype is still in the effective
        // subtype set, the Layer 4 effect was additive. Otherwise a
        // printed subtype was dropped → replacement.
        var isAdditive = printedSubtypes.All(effective.Contains);

        // Synthesized on demand (not stored on the card); the ability's
        // controller is the land's current controller, or an explicitly
        // supplied override.
        var owner = controller ?? land.Controller
            ?? throw new InvalidOperationException(
                $"Cannot synthesize basic mana ability for {land.Name}: no controller set.");

        var synthesized = newlyAcquiredBasics
            .Select(st => (IManaAbility)BuildBasicMana(land, owner, st));

        if (isAdditive)
        {
            // CR 305.7 — printed abilities preserved, basic mana for the
            // newly granted subtype added on top.
            return permanent.Abilities.OfType<IManaAbility>()
                .Concat(synthesized)
                .ToList();
        }

        // CR 305.6 — printed abilities lost, basic mana gained per new
        // basic land subtype.
        return synthesized.ToList();
    }

    private static ManaAbility BuildBasicMana(Land source, Player controller, CardSubtype basic)
    {
        var color = BasicLandManaColors.Map[basic];
        return new ManaAbility(source, controller, ManaCost.Parse(color));
    }
}
