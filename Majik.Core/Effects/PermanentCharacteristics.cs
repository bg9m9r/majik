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
}
