using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Goblin Sledder (Onslaught, {R}).
///
/// Creature — Goblin 1/1. Oracle text:
///   "Sacrifice a Goblin: Target creature gets +1/+1 until end of turn."
///
/// ## Implemented (v1)
/// - 1/1 Creature — Goblin, mana cost {R}, owner/controller wired.
/// - <b>Activated ability (CR 602)</b>: "Sacrifice a Goblin: Target
///   creature gets +1/+1 until end of turn." Wired as an
///   <see cref="ActivatedAbility"/> whose sole cost is a deterministic
///   "sacrifice a Goblin you control" payment (mirrors
///   <see cref="SkirkProspectorFactory"/>'s sac-a-Goblin gate). The
///   oracle has no "another" qualifier — Sledder itself is a legal
///   sacrifice (canonical goblin-aggro line: sledder eats sledder when
///   it's the only Goblin left). A single 1..1 "target creature"
///   <see cref="TargetRequest"/> is declared; on resolution the chosen
///   creature gets a Layer 7c <see cref="PumpUntilEndOfTurnEffect"/>(+1, +1)
///   via the supplied <see cref="ContinuousEffectsService"/> (no-op when
///   the target has no <see cref="Creature.ActiveEffects"/> service —
///   shape-only test path).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — card shape only. The activated
///   ability is attached; pump effect is a no-op without
///   <see cref="Creature.ActiveEffects"/> on the chosen target.
/// - The sac-a-Goblin cost is a custom <see cref="SacrificeAGoblinCost"/>
///   (defined here, not exported) — it picks deterministically: prefer
///   another Goblin first, fall back to self. Same heuristic as
///   <see cref="SkirkProspectorFactory"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Agent-driven sacrifice picker</b>: the cost picks the first
///   non-self Goblin available, falling back to self. Optimal play
///   ("save Sledder for last") is approximated, not agent-driven. Same
///   gap as Skirk Prospector.
/// </summary>
[CardName("Goblin Sledder")]
public static class GoblinSledderFactory
{
    public const string CardName = "Goblin Sledder";
    public const string PrintedManaCost = "{R}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Goblin Sledder. Activated ability attached; resolution
    /// is a no-op for callers that don't attach a
    /// <see cref="ContinuousEffectsService"/> to the chosen target.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Goblin });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // "Sacrifice a Goblin: Target creature gets +1/+1 until end of
        // turn." — CR 602 activated ability (NOT a mana ability — uses
        // the stack).
        // ----------------------------------------------------------------
        ActivatedAbility? pumpAbility = null;
        var pumpEffect = new Effect(
            $"{CardName}: target creature gets +1/+1 until end of turn",
            () =>
            {
                if (pumpAbility == null) return;
                if (pumpAbility.ChosenTargets.Count == 0) return;
                if (pumpAbility.ChosenTargets[0].Count == 0) return;
                if (pumpAbility.ChosenTargets[0][0] is not Creature target) return;

                // CR 608.2b — resolution-time legality. Target must still
                // be a creature on the battlefield.
                if (target.Zone != ZoneType.Battlefield) return;

                // CR 613.1f / Layer 7c. ActiveEffects null = shape-only
                // test path; pump silently no-ops in that case.
                target.ActiveEffects?.Register(
                    new PumpUntilEndOfTurnEffect(target, 1, 1));
            });

        pumpAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new SacrificeAGoblinCost(card, owner) },
            effects: new IEffect[] { pumpEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Buff),
            });

        card.AddAbility(pumpAbility);

        return card;
    }
}

/// <summary>
/// "Sacrifice a Goblin" additional cost — controller must have at least
/// one Goblin on the battlefield (includes self per oracle: no "another"
/// qualifier). Pays by sacrificing one Goblin via raw-zone manipulation,
/// preferring another Goblin first and falling back to self when self is
/// the only candidate. Mirrors the deterministic v1 picker used by
/// <see cref="SkirkProspectorFactory"/>'s mana-ability cost.
/// </summary>
public sealed class SacrificeAGoblinCost : ICost
{
    private readonly Creature _self;
    private readonly Player _controller;

    /// <summary>The Goblin actually sacrificed once <see cref="Pay"/>
    /// succeeded. Null before payment. Exposed for downstream effects /
    /// tests that want to inspect the chosen sacrifice.</summary>
    public Creature? Sacrificed { get; private set; }

    public SacrificeAGoblinCost(Creature self, Player controller)
    {
        _self = self ?? throw new ArgumentNullException(nameof(self));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
    }

    public string Description => "Sacrifice a Goblin";

    public bool CanPay(Player player)
    {
        if (player == null) return false;
        if (!ReferenceEquals(player, _controller) &&
            !ReferenceEquals(player, _self.Controller))
        {
            return false;
        }

        var ctrl = _self.Controller ?? _controller;
        return ctrl.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Any(c => c.HasSubtype(CardSubtype.Goblin));
    }

    public void Pay(Player player)
    {
        if (!CanPay(player))
        {
            throw new InvalidOperationException(
                "Cannot pay Sacrifice a Goblin: no Goblin on the battlefield.");
        }

        var ctrl = _self.Controller ?? _controller;

        // Deterministic v1: prefer another Goblin first; fall back to self.
        Creature? pick = ctrl.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .FirstOrDefault(c =>
                c.HasSubtype(CardSubtype.Goblin) && !ReferenceEquals(c, _self))
            ?? ctrl.Zones.Battlefield.GetCards()
                .OfType<Creature>()
                .FirstOrDefault(c => c.HasSubtype(CardSubtype.Goblin));

        if (pick == null)
        {
            throw new InvalidOperationException(
                "Sacrifice a Goblin: no Goblin found at payment time.");
        }

        ctrl.Zones.Battlefield.RemoveCard(pick);
        ctrl.Zones.Graveyard.AddCard(pick);
        pick.SetZone(ZoneType.Graveyard);
        Sacrificed = pick;
    }
}
