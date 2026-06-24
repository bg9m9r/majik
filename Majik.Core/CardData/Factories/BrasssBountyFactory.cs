using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Tokens;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Brass's Bounty (Rivals of Ixalan, {6}{R}).
///
/// Sorcery. Oracle text (verified against Scryfall 2026-06-24):
///   "For each land you control, create a Treasure token. (It's an artifact
///    with "{T}, Sacrifice this token: Add one mana of any color.")"
///
/// ## Why a named factory
/// "For each land you control, create a Treasure token" is a count-based mint:
/// N = number of lands the caster controls (CR 109.5 / 305.1 — "lands you
/// control"), and the engine creates that many Treasure tokens (CR 111.10).
/// All primitives already exist — the land count is a battlefield filter
/// (same posture as <see cref="ScapeshiftFactory"/>'s "lands you control"
/// filter) and the token mint reuses <see cref="TokenFactory.CreateTreasure"/>
/// (the same Treasure primitive <see cref="TreasureVaultFactory"/> and
/// <see cref="StrikeItRichFactory"/> use). No new engine mechanic is required;
/// it's a count-N loop over an existing token-creation effect.
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {6}{R}, red. Card shape comes from the embedded
///   JSON (<c>brasss-bounty.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - Resolve: count the caster's controlled lands on the battlefield, mint that
///   many Treasure tokens via <see cref="TokenFactory.CreateTreasure"/>,
///   threading the optional <see cref="ZoneService"/> so each token's ETB
///   <see cref="Majik.Core.Events.CardMovedEvent"/> fires.
///
/// ## Rules citations
/// - CR 109.5 / CR 305.1 — "each land you control" counts permanents with the
///   land card type the caster controls (lands animated into other types still
///   count while they have the land type; we filter on
///   <see cref="ICard.HasType"/>(<see cref="CardType.Land"/>)).
/// - CR 111.10 — each Treasure is a colourless artifact token with
///   "{T}, Sacrifice this token: Add one mana of any color."
/// - CR 608.2 — the count is evaluated as the spell resolves; lands that left
///   play before resolution don't contribute.
/// </summary>
[CardName(CardName)]
public static class BrasssBountyFactory
{
    public const string CardName = "Brass's Bounty";
    public const string Slug = "brasss-bounty";
    public const string PrintedManaCost = "{6}{R}";

    /// <summary>
    /// Build a Brass's Bounty sorcery owned and controlled by
    /// <paramref name="owner"/>. Card shape only — wire the resolve closure via
    /// <see cref="BuildResolveEffect"/> / <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> for Brass's Bounty. No targets,
    /// no modes, no variable X — a pure on-resolve count-and-mint effect.
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(Player caster, ZoneService? zones = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => BuildResolveEffect(caster, zones));
    }

    /// <summary>
    /// Build Brass's Bounty's resolve effect: count the lands
    /// <paramref name="caster"/> controls on the battlefield (CR 305.1) and mint
    /// that many Treasure tokens (CR 111.10). When <paramref name="zones"/> is
    /// supplied each Treasure enters via <see cref="ZoneService"/> so its ETB
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> fires; otherwise the token
    /// is placed directly on the battlefield (shape / dispatcher-test path).
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster, ZoneService? zones = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            new Effect($"{CardName}: create a Treasure for each land you control.", () =>
            {
                var controller = caster;

                // CR 608.2 — evaluate "each land you control" as the spell
                // resolves. CR 305.1 — a land is any permanent the caster
                // controls with the land card type.
                var landCount = controller.Zones.Battlefield.GetCards()
                    .Count(c => c.HasType(CardType.Land)
                                && ReferenceEquals(c.Controller, controller));

                // CR 111.10 — mint one colourless artifact Treasure token per
                // controlled land. The Treasure primitive
                // (TokenFactory.CreateTreasure) carries the full
                // "{T}, Sacrifice this token: Add one mana of any color." spec.
                for (var i = 0; i < landCount; i++)
                {
                    TokenFactory.CreateTreasure(controller, zones);
                }
            }),
        };
    }
}
