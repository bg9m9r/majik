using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Shizo, Death's Storehouse (Champions of Kamigawa,
/// Legendary Land).
///
/// Oracle text (Scryfall-confirmed):
///   "{T}: Add {B}.
///    {B}, {T}: Target legendary creature gains fear until end of turn.
///    (It can't be blocked except by artifact creatures and/or black
///    creatures.)"
///
/// ## Shape source
/// Structural near-twin of <see cref="SlayersStrongholdFactory"/> — a
/// mana-producing land carrying a single targeted activated ability that
/// grants a keyword until end of turn. Where Slayers' Stronghold pumps
/// +2/+0 and grants vigilance + haste to <i>target creature</i>, Shizo
/// grants a single keyword (fear) to <i>target legendary creature</i>.
///
/// ## Implemented (v1)
/// - <b>Legendary Land identity</b> — Legendary supertype + Land type,
///   materialised from the embedded JSON definition
///   (<c>shizo-deaths-storehouse.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory.Build(CardDefinition, Player)"/>.
/// - <b>{T}: Add {B}</b> — vanilla black <see cref="ManaAbility"/>
///   (CR 605.1), declared in the JSON.
/// - <b>{B}, {T}: Target legendary creature gains fear until end of
///   turn</b> — an <see cref="ActivatedAbility"/> (CR 602) with cost
///   <see cref="ManaCostCost"/>("{B}") + <see cref="AdditionalCost.Tap"/>
///   and a single 1..1 "target legendary creature" request. On resolution
///   it registers a Layer-6 <see cref="GrantKeywordUntilEndOfTurnEffect"/>
///   for "Fear" (CR 613.1c) against the chosen creature's own
///   <see cref="Creature.ActiveEffects"/> service. The grant expires in the
///   cleanup step (CR 514.2). Reuses the same primitive the Slayers'
///   Stronghold / Berserk / Legion Leadership grants use; the keyword set
///   is OrdinalIgnoreCase, so casing is irrelevant. Fear's combat semantics
///   ("can't be blocked except by artifact and/or black creatures",
///   CR 702.36) are handled by the combat system once the keyword is present.
///
/// ## v1 posture (CR 608.2b guards)
/// - No chosen target, an off-battlefield target, a non-Creature target, or a
///   target without a live continuous-effects service → documented no-op
///   (nothing happens; resolution does not throw). Same defence-in-depth
///   posture as Slayers' Stronghold.
///
/// The legendary-creature legality of the target is enforced at target
/// selection (the activated ability declares a 1..1 target request; the
/// shared targeting pipeline gathers legendary creatures). The resolve body
/// re-checks the target is a battlefield creature (CR 608.2b) before granting.
///
/// Adding this factory flips <c>IsImplemented</c> automatically via the
/// <see cref="ImplementedCardNames"/> registry — no seed regen needed.
/// </summary>
[CardName("Shizo, Death's Storehouse")]
public static class ShizoDeathsStorehouseFactory
{
    public const string CardName = "Shizo, Death's Storehouse";
    public const string Slug = "shizo-deaths-storehouse";

    /// <summary>Activation mana cost of the fear-grant ability — {B}.</summary>
    public const string GrantCost = "{B}";

    /// <summary>The keyword granted until end of turn (CR 702.36).</summary>
    public const string GrantedKeyword = "Fear";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Shizo, Death's Storehouse with no continuous-effects wiring
    /// (the <see cref="NamedCardFactory"/> dispatcher / shape path). Both
    /// abilities are attached so the card surface is complete; the fear grant
    /// is a documented no-op without a target / effects service.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name, Legendary Land, {T}: Add {B}) from the embedded JSON.
        var land = (Land)CardDefinitionFactory.Build(Definition, owner);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // {B}, {T}: Target legendary creature gains fear until end of turn.
        // CR 602 activated ability; CR 613.1c Layer-6 keyword grant;
        // CR 514.2 cleanup expiry. Same target-creature activated-ability
        // shape as SlayersStrongholdFactory; reuses the
        // GrantKeywordUntilEndOfTurnEffect primitive.
        // ----------------------------------------------------------------
        ActivatedAbility? grantAbility = null;
        var grantEffect = new Effect(
            $"{CardName}: target legendary creature gains fear until end of turn",
            () =>
            {
                if (grantAbility == null) return;
                if (grantAbility.ChosenTargets.Count == 0) return;
                if (grantAbility.ChosenTargets[0].Count == 0) return;
                if (grantAbility.ChosenTargets[0][0] is not Creature creature) return;

                // CR 608.2b — illegal target on resolution (left the
                // battlefield) → no-op. Defence-in-depth zone check.
                if (creature.Zone != Majik.Core.Zones.ZoneType.Battlefield) return;

                // Without a continuous-effects service on the target (shape-only
                // target) the grant simply isn't tracked — documented no-op.
                if (creature.ActiveEffects == null) return;

                // CR 613.1c — Layer-6 keyword grant until end of turn.
                creature.ActiveEffects.Register(
                    new GrantKeywordUntilEndOfTurnEffect(creature, GrantedKeyword));
            });

        grantAbility = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(GrantCost),
                AdditionalCost.Tap(land),
            },
            effects: new IEffect[] { grantEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target legendary creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.CombatTrick),
            });

        land.AddAbility(grantAbility);

        return land;
    }

    /// <summary>The {B}, {T} targeted fear-grant ability.</summary>
    public static ActivatedAbility GetGrantAbility(Land land)
    {
        ArgumentNullException.ThrowIfNull(land);
        return land.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 1);
    }
}
