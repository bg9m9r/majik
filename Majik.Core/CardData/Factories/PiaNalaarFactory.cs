using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Pia Nalaar (Kaladesh, {2}{R}).
///
/// Legendary Creature — Human Artificer 2/2. Oracle text (Scryfall, verified):
///   "When Pia Nalaar enters, create a 1/1 colorless Thopter artifact
///    creature token with flying.
///    {1}{R}: Target artifact creature gets +1/+0 until end of turn.
///    {1}, Sacrifice an artifact: Target creature can't block this turn."
///
/// Modern Boros / artifact-aggro support — a 3-mana 2/2 legend that prints a
/// flyer on entry then pumps your artifact creatures or shoves blockers out
/// of the way by sacrificing artifact fodder. Every ability shape it needs is
/// an existing engine primitive: the ETB Thopter token mirrors
/// <see cref="WhirlerVirtuosoFactory"/>'s token spec, the targeted +1/+0 pump
/// reuses <see cref="PumpUntilEndOfTurnEffect"/> (the Blinkmoth Nexus / Berserk
/// pump primitive), the "can't block" rider reuses
/// <see cref="CombatRestrictionEffect"/> with <see cref="CombatRestriction.CannotBlock"/>
/// (the Earthshaker Khenra primitive), and the sac-cost reuses
/// <see cref="SacrificeAnArtifactCost"/> (Arcbound Ravager / Voltage Surge).
///
/// ## Implemented (v1)
///
/// - 2/2 <see cref="Creature"/> — Legendary (CR 205.4a), Human Artificer, mana
///   cost {2}{R}. Owner / controller wired.
/// - <b>ETB triggered ability</b> (CR 603.6a): "When Pia Nalaar enters, create
///   a 1/1 colorless Thopter artifact creature token with flying." Wired via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/>; on resolution
///   <see cref="TokenFactory.CreateOnBattlefield"/> mints a 1/1 colourless
///   <see cref="CardSubtype.Thopter"/> creature token with Flying (CR 702.9),
///   then additively stamps <see cref="CardType.Artifact"/> so the token reports
///   Artifact + Creature — Thopter (CR 111.1). Identical shell to Whirler
///   Virtuoso's Thopter.
/// - <b>{1}{R} activated ability</b> (CR 602.1): "Target artifact creature gets
///   +1/+0 until end of turn." Single 1..1 "target artifact creature"
///   <see cref="TargetRequest"/>; on resolution registers a
///   <see cref="PumpUntilEndOfTurnEffect"/>(p:1, t:0) on the chosen creature's
///   <see cref="Creature.ActiveEffects"/> (CR 613.7c; CR 514.2 cleanup expiry).
///   Defends in depth at resolution: still-on-battlefield + still-an-artifact
///   recheck (CR 608.2b illegal-target → no-op).
/// - <b>{1}, Sacrifice an artifact activated ability</b> (CR 602.1 / 118.5):
///   "Target creature can't block this turn." Costs
///   <see cref="ManaCostCost"/>("{1}") + <see cref="SacrificeAnArtifactCost"/>;
///   single 1..1 "target creature" request. On resolution registers a
///   <see cref="CombatRestrictionEffect"/>(<see cref="CombatRestriction.CannotBlock"/>)
///   scoped to the chosen creature with default end-of-turn expiry — matching
///   the printed "this turn" rider (CR 509.1c; CR 514.2).
///
/// ## Deferred (v1 gaps)
///
/// - <b>Live TriggerManager / ZoneService wiring</b>: single-arg dispatcher
///   path; the ETB trigger is attached structurally for shape inspection (no
///   <see cref="TriggerManager"/> registration) and the Thopter token enters
///   via the no-<see cref="ZoneService"/> branch of
///   <see cref="TokenFactory.CreateOnBattlefield"/> — so no
///   <see cref="Majik.Core.Events.CardMovedEvent"/> publishes for the token.
///   Same posture as <see cref="WhirlerVirtuosoFactory.Create(Player)"/>.
/// - <b>"Target artifact creature" / "target creature" choose-time legality</b>:
///   the <see cref="TargetRequest.LegalCandidates"/> list is left empty (the
///   <see cref="TargetRequest.CandidateGatherer"/> supplies the live legal pool
///   for the agent prompt). The resolution closures re-validate legality
///   (CR 608.2b), so a target that stops being a legal artifact creature
///   between choose and resolve fizzles cleanly. Same posture as Earthshaker
///   Khenra / Blinkmoth Nexus.
/// </summary>
[CardName("Pia Nalaar")]
public static class PiaNalaarFactory
{
    public const string CardName = "Pia Nalaar";
    public const string PrintedManaCost = "{2}{R}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>Pump activation cost — {1}{R}.</summary>
    public const string PumpCost = "{1}{R}";

    /// <summary>"Can't block" activation mana cost — {1} (plus Sacrifice an artifact).</summary>
    public const string CantBlockManaCost = "{1}";

    public const string ThopterTokenName = "Thopter";
    public const int ThopterPower = 1;
    public const int ThopterToughness = 1;

    /// <summary>
    /// Construct Pia Nalaar owned and controlled by <paramref name="owner"/>.
    /// Single-arg dispatcher path: the ETB Thopter trigger and both activated
    /// abilities are attached to the card shape. The targeted pump / can't-block
    /// resolution closures register against the chosen creature's own
    /// <see cref="Creature.ActiveEffects"/> handle; when that handle is null
    /// (shape-only tests) the grant silently no-ops.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Human, CardSubtype.Artificer });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When Pia Nalaar enters, create a 1/1 colorless Thopter artifact
        //    creature token with flying."
        // Identical Thopter shell to Whirler Virtuoso: mint a 1/1 colourless
        // Thopter creature token with Flying, then additively stamp Artifact
        // (CR 111.1 — Thopter tokens are artifact creatures; the token shell
        // is Creature-only).
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: create 1/1 colourless Thopter token (flying)",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return; // CR 603.6c
                var controller = card.Controller ?? owner;

                var spec = new TokenFactory.TokenSpec(
                    Name: ThopterTokenName,
                    Power: ThopterPower,
                    Toughness: ThopterToughness,
                    Subtypes: new[] { CardSubtype.Thopter },
                    Keywords: new[] { "Flying" },
                    Colors: Array.Empty<ManaColor>());

                var token = TokenFactory.CreateOnBattlefield(spec, controller);
                token.AddCardType(CardType.Artifact);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);

        // ----------------------------------------------------------------
        // {1}{R}: Target artifact creature gets +1/+0 until end of turn.
        // CR 602.1 activated ability; CR 613.7c Layer-7c pump; CR 514.2 expiry.
        // Reuses the PumpUntilEndOfTurnEffect primitive (Blinkmoth Nexus /
        // Berserk). Distinguished from the can't-block ability by its single
        // ManaCostCost (no SacrificeAnArtifactCost).
        // ----------------------------------------------------------------
        ActivatedAbility? pumpAbility = null;
        var pumpEffect = new Effect(
            $"{CardName}: target artifact creature gets +1/+0 until end of turn",
            () =>
            {
                if (pumpAbility == null) return;
                if (pumpAbility.ChosenTargets.Count == 0) return;
                if (pumpAbility.ChosenTargets[0].Count == 0) return;
                if (pumpAbility.ChosenTargets[0][0] is not Creature target) return;

                // CR 608.2b — recheck legality at resolution: still on the
                // battlefield and still an artifact creature.
                if (target.Zone != ZoneType.Battlefield) return;
                if (!target.HasType(CardType.Artifact)) return;

                if (target.ActiveEffects == null) return; // shape-only — no-op
                target.ActiveEffects.Register(
                    new PumpUntilEndOfTurnEffect(target, p: 1, t: 0));
            });

        pumpAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(PumpCost) },
            effects: new IEffect[] { pumpEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target artifact creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.CombatTrick,
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Where(c => c.HasType(CardType.Artifact))
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(pumpAbility);

        // ----------------------------------------------------------------
        // {1}, Sacrifice an artifact: Target creature can't block this turn.
        // CR 602.1 activated ability; CR 118.5 (sacrifice as a cost);
        // CR 509.1c CannotBlock restriction, CR 514.2 end-of-turn expiry.
        // SacrificeAnArtifactCost is the Arcbound Ravager / Voltage Surge
        // primitive (source eligible — Pia herself is NOT an artifact, so the
        // cost only ever picks a real artifact). CombatRestrictionEffect is
        // the Earthshaker Khenra primitive.
        // ----------------------------------------------------------------
        ActivatedAbility? cantBlockAbility = null;
        var cantBlockEffect = new Effect(
            $"{CardName}: target creature can't block this turn",
            () =>
            {
                if (cantBlockAbility == null) return;
                if (cantBlockAbility.ChosenTargets.Count == 0) return;
                if (cantBlockAbility.ChosenTargets[0].Count == 0) return;
                if (cantBlockAbility.ChosenTargets[0][0] is not Creature target) return;

                if (target.Zone != ZoneType.Battlefield) return; // CR 608.2b

                if (target.ActiveEffects == null) return; // shape-only — no-op
                target.ActiveEffects.Register(
                    new CombatRestrictionEffect(CombatRestriction.CannotBlock, target));
            });

        cantBlockAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(CantBlockManaCost),
                new SacrificeAnArtifactCost(),
            },
            effects: new IEffect[] { cantBlockEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(cantBlockAbility);

        return card;
    }

    /// <summary>The {1}{R} targeted +1/+0 pump ability (no sacrifice cost).</summary>
    public static ActivatedAbility GetPumpAbility(Creature card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return card.Abilities.OfType<ActivatedAbility>()
            .Single(a => !a.Costs.OfType<SacrificeAnArtifactCost>().Any());
    }

    /// <summary>The {1}, Sacrifice an artifact: target creature can't block ability.</summary>
    public static ActivatedAbility GetCantBlockAbility(Creature card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return card.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<SacrificeAnArtifactCost>().Any());
    }
}
