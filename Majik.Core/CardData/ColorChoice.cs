using Majik.Core.ValueObjects;

namespace Majik.Core.CardData;

/// <summary>
/// CR 614.12 — mutable holder for an "as this enters, choose a color"
/// decision (Sunken Citadel, Temple of the Dragon Queen, Coldsteel Heart,
/// Utopia Sprawl, …). The choice isn't known when a card's mana ability is
/// bound (at deck-build time), so the synthesized
/// <see cref="Majik.Core.Abilities.ManaAbility"/> reads this holder through a
/// dynamic generator at activation time, while the ETB
/// <see cref="Majik.Core.Effects.ChooseColorReplacement"/> stamps the agent's
/// pick onto it as the permanent enters (CR 614.12 — the choice is part of the
/// permanent's entering, before it's on the battlefield).
///
/// <para>
/// Until the choice resolves, <see cref="Chosen"/> is the
/// <see cref="DefaultColor"/> seeded at construction (deterministic and
/// strictly NARROWER than the old over-permissive five-WUBRG binding — exactly
/// one colour is producible, not all five). Once stamped, the holder is
/// immutable in practice (a permanent's chosen colour never changes — CR
/// 614.12 leaves it set "for as long as it remains on the battlefield").
/// </para>
/// </summary>
public sealed class ColorChoice
{
    /// <summary>The five colours a "choose a color" decision may pick — only
    /// WUBRG; colourless is not a colour (CR 105.1 / 105.2c).</summary>
    public static readonly ManaColor[] ChoosableColors =
    {
        ManaColor.White, ManaColor.Blue, ManaColor.Black, ManaColor.Red, ManaColor.Green,
    };

    /// <summary>The colour chosen "as this enters" (CR 614.12). Seeded to
    /// <paramref name="defaultColor"/> until the ETB replacement stamps the
    /// agent's pick. Always one of W/U/B/R/G.</summary>
    public ManaColor Chosen { get; private set; }

    public ColorChoice(ManaColor defaultColor = ManaColor.White)
    {
        Chosen = Validate(defaultColor);
    }

    /// <summary>Stamp the chosen colour (CR 614.12). Throws for a non-WUBRG
    /// colour — colourless / generic are not legal "choose a color" picks.</summary>
    public void Choose(ManaColor color) => Chosen = Validate(color);

    /// <summary>The single-pip <see cref="ManaCost"/> of the chosen colour —
    /// what a "{T}: Add one mana of the chosen color" ability produces.</summary>
    public ManaCost SinglePip() => CostForColor(Chosen, doubled: false);

    /// <summary>The double-pip <see cref="ManaCost"/> of the chosen colour —
    /// what Sunken Citadel's "{T}: Add two mana of the chosen color" produces.</summary>
    public ManaCost DoublePip() => CostForColor(Chosen, doubled: true);

    private static ManaColor Validate(ManaColor color) => color switch
    {
        ManaColor.White or ManaColor.Blue or ManaColor.Black
            or ManaColor.Red or ManaColor.Green => color,
        _ => throw new ArgumentOutOfRangeException(
            nameof(color), color,
            "A chosen color must be one of W/U/B/R/G (CR 105.1)."),
    };

    private static ManaCost CostForColor(ManaColor color, bool doubled)
    {
        var pip = color switch
        {
            ManaColor.White => "W",
            ManaColor.Blue => "U",
            ManaColor.Black => "B",
            ManaColor.Red => "R",
            ManaColor.Green => "G",
            _ => throw new ArgumentOutOfRangeException(nameof(color), color, null),
        };
        return ManaCost.Parse(doubled ? pip + pip : pip);
    }
}
