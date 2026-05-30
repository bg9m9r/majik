namespace Majik.Core.Effects;

/// <summary>
/// CR 613 working-set common to every permanent. Seeded from printed
/// values, then mutated layer-by-layer. <see cref="CreatureCharacteristics"/>
/// extends this with P/T for creatures.
/// </summary>
public class PermanentCharacteristics
{
    public HashSet<Majik.Core.Cards.Types.CardType> Types { get; } = new();
    public HashSet<Majik.Core.Cards.Types.CardSubtype> Subtypes { get; } = new();
    public HashSet<string> Keywords { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// CR 105 / CR 613.1e — the permanent's effective colour set after the
    /// Layer-5 colour-changing pass. Seeded by
    /// <see cref="ContinuousEffectsService.Compute(Majik.Core.Cards.Permanent)"/>
    /// from the printed/static colour (mana-cost pips + colour indicator +
    /// token override + Devoid), then mutated by any active Layer-5 SET /
    /// ADD colour effects. Only ever holds the five real colours — never
    /// <see cref="Majik.Core.ValueObjects.ManaColor.Generic"/> or
    /// <see cref="Majik.Core.ValueObjects.ManaColor.Colorless"/>; an empty
    /// set is colourless.
    /// </summary>
    public HashSet<Majik.Core.ValueObjects.ManaColor> Colors { get; } = new();
}
