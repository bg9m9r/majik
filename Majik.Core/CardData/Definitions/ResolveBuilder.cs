namespace Majik.Core.CardData.Definitions;

/// <summary>
/// Target classes accepted by <see cref="TargetedEffect.To"/>. The set
/// mirrors the printed-MTG vocabulary used on simple removal/burn/buff
/// spells; richer filters (color, type-combo, "you control", "an
/// opponent controls", etc.) are layered on top of these by callers
/// that need them.
/// </summary>
public enum TargetKind
{
    /// <summary>"any target" — player, creature, planeswalker, or battle
    /// (CR 115.3). Bolt-style.</summary>
    AnyTarget,

    /// <summary>"target creature".</summary>
    Creature,

    /// <summary>"target player".</summary>
    Player,

    /// <summary>"target opponent".</summary>
    Opponent,

    /// <summary>"target creature or player" (pre-CR-115 wording — still
    /// shows up on legacy reprints).</summary>
    CreatureOrPlayer,

    /// <summary>"target nonblack creature" — used by Terror-shaped removal.</summary>
    NonblackCreature,

    /// <summary>"target nonblack, nonartifact creature" — full Terror filter.</summary>
    NonblackNonartifactCreature,

    /// <summary>"target creature an opponent controls".</summary>
    OpponentCreature,

    /// <summary>"target permanent".</summary>
    Permanent,
}

/// <summary>
/// Effect kinds emitted by <see cref="ResolveBuilder"/>. The discriminator
/// is read by <see cref="CardDefRuntime"/> when it materializes the
/// resolve body into engine effects.
///
/// ## Coordination with effects-primitive library
///
/// When PR #1 (<c>feat/effects-primitives</c>) lands a shared
/// <c>Majik.Core.Effects.Primitives.*</c> module, each of these kinds
/// becomes a thin alias for a primitive — keeping the DSL surface
/// (<c>c.DealDamage(3)</c>, <c>c.GainLife(2)</c>) identical at call sites.
/// </summary>
public enum ResolveEffectKind
{
    DealDamage,
    PumpUntilEndOfTurn,
    DestroyTarget,
    Mill,
    DrawCards,
    GainLife,
    LoseLife,
    Counter,
    CreateToken,
    AddMana,
}

/// <summary>
/// One discrete effect contributed by a <see cref="ResolveBuilder"/>.
/// Kept as a data record — interpretation lives in
/// <see cref="CardDefRuntime"/>.
/// </summary>
public sealed record ResolveEffect(
    ResolveEffectKind Kind,
    int IntArg,
    int IntArg2,
    TargetKind? Target,
    string? StringArg);

/// <summary>
/// The compiled resolve body produced by <see cref="ResolveBuilder.Build"/>.
/// Holds the ordered effect list — interpretation happens in
/// <see cref="CardDefRuntime"/>.
/// </summary>
public sealed class ResolveBody
{
    public IReadOnlyList<ResolveEffect> Effects { get; }

    internal ResolveBody(IReadOnlyList<ResolveEffect> effects) { Effects = effects; }
}

/// <summary>
/// Fluent builder for a spell's resolve-time effect list. Returned by
/// <see cref="CardDefBuilder.Resolve"/> and configured inline:
///
/// <code>
/// CardDef.Sorcery("Lightning Bolt", "{R}")
///     .Resolve(c => c.DealDamage(3).To(TargetKind.AnyTarget));
/// </code>
///
/// Some calls return a <see cref="TargetedEffect"/> instead of the
/// builder itself — these are the effects that demand a target. The
/// chained <see cref="TargetedEffect.To"/> closes over the target and
/// re-enters the builder so the rest of the body can chain.
/// </summary>
public sealed class ResolveBuilder
{
    private readonly List<ResolveEffect> _effects = new();

    internal ResolveBuilder() { }

    internal void AddEffect(ResolveEffect effect) => _effects.Add(effect);

    /// <summary>Deal N damage; finalize with <see cref="TargetedEffect.To"/>.</summary>
    public TargetedEffect DealDamage(int amount) =>
        new TargetedEffect(this, ResolveEffectKind.DealDamage, amount, 0);

    /// <summary>Give a creature +P/+T until end of turn (Giant Growth shape).
    /// Finalize with <see cref="TargetedEffect.To"/>.</summary>
    public TargetedEffect PumpUntilEndOfTurn(int power, int toughness) =>
        new TargetedEffect(this, ResolveEffectKind.PumpUntilEndOfTurn, power, toughness);

    /// <summary>Destroy a target permanent. Finalize with <see cref="TargetedEffect.To"/>.
    /// The shortcut overload below presets the target kind for the common
    /// Terror-shaped case.</summary>
    public TargetedEffect DestroyTarget() =>
        new TargetedEffect(this, ResolveEffectKind.DestroyTarget, 0, 0);

    /// <summary>Convenience — destroy with the target kind baked in.
    /// Equivalent to <c>DestroyTarget().To(kind)</c>.</summary>
    public ResolveBuilder DestroyTarget(TargetKind kind)
    {
        _effects.Add(new ResolveEffect(
            ResolveEffectKind.DestroyTarget, 0, 0, kind, null));
        return this;
    }

    /// <summary>Mill N cards from the spell's controller (CR 701.13).</summary>
    public ResolveBuilder Mill(int amount)
    {
        _effects.Add(new ResolveEffect(
            ResolveEffectKind.Mill, amount, 0, null, null));
        return this;
    }

    /// <summary>Draw N cards (controller).</summary>
    public ResolveBuilder DrawCards(int amount)
    {
        _effects.Add(new ResolveEffect(
            ResolveEffectKind.DrawCards, amount, 0, null, null));
        return this;
    }

    /// <summary>Controller gains N life.</summary>
    public ResolveBuilder GainLife(int amount)
    {
        _effects.Add(new ResolveEffect(
            ResolveEffectKind.GainLife, amount, 0, null, null));
        return this;
    }

    /// <summary>Controller loses N life.</summary>
    public ResolveBuilder LoseLife(int amount)
    {
        _effects.Add(new ResolveEffect(
            ResolveEffectKind.LoseLife, amount, 0, null, null));
        return this;
    }

    /// <summary>Counter target spell/ability (CR 701.5). Currently a stub
    /// — wired by <see cref="CardDefRuntime"/> once the counter primitive
    /// from the effects-primitive library lands.</summary>
    public ResolveBuilder Counter(TargetKind targetKind)
    {
        _effects.Add(new ResolveEffect(
            ResolveEffectKind.Counter, 0, 0, targetKind, null));
        return this;
    }

    /// <summary>Add a parsed mana cost (e.g. <c>"BBB"</c>, <c>"{R}{R}"</c>)
    /// to the controller's mana pool. Used by ritual-shaped spells.</summary>
    public ResolveBuilder AddMana(string manaShortForm)
    {
        if (string.IsNullOrWhiteSpace(manaShortForm))
            throw new ArgumentException("Mana shortform must be non-empty.", nameof(manaShortForm));
        _effects.Add(new ResolveEffect(
            ResolveEffectKind.AddMana, 0, 0, null, manaShortForm));
        return this;
    }

    internal ResolveBody Build() => new ResolveBody(_effects.ToArray());
}

/// <summary>
/// Half-built targeted effect — fluently closed by
/// <see cref="To"/> with a <see cref="TargetKind"/>. Returning a separate
/// chainable type forces the call site to specify a target rather than
/// silently emitting an untargeted effect.
/// </summary>
public sealed class TargetedEffect
{
    private readonly ResolveBuilder _parent;
    private readonly ResolveEffectKind _kind;
    private readonly int _intArg;
    private readonly int _intArg2;

    internal TargetedEffect(ResolveBuilder parent, ResolveEffectKind kind, int intArg, int intArg2)
    {
        _parent = parent;
        _kind = kind;
        _intArg = intArg;
        _intArg2 = intArg2;
    }

    /// <summary>Close the targeted effect with a target kind and rejoin
    /// the parent <see cref="ResolveBuilder"/> for further chaining.</summary>
    public ResolveBuilder To(TargetKind kind)
    {
        _parent.AddEffect(new ResolveEffect(_kind, _intArg, _intArg2, kind, null));
        return _parent;
    }
}
