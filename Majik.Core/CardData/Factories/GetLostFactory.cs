using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Get Lost (The Lost Caverns of Ixalan, {1}{W}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Destroy target creature, enchantment, or planeswalker. Its controller
///    creates two Map tokens. (They're artifacts with "{1}, {T}, Sacrifice this
///    token: Target creature you control explores. Activate only as a
///    sorcery.")"
///
/// ## Scryfall identity
/// <list type="bullet">
///   <item>Set: The Lost Caverns of Ixalan (lci)</item>
///   <item>Mana cost: {1}{W}; mana value 2; type Instant; colour W</item>
/// </list>
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {1}{W} (white). The card shape is loaded from the
///   embedded JSON definition (<c>get-lost.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> — same posture as
///   <see cref="AnOfferYouCantRefuseFactory"/> (counter + two Treasures), the
///   closest structural analogue ("kill the target, the TARGET'S controller
///   makes two tokens").
/// - Resolve-time <see cref="SpellDefinition"/> (via
///   <see cref="BuildSpellDefinition"/>): one 1..1 "target creature,
///   enchantment, or planeswalker" request (the <c>creature_enchantment_or_planeswalker</c>
///   <see cref="TargetFilters"/> predicate is the candidate gatherer AND the
///   CR 608.2b resolution re-check). On resolution:
///     1. CR 701.7 — destroy the target via
///        <see cref="Fx.MoveToGraveyard(ICard, ZoneMoveReason)"/> with
///        <see cref="ZoneMoveReason.Destroy"/> (Indestructible / regeneration
///        gates apply).
///     2. CR 111.10 — the destroyed permanent's CONTROLLER (captured before the
///        destroy zone-move, CR 608.2g last-known information) creates two Map
///        tokens via <see cref="TokenFactory.CreateMap"/>. Each Map carries its
///        own sorcery-speed "{1}, {T}, Sacrifice this token: Target creature you
///        control explores" ability (CR 701.40).
/// - CR 608.2b — an illegal target at resolution (no longer a creature /
///   enchantment / planeswalker on the battlefield) fizzles the WHOLE effect:
///   no destroy, no Maps (mirrors <see cref="AnOfferYouCantRefuseFactory"/>).
/// </summary>
[CardName("Get Lost")]
public static class GetLostFactory
{
    public const string CardName = "Get Lost";
    public const string Slug = "get-lost";
    public const string Filter = "creature_enchantment_or_planeswalker";

    /// <summary>
    /// Build the card shape from the embedded JSON definition. Behaviour
    /// (destroy + two Maps) is supplied at resolution via
    /// <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(definition, owner);
    }

    /// <summary>
    /// Build the "destroy target creature/enchantment/planeswalker; its
    /// controller creates two Maps" SpellDefinition. CR 608.2b — if the chosen
    /// target is illegal at resolution the whole effect does nothing.
    /// </summary>
    /// <param name="targetResolver">Target resolver from the caller's
    /// <see cref="GameContext"/> (chosen → live object). Pass <c>o =&gt; o</c>
    /// on the declarative cast path.</param>
    /// <param name="zoneService">Zone service for the destroy move + the Maps'
    /// battlefield ETB so <c>CardMovedEvent</c> publishes. Null → direct zone
    /// move (shape-only tests); the Maps still mint.</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> targetResolver,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                TargetFilters.ToTargetRequest(Filter, "destroy", BotIntent.Removal),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets.Count > 0 && p.Targets[0].Count > 0
                    ? p.Targets[0][0]
                    : null;
                var resolved = raw is null ? null : targetResolver(raw);

                return new IEffect[]
                {
                    new Effect(
                        "Get Lost — destroy target creature, enchantment, or " +
                        "planeswalker; its controller creates two Map tokens.",
                        () =>
                    {
                        // CR 608.2b — re-check the SAME printed filter at
                        // resolution; an illegal target fizzles the whole effect.
                        if (resolved is not Permanent target) return;
                        if (target.Zone != ZoneType.Battlefield) return;
                        if (!TargetFilters.Matches(Filter, target)) return;

                        // CR 608.2g — capture the controller BEFORE the destroy
                        // zone-move (last-known information for "its controller").
                        var controller = target.Controller;

                        // CR 701.7 — destroy (Indestructible / regeneration gate).
                        Fx.MoveToGraveyard(target, ZoneMoveReason.Destroy);

                        // CR 111.10 — the destroyed permanent's controller makes
                        // two Map tokens.
                        if (controller != null)
                        {
                            TokenFactory.CreateMap(controller, zoneService);
                            TokenFactory.CreateMap(controller, zoneService);
                        }
                    }),
                };
            });
    }
}
