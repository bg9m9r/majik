using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Refute (Modern Horizons 3, {1}{U}{U}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Counter target spell. Draw a card, then discard a card."
///
/// ## Why a named factory
/// Refute is two already-supported halves stitched together with no "unless
/// pays" rider and no type filter:
///   1. <b>Hard counter</b> — the vanilla "Counter target spell" of
///      <see cref="CancelFactory"/> (any spell on the stack is a legal target;
///      removed from the stack and sent to the graveyard, CR 701.5).
///   2. <b>Loot</b> — "Draw a card, then discard a card", the 1-for-1 form of
///      <see cref="IzzetCharmFactory"/>'s draw-then-discard body.
/// No single spell template binds the two together, so it gets a named factory.
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {1}{U}{U}, blue. Card shape comes from the
///   embedded JSON (<c>refute.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - A single resolve-time effect ordering the counter then the loot (CR 608.2c
///   — the spell's instructions resolve in printed order).
///
/// ## Deferred
/// - Real agent-driven "choose a card to discard" prompt — v1 discards the
///   last card in hand deterministically (same queue as Faithless Looting /
///   Izzet Charm / Connive).
/// </summary>
[CardName("Refute")]
public static class RefuteFactory
{
    public const string CardName = "Refute";
    public const string Slug = "refute";
    public const string PrintedManaCost = "{1}{U}{U}";

    /// <summary>Construct Refute as an Instant owned by <paramref name="owner"/>.</summary>
    public static Cards.Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Cards.Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/> for Refute:
    /// "Counter target spell. Draw a card, then discard a card."
    /// </summary>
    /// <param name="caster">Refute's controller — draws + discards.</param>
    /// <param name="targetResolver">Resolves the raw target token to a live engine object.</param>
    /// <param name="stack">Active stack; required to remove the countered spell.</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                // Any spell on the stack is a legal target (no type filter).
                new TargetRequest("target spell", 1, 1, Array.Empty<object>(), BotIntent.Counter),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    // CR 608.2c — the spell's instructions resolve in printed
                    // order: counter first, then loot.
                    new Effect("Refute — counter target spell, then loot", () =>
                    {
                        // 1. Counter target spell (CR 701.5).
                        if (stack != null && resolved is ISpell spell)
                        {
                            if (OracleSpellBinder.RemoveFromStack(stack, spell))
                            {
                                spell.Card.SetZone(ZoneType.Graveyard);
                            }
                        }

                        // 2. "Draw a card, then discard a card." (CR 121.1 / 701.16)
                        //    Empty-library draw flags SBA loss (CR 704.5b).
                        var top = caster.Zones.Library.GetCards().FirstOrDefault();
                        if (top == null)
                        {
                            caster.MarkTriedToDrawFromEmptyLibrary();
                        }
                        else
                        {
                            caster.Zones.Library.RemoveCard(top);
                            caster.Zones.Hand.AddCard(top);
                            top.SetZone(ZoneType.Hand);
                        }

                        // v1 deterministic last-in-hand discard (prompt deferred,
                        // same queue as Izzet Charm / Faithless Looting).
                        var pick = caster.Zones.Hand.GetCards().LastOrDefault();
                        if (pick != null)
                        {
                            caster.Zones.Hand.RemoveCard(pick);
                            caster.Zones.Graveyard.AddCard(pick);
                            pick.SetZone(ZoneType.Graveyard);
                        }
                    }),
                };
            });
    }
}
