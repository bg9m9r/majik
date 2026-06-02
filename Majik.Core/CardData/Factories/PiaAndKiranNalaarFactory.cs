using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Pia and Kiran Nalaar (Magic Origins, {2}{R}{R}).
///
/// Legendary Creature — Human Artificer 2/2. Oracle text (Scryfall, verified):
///   "When Pia and Kiran Nalaar enters, create two 1/1 colorless Thopter
///    artifact creature tokens with flying.
///    {2}{R}, Sacrifice an artifact: Pia and Kiran Nalaar deals 2 damage to
///    any target."
///
/// The parents' "big" version of <see cref="PiaNalaarFactory"/> — same Boros
/// artifact-aggro shell, but it prints <i>two</i> flyers on entry and turns
/// any artifact (Thopters included) into 2 reach damage. Every ability shape
/// it needs is an existing engine primitive, so the only card-specific code
/// is wiring; the rest is data.
///
/// Loads <c>Majik.Core/CardData/Cards/pia-and-kiran-nalaar.json</c> for the
/// 2/2 Legendary Human Artificer identity and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card; the ETB
/// trigger + sac-an-artifact damage ability are layered on here because the
/// JSON <c>AbilityDefinition</c> schema doesn't express token-minting ETB
/// triggers or sacrifice-cost any-target damage abilities yet (same posture
/// as <see cref="IntiSeneschalOfTheSunFactory"/> /
/// <see cref="OrnithopterOfParadiseFactory"/>).
///
/// ## Implemented (v1)
///
/// - 2/2 <see cref="Creature"/> — Legendary (CR 205.4a), Human Artificer,
///   mana cost {2}{R}{R}, built from the JSON definition.
/// - <b>ETB triggered ability</b> (CR 603.6a): "When Pia and Kiran Nalaar
///   enters, create two 1/1 colorless Thopter artifact creature tokens with
///   flying." Wired via <see cref="Triggers.OnEnterBattlefieldSelf"/>; on
///   resolution <see cref="TokenFactory.CreateOnBattlefield"/> mints two 1/1
///   colourless <see cref="CardSubtype.Thopter"/> creature tokens with Flying
///   (CR 702.9), each additively stamped <see cref="CardType.Artifact"/> so it
///   reports Artifact + Creature — Thopter (CR 111.1). Identical Thopter shell
///   to <see cref="PiaNalaarFactory"/> / Whirler Virtuoso, doubled.
/// - <b>{2}{R}, Sacrifice an artifact activated ability</b> (CR 602.1 /
///   CR 118.5): "Pia and Kiran Nalaar deals 2 damage to any target." Costs
///   <see cref="ManaCostCost"/>("{2}{R}") + <see cref="SacrificeAnArtifactCost"/>;
///   a single 1..1 "any target" <see cref="TargetRequest"/>. On resolution the
///   closure reads <see cref="ActivatedAbility.ChosenTargets"/> and routes
///   through <see cref="Fx.DealDamageAny"/> (Player → life loss CR 119.3,
///   Creature → marked damage CR 120.3, Planeswalker → loyalty removal
///   CR 306.7) — the same any-target damage primitive as Pyrite Spellbomb /
///   Lightning Bolt. Illegal-on-resolution targets fail silently (CR 608.2b).
///   Pia and Kiran is NOT an artifact, so the default
///   <see cref="SacrificeAnArtifactCost"/> (no exclude) only ever picks a real
///   artifact (a Thopter token, fodder, etc.).
///
/// ## Deferred (v1 gaps)
///
/// - <b>Live TriggerManager / ZoneService wiring</b>: single-arg dispatcher
///   path; the ETB trigger is attached structurally for shape inspection (no
///   <see cref="TriggerManager"/> registration) and the Thopter tokens enter
///   via the no-<see cref="ZoneService"/> branch of
///   <see cref="TokenFactory.CreateOnBattlefield"/> — so no
///   <see cref="Majik.Core.Events.CardMovedEvent"/> publishes for the tokens.
///   Same posture as <see cref="PiaNalaarFactory.Create(Player)"/>.
/// - <b>"Any target" choose-time legality</b>: the
///   <see cref="TargetRequest.LegalCandidates"/> list is left empty; the live
///   any-target pool is supplied by the activating agent / candidate gatherer,
///   and the resolution closure re-validates via <see cref="Fx.DealDamageAny"/>
///   (CR 608.2b). Same posture as Pyrite Spellbomb.
/// </summary>
[CardName("Pia and Kiran Nalaar")]
public static class PiaAndKiranNalaarFactory
{
    public const string CardName = "Pia and Kiran Nalaar";

    /// <summary>The sac-an-artifact damage ability's mana portion — {2}{R}.</summary>
    public const string DamageManaCost = "{2}{R}";

    /// <summary>Damage dealt by the sacrifice ability.</summary>
    public const int DamageAmount = 2;

    public const string ThopterTokenName = "Thopter";
    public const int ThopterPower = 1;
    public const int ThopterToughness = 1;
    public const int ThopterCount = 2;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("pia-and-kiran-nalaar");

    /// <summary>
    /// Construct Pia and Kiran Nalaar owned and controlled by
    /// <paramref name="owner"/>. The 2/2 Legendary Human Artificer identity
    /// comes from the JSON definition; the ETB two-Thopter trigger and the
    /// {2}{R},Sacrifice-an-artifact 2-damage ability are layered on here.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When Pia and Kiran Nalaar enters, create two 1/1 colorless
        //    Thopter artifact creature tokens with flying."
        // Identical Thopter shell to Pia Nalaar / Whirler Virtuoso, doubled:
        // mint two 1/1 colourless Thopter creature tokens with Flying, then
        // additively stamp Artifact (CR 111.1 — Thopter tokens are artifact
        // creatures; the token shell is Creature-only).
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: create two 1/1 colourless Thopter tokens (flying)",
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

                for (var i = 0; i < ThopterCount; i++)
                {
                    var token = TokenFactory.CreateOnBattlefield(spec, controller);
                    token.AddCardType(CardType.Artifact);
                }
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);

        // ----------------------------------------------------------------
        // {2}{R}, Sacrifice an artifact: Pia and Kiran Nalaar deals 2 damage
        // to any target.
        // CR 602.1 activated ability; CR 118.5 (sacrifice as a cost). The
        // any-target damage routes through Fx.DealDamageAny so Player /
        // Creature / Planeswalker targets each take the right shape of damage
        // (CR 119.3 / 120.3 / 306.7). Pia herself is NOT an artifact, so the
        // default SacrificeAnArtifactCost only ever picks a real artifact.
        // ----------------------------------------------------------------
        ActivatedAbility? damageAbility = null;
        var damageEffect = new Effect(
            $"{CardName}: deal {DamageAmount} damage to any target",
            () =>
            {
                if (damageAbility == null) return;
                if (damageAbility.ChosenTargets.Count == 0) return;
                if (damageAbility.ChosenTargets[0].Count == 0) return;

                var target = damageAbility.ChosenTargets[0][0];
                Fx.DealDamageAny(target, DamageAmount); // CR 608.2b — gated per shape
            });

        damageAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(DamageManaCost),
                new SacrificeAnArtifactCost(),
            },
            effects: new IEffect[] { damageEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "any target",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(damageAbility);

        return card;
    }
}
