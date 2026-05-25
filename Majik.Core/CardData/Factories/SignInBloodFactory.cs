using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sign in Blood (Magic 2010, {B}{B}).
///
/// Sorcery. Oracle text:
///   "Target player draws two cards and loses 2 life."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {B}{B}.
/// - One 1..1 "target player" request. Either player is a legal target
///   (CR 115.6 — "target player" with no restriction); Sign in Blood is
///   one of the cards that can be aimed at yourself or your opponent.
/// - On resolution the targeted player draws two then loses two life,
///   in that printed order, from a single <see cref="IEffect"/>
///   (CR 608.2 — resolve effects in oracle order).
/// - CR 608.2b illegal-target check: if the targeted player has left the
///   game (or the resolver returns a non-Player object), the effect
///   no-ops cleanly.
/// - Empty library: per-card short-circuit + flag for the SBA-driven
///   draw-from-empty loss (CR 704.5b). Life loss still runs — the
///   printed "and" is unconditional.
/// </summary>
[CardName("Sign in Blood")]
public static class SignInBloodFactory
{
    public const string CardName = "Sign in Blood";
    public const string PrintedManaCost = "{B}{B}";

    /// <summary>Printed life cost charged to the targeted player.</summary>
    public const int LifeLost = 2;

    /// <summary>Printed card draw count granted to the targeted player.</summary>
    public const int CardsDrawn = 2;

    /// <summary>
    /// Build a Sign in Blood sorcery owned by <paramref name="owner"/>.
    /// Card shape only; the resolve-time effect ships via
    /// <see cref="BuildSpellDefinition"/>.
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
    /// Build the resolve-time <see cref="SpellDefinition"/>. Single
    /// 1..1 "target player" request; on resolution the resolved
    /// player draws two then loses two life.
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
                new TargetRequest("target player", 1, 1, Array.Empty<object>(), BotIntent.Draw),
            },
            EffectFactory: chosen => new IEffect[]
            {
                new Effect("Sign in Blood: target player draws 2 and loses 2 life.", () =>
                {
                    if (chosen.Targets.Count == 0 || chosen.Targets[0].Count == 0) return;
                    var raw = resolver(chosen.Targets[0][0]);

                    // CR 608.2b — illegal-target check at resolution. The
                    // cast flow filters most cases; guard defensively so
                    // a stale handle no-ops instead of throwing.
                    if (raw is not Player target) return;

                    DrawN(target, CardsDrawn);
                    target.LoseLife(LifeLost);
                }),
            });
    }

    private static void DrawN(Player player, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var top = player.Zones.Library.GetCards().FirstOrDefault();
            if (top == null)
            {
                player.MarkTriedToDrawFromEmptyLibrary();
                return;
            }
            player.Zones.Library.RemoveCard(top);
            player.Zones.Hand.AddCard(top);
            top.SetZone(ZoneType.Hand);
        }
    }
}
