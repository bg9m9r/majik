using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Mana;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Strix Serenade (Bloomburrow, {U}).
///
/// Instant. Oracle text:
///   "Counter target artifact, creature, or planeswalker spell. Its controller
///    creates a 2/2 blue Bird creature token with flying."
///
/// ## Scryfall identity
/// <list type="bullet">
///   <item>Mana cost: {U}; mana value 1</item>
///   <item>Type line: Instant; colors: U (blue)</item>
/// </list>
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {U} (blue). The card shape is loaded from the
///   embedded JSON definition (<c>strix-serenade.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
///   <see cref="CardDefinitionFactory"/> — same posture as
///   <see cref="AnOfferYouCantRefuseFactory"/>.
/// - Resolve-time <see cref="SpellDefinition"/> (via
///   <see cref="BuildSpellDefinition"/>) declares one 1..1 "target artifact,
///   creature, or planeswalker spell" request. On resolution the target is
///   countered via <see cref="OracleSpellBinder.RemoveFromStack"/> + graveyard
///   zone-move (CR 701.5), then the countered spell's CONTROLLER creates a
///   2/2 blue Bird creature token with flying (CR 111.4).
/// - Type gate: at resolution, if the target spell is NOT an artifact, creature,
///   or planeswalker spell, the effect does nothing — neither counter nor token
///   (CR 608.2b). Same defensive posture as An Offer / Negate: the filter is
///   applied at resolve time rather than at choose-time.
///
/// ## Rules citations
/// - CR 701.5 — Counter: remove from stack to graveyard.
/// - CR 608.2b — illegal target at resolution → that part of the effect does
///   nothing (no counter, and therefore no token).
/// - CR 111.4 — token characteristics: 2/2 blue Bird with flying. Token colour
///   is stamped explicitly (tokens have no mana cost) via
///   <see cref="TokenFactory.TokenSpec.Colors"/>.
/// </summary>
[CardName("Strix Serenade")]
public static class StrixSerenadeFactory
{
    public const string CardName = "Strix Serenade";
    public const string Slug = "strix-serenade";

    /// <summary>
    /// Build the card shape from the embedded JSON definition. Behaviour
    /// (counter + Bird token) is supplied at resolution via
    /// <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(definition, owner);
    }

    /// <summary>
    /// Build the "counter target artifact/creature/planeswalker spell; its
    /// controller creates a 2/2 blue Bird with flying" SpellDefinition.
    /// CR 608.2b: if the chosen target is not an artifact, creature, or
    /// planeswalker spell at resolution time, the effect does nothing (illegal
    /// target — the spell remains on the stack and no token is created).
    /// </summary>
    /// <param name="targetResolver">Target resolver from the caller's
    /// <see cref="GameContext"/> (chosen → live stack object).</param>
    /// <param name="stack">Live stack — required to remove the countered
    /// spell. Null in pure-shape tests; the effect becomes a no-op.</param>
    /// <param name="zoneService">Zone service used to route the Bird token's
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
                new TargetRequest(
                    "target artifact, creature, or planeswalker spell", 1, 1,
                    Array.Empty<object>()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect(
                        "Strix Serenade — counter target artifact, creature, or " +
                        "planeswalker spell; its controller creates a 2/2 blue " +
                        "Bird creature token with flying.",
                        () =>
                    {
                        if (stack == null || resolved is not ISpell spell) return;

                        // CR 608.2b — if the target is not an artifact, creature,
                        // or planeswalker spell by resolution time, the whole
                        // effect does nothing for it: no counter, and (because
                        // the spell was not countered) no Bird token.
                        var card = spell.Card;
                        var isLegal = card.HasType(CardType.Artifact)
                            || card.HasType(CardType.Creature)
                            || card.HasType(CardType.Planeswalker);
                        if (!isLegal) return;

                        // CR 701.5 — counter the spell.
                        OracleSpellBinder.RemoveFromStack(stack, spell);
                        card.SetZone(ZoneType.Graveyard);

                        // CR 111.4 — the countered spell's CONTROLLER creates a
                        // 2/2 blue Bird token with flying (not the Serenade's
                        // caster).
                        var controller = spell.Controller;
                        var spec = new TokenFactory.TokenSpec(
                            Name: "Bird",
                            Power: 2,
                            Toughness: 2,
                            Subtypes: new[] { CardSubtype.Bird },
                            Keywords: new[] { "Flying" },
                            Colors: new[] { ManaColor.Blue });
                        TokenFactory.CreateOnBattlefield(spec, controller, zoneService);
                    }),
                };
            });
    }
}
