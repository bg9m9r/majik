using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ghost Warden (8th Edition / Portal, {1}{W}).
///
/// Creature — Spirit 1/1. Oracle text (verified against Scryfall):
///   "{T}: Target creature gets +1/+1 until end of turn."
///
/// The card's entire behaviour is the soundly-reconstructable targeted-pump
/// activated-ability shape ("{cost}: Target creature gets ±X/±Y until end of
/// turn" — CR 602.2 activated ability + CR 611 Layer-7c continuous effect with
/// CR 514.2 end-of-turn expiry). It is materialised fully data-driven via the
/// <see cref="CardDef"/> DSL: a <see cref="TapSelfCostDef"/> ({T}) cost and a
/// <see cref="PumpTargetEffectDef"/> (+1/+1) effect, whose
/// <see cref="EffectDefinition.ToTargetRequest"/> supplies the 1..1
/// target-creature slot. Because the pump effect reads
/// <c>ResolutionContext.Source</c> (RebindSafe) and registers the Layer-7c
/// <see cref="Majik.Core.Effects.PumpUntilEndOfTurnEffect"/> against the CHOSEN
/// target creature, this is also the canonical real card that
/// <see cref="OracleActivatedAbilityBinder"/>'s targeted-pump rebuild shape
/// re-homes for Agatha's Soul Cauldron (CR 613.1f / 702.49) — both the PRIMARY
/// RebindTo path and the oracle-rebuild fallback now cover it.
/// </summary>
[CardName(CardName)]
public static class GhostWardenFactory
{
    public const string CardName = "Ghost Warden";

    public static CardDef Define()
    {
        var ability = new ActivatedAbilityDefinition
        {
            Costs = { new TapSelfCostDef() },
            Effects = { new PumpTargetEffectDef { Power = 1, Toughness = 1, TargetFilter = "creature" } },
        };

        return CardDef
            .Creature(CardName, "{1}{W}", power: 1, toughness: 1)
            .WithSubtype(CardSubtype.Spirit)
            .WithAbility(ability.ToCardDefAbility())
            .Build();
    }

    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Creature)CardDefRuntime.Build(Define(), owner);
    }
}
