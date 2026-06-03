using Majik.Core.Cards.Types;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.MDFCs;

/// <summary>
/// CR 711 — the printed characteristics of a transform double-faced card's
/// BACK face. A transform DFC permanent carries both faces on one physical
/// card; while it is back-face up its characteristics are the BACK face's
/// (name, card types, subtypes, supertypes, keywords, colours, and — for a
/// creature back — power/toughness), replacing the front-printed values
/// (CR 711.4 / Layer-0 face replacement).
///
/// <para>This immutable carrier is attached to the front-face card's
/// <see cref="MdfcState.BackFaceCharacteristics"/> by the card's factory
/// (the factory knows both faces). When
/// <see cref="MdfcState.IsBackFace"/> is true,
/// <see cref="Majik.Core.Effects.ContinuousEffectsService"/> seeds its
/// CR 613 working set from THIS record instead of the front-printed values,
/// BEFORE the layer pipeline (anthems / +1/+1 counters / type+colour grants)
/// applies on top. Transforming back reverts to the front-printed seed;
/// face-down (CR 708.2) still wins over the back-face seed.</para>
///
/// <para>The optional <see cref="Loyalty"/> records a planeswalker back's
/// starting loyalty. On transform to the back face it seeds the
/// <see cref="Majik.Core.Cards.Permanent"/> transient-loyalty surface
/// (<see cref="Majik.Core.Cards.Permanent.SetTransientLoyalty"/>) so a
/// creature-front C# instance — a <see cref="Majik.Core.Cards.Creature"/>,
/// NOT a <see cref="Majik.Core.Cards.Planeswalker"/> — carries a working
/// loyalty body without re-classing: loyalty-removing DAMAGE (CR 306.7 /
/// 120.3) and the loyalty=0 DEATH SBA (CR 704.5j) both consult it via
/// <see cref="Majik.Core.Cards.Permanent.GetEffectiveLoyalty"/> /
/// <see cref="Majik.Core.Cards.Permanent.IsLoyaltyDead"/>. The back face's
/// activated LOYALTY ABILITIES ([+1]/[−2]/…) are still not wired — those are
/// keyed on the <see cref="Majik.Core.Abilities.LoyaltyAbility"/> /
/// <see cref="Majik.Core.Cards.Planeswalker"/> activation surface and remain
/// the residual of v1-deferrals #19a (low value — the played face is the
/// creature front).</para>
/// </summary>
public sealed class BackFaceCharacteristics
{
    /// <summary>The back face's printed name.</summary>
    public string Name { get; }

    /// <summary>True when the back face is a creature (carries P/T). When
    /// false, <see cref="Power"/> / <see cref="Toughness"/> are ignored.</summary>
    public bool IsCreature { get; }

    /// <summary>The back face's printed power (creature backs only).</summary>
    public int Power { get; }

    /// <summary>The back face's printed toughness (creature backs only).</summary>
    public int Toughness { get; }

    /// <summary>The back face's printed card types.</summary>
    public IReadOnlyList<CardType> Types { get; }

    /// <summary>The back face's printed subtypes.</summary>
    public IReadOnlyList<CardSubtype> Subtypes { get; }

    /// <summary>The back face's printed supertypes.</summary>
    public IReadOnlyList<CardSupertype> Supertypes { get; }

    /// <summary>The back face's printed keyword abilities (e.g. "Flying").</summary>
    public IReadOnlyList<string> Keywords { get; }

    /// <summary>The back face's printed colour set (the five real colours;
    /// empty = colourless).</summary>
    public IReadOnlyList<ManaColor> Colors { get; }

    /// <summary>Planeswalker back's starting loyalty, when applicable. Null
    /// for non-planeswalker backs. Carried but not yet honoured — see the
    /// class remarks.</summary>
    public int? Loyalty { get; }

    public BackFaceCharacteristics(
        string name,
        bool isCreature,
        int power,
        int toughness,
        IReadOnlyList<CardType> types,
        IReadOnlyList<CardSubtype>? subtypes = null,
        IReadOnlyList<CardSupertype>? supertypes = null,
        IReadOnlyList<string>? keywords = null,
        IReadOnlyList<ManaColor>? colors = null,
        int? loyalty = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException(nameof(name));
        Name = name;
        IsCreature = isCreature;
        Power = power;
        Toughness = toughness;
        Types = types ?? throw new ArgumentNullException(nameof(types));
        Subtypes = subtypes ?? Array.Empty<CardSubtype>();
        Supertypes = supertypes ?? Array.Empty<CardSupertype>();
        Keywords = keywords ?? Array.Empty<string>();
        Colors = colors ?? Array.Empty<ManaColor>();
        Loyalty = loyalty;
    }

    /// <summary>Convenience factory for a creature back face.</summary>
    public static BackFaceCharacteristics Creature(
        string name,
        int power,
        int toughness,
        IReadOnlyList<CardSubtype>? subtypes = null,
        IReadOnlyList<CardSupertype>? supertypes = null,
        IReadOnlyList<string>? keywords = null,
        IReadOnlyList<ManaColor>? colors = null) =>
        new(name, isCreature: true, power, toughness,
            new[] { CardType.Creature }, subtypes, supertypes, keywords, colors);
}
