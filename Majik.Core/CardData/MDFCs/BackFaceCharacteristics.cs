using Majik.Core.Cards.Types;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.MDFCs;

/// <summary>
/// CR 711 / CR 712 — the printed copiable characteristics of a transform
/// card's BACK face, captured at build time by the DFC's named-card factory
/// (which knows both faces). When the permanent is on its back face
/// (<see cref="MdfcState.IsBackFace"/>), the Layer-0 face-replacement seed in
/// <see cref="Majik.Core.Effects.ContinuousEffectsService.Compute(Majik.Core.Cards.Permanent)"/>
/// reads THIS record instead of the live (front-printed) Card object, so the
/// permanent's effective name / types / subtypes / supertypes / P/T (or
/// loyalty) / keywords / colour all reflect the back face. The normal CR 613
/// layer pipeline then applies on top (anthems, counters, type-grants, …).
///
/// <para>This is a pure data carrier — it never mutates the underlying Card.
/// Reverting is automatic: flipping <see cref="MdfcState.IsBackFace"/> back to
/// false makes <c>Compute</c> seed from the front-printed values again.</para>
///
/// <para>Triggered / activated abilities of the back face are NOT modelled
/// here — those are attached by the factory and gated on the active face
/// (Compute is a characteristic pipeline, not an ability registry). This
/// record covers the CR 613.1 characteristic-defining seed only.</para>
/// </summary>
public sealed class BackFaceCharacteristics
{
    /// <summary>Back-face name (CR 712 — the back face has its own name).</summary>
    public string Name { get; }

    /// <summary>Back-face card types (CR 711 — a creature front can have a
    /// planeswalker back, etc.). Seeded into <c>chars.Types</c>.</summary>
    public IReadOnlyList<CardType> Types { get; }

    /// <summary>Back-face subtypes. Seeded into <c>chars.Subtypes</c>.</summary>
    public IReadOnlyList<CardSubtype> Subtypes { get; }

    /// <summary>Back-face supertypes (e.g. a Legendary planeswalker back).
    /// Seeded into <c>chars.Supertypes</c>.</summary>
    public IReadOnlyList<CardSupertype> Supertypes { get; }

    /// <summary>Back-face printed keyword abilities (e.g. Flying on Insectile
    /// Aberration). Seeded into <c>chars.Keywords</c>.</summary>
    public IReadOnlyList<string> Keywords { get; }

    /// <summary>Back-face colours (the five real colours only). Seeded into
    /// <c>chars.Colors</c>. Empty = colourless.</summary>
    public IReadOnlyList<ManaColor> Colors { get; }

    /// <summary>Back-face printed power (creature backs only; null for a
    /// non-creature back face such as a planeswalker).</summary>
    public int? Power { get; }

    /// <summary>Back-face printed toughness (creature backs only).</summary>
    public int? Toughness { get; }

    /// <summary>Back-face starting loyalty (planeswalker backs only; null for
    /// a creature back). Recorded for inspection — the planeswalker-loyalty
    /// body seed is a documented residual (see deferral #19).</summary>
    public int? Loyalty { get; }

    public BackFaceCharacteristics(
        string name,
        IEnumerable<CardType> types,
        IEnumerable<CardSubtype>? subtypes = null,
        IEnumerable<CardSupertype>? supertypes = null,
        IEnumerable<string>? keywords = null,
        IEnumerable<ManaColor>? colors = null,
        int? power = null,
        int? toughness = null,
        int? loyalty = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Back-face name required", nameof(name));
        ArgumentNullException.ThrowIfNull(types);

        Name = name;
        Types = types.ToList();
        Subtypes = (subtypes ?? Enumerable.Empty<CardSubtype>()).ToList();
        Supertypes = (supertypes ?? Enumerable.Empty<CardSupertype>()).ToList();
        Keywords = (keywords ?? Enumerable.Empty<string>()).ToList();
        Colors = (colors ?? Enumerable.Empty<ManaColor>()).ToList();
        Power = power;
        Toughness = toughness;
        Loyalty = loyalty;

        if (Types.Count == 0)
            throw new ArgumentException("Back face must have at least one card type", nameof(types));
    }

    /// <summary>True if the back face is a creature (carries P/T). Drives the
    /// creature-body seed in Compute.</summary>
    public bool IsCreatureBack => Types.Contains(CardType.Creature) && Power.HasValue && Toughness.HasValue;
}
