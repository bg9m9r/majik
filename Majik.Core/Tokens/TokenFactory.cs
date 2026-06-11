using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Mana;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Tokens;

/// <summary>
/// CR 111 — builds token creatures on the battlefield. The returned card
/// is marked <see cref="Permanent.IsToken"/> so SBA 704.5d removes it from
/// any zone other than the battlefield.
/// </summary>
public static class TokenFactory
{
    /// <summary>
    /// Token characteristics shape. CR 111.4 — the effect that creates a
    /// token enumerates its name, P/T, subtypes, colour, and any granted
    /// keyword abilities. Token colour is carried explicitly (not derived
    /// from a printed mana cost — tokens have none) and stamped on the
    /// resulting <see cref="Card.TokenColorsOverride"/> so
    /// <see cref="CardColors.GetColors"/> returns the correct colour set
    /// for protection / lord-style pumps / colour-matters triggers
    /// (CR 105 / CR 903.4).
    /// </summary>
    /// <param name="Colors">Explicit colour set. <c>null</c> defaults to
    /// "colourless" (an empty override is stamped so the token reports
    /// no colours regardless of any future cost-derived inference).
    /// Single-colour tokens pass a one-element list (<c>[ManaColor.White]</c>
    /// for Ocelot Pride's Cats); multi-colour tokens pass each colour
    /// (<c>[ManaColor.Blue, ManaColor.Red]</c> for Stormchaser's Talent's
    /// Mercenary).</param>
    public sealed record TokenSpec(
        string Name,
        int Power,
        int Toughness,
        IReadOnlyList<CardSubtype>? Subtypes = null,
        IReadOnlyList<string>? Keywords = null,
        IReadOnlyList<ManaColor>? Colors = null);

    /// <summary>Create a creature token and put it onto the battlefield under
    /// the given controller. Uses <see cref="ZoneService"/> when supplied so
    /// CardMovedEvent fires (triggers Soul Warden etc.). The token's colour
    /// identity (CR 105 / CR 111.4) is stamped from
    /// <see cref="TokenSpec.Colors"/>; callers should pass the printed
    /// colour explicitly, including an empty list for colourless tokens
    /// (Wurmcoil's Wurms, Karn Scion's Constructs).</summary>
    public static Creature CreateOnBattlefield(
        TokenSpec spec,
        Player controller,
        ZoneService? zones = null)
    {
        if (spec == null) throw new ArgumentNullException(nameof(spec));
        if (controller == null) throw new ArgumentNullException(nameof(controller));

        var token = new Creature(
            spec.Name, manaCost: "",
            power: spec.Power, toughness: spec.Toughness,
            subtypes: spec.Subtypes ?? Array.Empty<CardSubtype>())
        {
            Owner = controller,
            Controller = controller,
            IsToken = true,
            HasSummoningSickness = true,
        };

        // CR 111.4 — stamp the token's colour identity. Always set the
        // override (even for null / empty input, where the explicit empty
        // override declares "colourless") so CardColors.GetColors stops
        // probing the empty mana cost and returns the authoritative set.
        token.SetTokenColors(spec.Colors ?? Array.Empty<ManaColor>());

        foreach (var kw in spec.Keywords ?? Array.Empty<string>())
        {
            token.AddAbility(new KeywordAbility(kw, token, controller));
        }

        // Tokens enter the battlefield directly (CR 111.6) — not from the library.
        token.SetZone(ZoneType.Library); // sentinel for ZoneService.MoveCard's from-check
        controller.Zones.Library.AddCard(token);

        if (zones != null)
        {
            zones.MoveCardTo(token, ZoneType.Battlefield, controller);
        }
        else
        {
            controller.Zones.Library.RemoveCard(token);
            token.SetZone(ZoneType.Battlefield);
            controller.Zones.Battlefield.AddCard(token);
        }

        return token;
    }

    /// <summary>
    /// CR 111 / CR 614 — bus-aware "create one or more tokens" entry point.
    /// Publishes a <see cref="TokenCreationIntent"/> through
    /// <paramref name="replacements"/> (when supplied) so token-doubling
    /// replacements (Doubling Season, Parallel Lives, Anointed Procession)
    /// can rewrite the count before any token is minted, then mints the
    /// post-replacement <c>Count</c> copies via
    /// <see cref="CreateOnBattlefield(TokenSpec, Player, ZoneService?)"/>.
    ///
    /// Returns the list of minted tokens (empty when the intent was
    /// cancelled or its count fell to zero). Callers that need a single
    /// token can continue calling the single-token overload directly;
    /// this overload is the recommended path whenever the number of
    /// tokens shipped is part of the printed effect ("Create two 1/1
    /// Soldier tokens", etc.).
    ///
    /// CR 616.1c — each registered doubler fires at most once per intent;
    /// two Parallel Lives stack multiplicatively (1 → 2 → 4).
    /// </summary>
    public static IReadOnlyList<Creature> CreateOnBattlefield(
        TokenSpec spec,
        Player controller,
        int count,
        ZoneService? zones,
        ReplacementBus? replacements)
    {
        if (spec == null) throw new ArgumentNullException(nameof(spec));
        if (controller == null) throw new ArgumentNullException(nameof(controller));
        if (count <= 0) return Array.Empty<Creature>();

        var intent = new TokenCreationIntent(controller, spec, count);

        if (replacements != null)
        {
            var replaced = replacements.Apply(intent);
            if (replaced == null) return Array.Empty<Creature>();
            intent = replaced;
        }

        if (intent.Count <= 0) return Array.Empty<Creature>();

        var minted = new List<Creature>(intent.Count);
        for (int i = 0; i < intent.Count; i++)
        {
            minted.Add(CreateOnBattlefield(intent.Spec, intent.Controller, zones));
        }
        return minted;
    }

    /// <summary>Amass token (CR 701.49). Creates a 0/0 black [tribe] Army
    /// creature token and puts it onto the battlefield.
    /// Caller then adds +1/+1 counters via AmassAction.</summary>
    public static Creature CreateArmy(
        Player controller,
        CardSubtype tribe,
        ZoneService? zones = null)
    {
        if (controller == null) throw new ArgumentNullException(nameof(controller));

        var subtypes = tribe == CardSubtype.Army
            ? new[] { CardSubtype.Army }
            : new[] { tribe, CardSubtype.Army };

        var token = new Creature(
            $"{tribe} Army", manaCost: "",
            power: 0, toughness: 0,
            subtypes: subtypes)
        {
            Owner = controller,
            Controller = controller,
            IsToken = true,
            HasSummoningSickness = true,
        };

        // CR 701.49 — Amass tokens are black [tribe] Army creatures.
        token.SetTokenColors(new[] { ManaColor.Black });

        // Use sentinel-library pattern so CardMovedEvent fires correctly.
        token.SetZone(ZoneType.Library);
        controller.Zones.Library.AddCard(token);

        if (zones != null)
        {
            zones.MoveCardTo(token, ZoneType.Battlefield, controller);
        }
        else
        {
            controller.Zones.Library.RemoveCard(token);
            token.SetZone(ZoneType.Battlefield);
            controller.Zones.Battlefield.AddCard(token);
        }

        return token;
    }

    /// <summary>Treasure (CR 111.10): colourless artifact token with
    /// "{T}, Sacrifice this artifact: Add one mana of any color." Bound as
    /// five ManaAbility options so the bot's mana picker can use a
    /// Treasure to satisfy any colour pip.</summary>
    public static Artifact CreateTreasure(Player controller, ZoneService? zones = null)
    {
        if (controller == null) throw new ArgumentNullException(nameof(controller));
        var token = new Artifact("Treasure", "",
            subtypes: new[] { CardSubtype.Treasure })
        {
            Owner = controller,
            Controller = controller,
            IsToken = true,
        };
        // CR 111.10 — Treasure tokens are colourless artifacts.
        token.SetTokenColors(Array.Empty<ManaColor>());
        // "{T}, Sacrifice this artifact: Add one mana of any color." — bound as
        // five ManaAbility options (one per colour) so the bot's mana picker can
        // satisfy any colour pip. Each option uses a DYNAMIC Func<ManaCost>
        // generator instead of a fixed cost so a Goldspan-style continuous
        // static (Treasures you control add TWO mana of any one color) can
        // modify production at activation time without re-binding these
        // abilities on every board change. The generator multiplies the printed
        // ONE pip of THIS colour by
        // TreasureManaModifierStaticAbility.ManaMultiplierFor(controller) — 1
        // when no modifier is in play (vanilla Treasure), 2 under Goldspan
        // (CR 611.2 — the modifier replaces "one mana" with "two mana of any one
        // color"; both pips are the same colour the option produces).
        foreach (var color in new[] { "W", "U", "B", "R", "G" })
        {
            var pip = color;
            // "{T}, Sacrifice this artifact: …" — taps AND sacrifices (CR
            // 701.16). The dynamic-generator + additionalCostPayer ctor taps
            // the source ({T}) and then runs the sacrifice payer, so a Treasure
            // is consumed on its first use (not a repeatable free mana rock).
            // canActivateCheck gates on untapped AND still-on-battlefield so a
            // sibling colour option can't be re-activated after the sacrifice.
            token.AddAbility(new ManaAbility(
                source: token,
                controller: controller,
                manaGenerator: () => BuildTreasureMana(pip, controller),
                canActivateCheck: () => !token.IsTapped
                                        && token.Zone == ZoneType.Battlefield,
                additionalCostPayer: _ => SacrificeToken(token),
                // Inspection seed = the printed ONE pip of this colour so the
                // bot's mana picker / UI see the colour before activation; the
                // generator still applies the live Goldspan multiplier on tap.
                printedManaGenerated: ManaCost.Parse(pip)));
        }
        PutOnBattlefield(token, controller, zones);
        return token;
    }

    /// <summary>Clue token (CR 111.10): colourless artifact with
    /// "{2}, Sacrifice this artifact: Draw a card."</summary>
    public static Artifact CreateClue(Player controller, ZoneService? zones = null)
    {
        if (controller == null) throw new ArgumentNullException(nameof(controller));
        var token = new Artifact("Clue", "",
            subtypes: new[] { CardSubtype.Clue })
        {
            Owner = controller,
            Controller = controller,
            IsToken = true,
        };
        // CR 111.10 — Clue tokens are colourless artifacts.
        token.SetTokenColors(Array.Empty<ManaColor>());

        // {2}, Sacrifice this artifact: Draw a card.
        token.AddAbility(BuildClueDrawAbility(token, controller));

        PutOnBattlefield(token, controller, zones);
        return token;
    }

    /// <summary>Food (CR 111.10): colorless artifact token with
    /// "{2}, {T}, Sacrifice this artifact: You gain 3 life."</summary>
    public static Artifact CreateFood(Player controller, ZoneService? zones = null)
    {
        if (controller == null) throw new ArgumentNullException(nameof(controller));
        var token = new Artifact("Food", "",
            subtypes: new[] { CardSubtype.Food })
        {
            Owner = controller,
            Controller = controller,
            IsToken = true,
        };
        // CR 111.10 — Food tokens are colourless artifacts.
        token.SetTokenColors(Array.Empty<ManaColor>());

        // {2}, {T}, Sacrifice this artifact: You gain 3 life.
        token.AddAbility(BuildFoodGainLifeAbility(token, controller));

        PutOnBattlefield(token, controller, zones);
        return token;
    }

    /// <summary>Blood (CR 111.10 / Innistrad: Crimson Vow): red artifact
    /// token with "{1}, {T}, Discard a card, Sacrifice this artifact: Draw
    /// a card." Voldaren Estate's red-mana-flavoured Treasure cousin —
    /// produced by Voldaren Epicure / Bloodtithe Harvester / Voldaren
    /// Bloodcaster and consumed by Falkenrath Pit Fighter, Voldaren Pariah's
    /// Anointed Deacon transform clause, and the broader Crimson Vow
    /// "sacrifice a Blood token" payoff family. Bound as a 4-cost activated
    /// ability ({1} mana + {T} tap + DiscardACard + sacrifice-self) with a
    /// single draw-one effect — same compositional shape as
    /// <see cref="CreateClue"/> / <see cref="CreateFood"/>. The
    /// <see cref="CardSubtype.Blood"/> subtype is stamped so Falkenrath Pit
    /// Fighter's "sacrifice another creature or Blood token" cost gate +
    /// the broader Blood-counts-as-X type predicates pick it up.</summary>
    public static Artifact CreateBlood(Player controller, ZoneService? zones = null)
    {
        if (controller == null) throw new ArgumentNullException(nameof(controller));
        var token = new Artifact("Blood", "",
            subtypes: new[] { CardSubtype.Blood })
        {
            Owner = controller,
            Controller = controller,
            IsToken = true,
        };
        // CR 111.10 — Blood tokens are red artifacts (the printed token frame
        // is red and the flavour text-box is treated as red mana for the
        // CardColors / colour-matters surface). Single-colour list per the
        // TokenSpec.Colors convention used by Stormchaser's Talent Mercenary.
        token.SetTokenColors(new[] { ManaColor.Red });

        // {1}, {T}, Discard a card, Sacrifice this artifact: Draw a card.
        token.AddAbility(BuildBloodDiscardDrawAbility(token, controller));

        PutOnBattlefield(token, controller, zones);
        return token;
    }

    /// <summary>Map (CR 111.10 / The Lost Caverns of Ixalan): colourless
    /// artifact token with "{1}, {T}, Sacrifice this token: Target creature you
    /// control explores. Activate only as a sorcery." (CR 701.40). Produced by
    /// Get Lost (two Maps), Sentinel of the Nameless City, Spyglass Siren,
    /// Cenote Scout's neighbours, Amalia Benavides Aguirre, etc. Bound as a
    /// single sorcery-speed activated ability ({1} mana + {T} tap +
    /// sacrifice-self) targeting a creature the activating player controls; on
    /// resolution that creature explores via the shared
    /// <see cref="Majik.Core.Keywords.ExploreAction.ExploreAsync"/> primitive
    /// (PR #2237) — the same body the ETB-explore family runs.</summary>
    public static Artifact CreateMap(Player controller, ZoneService? zones = null)
    {
        if (controller == null) throw new ArgumentNullException(nameof(controller));
        var token = new Artifact("Map", "",
            subtypes: new[] { CardSubtype.Map })
        {
            Owner = controller,
            Controller = controller,
            IsToken = true,
        };
        // CR 111.10 — Map tokens are colourless artifacts.
        token.SetTokenColors(Array.Empty<ManaColor>());

        // {1}, {T}, Sacrifice this token: Target creature you control explores.
        // Activate only as a sorcery.
        token.AddAbility(BuildMapExploreAbility(token, controller));

        PutOnBattlefield(token, controller, zones);
        return token;
    }

    /// <summary>Powerstone (CR 111.10 / The Brothers' War): colourless
    /// artifact token with "{T}: Add {C}. This mana can't be spent to cast
    /// a nonartifact spell." (Reckoner Bankbuster, Thran Spider, Loran's
    /// Smile, Mishra Lost to Phyrexia). Bound as a single
    /// <see cref="ManaAbility"/> producing one colourless mana stamped
    /// with a <see cref="SpendRestriction"/> recording the
    /// "artifact spells only" rider. The restriction is observational
    /// metadata at v1 — see <see cref="SpendRestriction"/> xmldoc — and
    /// the production payment-resolver gate is shared with Cavern of
    /// Souls / Eldrazi Temple.</summary>
    public static Artifact CreatePowerstone(Player controller, ZoneService? zones = null)
    {
        if (controller == null) throw new ArgumentNullException(nameof(controller));
        var token = new Artifact("Powerstone", "",
            subtypes: new[] { CardSubtype.Powerstone })
        {
            Owner = controller,
            Controller = controller,
            IsToken = true,
        };
        // CR 111.10 — Powerstone tokens are colourless artifacts.
        token.SetTokenColors(Array.Empty<ManaColor>());

        // "{T}: Add {C}." with the artifact-spell spend rider attached.
        // CR 106.4 — the rider lives on the generated mana via the
        // SpendRestriction metadata channel; payment-time enforcement is
        // shared with the Cavern of Souls / Eldrazi Temple pipeline.
        token.AddAbility(new ManaAbility(
            source: token,
            controller: controller,
            manaGenerated: ValueObjects.ManaCost.Parse("C"),
            canActivateCheck: null,
            spendRestriction: PowerstoneSpendRestriction));

        PutOnBattlefield(token, controller, zones);
        return token;
    }

    /// <summary>
    /// "Spend this mana only to cast artifact spells" — the rider stamped
    /// on every unit of mana a Powerstone token produces. Captured as a
    /// static so equal restrictions share a single instance (matches the
    /// <see cref="SpendRestriction"/> equality contract — delegate
    /// references compare by identity).
    /// </summary>
    public static readonly SpendRestriction PowerstoneSpendRestriction =
        new("artifact spell",
            spell => spell?.Card != null && spell.Card.HasType(CardType.Artifact));

    /// <summary>Eldrazi Spawn (CR 111.10): colorless creature token, 0/1, with
    /// "Sacrifice this creature: Add {C}." mana ability. CR 605.1 — the
    /// sacrifice rides the activation as a non-{T} additional cost (no tap):
    /// activating the ability sacrifices the token (CR 701.16, battlefield →
    /// owner's graveyard) and adds {C}. Because the cost has no {T}, the
    /// ability is usable even while the token is tapped or summoning-sick
    /// (CR 605.3a only gates {T} costs).</summary>
    public static Creature CreateEldraziSpawn(Player controller, ZoneService? zones = null)
    {
        if (controller == null) throw new ArgumentNullException(nameof(controller));

        var token = new Creature("Eldrazi Spawn", manaCost: "",
            power: 0, toughness: 1,
            subtypes: new[] { CardSubtype.Eldrazi, CardSubtype.Spawn })
        {
            Owner = controller,
            Controller = controller,
            IsToken = true,
            HasSummoningSickness = true,
        };
        // CR 111.10 — Eldrazi Spawn tokens are colourless creatures.
        token.SetTokenColors(Array.Empty<ManaColor>());

        // "Sacrifice this creature: Add {C}." — a sacrifice-cost (no-tap) mana
        // ability. tapsAsCost:false so summoning sickness / a tapped state
        // don't gate it; the additionalCostPayer performs the sacrifice
        // (CR 701.16) inline. canActivateCheck guards against a second
        // activation once the token has left the battlefield. Same pattern as
        // Lotus Petal, minus the tap.
        token.AddAbility(new ManaAbility(
            source: token,
            controller: controller,
            manaGenerated: Majik.Core.ValueObjects.ManaCost.Parse("C"),
            canActivateCheck: () => token.Zone == ZoneType.Battlefield,
            additionalCostPayer: _ => SacrificeToken(token),
            tapsAsCost: false));

        // Put the token onto the battlefield using the sentinel-library pattern
        // shared by CreateTreasure / CreateFood so CardMovedEvent fires correctly.
        token.SetZone(ZoneType.Library);
        controller.Zones.Library.AddCard(token);

        if (zones != null)
        {
            zones.MoveCardTo(token, ZoneType.Battlefield, controller);
        }
        else
        {
            controller.Zones.Library.RemoveCard(token);
            token.SetZone(ZoneType.Battlefield);
            controller.Zones.Battlefield.AddCard(token);
        }

        return token;
    }

    /// <summary>"{2}, Sacrifice this artifact: Draw a card." — Clue ability.</summary>
    /// <summary>
    /// Build the <see cref="ManaCost"/> a Treasure's single-colour mana option
    /// produces right now. The printed Treasure adds ONE pip of
    /// <paramref name="pip"/>; under a Goldspan-style modifier the controller
    /// controls (<see cref="TreasureManaModifierStaticAbility.ManaMultiplierFor"/>)
    /// the option adds N pips of that SAME colour ("two mana of any one color",
    /// CR 611.2). Evaluated at activation time (CR 605.1) so the live board
    /// decides the amount — no re-binding of the Treasure's abilities when a
    /// modifier enters or leaves.
    /// </summary>
    private static ManaCost BuildTreasureMana(string pip, Player controller)
    {
        var multiplier = TreasureManaModifierStaticAbility.ManaMultiplierFor(controller);
        return ManaCost.Parse(string.Concat(Enumerable.Repeat($"{{{pip}}}", multiplier)));
    }

    /// <summary>
    /// CR 701.16 — sacrifice a permanent: its controller moves it from the
    /// battlefield to its owner's graveyard. Shared by the sacrifice-cost mana
    /// abilities on Eldrazi Spawn / Eldrazi Scion / Treasure (same zone-move
    /// pattern Lotus Petal uses inline). Idempotent: if the permanent has
    /// already left the battlefield (defensive — the canActivateCheck gate
    /// should prevent it) this is a no-op.
    /// </summary>
    internal static void SacrificeToken(Permanent permanent)
    {
        if (permanent.Zone != ZoneType.Battlefield) return;

        var controller = permanent.Controller;
        var owner = permanent.Owner;
        if (controller == null || owner == null) return;

        controller.Zones.Battlefield.RemoveCard(permanent);
        owner.Zones.Graveyard.AddCard(permanent);
        permanent.SetZone(ZoneType.Graveyard);
    }

    private static ActivatedAbility BuildClueDrawAbility(Artifact source, Player controller)
    {
        var costs = new ICost[]
        {
            new ManaCostCost(ValueObjects.ManaCost.Parse("2")),
            AdditionalCost.Sacrifice(source),
        };
        var effects = new IEffect[]
        {
            new Effect("draw 1 from Clue", () => DrawOneCard(controller)),
        };
        return new ActivatedAbility(source, controller, costs: costs, effects: effects);
    }

    /// <summary>"{2}, {T}, Sacrifice this artifact: You gain 3 life." — Food ability.</summary>
    private static ActivatedAbility BuildFoodGainLifeAbility(Artifact source, Player controller)
    {
        var costs = new ICost[]
        {
            new ManaCostCost(ValueObjects.ManaCost.Parse("2")),
            AdditionalCost.Tap(source),
            AdditionalCost.Sacrifice(source),
        };
        var effects = new IEffect[]
        {
            new Effect("Food: gain 3 life", () => controller.GainLife(3)),
        };
        return new ActivatedAbility(source, controller, costs: costs, effects: effects);
    }

    /// <summary>"{1}, {T}, Discard a card, Sacrifice this artifact: Draw a
    /// card." — Blood ability. Four costs in cost-list declaration order
    /// (mana, tap, discard, sacrifice); the sacrifice payment is performed
    /// inside the effect closure because the generic
    /// <see cref="AdditionalCost.Sacrifice"/> payment is a no-op stub
    /// (mirrors Caustic Caterpillar / Insolent Neonate / Aether Spellbomb).
    /// The draw is one card from the top of the controller's library — empty
    /// library flags the SBA loss via
    /// <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/> (CR 704.5b),
    /// matching the Clue / Food posture.</summary>
    private static ActivatedAbility BuildBloodDiscardDrawAbility(Artifact source, Player controller)
    {
        var costs = new ICost[]
        {
            new ManaCostCost(ValueObjects.ManaCost.Parse("1")),
            AdditionalCost.Tap(source),
            new DiscardACardCost(),
            AdditionalCost.Sacrifice(source),
        };
        var effects = new IEffect[]
        {
            new Effect("Blood: sacrifice self + draw a card", () =>
            {
                // Sacrifice payment — AdditionalCost.Sacrifice is a no-op
                // stub (see Insolent Neonate / Caustic Caterpillar
                // precedent), so we route the battlefield → graveyard move
                // here. CR 701.16 — idempotent re-entry guard.
                if (source.Zone == ZoneType.Battlefield)
                {
                    controller.Zones.Battlefield.RemoveCard(source);
                    controller.Zones.Graveyard.AddCard(source);
                    source.SetZone(ZoneType.Graveyard);
                }

                // CR 121.1 — draw one card from top of library. Empty
                // library flags the SBA loss via the standard helper
                // (Insolent Neonate / Faithless Looting parity).
                var top = controller.Zones.Library.GetCards().FirstOrDefault();
                if (top == null)
                {
                    controller.MarkTriedToDrawFromEmptyLibrary();
                    return;
                }
                controller.Zones.Library.RemoveCard(top);
                controller.Zones.Hand.AddCard(top);
                top.SetZone(ZoneType.Hand);
            }),
        };
        return new ActivatedAbility(source, controller, costs: costs, effects: effects);
    }

    /// <summary>
    /// "{1}, {T}, Sacrifice this token: Target creature you control explores.
    /// Activate only as a sorcery." — Map ability (CR 111.10 / CR 701.40).
    /// Three costs in declaration order (mana {1}, tap, sacrifice); the
    /// sacrifice payment is performed inside the effect closure (the generic
    /// <see cref="AdditionalCost.Sacrifice"/> payment is a no-op stub — same
    /// posture as Clue / Food / Blood). The effect reads the chosen creature off
    /// the ability's <see cref="ActivatedAbility.ChosenTargets"/> and explores it
    /// under its controller via the shared
    /// <see cref="Majik.Core.Keywords.ExploreAction.ExploreAsync"/> primitive
    /// (PR #2237); the keep-on-top / graveyard choice (CR 701.40c) consults the
    /// resolving context's agent (falling back to the registry, then
    /// keep-on-top) and a <see cref="Majik.Core.Events.CreatureExploredEvent"/>
    /// is published so explore payoffs fire (CR 701.40e). CR 608.2b — an illegal
    /// target at resolution fizzles the explore (but the token is still
    /// sacrificed — the cost was already paid). <c>sorcerySpeed: true</c> carries
    /// the CR 117.1a / 307.5 "Activate only as a sorcery" timing rider.
    /// </summary>
    private static ActivatedAbility BuildMapExploreAbility(Artifact source, Player controller)
    {
        var costs = new ICost[]
        {
            new ManaCostCost(ValueObjects.ManaCost.Parse("1")),
            AdditionalCost.Tap(source),
            AdditionalCost.Sacrifice(source),
        };

        ActivatedAbility? ability = null;
        var effect = new Effect(
            "Map: sacrifice self + target creature you control explores",
            async ctx =>
            {
                // Sacrifice payment — AdditionalCost.Sacrifice is a no-op stub
                // (Clue / Food / Blood precedent), so route the
                // battlefield → graveyard move here. CR 701.16 — idempotent
                // re-entry guard.
                if (source.Zone == ZoneType.Battlefield)
                {
                    controller.Zones.Battlefield.RemoveCard(source);
                    controller.Zones.Graveyard.AddCard(source);
                    source.SetZone(ZoneType.Graveyard);
                }

                // CR 608.2b — read the chosen target; an illegal / absent target
                // fizzles the explore (the token is already sacrificed).
                if (ability == null) return;
                if (ability.ChosenTargets.Count == 0) return;
                if (ability.ChosenTargets[0].Count == 0) return;
                if (ability.ChosenTargets[0][0] is not Creature target) return;
                if (target.Zone != ZoneType.Battlefield) return;

                // CR 701.40a — explore under the exploring permanent's own
                // controller. The agent / event bus / zone service all default
                // to their registries (or the resolving context's agent).
                var explorerController = target.Controller ?? controller;
                await Majik.Core.Keywords.ExploreAction.ExploreAsync(
                    creature: target,
                    controller: explorerController,
                    agent: ctx.Agent ?? Majik.Core.Players.Agents.AgentRegistry.Get(explorerController),
                    game: ctx.Game,
                    replacements: null,
                    eventBus: null,
                    zones: ZoneServiceRegistry.Get(explorerController),
                    ct: ctx.Ct).ConfigureAwait(false);
            });

        ability = new ActivatedAbility(
            source: source,
            controller: controller,
            costs: costs,
            effects: new IEffect[] { effect },
            targetRequests: new[]
            {
                Majik.Core.CardData.Definitions.TargetFilters.ToTargetRequest(
                    "creature_you_control", "explore", BotIntent.Buff),
            },
            sorcerySpeed: true);

        return ability;
    }

    /// <summary>Move the top card of <paramref name="player"/>'s library to
    /// their hand (CR 121.2). No-ops silently if the library is empty
    /// (empty-library state-loss is handled by SBAs, not here).</summary>
    private static void DrawOneCard(Player player)
    {
        var top = player.Zones.Library.GetCards().FirstOrDefault();
        if (top == null) return;
        player.Zones.Library.RemoveCard(top);
        player.Zones.Hand.AddCard(top);
        top.SetZone(ZoneType.Hand);
    }

    private static void PutOnBattlefield(Artifact token, Player controller, ZoneService? zones)
    {
        token.SetZone(ZoneType.Library); // sentinel; ZoneService validates from-zone
        controller.Zones.Library.AddCard(token);
        if (zones != null)
        {
            zones.MoveCardTo(token, ZoneType.Battlefield, controller);
        }
        else
        {
            // ZoneManager.MoveCard publishes CardMovedEvent for log /
            // trigger subscribers (Treasure ETB visible, downstream
            // triggers like Soul Warden fire).
            controller.Zones.MoveCard(token, ZoneType.Library, ZoneType.Battlefield);
            token.SetZone(ZoneType.Battlefield);
        }
    }
}
