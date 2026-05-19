using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.Effects;

/// <summary>
/// CR 613 layer-system executor. Cards/combat consult this to get a
/// creature's CURRENT power/toughness/keywords after every continuous
/// effect has been applied in layer order.
///
/// MVP supports layers 6 (Abilities) and 7c (PT_Modify). Other layers
/// can register effects today; they'll apply in numeric order. Full
/// dependency-ordering (CR 613.8) is a Phase 16.x task — current
/// implementation uses simple Timestamp tie-break inside each layer.
/// </summary>
public sealed class ContinuousEffectsService
{
    private readonly List<ContinuousEffect> _effects = new();

    public void Register(ContinuousEffect effect)
    {
        if (effect == null) throw new ArgumentNullException(nameof(effect));
        _effects.Add(effect);
    }

    public void Unregister(ContinuousEffect effect) => _effects.Remove(effect);

    /// <summary>Drop any inactive (expired) effects.</summary>
    public void Prune()
    {
        _effects.RemoveAll(e => !e.IsActive());
    }

    /// <summary>
    /// Compute the current characteristics of a creature by applying all
    /// matching effects in layer order. Starts from printed values, runs
    /// effects, returns the final working set.
    /// </summary>
    public CreatureCharacteristics Compute(Creature creature)
    {
        var chars = new CreatureCharacteristics
        {
            Power = creature.BasePower,
            Toughness = creature.BaseToughness,
        };

        // Bake in keywords already attached as KeywordAbility markers
        // (printed evergreens like Flying on Air Elemental).
        foreach (var kw in creature.Abilities.OfType<KeywordAbility>())
        {
            chars.Keywords.Add(kw.Keyword);
        }
        // Seed printed subtypes; Layer 4 effects add/remove on top.
        foreach (var st in creature.Subtypes) chars.Subtypes.Add(st);

        var applicable = _effects
            .Where(e => e.IsActive() && e.AppliesTo(creature))
            .OrderBy(e => (int)e.Layer)
            .ThenBy(e => e.Timestamp);

        foreach (var effect in applicable)
        {
            effect.Apply(chars);
        }

        // Layer 7c — +1/+1 and -1/-1 counter P/T adjustment (CR 122.1g).
        // Applied after other 7c effects per CR 613.7 (counters last).
        if (creature is Majik.Core.Cards.Permanent perm)
        {
            var plus = perm.Counters.Count(Majik.Core.Counters.CounterType.PlusOnePlusOne);
            var minus = perm.Counters.Count(Majik.Core.Counters.CounterType.MinusOneMinusOne);
            chars.Power += plus - minus;
            chars.Toughness += plus - minus;
        }

        return chars;
    }

    /// <summary>Expire and drop all effects whose duration is "until end of turn".</summary>
    public void ExpireEndOfTurn()
    {
        _effects.RemoveAll(e => e.ExpiresAtEndOfTurn);
    }

    /// <summary>
    /// CR 613.2 — current controller of a permanent after applying any
    /// active Layer 2 control-change effects (latest-timestamp wins). Falls
    /// back to <see cref="Permanent.Controller"/> when no override is active.
    /// </summary>
    public Player EffectiveController(Permanent perm)
    {
        if (perm == null) throw new ArgumentNullException(nameof(perm));
        var swap = _effects.OfType<ControlChangeEffect>()
            .Where(e => e.IsActive() && ReferenceEquals(e.Target, perm))
            .OrderByDescending(e => e.Timestamp)
            .FirstOrDefault();
        return swap?.NewController ?? perm.Controller!;
    }
}
