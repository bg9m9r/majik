namespace Majik.Core.ValueObjects;

/// <summary>
/// Single-mana-pip colour identifier. <see cref="Generic"/> is used by
/// hybrid pips ("2/W") to denote the alternative non-coloured payment.
/// </summary>
public enum ManaColor
{
    White,
    Blue,
    Black,
    Red,
    Green,
    Colorless,
    Generic,
}

/// <summary>
/// CR 107.4e — hybrid pip. Pay one mana of either <see cref="Color1"/>
/// or <see cref="Color2"/>. For "monocolored hybrid" (Reaper King), Color1
/// is generic with <see cref="GenericAlternative"/> set (e.g. {2/W}).
/// </summary>
public sealed record HybridPip(ManaColor Color1, ManaColor Color2, int GenericAlternative = 0);
