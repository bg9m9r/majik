using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mindslaver (Mirrodin, {6}).
///
/// Legendary Artifact. Oracle text:
///   "{4}, {T}, Sacrifice Mindslaver: You control target player during
///    that player's next turn. (You see all cards that player could see
///    and make all decisions for them.)"
///
/// ## Implemented (v1)
/// - Card identity (Legendary Artifact, mana cost {6}, owner / controller).
/// - <b>{4}, {T}, Sacrifice Mindslaver: control target player next turn</b>
///   — <see cref="ActivatedAbility"/> with <see cref="ManaCostCost"/>("{4}")
///   + <see cref="AdditionalCost"/>.Tap + <see cref="AdditionalCost"/>.Sacrifice.
///   A 1..1 <see cref="TargetRequest"/> for "target player" is declared.
///   On resolution the factory reads
///   <see cref="ActivatedAbility.ChosenTargets"/> and confirms the choice
///   is a <see cref="Player"/>; the Mindslaver self-sac is performed by
///   the effect closure (mirrors Aether Spellbomb — the generic
///   <see cref="AdditionalCost.Pay"/> sacrifice path is a stub).
///
/// ## Deferred (v1 gaps — known)
/// - <b>Mind-control of a player</b>: CR 720 ("Controlling Another Player")
///   — the engine has no primitive for swapping a player's decision-
///   making agent for one turn. The resolution closure records the
///   intended target via the optional <paramref name="mindControlSink"/>
///   callback (caller-supplied — typically a test hook or future
///   ControlPlayer service shim) and sacrifices Mindslaver, but the
///   actual turn-substitution does not happen. This is the same gap
///   listed for the unimplemented Emrakul, the Promised End (cast
///   trigger "you control target opponent during that player's next
///   turn") — when a ControlPlayer primitive lands, both cards switch
///   over to it. Until then Mindslaver is shape-only on the mind-
///   control half: the cost is paid, the target is recorded, the
///   artifact is sacrificed, but no turn is taken over.
/// </summary>
[CardName("Mindslaver")]
public static class MindslaverFactory
{
    public const string CardName = "Mindslaver";
    public const string Cost = "{6}";

    /// <summary>
    /// Construct Mindslaver with no live runtime wiring. The activated
    /// ability is attached for shape observability; resolution sacrifices
    /// Mindslaver and records the chosen target (if any) but no mind-
    /// control primitive runs.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, mindControlSink: null);

    /// <summary>
    /// Construct Mindslaver. When <paramref name="mindControlSink"/> is
    /// supplied, the activated ability's resolution closure invokes it
    /// with the chosen target player (after Mindslaver has been
    /// sacrificed). The sink is typically a test hook or a future
    /// ControlPlayer service shim — see the class xmldoc gap note.
    /// </summary>
    public static Artifact Create(
        Player owner,
        Action<Player>? mindControlSink)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var slaver = new Artifact(
            CardName,
            Cost,
            supertypes: new[] { CardSupertype.Legendary });
        slaver.SetOwner(owner);
        slaver.SetController(owner);

        // ----------------------------------------------------------------
        // {4}, {T}, Sacrifice Mindslaver: You control target player
        // during that player's next turn.
        // ----------------------------------------------------------------
        ActivatedAbility? mindControlAbility = null;
        var mindControlEffect = new Effect(
            $"{CardName}: control target player next turn + sac self",
            () =>
            {
                // Self-sac stub — perform the zone move so visible state
                // matches CR 701.16. Done first so the target-player
                // record is independent of Mindslaver's zone.
                if (slaver.Zone == ZoneType.Battlefield)
                {
                    owner.Zones.Battlefield.RemoveCard(slaver);
                    owner.Zones.Graveyard.AddCard(slaver);
                    slaver.SetZone(ZoneType.Graveyard);
                }

                // Resolve target.
                if (mindControlAbility == null) return;
                if (mindControlAbility.ChosenTargets.Count == 0) return;
                if (mindControlAbility.ChosenTargets[0].Count == 0) return;
                if (mindControlAbility.ChosenTargets[0][0] is not Player targetPlayer)
                    return; // CR 608.2b — illegal target → no-op

                // v1 gap — no ControlPlayer primitive. Record the intent
                // via the sink (test hook / future service shim).
                mindControlSink?.Invoke(targetPlayer);
            });

        mindControlAbility = new ActivatedAbility(
            source: slaver,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{4}"),
                AdditionalCost.Tap(slaver),
                AdditionalCost.Sacrifice(slaver),
            },
            effects: new IEffect[] { mindControlEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target player",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        slaver.AddAbility(mindControlAbility);

        return slaver;
    }
}
