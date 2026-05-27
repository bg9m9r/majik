using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Extirpate (Planar Chaos, <c>{B}</c>).
///
/// Instant. Oracle text:
///   "Split second (As long as this spell is on the stack, players can't
///    cast spells or activate abilities that aren't mana abilities.)
///    Choose target card in a graveyard other than a basic land card.
///    Search its owner's graveyard, hand, and library for any number of
///    cards with the same name as that card and exile them. Then that
///    player shuffles."
///
/// ## Implemented (v1)
/// - Instant {B} card shape with owner / controller wiring.
/// - <b>Split second</b> (CR 702.61) modelled as a
///   <see cref="KeywordAbility"/> marker ("Split second"). The full
///   restriction surface (preventing other spells / non-mana activated
///   abilities while the spell is on the stack) is enforced elsewhere
///   when the priority-manager learns to consult the marker — this
///   factory just declares the keyword on the card, matching the
///   project-wide convention for evergreen-style keyword markers (see
///   <c>LeylineOfCombustionFactory</c>, <c>TheOneRingFactory</c>).
/// - <see cref="BuildDefinition"/> wires the resolve effect:
///   * 1..1 TargetRequest restricted to cards in any graveyard that are
///     NOT basic land cards (CR 601.2c — illegal targets rewind the cast).
///   * EffectFactory rejects illegal targets defensively (non-graveyard,
///     basic land, missing owner) so an adversarial caller cannot bypass
///     the LegalCandidates filter.
///   * On resolve: sweep target owner's graveyard + hand + library for
///     every card with a matching name (case-insensitive), exile them
///     all, then shuffle the owner's library (CR 701.19c / 701.20a).
/// - Card-name match uses
///   <see cref="StringComparison.OrdinalIgnoreCase"/>; the target itself
///   is included in the sweep (it shares its own name).
///
/// ## Deferred (v1 gaps)
/// - Split second restriction enforcement on the stack — the marker is
///   present, but the priority-manager does not yet consult it. Tracked
///   alongside the broader keyword-restriction system.
/// </summary>
[CardName("Extirpate")]
public static class ExtirpateFactory
{
    public const string CardName = "Extirpate";
    public const string PrintedManaCost = "{B}";

    /// <summary>Construct the printed instant card with the Split second marker.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        // CR 702.61 — Split second declared as a keyword marker. The priority
        // manager will consult markers like this once split-second
        // restriction enforcement lands; for now the marker documents the
        // card's printed keyword and unblocks downstream wiring.
        card.AddAbility(new KeywordAbility("Split second", card, owner));
        return card;
    }

    /// <summary>
    /// Build the SpellDefinition. <paramref name="allGraveyardCards"/>
    /// supplies the legal-candidate set (every card in every graveyard,
    /// excluding basic lands). At resolution the chosen card's name is
    /// used to sweep the target's owner's graveyard, hand, and library.
    /// </summary>
    public static SpellDefinition BuildDefinition(
        IReadOnlyList<ICard> allGraveyardCards) =>
        new(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target card in a graveyard other than a basic land card",
                    MinTargets: 1, MaxTargets: 1,
                    LegalCandidates: allGraveyardCards
                        .Where(c => c.Zone == ZoneType.Graveyard
                                    && !IsBasicLand(c))
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: p =>
            {
                // EffectFactory runs synchronously inside SpellCastFlow.CastAsync
                // BEFORE the spell is pushed onto the stack — so throwing here
                // aborts the cast (CR 601.2c — illegal target = cast rewound).
                var rawTarget = p.Targets.Count > 0 && p.Targets[0].Count > 0
                    ? p.Targets[0][0]
                    : null;
                if (rawTarget is not ICard target)
                {
                    throw new InvalidOperationException(
                        "Extirpate requires a card target.");
                }
                if (target.Zone != ZoneType.Graveyard)
                {
                    throw new InvalidOperationException(
                        "Extirpate's target must be in a graveyard.");
                }
                if (IsBasicLand(target))
                {
                    throw new InvalidOperationException(
                        "Extirpate cannot target a basic land card.");
                }
                if (target.Owner == null)
                {
                    throw new InvalidOperationException(
                        "Extirpate's target has no owner.");
                }
                var targetOwner = target.Owner;
                var targetName = target.Name;

                return new IEffect[]
                {
                    new Effect(
                        $"Extirpate — exile all '{targetName}' from {targetOwner.Name}'s graveyard/hand/library + shuffle",
                        () =>
                        {
                            var sweep = new List<ICard>();
                            sweep.AddRange(targetOwner.Zones.Graveyard.GetCards()
                                .Where(c => string.Equals(c.Name, targetName, StringComparison.OrdinalIgnoreCase)));
                            sweep.AddRange(targetOwner.Zones.Hand.GetCards()
                                .Where(c => string.Equals(c.Name, targetName, StringComparison.OrdinalIgnoreCase)));
                            sweep.AddRange(targetOwner.Zones.Library.GetCards()
                                .Where(c => string.Equals(c.Name, targetName, StringComparison.OrdinalIgnoreCase)));

                            foreach (var card in sweep)
                            {
                                var from = card.Zone;
                                switch (from)
                                {
                                    case ZoneType.Graveyard:
                                        targetOwner.Zones.Graveyard.RemoveCard(card);
                                        break;
                                    case ZoneType.Hand:
                                        targetOwner.Zones.Hand.RemoveCard(card);
                                        break;
                                    case ZoneType.Library:
                                        targetOwner.Zones.Library.RemoveCard(card);
                                        break;
                                }
                                targetOwner.Zones.Exile.AddCard(card);
                                card.SetZone(ZoneType.Exile);
                            }

                            // CR 701.19c — shuffle the searched library.
                            ShuffleLibrary(targetOwner);
                        }),
                };
            });

    private static bool IsBasicLand(ICard card) =>
        card.HasType(CardType.Land) && card.HasSupertype(CardSupertype.Basic);

    /// <summary>
    /// CR 701.20a — library shuffle via the shared primitive.
    /// </summary>
    private static void ShuffleLibrary(Player player)
    {
        Majik.Core.Zones.LibraryShuffle.ShuffleLibrary(player, "extirpate");
    }
}
