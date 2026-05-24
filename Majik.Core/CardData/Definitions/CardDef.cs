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
/// ## Coordination with effects-primitive library
///
/// A parallel branch (<c>feat/effects-primitives</c>) is growing a shared
/// effects-primitive library under <c>Majik.Core/Effects/Primitives/</c>.
/// At branch time that folder is empty, so <see cref="ResolveBuilder"/>
/// ships with minimal inline effect builders that route through the
/// engine's existing <see cref="Majik.Core.Abilities.Effect"/> +
/// <see cref="Majik.Core.CardData.OracleSpellBinder"/> helpers. When the
/// primitive library lands, each <c>ResolveBuilder</c> method migrates
/// to compose those primitives instead of inlining the action — the
/// call sites in factories stay identical.
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
        ResolveBody? resolveBody)
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
