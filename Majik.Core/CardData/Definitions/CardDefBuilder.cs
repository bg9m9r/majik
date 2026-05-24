using Majik.Core.Cards.Types;

namespace Majik.Core.CardData.Definitions;

/// <summary>
/// Mutable builder behind the fluent <see cref="CardDef"/> DSL.
///
/// Builders are produced by <see cref="CardDef.Instant"/>,
/// <see cref="CardDef.Creature"/>, etc.; each fluent method returns
/// <c>this</c> for chaining. Call <see cref="Build"/> (or rely on the
/// implicit <see cref="CardDef"/> conversion) at the end.
///
/// <para>
/// Typical use lives in a factory's <c>Define()</c> method:
/// </para>
/// <code>
/// public static CardDef Define() => CardDef
///     .Creature("Grizzly Bears", "{1}{G}", power: 2, toughness: 2)
///     .WithSubtype(CardSubtype.Bear);
/// </code>
/// </summary>
public sealed class CardDefBuilder
{
    private readonly string _name;
    private readonly string _manaCost;
    private readonly CardType _primaryType;
    private readonly List<CardType> _additionalTypes = new();
    private readonly List<CardSupertype> _supertypes = new();
    private readonly List<CardSubtype> _subtypes = new();
    private readonly List<string> _keywords = new();
    private readonly List<string> _manaAbilities = new();
    private int? _power;
    private int? _toughness;
    private int? _loyalty;
    private ResolveBody? _resolveBody;

    internal CardDefBuilder(string name, string manaCost, CardType primaryType)
    {
        _name = name ?? throw new ArgumentNullException(nameof(name));
        _manaCost = manaCost ?? string.Empty;
        _primaryType = primaryType;
    }

    internal CardDefBuilder WithPowerToughness(int power, int toughness)
    {
        _power = power;
        _toughness = toughness;
        return this;
    }

    internal CardDefBuilder WithLoyalty(int loyalty)
    {
        _loyalty = loyalty;
        return this;
    }

    /// <summary>
    /// Append a creature/land/etc. subtype (CR 205.3). Multiple calls
    /// stack — invoke once per subtype.
    /// </summary>
    public CardDefBuilder WithSubtype(CardSubtype subtype)
    {
        _subtypes.Add(subtype);
        return this;
    }

    /// <summary>Append several subtypes in one shot.</summary>
    public CardDefBuilder WithSubtypes(params CardSubtype[] subtypes)
    {
        _subtypes.AddRange(subtypes);
        return this;
    }

    /// <summary>Append a supertype (Basic / Legendary / Snow / World).</summary>
    public CardDefBuilder WithSupertype(CardSupertype supertype)
    {
        _supertypes.Add(supertype);
        return this;
    }

    /// <summary>Append an additional card type (e.g. layering Creature
    /// onto a Planeswalker, or Artifact onto a Creature).</summary>
    public CardDefBuilder WithType(CardType type)
    {
        _additionalTypes.Add(type);
        return this;
    }

    /// <summary>
    /// Attach a keyword-ability marker (Haste, Flash, Delve, …). The
    /// runtime wires a <see cref="Majik.Core.Abilities.KeywordAbility"/>
    /// so introspection (UI, bots) sees the keyword on the card; the
    /// keyword's gameplay mechanic is enforced by the keyword's own
    /// subsystem (cost paths, timing rules, etc.).
    /// </summary>
    public CardDefBuilder WithKeyword(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            throw new ArgumentException("Keyword must be non-empty.", nameof(keyword));
        _keywords.Add(keyword);
        return this;
    }

    /// <summary>
    /// Attach a basic mana ability — "{T}: Add &lt;output&gt;". The
    /// output string is passed verbatim to
    /// <see cref="Majik.Core.ValueObjects.ManaCost.Parse"/>, so the
    /// short forms ("G", "WU", "C") and bracketed forms ("{G}{G}")
    /// both work.
    /// </summary>
    public CardDefBuilder ManaAbility(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            throw new ArgumentException("Mana output must be non-empty.", nameof(output));
        _manaAbilities.Add(output);
        return this;
    }

    /// <summary>
    /// Declare a spell's resolve-time body. The supplied callback configures
    /// a <see cref="ResolveBuilder"/> with the effects this spell triggers
    /// when it resolves. Only valid on Instant + Sorcery (other card types
    /// build resolve bodies via triggered/activated abilities instead).
    ///
    /// <code>
    /// CardDef.Sorcery("Lightning Bolt", "{R}")
    ///     .Resolve(c => c.DealDamage(3).To(TargetKind.AnyTarget));
    /// </code>
    /// </summary>
    public CardDefBuilder Resolve(Action<ResolveBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var rb = new ResolveBuilder();
        configure(rb);
        _resolveBody = rb.Build();
        return this;
    }

    /// <summary>
    /// Materialize the immutable <see cref="CardDef"/>.
    /// </summary>
    public CardDef Build() => new CardDef(
        _name,
        _manaCost,
        _primaryType,
        _additionalTypes.ToArray(),
        _power,
        _toughness,
        _loyalty,
        _supertypes.ToArray(),
        _subtypes.ToArray(),
        _keywords.ToArray(),
        _manaAbilities.ToArray(),
        _resolveBody);

    /// <summary>Lets callers omit the explicit <c>.Build()</c> call.</summary>
    public static implicit operator CardDef(CardDefBuilder b) => b.Build();
}
