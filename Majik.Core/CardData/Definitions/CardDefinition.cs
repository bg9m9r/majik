using System.Text.Json.Serialization;

namespace Majik.Core.CardData.Definitions;

/// <summary>
/// Data-only card description loaded from a JSON file. Replaces hand-coded
/// per-card C# factories for cards whose abilities fit the supported
/// schema. Phase-3 scope (this seam): names, types, P/T, mana abilities.
/// Activated/triggered/static abilities will land in follow-up PRs as
/// the schema grows.
///
/// Authoring rules:
/// - Authoritative — when this file disagrees with C# code, the JSON wins.
///   The C# factory for any migrated card becomes a thin wrapper that
///   loads its JSON and calls <see cref="CardDefinitionFactory"/>.
/// - Stable identifiers — string enums (CardType, CardSubtype) are
///   serialized by name to keep diffs reviewable.
/// - JSON-serializable only — no closures, no references to engine
///   services. The factory binds those at construction time.
/// </summary>
public sealed class CardDefinition
{
    /// <summary>Canonical card name (matches the Scryfall <c>name</c>
    /// field). Used as the cache + lookup key.</summary>
    public string Name { get; set; } = "";

    /// <summary>Card types, e.g. <c>["Land"]</c>, <c>["Artifact", "Creature"]</c>.
    /// Must contain at least one entry.</summary>
    public List<string> Types { get; set; } = new();

    /// <summary>Optional supertype list, e.g. <c>["Basic"]</c>, <c>["Legendary"]</c>.
    /// </summary>
    public List<string> Supertypes { get; set; } = new();

    /// <summary>Optional subtype list, e.g. <c>["Construct"]</c>,
    /// <c>["Phyrexian", "Human", "Cleric"]</c>.</summary>
    public List<string> Subtypes { get; set; } = new();

    /// <summary>Bracketed mana cost like <c>"{1}{G}"</c> or unbracketed
    /// <c>"1G"</c>. Empty string for cards with no cost (lands).</summary>
    public string ManaCost { get; set; } = "";

    /// <summary>Creature power. Null for non-creatures.</summary>
    public int? Power { get; set; }

    /// <summary>Creature toughness. Null for non-creatures.</summary>
    public int? Toughness { get; set; }

    /// <summary>Starting loyalty. Null for non-planeswalkers.</summary>
    public int? Loyalty { get; set; }

    /// <summary>Ability list. Each entry is a discriminated union via
    /// the <c>kind</c> JSON property.</summary>
    public List<AbilityDefinition> Abilities { get; set; } = new();
}

/// <summary>
/// Discriminated union for ability shapes. Polymorphism is wired with
/// <see cref="JsonPolymorphicAttribute"/> so deserializers pick the
/// right concrete type from the <c>kind</c> property.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ManaAbilityDefinition), "mana")]
[JsonDerivedType(typeof(ActivatedAbilityDefinition), "activated")]
public abstract class AbilityDefinition { }

/// <summary>
/// Tap-to-produce-mana ability. <see cref="Produces"/> is a Scryfall
/// single-letter color code or short cost — the same string accepted by
/// <see cref="Majik.Core.ValueObjects.ManaCost.Parse"/> (e.g. "G", "B",
/// "WU" for either-or hybrid handled elsewhere).
///
/// CR 605.1 — mana abilities don't use the stack. The runtime factory
/// builds a <see cref="Majik.Core.Abilities.ManaAbility"/> with the
/// parsed cost; no priority round is incurred when activated.
/// </summary>
public sealed class ManaAbilityDefinition : AbilityDefinition
{
    public string Produces { get; set; } = "";
}

/// <summary>
/// Non-mana activated ability — pay all <see cref="Costs"/>, then
/// resolve every entry in <see cref="Effects"/> in order. Uses the
/// stack (mana abilities do not — CR 605.1). Cost / effect variants
/// live under <see cref="CostDefinition"/> + <see cref="EffectDefinition"/>.
///
/// <see cref="SorcerySpeed"/>: per CR 307.4 / 606.4, an "Activate only
/// as a sorcery" rider. Currently informational — the runtime activator
/// doesn't yet gate on it; the JSON faithfully records the intent so
/// the gate lands cleanly when the activator learns the flag.
/// </summary>
public sealed class ActivatedAbilityDefinition : AbilityDefinition
{
    public List<CostDefinition> Costs { get; set; } = new();
    public List<EffectDefinition> Effects { get; set; } = new();
    public bool SorcerySpeed { get; set; } = false;
}
