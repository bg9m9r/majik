using Majik.Core.Cards.Types;

namespace Majik.Core.CardData.Definitions;

/// <summary>
/// Fluent, data-only description of a Magic card produced by a factory's
/// <c>CardDef Define()</c> static method. Companion to the heavier
/// <see cref="CardDefinition"/> JSON schema — this one is a code-side
/// builder that collapses ~50–200 LOC of boilerplate per simple card
/// into ~10 LOC of fluent calls.
///
/// ## Authoring
///
/// Factories opt in by exposing <c>public static CardDef Define()</c>
/// and dropping their hand-rolled <c>Create(Player owner)</c>; the
/// <c>NamedCardFactoryGenerator</c> source generator detects the
/// <c>Define()</c> method and emits the dispatch arm + a
/// <c>Create(Player owner)</c> shim that calls
/// <see cref="CardDefRuntime.Build"/>.
///
/// <code>
/// [CardName("Lightning Bolt")]
/// public static class LightningBoltFactory
/// {
///     public static CardDef Define() => CardDef
///         .Sorcery("Lightning Bolt", "{R}")
///         .Resolve(c => c.DealDamage(3).To(TargetKind.AnyTarget));
/// }
/// </code>
///
/// ## Convergence direction (PLAN 03)
///
/// This DSL <see cref="CardDef"/> is the <b>target model</b> for all
/// declarative card authoring. The JSON
/// <see cref="CardDefinition"/> schema is a serialization of the same
/// shape (it deserializes into a <see cref="CardDef"/> in later slices),
/// and both vocabularies compile down to <b>one</b> shared
/// effect/cost/trigger primitive set:
/// <list type="bullet">
///   <item>effects → <see cref="Majik.Core.Primitives.Fx"/></item>
///   <item>costs → <see cref="Majik.Core.Primitives.Costs"/></item>
///   <item>triggers → <see cref="Majik.Core.Abilities.Triggers"/></item>
/// </list>
/// The primitive home is <c>Majik.Core/Primitives/</c> (NOT the never-built
/// <c>Majik.Core/Effects/Primitives/</c> the older TODOs pointed at).
/// <see cref="CardDefRuntime.MaterializeStep"/> already routes resolve
/// steps through <c>Fx.*</c> where the inlined logic is byte-identical;
/// remaining inline branches converge as the primitives gain coverage —
/// the DSL call sites in factories (<c>c.DealDamage(3)</c>,
/// <c>c.GainLife(2)</c>) stay identical throughout.
/// </summary>
public sealed class CardDef
{
    /// <summary>Canonical printed card name.</summary>
    public string Name { get; }

    /// <summary>Printed mana cost, e.g. <c>"{1}{G}"</c>. Empty for lands.</summary>
    public string ManaCost { get; }

    /// <summary>The card's primary type — picks the runtime C# class
    /// (Land / Creature / Instant / …) at <see cref="CardDefRuntime.Build"/>
    /// time.</summary>
    public CardType PrimaryType { get; }

    /// <summary>Additional card types appended via
    /// <see cref="CardDefBuilder.WithType"/> (e.g. Artifact Creature).</summary>
    public IReadOnlyList<CardType> AdditionalTypes { get; }

    /// <summary>Creature power. Null for non-creatures.</summary>
    public int? Power { get; }

    /// <summary>Creature toughness. Null for non-creatures.</summary>
    public int? Toughness { get; }

    /// <summary>Starting loyalty. Null for non-planeswalkers.</summary>
    public int? Loyalty { get; }

    /// <summary>Supertypes (Basic / Legendary / Snow / World).</summary>
    public IReadOnlyList<CardSupertype> Supertypes { get; }

    /// <summary>Subtypes (Bear, Wizard, Mountain, …).</summary>
    public IReadOnlyList<CardSubtype> Subtypes { get; }

    /// <summary>Keyword-ability markers (Haste, Flash, Delve, …).</summary>
    public IReadOnlyList<string> Keywords { get; }

    /// <summary>Mana-ability outputs declared via <see cref="CardDefBuilder.ManaAbility"/>
    /// — each entry is a parseable <see cref="Majik.Core.ValueObjects.ManaCost"/>
    /// short form (e.g. <c>"G"</c>, <c>"WU"</c>).</summary>
    public IReadOnlyList<string> ManaAbilities { get; }

    /// <summary>Resolve-time spell body, if the card declared one via
    /// <see cref="CardDefBuilder.Resolve"/>. Null for shape-only cards.</summary>
    public ResolveBody? ResolveBody { get; }

    /// <summary>
    /// Canonical activated / triggered / mana abilities (PLAN 03 S2). This
    /// is the in-memory home for the abilities the JSON
    /// <see cref="CardDefinition"/> schema carries — <see cref="CardDefinition.ToCardDef"/>
    /// maps its <c>Abilities</c> union onto this list, and
    /// <see cref="CardDefRuntime.Build"/> materializes each entry into a live
    /// <see cref="Majik.Core.Abilities.IAbility"/>. Distinct from the simple
    /// <see cref="ManaAbilities"/> shorthand the fluent DSL emits for the
    /// vanilla "{T}: Add X" case (which stays as-is for the DSL path).
    /// Empty for cards with no such abilities.
    /// </summary>
    public IReadOnlyList<CardDefAbility> Abilities { get; }

    /// <summary>
    /// Printed colour indicator (CR 202.2c) — the round dot left of the type
    /// line. Single-letter Scryfall codes (W/U/B/R/G). Empty means "no
    /// indicator printed; colour derives from the mana cost". Carried so the
    /// JSON path (Dryad Arbor) round-trips through
    /// <see cref="CardDefinition.ToCardDef"/> without losing its indicator.
    /// </summary>
    public IReadOnlyList<string> ColorIndicator { get; }

    internal CardDef(
        string name,
        string manaCost,
        CardType primaryType,
        IReadOnlyList<CardType> additionalTypes,
        int? power,
        int? toughness,
        int? loyalty,
        IReadOnlyList<CardSupertype> supertypes,
        IReadOnlyList<CardSubtype> subtypes,
        IReadOnlyList<string> keywords,
        IReadOnlyList<string> manaAbilities,
        ResolveBody? resolveBody,
        IReadOnlyList<CardDefAbility>? abilities = null,
        IReadOnlyList<string>? colorIndicator = null)
    {
        Name = name;
        ManaCost = manaCost;
        PrimaryType = primaryType;
        AdditionalTypes = additionalTypes;
        Power = power;
        Toughness = toughness;
        Loyalty = loyalty;
        Supertypes = supertypes;
        Subtypes = subtypes;
        Keywords = keywords;
        ManaAbilities = manaAbilities;
        ResolveBody = resolveBody;
        Abilities = abilities ?? Array.Empty<CardDefAbility>();
        ColorIndicator = colorIndicator ?? Array.Empty<string>();
    }

    // ---- Entry points -----------------------------------------------------

    /// <summary>Start an Instant card builder.</summary>
    public static CardDefBuilder Instant(string name, string manaCost) =>
        new CardDefBuilder(name, manaCost, CardType.Instant);

    /// <summary>Start a Sorcery card builder.</summary>
    public static CardDefBuilder Sorcery(string name, string manaCost) =>
        new CardDefBuilder(name, manaCost, CardType.Sorcery);

    /// <summary>Start a Creature card builder.</summary>
    public static CardDefBuilder Creature(string name, string manaCost, int power, int toughness) =>
        new CardDefBuilder(name, manaCost, CardType.Creature)
            .WithPowerToughness(power, toughness);

    /// <summary>Start an Enchantment card builder.</summary>
    public static CardDefBuilder Enchantment(string name, string manaCost) =>
        new CardDefBuilder(name, manaCost, CardType.Enchantment);

    /// <summary>Start an Artifact card builder.</summary>
    public static CardDefBuilder Artifact(string name, string manaCost) =>
        new CardDefBuilder(name, manaCost, CardType.Artifact);

    /// <summary>Start a Land card builder. Lands have no printed mana
    /// cost, so the cost arg is fixed to the empty string.</summary>
    public static CardDefBuilder Land(string name) =>
        new CardDefBuilder(name, manaCost: string.Empty, CardType.Land);

    /// <summary>Start a Planeswalker card builder.</summary>
    public static CardDefBuilder Planeswalker(string name, string manaCost, int loyalty) =>
        new CardDefBuilder(name, manaCost, CardType.Planeswalker)
            .WithLoyalty(loyalty);
}
