using Majik.Core.Cards;
using Majik.Core.ValueObjects;

namespace Majik.Core.Effects;

/// <summary>
/// CR 613.1e — Layer 5 colour-changing effect that <em>sets</em> the
/// effective colour(s) of every permanent matched by a predicate,
/// replacing whatever colours the lower layers produced. This is the
/// "becomes [colour]" / "is all colours" shape.
///
/// <para>Examples: Leyline of the Guildpact — "Each nonland permanent you
/// control is all colors." Painter's Servant — "[chosen colour]". The
/// SET semantics (vs <see cref="AddColorsEffect"/>'s ADD) match the most
/// common wording: a permanent that "is white" is <em>only</em> white,
/// not white-plus-its-printed-colours.</para>
///
/// Only the five real colours are stored; <see cref="ManaColor.Generic"/>
/// and <see cref="ManaColor.Colorless"/> are filtered out. An empty
/// colour set means the permanent becomes colourless.
/// </summary>
public sealed class SetColorsEffect : ContinuousEffect
{
    private static readonly IReadOnlyList<ManaColor> AllFiveColors = new[]
    {
        ManaColor.White, ManaColor.Blue, ManaColor.Black,
        ManaColor.Red, ManaColor.Green,
    };

    private readonly Permanent _source;
    private readonly Func<Permanent, bool> _scope;
    private readonly IReadOnlyList<ManaColor> _colors;

    public SetColorsEffect(
        Permanent source,
        Func<Permanent, bool> scope,
        IEnumerable<ManaColor> colors)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        if (colors == null) throw new ArgumentNullException(nameof(colors));
        _colors = colors
            .Where(c => c != ManaColor.Generic && c != ManaColor.Colorless)
            .Distinct()
            .ToList();
    }

    /// <summary>CR 105.2c — "is all colors" convenience: SET to W/U/B/R/G.</summary>
    public static SetColorsEffect AllColors(Permanent source, Func<Permanent, bool> scope) =>
        new(source, scope, AllFiveColors);

    public override Layer Layer => Layer.Color;

    public override Permanent? Source => _source;

    public override bool IsActive() =>
        _source.Zone == Majik.Core.Zones.ZoneType.Battlefield;

    public override bool AppliesTo(Creature creature) => AppliesTo((Permanent)creature);

    public override bool AppliesTo(Permanent permanent) =>
        permanent.Zone == Majik.Core.Zones.ZoneType.Battlefield && _scope(permanent);

    public override void Apply(CreatureCharacteristics chars) =>
        Apply((PermanentCharacteristics)chars);

    public override void Apply(PermanentCharacteristics chars)
    {
        // CR 613.1e — SET replaces the colour set produced by lower layers.
        chars.Colors.Clear();
        foreach (var c in _colors) chars.Colors.Add(c);
    }
}

/// <summary>
/// CR 613.1e — Layer 5 colour-changing effect that <em>adds</em> colour(s)
/// to every permanent matched by a predicate, unioning onto whatever
/// colours the lower layers (and any earlier-timestamp Layer-5 effects)
/// produced. This is the "is [colour] in addition to its other colors"
/// shape.
///
/// <para>Examples: many "becomes ... in addition to its other colors"
/// effects; the additive half of multi-clause colour rewrites. Differs
/// from <see cref="SetColorsEffect"/> only in that it preserves existing
/// colours instead of clearing them first.</para>
/// </summary>
public sealed class AddColorsEffect : ContinuousEffect
{
    private readonly Permanent _source;
    private readonly Func<Permanent, bool> _scope;
    private readonly IReadOnlyList<ManaColor> _colors;

    public AddColorsEffect(
        Permanent source,
        Func<Permanent, bool> scope,
        IEnumerable<ManaColor> colors)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        if (colors == null) throw new ArgumentNullException(nameof(colors));
        _colors = colors
            .Where(c => c != ManaColor.Generic && c != ManaColor.Colorless)
            .Distinct()
            .ToList();
    }

    public override Layer Layer => Layer.Color;

    public override Permanent? Source => _source;

    public override bool IsActive() =>
        _source.Zone == Majik.Core.Zones.ZoneType.Battlefield;

    public override bool AppliesTo(Creature creature) => AppliesTo((Permanent)creature);

    public override bool AppliesTo(Permanent permanent) =>
        permanent.Zone == Majik.Core.Zones.ZoneType.Battlefield && _scope(permanent);

    public override void Apply(CreatureCharacteristics chars) =>
        Apply((PermanentCharacteristics)chars);

    public override void Apply(PermanentCharacteristics chars)
    {
        // CR 613.1e — ADD unions onto the existing colour set.
        foreach (var c in _colors) chars.Colors.Add(c);
    }
}
