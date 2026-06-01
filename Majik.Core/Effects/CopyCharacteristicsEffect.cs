using Majik.Core.Abilities;
using Majik.Core.Cards;

namespace Majik.Core.Effects;

/// <summary>
/// CR 707.2 / 613.2 (Layer 1) — generalized "becomes a copy of" continuous
/// effect. The target permanent takes on the full copiable characteristics
/// (CR 707.2: name, mana cost, color indicator, card types, subtypes,
/// supertypes, rules text / abilities, and P/T) of an arbitrary source
/// permanent CARD — creature, artifact, enchantment, or land — applied in
/// place. Unlike <see cref="CopyEffect"/> (creature-only, additive P/T +
/// keywords MVP), this effect REPLACES the target's type line and
/// characteristics, matching the rule that copiable values overwrite rather
/// than add.
///
/// Usage shapes:
/// <list type="bullet">
///   <item><b>In-place "becomes a copy until end of turn"</b> (Shifting
///   Woodland's "{2}{G}{G}: this land becomes a copy of target permanent
///   card in your graveyard until end of turn"). Pass
///   <c>expiresAtEndOfTurn: true</c>; the effect is dropped at the cleanup
///   step (CR 514.2) by
///   <see cref="ContinuousEffectsService.ExpireEndOfTurn"/>.</item>
///   <item><b>Permanent Clone-style copy</b> — pass
///   <c>expiresAtEndOfTurn: false</c> (the default) for a copy that lasts as
///   long as the target is on the battlefield.</item>
/// </list>
///
/// ## Type-line replacement (CR 707.2)
/// Layer 1 seeds the working-set from the TARGET's printed values; this
/// effect clears <see cref="PermanentCharacteristics.Types"/> /
/// <see cref="PermanentCharacteristics.Subtypes"/> /
/// <see cref="PermanentCharacteristics.Keywords"/> and re-seeds them from the
/// SOURCE. So a Land copying an Artifact stops being a Land and becomes an
/// Artifact (contrast Creeping Tar Pit's "still a land" ADD effect). Later
/// layers (type-changing, P/T modify, counters) apply on top per CR 613.
///
/// ## P/T surfacing (known manland-on-a-Land gap)
/// When the target is itself a <see cref="Creature"/> instance,
/// <see cref="ContinuousEffectsService.Compute(Permanent)"/> seeds a
/// <see cref="CreatureCharacteristics"/> and this effect writes the copied
/// P/T into it (surfaces through <see cref="Creature.GetPower"/>). When the
/// target is a non-creature runtime instance (e.g. a <see cref="Land"/>),
/// Compute seeds a plain <see cref="PermanentCharacteristics"/> with no P/T
/// fields — identical to <see cref="CreepingTarPitBecomesPTEffect"/>. The
/// copied P/T is still recorded on <see cref="CopiedPower"/> /
/// <see cref="CopiedToughness"/> for inspection until Compute can upgrade a
/// non-creature row to a creature row once Layer 1/4 grants Creature type.
///
/// ## Supertypes + colour (now surfaced)
/// CR 707.2 — supertypes and colour are copiable. This effect re-seeds the
/// target's <see cref="PermanentCharacteristics.Supertypes"/> (#1715 slot) and
/// <see cref="PermanentCharacteristics.Colors"/> (#1681 Layer-5 slot) from the
/// source's printed values, so a clone of a Legendary permanent copies
/// Legendary (the legend-rule SBA reads <see cref="Permanent.HasEffectiveSupertype"/>)
/// and a clone of a colored permanent copies its colour (read back via
/// <see cref="Permanent.GetEffectiveColors"/>).
///
/// ## v1 lossy
/// - <b>Name / mana cost</b> are copiable (CR 707.2) but
///   <see cref="Card.Name"/> / <see cref="Card.ManaCost"/> are immutable on
///   the runtime instance; the copied identity is exposed via
///   <see cref="CopiedName"/> / <see cref="CopiedManaCost"/> rather than
///   mutating the target card.
/// - <b>Non-keyword abilities</b> — only <see cref="KeywordAbility"/>
///   markers are mirrored into the keyword set; arbitrary printed activated /
///   triggered abilities of the source are not re-instantiated on the target
///   (same boundary as <see cref="CopyEffect"/>).
/// </summary>
public sealed class CopyCharacteristicsEffect : ContinuousEffect
{
    private readonly Permanent _target;
    private readonly Permanent _source;
    private readonly bool _expiresAtEndOfTurn;

    /// <summary>
    /// Construct a copy effect.
    /// </summary>
    /// <param name="target">The permanent that becomes a copy (modified in
    /// place).</param>
    /// <param name="source">The permanent card whose copiable
    /// characteristics are copied.</param>
    /// <param name="expiresAtEndOfTurn">When true, the effect is dropped at
    /// the cleanup step (CR 514.2). Defaults to false (lasts while the
    /// target is on the battlefield, Clone-style).</param>
    public CopyCharacteristicsEffect(Permanent target, Permanent source, bool expiresAtEndOfTurn = false)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _expiresAtEndOfTurn = expiresAtEndOfTurn;

        CopiedName = source.Name;
        CopiedManaCost = source.ManaCost;
        CopiedPower = source is Creature c1 ? c1.BasePower : 0;
        CopiedToughness = source is Creature c2 ? c2.BaseToughness : 0;
    }

    /// <summary>CR 707.2 — copied name (target Name is immutable in v1).</summary>
    public string CopiedName { get; }

    /// <summary>CR 707.2 — copied mana cost string.</summary>
    public string CopiedManaCost { get; }

    /// <summary>CR 707.2 — copied base power (0 when the source isn't a creature).</summary>
    public int CopiedPower { get; }

    /// <summary>CR 707.2 — copied base toughness (0 when the source isn't a creature).</summary>
    public int CopiedToughness { get; }

    /// <summary>The permanent being turned into a copy.</summary>
    public Permanent Target => _target;

    /// <summary>The permanent whose characteristics are copied.</summary>
    public Permanent CopySource => _source;

    // CR 613.1g source-suppression — for a copy effect the "source generating
    // the effect" is the copying permanent itself (the target), so Layer-6
    // strip suppression keys on the target.
    public override Permanent? Source => _target;

    public override Layer Layer => Layer.Copy;

    public override bool ExpiresAtEndOfTurn => _expiresAtEndOfTurn;

    public override bool IsActive() =>
        _target.Zone == Majik.Core.Zones.ZoneType.Battlefield;

    public override bool AppliesTo(Creature creature) => ReferenceEquals(creature, _target);

    public override bool AppliesTo(Permanent permanent) => ReferenceEquals(permanent, _target);

    public override void Apply(CreatureCharacteristics chars)
    {
        ApplyShared(chars);
        // CR 707.2 — copy the source's P/T when it has one. A Land copying a
        // creature surfaces P/T here only when Compute seeded a creature row
        // (target is a Creature instance); otherwise see Apply(Permanent).
        chars.Power = CopiedPower;
        chars.Toughness = CopiedToughness;
    }

    public override void Apply(PermanentCharacteristics chars)
    {
        if (chars is CreatureCharacteristics cc)
        {
            Apply(cc);
            return;
        }
        ApplyShared(chars);
    }

    /// <summary>
    /// CR 707.2 — replace the target's copiable type line + supertypes +
    /// colour + keyword set with the source's. Clears the seeded (target's
    /// printed) values first so the copy overwrites rather than unions.
    /// </summary>
    private void ApplyShared(PermanentCharacteristics chars)
    {
        chars.Types.Clear();
        foreach (var t in _source.CardTypes) chars.Types.Add(t);

        chars.Subtypes.Clear();
        foreach (var st in _source.Subtypes) chars.Subtypes.Add(st);

        // CR 707.2 / 205.4 — supertypes are copiable. Re-seed from the source's
        // printed supertypes (#1715 slot) so a clone of a Legendary permanent
        // copies Legendary; the legend-rule SBA reads HasEffectiveSupertype,
        // which consults this set via Compute.
        chars.Supertypes.Clear();
        foreach (var sup in _source.Supertypes) chars.Supertypes.Add(sup);

        // CR 707.2 / 105.3 — colour is copiable. Re-seed the Layer-5 colour
        // slot (#1681) from the source's printed/static colour so a clone of a
        // colored permanent copies its colour (read back via
        // Permanent.GetEffectiveColors). Later-timestamp Layer-5 SET/ADD colour
        // effects still apply on top per CR 613.
        chars.Colors.Clear();
        foreach (var c in Majik.Core.Cards.CardColors.GetColors(_source))
        {
            chars.Colors.Add(c);
        }

        chars.Keywords.Clear();
        foreach (var kw in _source.Abilities.OfType<KeywordAbility>())
        {
            chars.Keywords.Add(kw.Keyword);
        }
    }
}
