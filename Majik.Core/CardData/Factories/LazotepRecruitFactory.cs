using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Lazotep Recruit — synthetic Amass keyword
/// fixture (loosely modelled on the War of the Spark "Lazotep" Amass
/// creatures). No clean Modern-legal printed Amass card had a
/// sufficiently isolated trigger without additional riders (Eternal
/// Skylord has flying-grant, Widespread Brutality also deals damage).
///
/// Creature — Zombie {1}{B} 1/1. Oracle text:
///   "When Lazotep Recruit enters, amass Zombies 1."
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/lazotep-recruit.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card. Single
/// ETB-triggered <c>amass_self</c> ability — JSON only.
///
/// ## Implemented (v1)
/// - Vanilla 1/1 Zombie shell with no Modern-relevant rider.
/// - ETB trigger: Amass Zombies 1 (CR 701.49). If the controller has no
///   Army on the battlefield, creates a 0/0 black Zombie Army creature
///   token. Then puts 1 +1/+1 counter on an Army that controller controls
///   (v1 auto-picks the first Army found on the battlefield via
///   <see cref="Majik.Core.Keywords.AmassAction"/>).
///
/// ## Deferred
/// - Army-target player prompt (when multiple Armies exist) waits on the
///   target-prompt system. v1 auto-picks deterministically.
/// </summary>
[CardName("Lazotep Recruit")]
public static class LazotepRecruitFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("lazotep-recruit");

    /// <summary>
    /// Construct Lazotep Recruit owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Creature Create(Player owner) =>
        (Creature)CardDefinitionFactory.Build(Definition, owner);
}
