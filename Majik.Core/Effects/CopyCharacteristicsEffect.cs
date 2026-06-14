using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;

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
/// ## Name / mana cost (now surfaced through the layer system)
/// CR 707.2 — name and mana cost are copiable. <see cref="Card.Name"/> /
/// <see cref="Card.ManaCost"/> stay immutable on the runtime instance, but this
/// effect overwrites the Layer-1 effective name / mana-cost slots
/// (<see cref="PermanentCharacteristics.Name"/> /
/// <see cref="PermanentCharacteristics.ManaCost"/>), so a clone of a permanent
/// named X reads back as named X via <see cref="Permanent.GetEffectiveName"/>
/// (same-name matching — Izzet Staticaster, "another permanent named X") and
/// reports the copied mana cost via <see cref="Permanent.GetEffectiveManaCost"/>
/// (mana-value reads). <see cref="CopiedName"/> / <see cref="CopiedManaCost"/>
/// remain for direct inspection.
///
/// ## Arbitrary printed activated / triggered abilities (CR 707.2)
/// A copy gets ALL the source's copiable abilities, not only its keyword
/// markers. The keyword markers are mirrored into the characteristic keyword
/// set by this effect's layer pass (above). The source's printed NON-keyword
/// activated / triggered abilities (which carry costs / targets / closures
/// that capture the source permanent) cannot be re-pointed in place — their
/// closures reference the original source. They are instead RE-INSTANTIATED
/// bound to the target via a caller-supplied <c>abilityRebind</c> delegate and
/// granted onto the copy through the existing <see cref="GrantAbilityEffect"/>
/// primitive by <see cref="RegisterCopy"/>. The grant shares the copy's
/// lifetime: it follows the copy onto the battlefield, is revoked when the copy
/// leaves play (CR 613.6e), and (for an until-EOT copy) is dropped at the
/// cleanup step (CR 514.2). Granted triggered abilities auto-register with the
/// <see cref="Abilities.TriggerManager"/> the moment they land on the copy's
/// <see cref="Card.Abilities"/> list (its <c>SyncCardRegistration</c> re-scans
/// the bearer). A <c>null</c> rebind reproduces the legacy keyword-only posture.
///
/// ## v1 lossy
/// - <b>Ability rebind is caller-supplied</b> — there is no generic
///   closure-introspection that re-points an arbitrary printed ability's
///   captured source. The clone seam (<see cref="EntersAsCopyReplacement"/>) /
///   the bespoke factory provides the rebind, which re-creates the source's
///   activated / triggered abilities bound to the target. A source whose
///   <see cref="RebindablePrintedAbilities"/> rebuild requires data not carried
///   on the runtime instance (e.g. an oracle-bound closure) stays
///   keyword-only — but the common case (an ability that only references its
///   own source / controller) rebinds cleanly.
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
        // CR 712.4 — when the source is a transform DFC currently flipped to
        // its (non-creature) back face, its COPIABLE values come from the
        // currently-up back face, not the printed front. A creature-front DFC
        // flipped to a planeswalker back (Ral, Monsoon Mage // Ral, Leyline
        // Prodigy) is built as a Creature C# instance carrying a transient
        // loyalty body, so reading _source.CardTypes (the printed front) would
        // wrongly make the clone a creature. Prefer the back-face seed so the
        // clone becomes a copy of the planeswalker back (CR 707.2 + 712.4).
        var backFace = EffectiveBackFace(_source);

        // CR 707.2 / 613.2 (Layer 1) — name + mana cost are copiable values.
        // The runtime Card.Name / Card.ManaCost stay immutable; overwrite the
        // layer-system effective slots so GetEffectiveName / GetEffectiveManaCost
        // (same-name matching, mana-value reads) report the copied identity.
        chars.Name = backFace?.Name ?? _source.Name;
        chars.ManaCost = _source.ManaCost;  // back faces have no mana cost (CR 711.4)

        chars.Types.Clear();
        foreach (var t in backFace?.Types ?? (IEnumerable<CardType>)_source.CardTypes)
            chars.Types.Add(t);

        chars.Subtypes.Clear();
        foreach (var st in backFace?.Subtypes ?? (IEnumerable<CardSubtype>)_source.Subtypes)
            chars.Subtypes.Add(st);

        // CR 707.2 / 205.4 — supertypes are copiable. Re-seed from the source's
        // printed supertypes (#1715 slot) so a clone of a Legendary permanent
        // copies Legendary; the legend-rule SBA reads HasEffectiveSupertype,
        // which consults this set via Compute.
        chars.Supertypes.Clear();
        foreach (var sup in backFace?.Supertypes ?? (IEnumerable<CardSupertype>)_source.Supertypes)
            chars.Supertypes.Add(sup);

        // CR 707.2 / 105.3 — colour is copiable. Re-seed the Layer-5 colour
        // slot (#1681) from the source's printed/static colour so a clone of a
        // colored permanent copies its colour (read back via
        // Permanent.GetEffectiveColors). Later-timestamp Layer-5 SET/ADD colour
        // effects still apply on top per CR 613.
        chars.Colors.Clear();
        var sourceColors = backFace is { Colors.Count: > 0 }
            ? (IEnumerable<Majik.Core.ValueObjects.ManaColor>)backFace.Colors
            : Majik.Core.Cards.CardColors.GetColors(_source);
        foreach (var c in sourceColors)
        {
            chars.Colors.Add(c);
        }

        chars.Keywords.Clear();
        if (backFace != null)
        {
            foreach (var kw in backFace.Keywords) chars.Keywords.Add(kw);
        }
        else
        {
            foreach (var kw in _source.Abilities.OfType<KeywordAbility>())
            {
                chars.Keywords.Add(kw.Keyword);
            }
        }
    }

    /// <summary>
    /// CR 712.4 — the source's currently-up BACK-face characteristics when it
    /// is a transform DFC flipped to a back face that re-classes it to an
    /// effective planeswalker (a creature-front DFC built as a Creature instance
    /// carrying a transient loyalty body). Returns <c>null</c> for an unflipped
    /// permanent, a real <see cref="Planeswalker"/> (its copiable PW values come
    /// from the printed face), or a back face that grants no loyalty body — so
    /// the copy falls back to the printed-characteristic path in every other
    /// case (byte-for-byte unchanged for the existing artifact / creature / land
    /// clones).
    /// </summary>
    internal static Majik.Core.CardData.MDFCs.BackFaceCharacteristics? EffectiveBackFace(
        Permanent source)
    {
        if (source is Planeswalker) return null;
        if (source.MdfcState is not { IsBackFace: true } state) return null;
        var back = state.BackFaceCharacteristics;
        return back is { Loyalty: not null } ? back : null;
    }

    /// <summary>
    /// CR 707.2 — register a "becomes a copy of <paramref name="source"/>"
    /// effect on <paramref name="effects"/> that copies the FULL copiable
    /// characteristics PLUS the source's printed activated / triggered
    /// abilities (re-instantiated bound to <paramref name="target"/>).
    ///
    /// Builds on the existing primitives: the characteristic copy is the
    /// ordinary <see cref="CopyCharacteristicsEffect"/>; each rebuilt non-keyword
    /// ability is granted onto the target through a companion
    /// <see cref="GrantAbilityEffect"/> (source = target), so the grant follows
    /// the copy's lifetime, revokes when the copy leaves play (CR 613.6e), and
    /// — when <paramref name="expiresAtEndOfTurn"/> is set — is dropped at the
    /// cleanup step alongside the copy (CR 514.2). Triggered abilities auto-bind
    /// to the <see cref="Abilities.TriggerManager"/> the moment they land on the
    /// copy's <see cref="Card.Abilities"/> list.
    /// </summary>
    /// <param name="effects">The continuous-effects service to register on.</param>
    /// <param name="target">The permanent that becomes a copy (modified in place).</param>
    /// <param name="source">The permanent whose copiable characteristics +
    /// printed abilities are copied.</param>
    /// <param name="abilityRebind">
    /// Re-creates the source's printed NON-keyword activated / triggered
    /// abilities bound to the target (CR 707.2). Receives <c>(source, target)</c>
    /// and returns target-bound <see cref="IAbility"/> instances. Null ⇒
    /// keyword-only copy (legacy posture). The default rebind for the common
    /// "ability references only its own source" case is
    /// <see cref="RebindablePrintedAbilities"/> — but it cannot rebuild abilities
    /// whose closures capture the source (those are caller-rebuilt or stay
    /// keyword-only).
    /// </param>
    /// <param name="expiresAtEndOfTurn">When true, both the copy and the
    /// mirrored abilities drop at the cleanup step (CR 514.2).</param>
    /// <returns>The registered <see cref="CopyCharacteristicsEffect"/>.</returns>
    public static CopyCharacteristicsEffect RegisterCopy(
        ContinuousEffectsService effects,
        Permanent target,
        Permanent source,
        Func<Permanent, Permanent, IEnumerable<IAbility>>? abilityRebind = null,
        bool expiresAtEndOfTurn = false)
    {
        ArgumentNullException.ThrowIfNull(effects);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);

        var copy = new CopyCharacteristicsEffect(target, source, expiresAtEndOfTurn);
        effects.Register(copy);

        // CR 712.4 / 711 / 306.5b — copy-of-effective-planeswalker. When the
        // source is a transform DFC flipped to its planeswalker back (an
        // effective planeswalker that is NOT a real Planeswalker instance), the
        // characteristic copy above already overwrites the copy's type line with
        // the back face (Planeswalker type, via EffectiveBackFace). The copy must
        // ALSO gain a working loyalty BODY + the back face's loyalty ABILITIES,
        // reproduced on the copy's own Option-B transient surface rather than by
        // re-instancing the copy as a Planeswalker (the rejected re-classing
        // approach). A real Planeswalker source is excluded by EffectiveBackFace
        // (its loyalty lives on its own field; that copy path is separate).
        var backFace = EffectiveBackFace(source);
        if (backFace is { Loyalty: { } startingLoyalty } && target is not Planeswalker)
        {
            // CR 306.5b — seed the copy's transient loyalty body from the back
            // face's starting loyalty (inert on a real Planeswalker target,
            // which keeps its own authoritative field).
            target.SetTransientLoyalty(startingLoyalty);

            // CR 707.2 / 606 — mirror the back face's loyalty abilities onto the
            // copy, bound to ITS Permanent-typed loyalty surface (4A) via the
            // same OracleLoyaltyAbilityBinder the transform path uses, so "[+1]"
            // raises the COPY's loyalty. Granted through GrantAbilityEffect so
            // the grant shares the copy's lifetime (revoked on LTB / EOT).
            var controller = target.Controller ?? target.Owner;
            if (controller != null && !string.IsNullOrWhiteSpace(backFace.OracleText))
            {
                foreach (var loyaltyAbility in
                    Majik.Core.CardData.OracleLoyaltyAbilityBinder.RebindOracleText(
                        target, backFace.OracleText!, controller))
                {
                    effects.Register(new GrantAbilityEffect(
                        source: target,
                        target: target,
                        ability: loyaltyAbility,
                        expiresAtEndOfTurn: expiresAtEndOfTurn));
                }
            }
        }

        if (abilityRebind != null)
        {
            foreach (var ability in abilityRebind(source, target))
            {
                if (ability == null) continue;
                // CR 613.1f Layer-6 grant; source = target so the grant lives
                // exactly as long as the copy does (and shares its EOT lifetime).
                effects.Register(new GrantAbilityEffect(
                    source: target,
                    target: target,
                    ability: ability,
                    expiresAtEndOfTurn: expiresAtEndOfTurn));
            }
        }

        return copy;
    }

    /// <summary>
    /// CR 707.2 — the source's printed NON-keyword activated + triggered
    /// abilities (keyword markers are handled by the layer pass). Surfaced for
    /// inspection and as the input a caller's rebind delegate maps over. NOTE:
    /// these instances are still bound to <paramref name="source"/> — a rebind
    /// delegate must re-create equivalents bound to the copy target before they
    /// are granted (their costs / closures capture the original source).
    /// </summary>
    public static IReadOnlyList<IAbility> RebindablePrintedAbilities(Permanent source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.Abilities
            .Where(a => a is ActivatedAbility or TriggeredAbility && a is not ManaAbility)
            .ToList();
    }

    /// <summary>
    /// CR 707.2 — the default ability-rebind for the copy machinery: re-creates
    /// the source's printed non-keyword activated / triggered abilities
    /// (<see cref="RebindablePrintedAbilities"/>) bound to the copy target via
    /// <see cref="ActivatedAbility.RebindTo"/> / <see cref="TriggeredAbility.RebindTo"/>.
    /// Correct for the common "ability references only its own source /
    /// controller" case (the boundary is documented on the RebindTo methods).
    /// Pass this as the <c>abilityRebind</c> argument of <see cref="RegisterCopy"/>.
    /// </summary>
    public static IEnumerable<IAbility> DefaultAbilityRebind(Permanent source, Permanent target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        var controller = target.Controller ?? target.Owner
            ?? throw new InvalidOperationException("Copy target has no controller/owner to rebind abilities to.");

        foreach (var ability in RebindablePrintedAbilities(source))
        {
            switch (ability)
            {
                case ActivatedAbility aa:
                    yield return aa.RebindTo(target, controller);
                    break;
                case TriggeredAbility ta:
                    yield return ta.RebindTo(target, controller);
                    break;
            }
        }
    }
}
