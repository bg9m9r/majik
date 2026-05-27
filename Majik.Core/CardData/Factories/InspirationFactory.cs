using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Inspiration (Tempest / various, {3}{U}).
///
/// Instant. Oracle text:
///   "Target player draws two cards."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {3}{U}, blue, mana value 4.
/// - One target request — "target player" (min 1 / max 1,
///   CR 601.2c). The target may be either player.
/// - Resolve effect: the TARGET player draws two cards via direct
///   top-of-library moves (CR 121.1). Empty library mid-draw marks the
///   player for the SBA-driven loss (CR 704.5b) via
///   <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/> and
///   short-circuits the remaining draw.
/// - No life-loss clause (contrast Sign in Blood's "{C}{C}" hybrid rider).
///
/// Pattern mirrors <see cref="ArchmagesCharmFactory"/>'s draw-mode
/// (BuildDrawEffect) for the target-player-draws-two shape, simplified
/// to a single-mode non-modal instant.
/// </summary>
[CardName("Inspiration")]
public static class InspirationFactory
{
    public const string CardName = "Inspiration";
    public const string PrintedManaCost = "{3}{U}";

    /// <summary>Build an Inspiration Instant owned by <paramref name="owner"/>.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the SpellDefinition for Inspiration.
    ///
    /// Single target request (target player, 1..1). At resolution the
    /// TARGET player draws two cards top-of-library (CR 121.1). If the
    /// library is exhausted mid-draw the player is marked for the SBA
    /// loss (CR 704.5b) and drawing stops.
    /// </summary>
    /// <param name="targetResolver">
    /// Resolves raw target tokens to live engine objects (pass <c>o =&gt; o</c>
    /// in tests that supply direct references).
    /// </param>
    public static SpellDefinition BuildSpellDefinition(Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                // CR 601.2c — one player target, required.
                new TargetRequest("target player", 1, 1, Array.Empty<object>(), BotIntent.Draw),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect("Inspiration — target player draws two cards", () =>
                    {
                        if (resolved is not Player target) return;

                        // CR 121.1 — two top-of-library draws.
                        // CR 704.5b — empty library mid-draw sets SBA flag.
                        for (var i = 0; i < 2; i++)
                        {
                            var top = target.Zones.Library.GetCards().FirstOrDefault();
                            if (top == null)
                            {
                                target.MarkTriedToDrawFromEmptyLibrary();
                                break;
                            }
                            target.Zones.Library.RemoveCard(top);
                            target.Zones.Hand.AddCard(top);
                            top.SetZone(ZoneType.Hand);
                        }
                    }),
                };
            });
    }
}
