using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mirran Crusader (Mirrodin Besieged, {1}{W}{W}).
/// Creature — Human Knight 2/2. Oracle text (verified against Scryfall):
///   "Double strike, protection from black and from green"
///
/// The card's base shape (name, type, Human + Knight subtypes, {1}{W}{W},
/// 2/2) is materialised from the embedded JSON definition
/// (<c>mirran-crusader.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two printed static
/// keyword riders (Double strike, the two protections) are layered on top
/// here — the JSON <c>AbilityDefinition</c> schema doesn't yet express
/// keyword markers or protection qualities, so they live in the factory
/// (same posture as <see cref="StormscaleScionFactory"/> and the other
/// JSON-backed creatures whose behaviour outgrows the schema).
///
/// ## Implemented (v1)
/// - <b>Double strike (CR 702.4)</b> — wired as a <see cref="KeywordAbility"/>
///   marker so <see cref="Majik.Core.Combat.CombatAbilities.HasDoubleStrike"/>
///   surfaces the combat property (first-strike + regular-damage steps).
///   Same marker shape as Stormscale Scion's Flying.
/// - <b>Protection from black and from green (CR 702.16)</b> — two
///   <see cref="ProtectionAbility"/> markers attached directly to the
///   creature. Unlike the Sword-equipment riders (which re-project the
///   protection onto the equipped creature), Mirran Crusader's protection
///   is intrinsic and always-on, so the markers live on the creature
///   itself — the canonical shape that
///   <see cref="Majik.Core.Rules.Protection.HasProtectionFromColor"/> reads
///   (DEBT-A: can't be Damaged / Enchanted-Equipped / Blocked / Targeted by
///   anything black or green). Identical wiring shape to
///   <see cref="GoblinPiledriverFactory"/>'s intrinsic protection-from-blue
///   rider.
///
/// ## Deferred (v1 gaps)
/// - None. Mirran Crusader has no triggered or activated abilities — its
///   two clauses are both static evergreen keyword riders the engine
///   already models. The single-arg dispatcher path is fully wired (there
///   is no service-dependent behaviour to gate).
/// </summary>
[CardName("Mirran Crusader")]
public static class MirranCrusaderFactory
{
    public const string CardName = "Mirran Crusader";
    public const string Slug = "mirran-crusader";

    /// <summary>
    /// Construct Mirran Crusader. Fully wired — Double strike + the two
    /// protection riders are all static markers with no service
    /// dependency, so this single overload is also the
    /// <see cref="NamedCardFactory"/> dispatch target.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Human + Knight subtypes, {1}{W}{W}, 2/2). The JSON carries no
        // abilities — the keyword riders are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.4 — Double strike. KeywordAbility marker so
        // CombatAbilities.HasDoubleStrike surfaces the first-strike +
        // regular-damage combat behaviour.
        card.AddAbility(new KeywordAbility("Double strike", card, owner));

        // CR 702.16 — Protection from black and from green. Qualities stored
        // normalised; the Rules.Protection / TargetLegality / CombatAbilities
        // helpers interpret them (DEBT-A). Same intrinsic-marker shape as
        // Goblin Piledriver's protection-from-blue.
        card.AddAbility(new ProtectionAbility("black"));
        card.AddAbility(new ProtectionAbility("green"));

        return card;
    }
}
