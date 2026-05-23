using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.Effects;

/// <summary>
/// Wrapper applied by <see cref="EffectiveManaAbilities.For(Permanent,
/// ContinuousEffectsService?, Player?, IEnumerable{Player}?)"/> when a
/// Damping Sphere is on the battlefield (under any controller).
///
/// Damping Sphere (Dominaria — Artifact {2}) — "If a land is tapped for
/// two or more mana, it produces {C} instead of any other type and
/// amount." The cap is applied at activation time: the inner ability is
/// invoked normally (so the source still taps and any additional costs
/// fire), then the returned mana is replaced with a single {C} whenever
/// it would have totalled two or more.
///
/// Scope: only mana abilities sourced from a <see cref="Land"/> are
/// affected — the printed text is "If a land is tapped for two or more
/// mana", so non-land mana sources (Mox, Lotus Petal, Sol Ring, etc.)
/// pass through unchanged. Mana abilities that produce {1} or less pass
/// through unchanged regardless of source.
///
/// Symmetric — Damping Sphere caps everyone's lands, including its own
/// controller's. The wrapper is rebuilt every time <see cref="EffectiveManaAbilities.For"/>
/// runs, so when Damping Sphere leaves play the next mana-ability lookup
/// returns the unwrapped abilities.
/// </summary>
public sealed class DampingSphereCappedManaAbility : IManaAbility
{
    public const string CardName = "Damping Sphere";

    private readonly IManaAbility _inner;

    public DampingSphereCappedManaAbility(IManaAbility inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public object Source => _inner.Source;
    public Player Controller => _inner.Controller;
    public ManaCost ManaGenerated => Cap(_inner.ManaGenerated);
    public bool CanActivate() => _inner.CanActivate();

    public ManaCost Activate()
    {
        var produced = _inner.Activate();
        return Cap(produced);
    }

    private static ManaCost Cap(ManaCost produced)
    {
        if (produced == null) return ManaCost.Zero;
        return produced.TotalValue >= 2 ? ManaCost.Parse("C") : produced;
    }

    /// <summary>
    /// Wrap each ability in <paramref name="abilities"/> when any player in
    /// <paramref name="allPlayers"/> controls a Damping Sphere on the
    /// battlefield AND the ability's source is a <see cref="Land"/>. Pass
    /// null <paramref name="allPlayers"/> to skip the scan.
    /// </summary>
    public static IReadOnlyList<IManaAbility> WrapIfPresent(
        IReadOnlyList<IManaAbility> abilities,
        IEnumerable<Player>? allPlayers)
    {
        if (abilities == null) throw new ArgumentNullException(nameof(abilities));
        if (allPlayers == null || abilities.Count == 0) return abilities;

        if (!AnyDampingSphereOnBattlefield(allPlayers)) return abilities;

        var wrapped = new List<IManaAbility>(abilities.Count);
        foreach (var a in abilities)
        {
            wrapped.Add(a.Source is Land ? new DampingSphereCappedManaAbility(a) : a);
        }
        return wrapped;
    }

    private static bool AnyDampingSphereOnBattlefield(IEnumerable<Player> players)
    {
        foreach (var p in players)
        {
            if (p == null) continue;
            foreach (var c in p.Zones.Battlefield.GetCards())
            {
                if (string.Equals(c.Name, CardName, StringComparison.Ordinal))
                    return true;
            }
        }
        return false;
    }
}
