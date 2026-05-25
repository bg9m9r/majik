using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Night's Whisper (Fifth Dawn, {1}{B}).
///
/// Sorcery. Oracle text:
///   "You draw two cards and you lose 2 life."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {1}{B}.
/// - No targets. Single-clause oracle text — both the draw and the life
///   loss resolve as one event from a single <see cref="IEffect"/>, in
///   that printed order (CR 608.2 — resolve a spell's effects in the
///   order they appear). The life loss is not gated on the draw count;
///   even an empty library still triggers the 2-life payment because the
///   printed conjunction is "and", not a conditional rider (mirrors paper
///   Night's Whisper — the controller takes 2 unconditionally).
/// - Empty library: draw loop short-circuits per card and flags the
///   player for the state-based draw-from-empty penalty (CR 704.5b) via
///   <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/>. Life loss
///   still runs.
///
/// ## Skipped — Divination intentionally NOT added
/// Divination ("Draw two cards.") is already covered by the generic
/// <c>DrawCardsTemplate</c> in the spell-template registry. Adding a
/// named factory would be a strict duplicate of the template binding —
/// the template's <c>ResourceSpellFactory.DrawNSpell</c> produces the
/// identical resolve body. See <c>OracleSpellBinderTests.Draw_NCards_BuildsDrawSpell</c>.
/// </summary>
[CardName("Night's Whisper")]
public static class NightsWhisperFactory
{
    public const string CardName = "Night's Whisper";
    public const string PrintedManaCost = "{1}{B}";

    /// <summary>Printed life cost paid as part of the spell's resolution.</summary>
    public const int LifePaid = 2;

    /// <summary>Printed card draw count.</summary>
    public const int CardsDrawn = 2;

    /// <summary>
    /// Build a Night's Whisper sorcery owned by <paramref name="owner"/>.
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
    /// Build the resolve-time <see cref="SpellDefinition"/>. No targets,
    /// no modes — a single effect draws two cards then deducts two life
    /// from the controller, in that printed order.
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => new IEffect[]
            {
                new Effect("Night's Whisper: draw 2, lose 2 life.", () =>
                {
                    DrawN(caster, CardsDrawn);
                    caster.LoseLife(LifePaid);
                }),
            });
    }

    // Simple top-of-library draw loop. Empty library short-circuits the
    // remaining iterations and marks the player for the SBA-driven loss
    // (CR 704.5b) — same shape every other resource-template draw uses.
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
