using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Staggershock (Rise of the Eldrazi, {2}{R}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Staggershock deals 2 damage to any target.
///    Rebound (If you cast this spell from your hand, exile it as it
///    resolves. At the beginning of your next upkeep, you may cast this
///    card from exile without paying its mana cost.)"
///
/// Staggershock composes two shapes the engine already supports as far as
/// v1 goes:
/// - The <b>burn body</b> is identical to <see cref="ShockFactory"/> /
///   <see cref="PlayWithFireFactory"/> — a single 1..1 "any target" request
///   that deals 2 damage via <see cref="Fx.DealDamageAny"/> (CR 115.3 —
///   "any target" = creature, player, planeswalker, or battle;
///   CR 120.3 / CR 306.7 — planeswalker damage becomes loyalty removal).
/// - The <b>Rebound rider</b> (CR 702.88) is attached only as a
///   <see cref="KeywordAbility"/>("Rebound") marker, matching the
///   <see cref="EphemerateFactory"/> convention.
///
/// Card shape comes from the embedded JSON (<c>staggershock.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/>. The resolve-time damage body lives
/// in <see cref="BuildSpellDefinition"/> because a
/// <see cref="SpellDefinition"/> needs a target resolver supplied by the
/// caller's <see cref="GameContext"/> (not expressible in the data-only
/// JSON schema).
///
/// ## Deferred (v1 gap)
/// - <b>Rebound mechanic</b> (CR 702.88): "If you cast this spell from your
///   hand, exile it as it resolves. At the beginning of your next upkeep,
///   you may cast this card from exile without paying its mana cost."
///   Requires (1) a cast-from-hand replacement that routes Stack → Exile
///   instead of Stack → Graveyard on resolution (CR 702.88a), and (2) a
///   delayed triggered ability registered on resolve that fires on the
///   controller's next upkeep and offers a free-cast prompt from exile
///   (CR 702.88b). Neither half exists as a reusable primitive today, so
///   the rider is deferred and only the keyword marker is attached — the
///   same posture as <see cref="EphemerateFactory"/> (the marker becomes
///   the wiring point once the "cast from exile without paying" primitive
///   lands). The damage body is shape-correct without Rebound.
/// </summary>
[CardName("Staggershock")]
public static class StaggershockFactory
{
    public const string CardName = "Staggershock";
    public const string Slug = "staggershock";
    public const string PrintedManaCost = "{2}{R}";

    /// <summary>CR 119 / CR 120.3 — fixed 2 damage to any target.</summary>
    public const int Damage = 2;

    /// <summary>
    /// Build the card shape from the embedded JSON definition and attach the
    /// Rebound keyword marker (CR 702.88 — rider deferred, see class xmldoc).
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Instant)CardDefinitionFactory.Build(def, owner);

        // CR 702.88 — Rebound marker. The actual rider (exile-on-resolve +
        // next-upkeep free cast from exile) is deferred; the marker is
        // attached so oracle audits / KeywordRegistry consumers detect the
        // keyword without inspecting the SpellDefinition shape.
        card.AddAbility(new KeywordAbility("Rebound", card, owner));

        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Staggershock is cast.
    /// Single 1..1 "any target" request, no X. On resolution deals
    /// <see cref="Damage"/> (2) damage to the chosen target through
    /// <see cref="Fx.DealDamageAny"/> (CR 120.3). The Rebound exile-on-resolve
    /// rider is NOT modelled at this surface (see class xmldoc gap).
    /// </summary>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("any target", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var target = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    Fx.Inline("Staggershock: 2 damage to any target", () =>
                        Fx.DealDamageAny(target, Damage)),
                };
            });
    }
}
