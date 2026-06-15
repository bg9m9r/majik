using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Dragon Blood (Fifth Edition / Ice Age, {3}).
///
/// Artifact. Oracle text (verified against Scryfall):
///   "{3}, {T}: Put a +1/+1 counter on target creature."
///
/// The card's entire behaviour is the soundly-reconstructable targeted-counter
/// activated-ability shape ("{cost}: Put a/N +1/+1 counter(s) on target
/// creature" — CR 602.2 activated ability + CR 122.1 counter placement). It is
/// materialised fully data-driven via the <see cref="CardDef"/> DSL: a
/// <see cref="ManaCostDef"/> ({3}) + <see cref="TapSelfCostDef"/> ({T}) cost and
/// a <see cref="PutCounterEffectDef"/> with <c>Target = "creature"</c>, whose
/// <see cref="EffectDefinition.ToTargetRequest"/> supplies the 1..1
/// target-creature slot. At resolution the effect reads the chosen creature off
/// <c>ResolutionContext.ChosenTargets</c> and adds the +1/+1 counter to THAT
/// creature's own <see cref="Majik.Core.Counters.CounterCollection"/> — the
/// on-card counterpart of <see cref="OracleActivatedAbilityBinder"/>'s
/// targeted-counter rebuild shape (Agatha's Soul Cauldron, CR 613.1f / 702.49).
/// </summary>
[CardName(CardName)]
public static class DragonBloodFactory
{
    public const string CardName = "Dragon Blood";

    public static CardDef Define()
    {
        var ability = new ActivatedAbilityDefinition
        {
            Costs = { new ManaCostDef { Amount = "{3}" }, new TapSelfCostDef() },
            Effects = { new PutCounterEffectDef { Counter = "+1/+1", Amount = 1, Target = "creature" } },
        };

        return CardDef
            .Artifact(CardName, "{3}")
            .WithAbility(ability.ToCardDefAbility())
            .Build();
    }

    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Artifact)CardDefRuntime.Build(Define(), owner);
    }
}
