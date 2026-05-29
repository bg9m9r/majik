using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for An Offer You Can't Refuse (Streets of New Capenna, {U}).
///
/// Instant. Oracle text:
///   "Counter target noncreature spell. Its controller creates two Treasure
///    tokens. (They're artifacts with "{T}, Sacrifice this token: Add one
///    mana of any color.")"
///
/// ## Scryfall identity
/// <list type="bullet">
///   <item>Set: Streets of New Capenna (snc)</item>
///   <item>Mana cost: {U}; mana value 1</item>
///   <item>Type line: Instant; colors: U</item>
/// </list>
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {U} (blue). The card shape is loaded from the
///   embedded JSON definition (<c>an-offer-you-cant-refuse.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
///   <see cref="CardDefinitionFactory"/> — same posture as the other
///   data-backed factories.
/// - Resolve-time <see cref="SpellDefinition"/> (via
///   <see cref="BuildSpellDefinition"/>) declares one 1..1 "target noncreature
///   spell" request. On resolution the target is countered via
///   <see cref="OracleSpellBinder.RemoveFromStack"/> + graveyard zone-move
///   (CR 701.5), then the countered spell's CONTROLLER creates two Treasure
///   tokens via <see cref="TokenFactory.CreateTreasure"/> (CR 111.10).
/// - Noncreature gate: at resolution, if the target spell has type Creature
///   (<see cref="CardType.Creature"/>) the effect does nothing — neither
///   counter nor Treasures (CR 608.2b). Same defensive posture as
///   <see cref="NegateFactory"/>: the filter is applied at resolve time
///   rather than at choose-time.
///
/// ## Rules citations
/// - CR 701.5 — Counter: remove from stack to graveyard.
/// - CR 608.2b — illegal target at resolution → that part of the effect does
///   nothing.
/// - CR 111.10 — Treasure token: colourless artifact with "{T}, Sacrifice
///   this token: Add one mana of any color."
/// </summary>
[CardName("An Offer You Can't Refuse")]
public static class AnOfferYouCantRefuseFactory
{
    public const string CardName = "An Offer You Can't Refuse";
    public const string Slug = "an-offer-you-cant-refuse";

    /// <summary>
    /// Build the card shape from the embedded JSON definition. Behaviour
    /// (counter + Treasures) is supplied at resolution via
    /// <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(definition, owner);
    }

    /// <summary>
    /// Build the "counter target noncreature spell; its controller creates two
    /// Treasures" SpellDefinition.
    /// CR 608.2b: if the chosen target is a creature spell at resolution time,
    /// the effect does nothing (illegal target — the spell remains on the
    /// stack and no Treasures are created).
    /// </summary>
    /// <param name="targetResolver">Target resolver from the caller's
    /// <see cref="GameContext"/> (chosen → live stack object).</param>
    /// <param name="stack">Live stack — required to remove the countered
    /// spell. Null in pure-shape tests; the effect becomes a no-op.</param>
    /// <param name="zoneService">Zone service used to route each Treasure's
    /// battlefield ETB through <see cref="ZoneService"/> so
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> publishes. Null → direct
    /// zone move (shape-only tests).</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack,
        ZoneService? zoneService)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target noncreature spell", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect(
                        "An Offer You Can't Refuse — counter target noncreature spell; " +
                        "its controller creates two Treasure tokens.",
                        () =>
                    {
                        if (stack == null || resolved is not ISpell spell) return;

                        // CR 608.2b — if the target has become a creature spell
                        // by resolution time, the whole effect does nothing for
                        // it: no counter, and (because the spell was not
                        // countered) no Treasures.
                        if (spell.Card.HasType(CardType.Creature)) return;

                        // CR 701.5 — counter the noncreature spell.
                        OracleSpellBinder.RemoveFromStack(stack, spell);
                        spell.Card.SetZone(ZoneType.Graveyard);

                        // CR 111.10 — the countered spell's CONTROLLER creates
                        // two Treasure tokens (not the Offer's caster).
                        var controller = spell.Controller;
                        TokenFactory.CreateTreasure(controller, zoneService);
                        TokenFactory.CreateTreasure(controller, zoneService);
                    }),
                };
            });
    }
}
