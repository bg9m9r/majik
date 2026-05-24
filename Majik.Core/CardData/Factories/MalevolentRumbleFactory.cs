using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Malevolent Rumble (Modern Horizons 3, {1}{G}).
///
/// Sorcery. Oracle text (Scryfall, 2024-06-14 release):
///   "Reveal the top four cards of your library. You may put a permanent
///    card from among them into your hand. Put the rest into your
///    graveyard. Create a 0/1 colorless Eldrazi Spawn creature token with
///    \"Sacrifice this token: Add {C}.\""
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {1}{G}.
/// - Resolve effect (via <see cref="BuildResolveEffect"/>):
///     1. Peek up to top 4 cards of the caster's library (CR 701.15 reveal
///        is folded into the same peek — no <c>CardsRevealedEvent</c> fires
///        yet; same gap as Ancient Stirrings / Atraxa / Goblin Matron).
///     2. Pick the first permanent card (CR 110.1 — artifact, creature,
///        enchantment, land, planeswalker, battle) and move it to the
///        caster's hand.
///     3. Move every other peeked card to the caster's graveyard
///        (CR 701.4 — "put into your graveyard" from the library is a
///        zone change, not a mill, but it's mechanically identical here).
///     4. Create one 0/1 colorless Eldrazi Spawn creature token via
///        <see cref="TokenFactory.CreateEldraziSpawn"/> — the token's
///        "Sacrifice this token: Add {C}." ability is wired there as a
///        <see cref="ManaAbility"/> producing one colourless. Token
///        creation is unconditional: even with an empty library the
///        Spawn still enters the battlefield.
///
/// ## Why a named factory (template already ships)
/// The existing
/// <see cref="Majik.Core.CardData.SpellTemplates.Templates.Bespoke.MalevolentRumblePatternTemplate"/>
/// binds Malevolent Rumble at the data-driven layer via
/// <see cref="OracleSpellBinder"/>. This factory mirrors the template's
/// resolve body and exposes a card-shape constructor on the same plan as
/// <see cref="FaithlessLootingFactory"/> / <see cref="TribalFlamesFactory"/>:
/// tests and call sites that build cards directly (the bot's deck loader,
/// integration fixtures that bypass the Scryfall import) get a typed
/// <see cref="Sorcery"/> without round-tripping through a CardEntity.
/// Both code paths exercise the same downstream <see cref="TokenFactory"/>
/// so the live behaviour stays in sync — if the template's body changes,
/// this factory's <see cref="BuildResolveEffect"/> should track it.
///
/// ## Deferred (v1 gaps)
/// - "You may put …" is optional — v1 always picks the first permanent if
///   one is present (opt-out awaits the agent prompt system, same queue as
///   Dredger's Insight / Ancient Stirrings).
/// - Real player choice among the revealed permanents — v1 takes the first
///   permanent in library order (deterministic).
/// - No <c>CardsRevealedEvent</c> is published; no live observer cares
///   yet.
/// </summary>
[CardName("Malevolent Rumble")]
public static class MalevolentRumbleFactory
{
    public const string CardName = "Malevolent Rumble";
    public const string PrintedManaCost = "{1}{G}";

    /// <summary>
    /// Build a Malevolent Rumble sorcery owned by <paramref name="owner"/>.
    /// Card shape only — the resolve effect is built on demand via
    /// <see cref="BuildResolveEffect"/> so tests / integrations can splice
    /// it into a <see cref="Majik.Core.Game.SpellDefinition"/> or pass it
    /// directly to a <see cref="Majik.Core.Spells.Spell"/>.
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
    /// Build Malevolent Rumble's resolve effect — reveal top 4, first
    /// permanent (if any) to hand, rest to graveyard, then create one
    /// Eldrazi Spawn 0/1 token. Mirrors
    /// <see cref="Majik.Core.CardData.SpellTemplates.Templates.Bespoke.MalevolentRumblePatternTemplate"/>.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return new IEffect[]
        {
            new Effect(
                "Malevolent Rumble: reveal top 4, may put a permanent into hand, " +
                "rest into graveyard, then create a 0/1 colorless Eldrazi Spawn token.",
                () =>
                {
                    // Reveal top 4 (may be fewer if library is smaller —
                    // CR 121.2 / CR 701.15a: revealing N when the library
                    // has fewer than N is legal; the player reveals what's
                    // there).
                    var top4 = caster.Zones.Library.GetCards().Take(4).ToList();

                    if (top4.Count > 0)
                    {
                        // CR 110.1 — permanent card types. Battle is in the
                        // printed list but the engine's CardType enum
                        // predates MoM; the predicate walks the five types
                        // currently modelled. When Battle ships, add it
                        // here.
                        var permanentCard = top4.FirstOrDefault(c =>
                            c.HasType(CardType.Creature) ||
                            c.HasType(CardType.Artifact) ||
                            c.HasType(CardType.Enchantment) ||
                            c.HasType(CardType.Land) ||
                            c.HasType(CardType.Planeswalker));

                        foreach (var c in top4)
                        {
                            caster.Zones.Library.RemoveCard(c);
                            if (ReferenceEquals(c, permanentCard))
                            {
                                caster.Zones.Hand.AddCard(c);
                                c.SetZone(ZoneType.Hand);
                            }
                            else
                            {
                                caster.Zones.Graveyard.AddCard(c);
                                c.SetZone(ZoneType.Graveyard);
                            }
                        }
                    }

                    // Token creation is unconditional — not gated on
                    // library size. Even an empty library yields a Spawn.
                    TokenFactory.CreateEldraziSpawn(caster);
                }),
        };
    }
}
