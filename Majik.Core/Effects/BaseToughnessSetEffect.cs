using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.Effects;

/// <summary>
/// CR 613.7b — Layer 7b set-base-TOUGHNESS effect scoped to a dynamic group
/// of creatures. "Creatures your opponents control have base toughness 1."
/// (Maha, Its Feathers Night.)
///
/// <para>This is the toughness-only sibling of <see cref="BecomesPTEffect"/>
/// (which sets BOTH base power and base toughness). Maha leaves base power
/// untouched and overwrites only base toughness, so this effect mutates only
/// <see cref="CreatureCharacteristics.Toughness"/> in Layer 7b. Layer 7c
/// pump / counters then pile on top per CR 613.7 (e.g. a +1/+1 counter still
/// raises an affected creature to toughness 2).</para>
///
/// <para>Unlike <see cref="BecomesPTEffect"/> — which targets one fixed
/// permanent via a snapshot reference — this is a dynamic-group static
/// (CR 613.7c scope) anchored to a source permanent, exactly like
/// <see cref="LordStaticEffect"/>: membership is recomputed on every
/// <see cref="ContinuousEffectsService.Compute(Permanent)"/> via
/// <see cref="AppliesTo"/>, so a creature an opponent plays AFTER the source
/// is on the battlefield is scoped automatically (no per-creature
/// registration / CardMovedEvent snapshot needed). When <c>opponentsOnly</c>
/// is set, the controller filter reads <c>Source.Controller</c> live, so a
/// stolen source debuffs its NEW controller's opponents (CR 109.5).</para>
///
/// <para>Active only while the source is on the battlefield
/// (<see cref="IsActive"/>); when the source leaves, the service stops
/// applying it and affected creatures revert to their printed base toughness.</para>
/// </summary>
public sealed class BaseToughnessSetEffect : ContinuousEffect
{
    private readonly Permanent _source;
    private readonly int _baseToughness;
    private readonly bool _opponentsOnly;

    /// <summary>
    /// Construct the dynamic-group base-toughness set.
    /// </summary>
    /// <param name="source">The permanent generating the static (Maha). The
    /// effect is active while this is on the battlefield; its
    /// <see cref="Permanent.Controller"/> is read live to resolve the
    /// opponents scope (CR 109.5).</param>
    /// <param name="baseToughness">The base toughness every affected creature
    /// is set to (Maha: 1).</param>
    /// <param name="opponentsOnly">When true (Maha's "Creatures your opponents
    /// control"), the effect applies only to creatures controlled by a player
    /// OTHER than the source's controller — the source itself is always
    /// excluded. When false the effect applies to ALL creatures on the
    /// battlefield.</param>
    public BaseToughnessSetEffect(Permanent source, int baseToughness, bool opponentsOnly = true)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _baseToughness = baseToughness;
        _opponentsOnly = opponentsOnly;
    }

    // CR 613.7b — set base P/T layer (toughness only).
    public override Layer Layer => Layer.PT_SetBase;

    /// <summary>CR 613.1g — the permanent generating this static effect.</summary>
    public override Permanent? Source => _source;

    public override bool IsActive() =>
        _source.Zone == Majik.Core.Zones.ZoneType.Battlefield;

    public override bool AppliesTo(Creature creature)
    {
        if (creature.Zone != Majik.Core.Zones.ZoneType.Battlefield) return false;
        if (!_opponentsOnly) return true;

        // CR 109.5 — "creatures your opponents control" excludes everything the
        // source's controller controls (including the source itself). Read the
        // controller live so a stolen Maha debuffs its current controller's
        // opponents.
        return !ReferenceEquals(creature.Controller, _source.Controller);
    }

    public override void Apply(CreatureCharacteristics chars)
    {
        // CR 613.7b — set ONLY base toughness; base power is left as printed.
        chars.Toughness = _baseToughness;
    }

    /// <summary>
    /// Sim-only: reconstruct an identical <see cref="BaseToughnessSetEffect"/>
    /// bound to <paramref name="clonedSource"/> for the search-sandbox clone.
    /// preserves: _baseToughness, _opponentsOnly; source → clonedSource. The
    /// opponents scope derives from <c>Source.Controller</c> (correctly wired on
    /// the cloned permanent), so the <paramref name="clonedPlayers"/> resolver is
    /// accepted but unused — same posture as <see cref="LordStaticEffect"/>.
    /// </summary>
    internal override ContinuousEffect? CloneForSim(
        Permanent clonedSource,
        System.Func<System.Collections.Generic.IReadOnlyList<Player>>? clonedPlayers)
        => new BaseToughnessSetEffect(clonedSource, _baseToughness, _opponentsOnly);
}
