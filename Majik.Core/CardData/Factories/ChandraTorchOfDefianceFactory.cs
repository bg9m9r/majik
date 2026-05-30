using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Chandra, Torch of Defiance (Kaladesh, {2}{R}{R}).
///
/// Legendary Planeswalker — Chandra. Starting loyalty 4.
/// Oracle text (Scryfall, verified):
///   "+1: Exile the top card of your library. You may cast that card. If you
///        don't, Chandra deals 2 damage to each opponent.
///    +1: Add {R}{R}.
///    −3: Chandra deals 4 damage to target creature.
///    −7: You get an emblem with 'Whenever you cast a spell, this emblem deals
///         5 damage to any target.'"
///
/// The base shape (name, Legendary Planeswalker — Chandra, {2}{R}{R}, loyalty
/// 4) is materialised from the embedded JSON definition
/// (<c>chandra-torch-of-defiance.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The four loyalty abilities are
/// layered on here — the JSON <c>AbilityDefinition</c> schema doesn't express
/// loyalty abilities, exile-cast clauses, damage, or emblems, so they live in
/// the factory (same posture as <see cref="TeferiHeroOfDominariaFactory"/> and
/// <see cref="UginEyeOfTheStormsFactory"/>).
///
/// ## Implemented (v1)
/// - <b>+1 (impulse): Exile the top card of your library. You may cast that
///   card. If you don't, Chandra deals 2 damage to each opponent
///   (CR 606 + CR 401 + CR 119)</b>: exiles the top card of the controller's
///   library. The "you may cast that card" choice is auto-resolved in v1 to
///   <em>decline</em> (the engine has no cast-from-exile-during-resolution
///   primitive — see "Deferred"), so the "if you don't" rider always fires:
///   Chandra deals 2 damage to each opponent (every player in
///   <paramref name="allPlayersResolver"/> other than the controller) via
///   <see cref="Fx.DealDamageAny"/>. With an empty library the clause no-ops
///   (CR 608.2c — no card to exile, so the optional cast and its rider don't
///   resolve). Without a player resolver the exile still happens; the
///   each-opponent damage no-ops.
/// - <b>+1 (ritual): Add {R}{R} (CR 606 + CR 106.4)</b>: adds two red mana to
///   the controller's mana pool via <see cref="Player.AddManaToPool"/>.
/// - <b>−3: Chandra deals 4 damage to target creature (CR 606 + CR 119)</b>:
///   deals 4 damage to the first creature from
///   <paramref name="targetCreatureResolver"/> via
///   <see cref="Fx.DealDamageAny"/>. Without a resolver the clause no-ops.
/// - <b>−7: emblem with "Whenever you cast a spell, this emblem deals 5 damage
///   to any target" (CR 606 + CR 114 + CR 603.1 + CR 119)</b>: mints a
///   structural <see cref="Emblem"/> in the controller's command zone. When
///   <paramref name="triggers"/> is wired, the emblem carries a
///   <see cref="TriggeredAbility"/> over <see cref="SpellCastEvent"/> gated to
///   spells the emblem's controller casts; its effect deals 5 damage to the
///   "any target" supplied by <paramref name="anyTargetResolver"/> via
///   <see cref="Fx.DealDamageAny"/> (routes Player / Creature / Planeswalker).
///   Structural-only without the trigger service (matches Teferi −8 posture).
///
/// ## Deferred (v1 gaps)
/// - <b>+1 "you may cast that card"</b>: casting a card from exile as part of
///   a resolving ability requires a cast-during-resolution decision surface
///   (agent prompt + a recursive SpellCaster hop mid-resolution). The engine
///   has no such primitive yet — same queue as Light Up the Stage / impulsive
///   draw. v1 deterministically declines, which always triggers the
///   "if you don't" 2-damage-to-each-opponent rider. The exiled card stays in
///   exile (the "until end of turn" / cast window is not modelled).
/// - <b>Target prompts</b>: <see cref="LoyaltyAbility"/> and the emblem's
///   triggered ability don't declare
///   <see cref="Majik.Core.Targeting.TargetRequest"/>s; the −3 creature target
///   and the emblem's "any target" are picked from the supplied resolvers
///   rather than via the agent. Same gap Teferi / Karn / Liliana share.
/// </summary>
[CardName("Chandra, Torch of Defiance")]
public static class ChandraTorchOfDefianceFactory
{
    public const string CardName = "Chandra, Torch of Defiance";
    public const string Slug = "chandra-torch-of-defiance";
    public const int StartingLoyalty = 4;
    public const int Plus1OpponentDamage = 2;
    public const int Minus3Damage = 4;
    public const int UltimateLoyaltyCost = -7;
    public const int EmblemDamage = 5;
    private const string RitualMana = "{R}{R}";

    /// <summary>
    /// Construct Chandra with no resolvers / triggers wired — the +1 impulse
    /// exiles the top card but the each-opponent damage no-ops, the +1 ritual
    /// adds {R}{R}, −3 no-ops, and −7 mints a structural-only emblem. Loyalty
    /// changes still apply. Suitable for shape / dispatcher tests. This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Planeswalker Create(Player owner) =>
        Create(owner, allPlayersResolver: null, targetCreatureResolver: null,
            anyTargetResolver: null, triggers: null);

    /// <summary>
    /// Construct Chandra, Torch of Defiance.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="allPlayersResolver">Returns the full player list for the +1
    /// impulse's "2 damage to each opponent" rider (every player other than the
    /// controller). May be null — the rider no-ops.</param>
    /// <param name="targetCreatureResolver">Returns candidate creatures for the
    /// −3 "4 damage to target creature" clause. v1 picks the first. May be null
    /// — the clause no-ops.</param>
    /// <param name="anyTargetResolver">Returns the "any target" (Player /
    /// Creature / Planeswalker) the −7 emblem's cast-trigger deals 5 to. May be
    /// null — the emblem effect no-ops.</param>
    /// <param name="triggers">TriggerManager used to register the −7 emblem's
    /// spell-cast trigger. May be null — the emblem is structural-only.</param>
    public static Planeswalker Create(
        Player owner,
        Func<IReadOnlyList<Player>>? allPlayersResolver,
        Func<IReadOnlyList<Creature>>? targetCreatureResolver,
        Func<object?>? anyTargetResolver,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary
        // Planeswalker — Chandra, {2}{R}{R}, loyalty 4). The JSON carries no
        // abilities — the four loyalty abilities are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var chandra = (Planeswalker)CardDefinitionFactory.Build(definition, owner);

        // -- +1 (impulse): Exile the top card of your library. You may cast
        //    that card. If you don't, Chandra deals 2 damage to each opponent.
        // CR 606 (loyalty) + CR 401 (library top) + CR 119 (damage). v1
        // declines the optional cast (no cast-from-exile-during-resolution
        // primitive — see class xmldoc), so the "if you don't" rider always
        // resolves: 2 damage to each opponent.
        chandra.AddAbility(new LoyaltyAbility(chandra, +1, () =>
        {
            var controller = chandra.Controller ?? owner;

            var top = controller.Zones.Library.GetCards().FirstOrDefault();
            if (top == null) return; // empty library — nothing to exile (CR 608.2c)

            // Exile the top card (CR 701.21).
            controller.Zones.Library.RemoveCard(top);
            controller.Zones.Exile.AddCard(top);
            top.SetZone(ZoneType.Exile);

            // v1: decline the cast → "if you don't, deal 2 to each opponent".
            var players = allPlayersResolver?.Invoke();
            if (players == null) return;
            foreach (var p in players)
            {
                if (ReferenceEquals(p, controller)) continue; // "each opponent"
                Fx.DealDamageAny(p, Plus1OpponentDamage);
            }
        }));

        // -- +1 (ritual): Add {R}{R}. -------------------------------------------
        // CR 606 (loyalty) + CR 106.4 (mana into the controller's pool).
        chandra.AddAbility(new LoyaltyAbility(chandra, +1, () =>
        {
            var controller = chandra.Controller ?? owner;
            controller.AddManaToPool(ManaCost.Parse(RitualMana));
        }));

        // -- −3: Chandra deals 4 damage to target creature. --------------------
        // CR 606 (loyalty) + CR 119 (damage). v1 picks the first creature from
        // the resolver (no agent target prompt yet — see class xmldoc).
        chandra.AddAbility(new LoyaltyAbility(chandra, -3, () =>
        {
            var candidates = targetCreatureResolver?.Invoke();
            if (candidates == null) return;
            foreach (var creature in candidates)
            {
                if (creature == null) continue;
                if (creature.Zone != ZoneType.Battlefield) continue;
                Fx.DealDamageAny(creature, Minus3Damage);
                return; // "target creature" — a single creature.
            }
        }));

        // -- −7: You get an emblem with "Whenever you cast a spell, this emblem
        //    deals 5 damage to any target." ------------------------------------
        // CR 606 (loyalty) + CR 114 (emblem) + CR 603.1 (whenever-trigger) +
        // CR 119 (damage). When the trigger service is wired the emblem carries
        // a SpellCastEvent trigger gated to spells the emblem controller casts.
        // Structural-only on the no-triggers path (matches Teferi −8).
        chandra.AddAbility(new LoyaltyAbility(chandra, UltimateLoyaltyCost, () =>
        {
            var controller = chandra.Controller ?? owner;

            // Build the emblem's abilities up-front — Emblem snapshots its
            // Abilities at construction (CR 114), so the trigger must exist
            // before the emblem is minted.
            var emblemAbilities = new List<IAbility>();

            if (triggers != null)
            {
                var burnEffect = new Effect(
                    $"{CardName} emblem: deal {EmblemDamage} damage to any target",
                    () =>
                    {
                        var target = anyTargetResolver?.Invoke();
                        if (target == null) return;
                        Fx.DealDamageAny(target, EmblemDamage);
                    });

                // "Whenever you cast a spell" — gate to spells cast by the
                // emblem's controller (CR 603.1). The spell's controller is the
                // caster of the spell's source card.
                var castAbility = new TriggeredAbility(
                    source: chandra,
                    controller: controller,
                    condition: new EventTriggerCondition<SpellCastEvent>(
                        (e, _) => ReferenceEquals(e.Spell.Card.Controller, controller)),
                    effects: new IEffect[] { burnEffect });

                emblemAbilities.Add(castAbility);
                triggers.RegisterTriggeredAbility(castAbility);
            }

            var emblem = new Emblem(
                controller: controller,
                sourceName: $"{CardName} — cast-burn emblem",
                abilities: emblemAbilities);
            controller.AddEmblem(emblem);
        }));

        return chandra;
    }
}
