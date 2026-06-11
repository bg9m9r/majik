using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Gisa's Bidding (Eldritch Moon, {2}{B}{B}).
///
/// Sorcery. Oracle text (verified against Scryfall 2026-06-10):
///   "Create two 2/2 black Zombie creature tokens.
///    Madness {2}{B}"
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {2}{B}{B}.
/// - No target requests — the effect resolves entirely on the caster.
/// - Resolve effect via <see cref="BuildResolveEffect"/>: calls
///   <see cref="TokenFactory.CreateOnBattlefield"/> twice, each with a
///   <see cref="TokenFactory.TokenSpec"/> for a 2/2 black Zombie creature
///   token (CR 111 / 111.4). Same posture as
///   <see cref="DragonFodderFactory"/> (two 1/1 red Goblins).
///
/// ## Madness {2}{B} — intrinsic, NOT wired here
/// Madness (CR 702.35) is handled engine-wide: when Gisa's Bidding is
/// discarded, the central discard funnel (<c>Fx.DiscardCard</c>) consults
/// <c>MadnessCatalog</c> (which lists "Gisa's Bidding" → {2}{B}) and routes
/// the card to exile with the option to cast it for its madness cost. No
/// per-card factory code is required for the madness line — only the
/// token-creating spell body above.
/// </summary>
[CardName("Gisa's Bidding")]
public static class GisasBiddingFactory
{
    public const string CardName = "Gisa's Bidding";
    public const string PrintedManaCost = "{2}{B}{B}";

    public const int TokenPower = 2;
    public const int TokenToughness = 2;

    /// <summary>
    /// Construct Gisa's Bidding as a Sorcery owned by <paramref name="owner"/>.
    /// Card shape only — the resolve closure is produced by
    /// <see cref="BuildResolveEffect"/> / <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> for Gisa's Bidding. No modes,
    /// no X, no target requests — the body resolves entirely on the caster.
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        ZoneService? zones = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => BuildResolveEffect(caster, zones));
    }

    /// <summary>
    /// Build the resolve effect: create two 2/2 black Zombie creature tokens
    /// on the caster's battlefield (CR 111 / 111.4).
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        ZoneService? zones = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: create two 2/2 black Zombie creature tokens.",
                () =>
                {
                    var spec = new TokenFactory.TokenSpec(
                        Name: "Zombie",
                        Power: TokenPower,
                        Toughness: TokenToughness,
                        Subtypes: new[] { CardSubtype.Zombie },
                        Keywords: null,
                        // CR 111.4 — printed "2/2 black Zombie creature token".
                        Colors: new[] { ManaColor.Black });

                    // Create two tokens (CR 111 — one token per "create").
                    TokenFactory.CreateOnBattlefield(spec, caster, zones);
                    TokenFactory.CreateOnBattlefield(spec, caster, zones);
                }),
        };
    }
}
