using Majik.Core.Cards.Types;
using Majik.Core.ValueObjects;

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

    /// <summary>"target spell" — any spell on the stack (CR 701.5).
    /// Used by Counterspell / Force of Will / Pact of Negation.</summary>
    Spell,

    /// <summary>"target noncreature spell" — used by Negate /
    /// Force of Negation / Disrupting Shoal.</summary>
    NoncreatureSpell,
}

/// <summary>
/// Effect kinds emitted by <see cref="ResolveBuilder"/>. The discriminator
/// is read by <see cref="CardDefRuntime"/> when it materializes the
/// resolve body into engine effects.
///
/// ## Shared primitive vocabulary (PLAN 03)
///
/// Each kind materializes (in <see cref="CardDefRuntime.MaterializeStep"/>)
/// onto the shared <see cref="Majik.Core.Primitives.Fx"/> effect vocabulary
/// — the same one the JSON <see cref="CardDefinitionFactory"/> uses (cost
/// shapes live in <see cref="Majik.Core.Primitives.Costs"/>, triggers in
/// <see cref="Majik.Core.Abilities.Triggers"/>). The DSL surface
/// (<c>c.DealDamage(3)</c>, <c>c.GainLife(2)</c>) stays identical at call
/// sites as branches converge onto the primitives. The canonical home is
/// <c>Majik.Core/Primitives/</c>, not the never-built
/// <c>Majik.Core/Effects/Primitives/</c> earlier TODOs referenced.
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
    string? StringArg,
    object? Payload = null);

/// <summary>
/// Token spec carried in <see cref="ResolveEffect.Payload"/> for
/// <see cref="ResolveEffectKind.CreateToken"/>. Mirrors
/// <c>TokenFactory.TokenSpec</c> but lives at the DSL layer to keep the
/// Definitions namespace free of a hard dependency on the Tokens module.
/// CardDefRuntime translates this to a TokenFactory.TokenSpec at
/// materialization time.
/// </summary>
public sealed record TokenBlueprint(
    string Name,
    int Power,
    int Toughness,
    IReadOnlyList<CardSubtype> Subtypes,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<ManaColor>? Colors);

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

    /// <summary>
    /// CR 701.5 — counter the chosen spell. Defaults to
    /// <see cref="TargetKind.Spell"/>; pass
    /// <see cref="TargetKind.NoncreatureSpell"/> for Negate-shape filters.
    /// At resolve time <see cref="CardDefRuntime"/> routes through
    /// <see cref="Majik.Core.Primitives.Fx.Counter"/> (alias for
    /// <see cref="Majik.Core.CardData.OracleSpellBinder.RemoveFromStack"/>
    /// + graveyard tail). CR 608.2b illegal-target gating happens in the
    /// SpellCastFlow target-check pass before the effect runs.
    /// </summary>
    public ResolveBuilder Counter(TargetKind targetKind = TargetKind.Spell)
    {
        _effects.Add(new ResolveEffect(
            ResolveEffectKind.Counter, 0, 0, targetKind, null));
        return this;
    }

    /// <summary>
    /// CR 111 — create a creature token on the controller's battlefield.
    /// Returns a <see cref="TokenBuilder"/> so callers can chain
    /// <c>.Colors(...)</c> / <c>.WithKeyword(...)</c> riders before the
    /// next resolve step. The token is materialized at resolve time via
    /// <see cref="Majik.Core.Tokens.TokenFactory.CreateOnBattlefield"/>.
    ///
    /// <code>
    /// CardDef.Sorcery("Raise the Alarm", "{1}{W}")
    ///     .Resolve(c => c
    ///         .CreateToken("Soldier", 1, 1, CardSubtype.Soldier).Colors(ManaColor.White)
    ///         .CreateToken("Soldier", 1, 1, CardSubtype.Soldier).Colors(ManaColor.White));
    /// </code>
    /// </summary>
    public TokenBuilder CreateToken(
        string tokenName,
        int power,
        int toughness,
        params CardSubtype[] subtypes)
    {
        if (string.IsNullOrWhiteSpace(tokenName))
            throw new ArgumentException("Token name must be non-empty.", nameof(tokenName));
        var blueprint = new TokenBlueprint(
            tokenName, power, toughness,
            (subtypes ?? Array.Empty<CardSubtype>()).ToArray(),
            Array.Empty<string>(),
            Colors: null);
        _effects.Add(new ResolveEffect(
            ResolveEffectKind.CreateToken, 0, 0, null, null, blueprint));
        return new TokenBuilder(this, _effects.Count - 1);
    }

    /// <summary>Internal accessor for <see cref="TokenBuilder"/>'s in-place mutate.</summary>
    internal ResolveEffect GetEffectAt(int index) => _effects[index];

    /// <summary>Internal accessor for <see cref="TokenBuilder"/>'s in-place mutate.</summary>
    internal void ReplaceEffectAt(int index, ResolveEffect effect) => _effects[index] = effect;

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
/// Mutator for the most-recently-emitted <see cref="ResolveEffectKind.CreateToken"/>
/// effect. The effect is appended eagerly when
/// <see cref="ResolveBuilder.CreateToken"/> is called (so the resolve body
/// captures it even when the call site drops the return value); subsequent
/// <see cref="Colors"/> / <see cref="WithKeyword"/> calls rewrite the
/// blueprint in place. All <see cref="ResolveBuilder"/> verbs are proxied
/// so the chain keeps reading like one fluent statement.
/// </summary>
public sealed class TokenBuilder
{
    private readonly ResolveBuilder _parent;
    private readonly int _effectIndex;

    internal TokenBuilder(ResolveBuilder parent, int effectIndex)
    {
        _parent = parent;
        _effectIndex = effectIndex;
    }

    /// <summary>CR 105 / CR 111.4 — stamp the token's printed colour
    /// identity. Pass each colour pip (e.g. <c>.Colors(ManaColor.White,
    /// ManaColor.Red)</c>). Omitting this leaves the token colourless.</summary>
    public TokenBuilder Colors(params ManaColor[] colors)
    {
        Mutate(b => b with { Colors = colors?.ToArray() ?? Array.Empty<ManaColor>() });
        return this;
    }

    /// <summary>Grant the token a keyword ability (Flying, Haste, etc.).
    /// Multiple calls stack.</summary>
    public TokenBuilder WithKeyword(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            throw new ArgumentException("Keyword must be non-empty.", nameof(keyword));
        Mutate(b =>
        {
            var kws = b.Keywords.ToList();
            kws.Add(keyword);
            return b with { Keywords = kws.ToArray() };
        });
        return this;
    }

    /// <summary>
    /// Return to the parent <see cref="ResolveBuilder"/> explicitly.
    /// Callers normally don't need this — the proxy pass-through methods
    /// and the implicit conversion below make the chain feel like
    /// <see cref="ResolveBuilder"/> throughout.
    /// </summary>
    public ResolveBuilder Done() => _parent;

    // ----- Pass-through proxies so the resolve chain keeps reading fluently.

    /// <summary>Chain another <see cref="ResolveBuilder.CreateToken"/>.</summary>
    public TokenBuilder CreateToken(string tokenName, int power, int toughness, params CardSubtype[] subtypes)
        => _parent.CreateToken(tokenName, power, toughness, subtypes);

    /// <summary>Proxy <see cref="ResolveBuilder.DealDamage"/>.</summary>
    public TargetedEffect DealDamage(int amount) => _parent.DealDamage(amount);
    /// <summary>Proxy <see cref="ResolveBuilder.PumpUntilEndOfTurn"/>.</summary>
    public TargetedEffect PumpUntilEndOfTurn(int p, int t) => _parent.PumpUntilEndOfTurn(p, t);
    /// <summary>Proxy <see cref="ResolveBuilder.GainLife"/>.</summary>
    public ResolveBuilder GainLife(int amount) => _parent.GainLife(amount);
    /// <summary>Proxy <see cref="ResolveBuilder.LoseLife"/>.</summary>
    public ResolveBuilder LoseLife(int amount) => _parent.LoseLife(amount);
    /// <summary>Proxy <see cref="ResolveBuilder.DrawCards"/>.</summary>
    public ResolveBuilder DrawCards(int amount) => _parent.DrawCards(amount);
    /// <summary>Proxy <see cref="ResolveBuilder.Mill"/>.</summary>
    public ResolveBuilder Mill(int amount) => _parent.Mill(amount);
    /// <summary>Proxy <see cref="ResolveBuilder.Counter"/>.</summary>
    public ResolveBuilder Counter(TargetKind targetKind = TargetKind.Spell) => _parent.Counter(targetKind);
    /// <summary>Proxy <see cref="ResolveBuilder.AddMana"/>.</summary>
    public ResolveBuilder AddMana(string mana) => _parent.AddMana(mana);
    /// <summary>Proxy <see cref="ResolveBuilder.DestroyTarget(TargetKind)"/>.</summary>
    public ResolveBuilder DestroyTarget(TargetKind kind) => _parent.DestroyTarget(kind);

    private void Mutate(Func<TokenBlueprint, TokenBlueprint> mutator)
    {
        var effect = _parent.GetEffectAt(_effectIndex);
        var blueprint = (TokenBlueprint)effect.Payload!;
        _parent.ReplaceEffectAt(_effectIndex, effect with { Payload = mutator(blueprint) });
    }

    /// <summary>Implicit return-to-parent so the resolve chain reads as a
    /// single fluent statement.</summary>
    public static implicit operator ResolveBuilder(TokenBuilder b) => b._parent;
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
