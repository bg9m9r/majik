using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Walking Ballista (Kaladesh, {X}{X}).
///
/// Walking Ballista is an Artifact Creature — Construct 0/0.
/// Oracle text:
///   "Walking Ballista enters the battlefield with X +1/+1 counters on it.
///    {4}: Put a +1/+1 counter on Walking Ballista. Activate only as a sorcery.
///    Remove a +1/+1 counter from Walking Ballista: It deals 1 damage to any target."
///
/// Now a thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/walking-ballista.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card. Both
/// activated abilities are now JSON: <c>{4}: put counter</c> and
/// <c>remove counter: deal 1 damage stub</c>.
///
/// ## Deferred (v1 gaps, see linked issues)
/// - <b>ETB X counters</b>: requires plumbing ChosenSpellParams.X through
///   the ZoneMoveIntent / ETB hook layer. Until that infrastructure
///   exists, Walking Ballista enters as a 0/0 with zero counters
///   (state-based actions will immediately put it in the graveyard —
///   acceptable for unit tests that pre-seed counters manually).
/// - <b>Sorcery-speed restriction on {4}</b>: JSON
///   <c>"sorcerySpeed": true</c> threads through
///   <c>CardDefinitionFactory</c> onto the runtime ActivatedAbility's
///   <c>IsSorcerySpeed</c> flag; ActionValidator gates the activation
///   on the controller's main phase + empty stack (CR 117.1a / 307.5).
/// - <b>Target prompt for ping damage</b>: emitted as
///   <c>deal_damage_stub</c> in JSON; the effect fires but does not
///   route damage to a chosen target. Full targeting requires the
///   active prompt system (ITarget / TargetResolver).
/// </summary>
[CardName("Walking Ballista")]
public static class WalkingBallistaFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("walking-ballista");

    /// <summary>
    /// Construct a Walking Ballista for the given owner. The returned
    /// <see cref="Creature"/> also carries
    /// <see cref="Cards.Types.CardType.Artifact"/> (multi-type — CR 301.1 /
    /// 302.1) and both activated abilities described in the class xmldoc.
    /// </summary>
    public static Creature Create(Player owner) =>
        (Creature)CardDefinitionFactory.Build(Definition, owner);
}
