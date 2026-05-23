using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData.Factories;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData;

/// <summary>
/// Convenience builder for the handful of cards tests construct without
/// hitting the DB. Produces typed Card subclasses with the minimum
/// ability set (basic lands get a tap-for-mana ability inline; other
/// cards are vanilla).
///
/// Production code paths go through <see cref="ScryfallCardFactory"/>
/// instead — that route runs the full data-driven binders against
/// real Scryfall rows.
/// </summary>
public static class NamedCardFactory
{
    public static ICard Create(string name, Player owner)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name required", nameof(name));
        if (owner == null) throw new ArgumentNullException(nameof(owner));

        ICard card = name switch
        {
            // Basic lands — given an inline mana ability so the simplest
            // tests don't need a fake repo just to pay {R}, etc.
            "Mountain" => Land(name, CardSubtype.Mountain),
            "Forest"   => Land(name, CardSubtype.Forest),
            "Plains"   => Land(name, CardSubtype.Plains),
            "Island"   => Land(name, CardSubtype.Island),
            "Swamp"    => Land(name, CardSubtype.Swamp),
            "Wastes"   => Land(name, CardSubtype.Wastes),

            // A few common vanilla creatures the test suite relies on.
            "Grizzly Bears"   => new Creature(name, "1G", 2, 2),
            "Runeclaw Bear"   => new Creature(name, "1G", 2, 2),
            "Hill Giant"      => new Creature(name, "3R", 3, 3),
            "Centaur Courser" => new Creature(name, "2G", 3, 3),

            // Named-card factories — fully-wired cards with real abilities.
            "Walking Ballista" => WalkingBallistaFactory.Create(owner),
            "Dryad Arbor"      => DryadArborFactory.Create(owner),
            "Yawgmoth, Thran Physician" => YawgmothFactory.Create(owner),
            "Boseiju, Who Endures" => BoseijuFactory.Create(owner),
            // Artifact — {2}. Static suppression wired via TorporOrbFactory.Create(owner,
            // triggerManager, eventBus) when runtime services are available; this path
            // produces the correct card shape without live suppression (test/vanilla use).
            "Torpor Orb" => TorporOrbFactory.Create(owner),

            // U/B dual surveil land — Murders at Karlov Manor (UndergroundMortuaryFactory).
            // {T}: Add {U} or {B} — two ManaAbility instances wired.
            // ETB trigger: surveil 1 (default-all-graveyard decision) — wired.
            // ETB-tapped restriction + untapped gate on surveil + player prompt deferred.
            "Underground Mortuary" => UndergroundMortuaryFactory.Create(owner),

            // Legendary Creature — Halfling Citizen 1/2 (DelightedHalflingFactory).
            // {T}: Add one mana of any color — 5 ManaAbility instances (one per WUBRG) wired.
            // Usage restriction (legendary-only mana) + "spell can't be countered" rider deferred.
            "Delighted Halfling" => DelightedHalflingFactory.Create(owner),

            // G/B dual land — Bloomburrow (WastewoodVergeFactory).
            // {T}: Add {G} and {T}: Add {B} — two ManaAbility instances wired.
            // {B} activation restriction ("if you control a Swamp or Forest") deferred.
            "Wastewood Verge" => WastewoodVergeFactory.Create(owner),

            // Land — Bloomburrow (SpymastersVaultFactory).
            // {T}: Add {B} — wired.
            // ETB-tapped restriction + connive activated ability deferred.
            "Spymaster's Vault" => SpymastersVaultFactory.Create(owner),

            // Artifact — {1} (VexingBaubleFactory).
            // {1}, {T}, Sacrifice: Draw a card — wired.
            // "Counter free spells" triggered ability deferred.
            "Vexing Bauble" => VexingBaubleFactory.Create(owner),

            // Enchantment — {1}{G} (DredgersInsightFactory).
            // ETB: mill 4, auto-pick first artifact/creature/land → hand.
            // Lifegain trigger: artifact/creature leaves controller's graveyard → gain 1 life.
            // "You may put …" opt-out + batched simultaneous-leavers deferred.
            "Dredger's Insight" => DredgersInsightFactory.Create(owner),

            // Creature — Insect Warrior {1}{G} 3/2 (KraulHarpoonerFactory).
            // Reach keyword wired.
            // ETB Undergrowth: +X/+0 EOT, X = creature cards in controller's graveyard — wired.
            // Targeting (flying creature you don't control) + fight step + "you may" prompt deferred.
            "Kraul Harpooner" => KraulHarpoonerFactory.Create(owner),

            // Creature — Bear {G} 1/1 (BadgermoleCubFactory). Shell only.
            // Earthbend 1 ETB (animate-land mechanic) deferred — no land→creature infra.
            // "Whenever you tap a creature for mana, add {G}" deferred — no tap-watcher infra.
            "Badgermole Cub" => BadgermoleCubFactory.Create(owner),

            // Creature — Orc Archer {1}{B} 1/1 (OrcishBowmastersFactory).
            // Flash keyword wired.
            // ETB damage trigger + opponent-draw watcher + amass Orcs 1 deferred.
            "Orcish Bowmasters" => OrcishBowmastersFactory.Create(owner),

            // Artifact — {2} (AgathasSoulCauldronFactory).
            // {T}: Exile first card from controller's graveyard; if creature, +1/+1 counter
            // on first creature on controller's battlefield — wired (v1 auto-pick).
            // Static mana-color-substitute + ability-grant via imprint deferred.
            "Agatha's Soul Cauldron" => AgathasSoulCauldronFactory.Create(owner),

            // Artifact — {0} (MishrasBaubleFactory).
            // {T}, Sacrifice this artifact: Look at top of target player's library;
            // draw a card at the beginning of the next turn's upkeep — wired.
            // The single-arg overload omits TriggerManager wiring so the delayed
            // draw is no-op (suitable for shape tests). Use the 2-arg overload
            // for fully-wired behavior. Real targeting prompt for "target
            // player's library" deferred.
            "Mishra's Bauble" => MishrasBaubleFactory.Create(owner),

            // Enchantment — {1}{R} (GoblinBombardmentFactory).
            // Sacrifice a creature: This enchantment deals 1 damage to any
            // target — wired. The shell uses Create(owner) (no pre-bound
            // ping); BuildPingAbility / CreateForBot wire a concrete
            // sacrifice+target activation. Real prompt-driven targeting +
            // ad-hoc cost composition deferred.
            "Goblin Bombardment" => GoblinBombardmentFactory.Create(owner),

            // Legendary Planeswalker — Grist {1}{B}{G} loyalty 3 (GristFactory).
            // Creature type + Insect subtype added unconditionally (v1 simplification).
            // "Only when not on battlefield" conditional layer-4 effect deferred.
            // Loyalty abilities (+1, -2, -5) wired by OracleLoyaltyAbilityBinder.
            "Grist, the Hunger Tide" => GristFactory.Create(owner),

            // Creature — Elemental Incarnation {3}{W}{W} 3/2 (SolitudeFactory).
            // Flash + Lifelink + Evoke keyword markers wired. ETB exile-target-
            // creature trigger wired (lifegain to target's controller equal to
            // exiled creature's power). Evoke alt-cost = "exile a white card
            // from hand" via EvokeAlternativeCost; printed evoke-sacrifice
            // trigger fires when Solitude enters if evoke was paid (CR 702.74b).
            // Opponent pitch-back ("controller may exile a non-Elemental,
            // non-Incarnation white card from their hand to return") deferred.
            "Solitude" => SolitudeFactory.Create(owner),

            // U/R Horizon Canopy painless dual — Modern Horizons (FieryIsletFactory).
            // {T}, Pay 1 life: Add {U} or {R} — two ManaAbility instances, each with
            // a life-cost activation gate (CR 119.4) and a LoseLife side-effect.
            // {1}, {T}, Sacrifice this land: Draw a card — wired.
            // Sacrifice-cost zone movement deferred (see HorizonLandBinder.AttachSacDraw).
            "Fiery Islet" => FieryIsletFactory.Create(owner),

            // R/W Horizon Canopy painless dual — Modern Horizons (SunbakedCanyonFactory).
            // Same shape as Fiery Islet; only colour differs.
            "Sunbaked Canyon" => SunbakedCanyonFactory.Create(owner),

            // U/R surveil land — Foundations (ThunderingFallsFactory).
            // {T}: Add {U} or {R} — two ManaAbility instances wired.
            // ETB trigger: surveil 1 — default-all-graveyard decision wired.
            // ETB-tapped + surveil player prompt deferred (mirrors Underground Mortuary).
            "Thundering Falls" => ThunderingFallsFactory.Create(owner),

            // R/W surveil land — Foundations (ElegantParlorFactory).
            // Same shape as Thundering Falls; only colour differs.
            "Elegant Parlor" => ElegantParlorFactory.Create(owner),

            // R/W fastland — Kaladesh (InspiringVantageFactory).
            // {T}: Add {R} or {W} — two ManaAbility instances wired.
            // ETB-tapped-unless-two-or-fewer-other-lands handled via
            // ConditionalEntersTappedBinder in the production load path.
            "Inspiring Vantage" => InspiringVantageFactory.Create(owner),

            // Enchantment — {2}{R} (BloodMoonFactory). "Nonbasic lands are
            // Mountains." CR 305.6 / 613.1d. Implemented as a Layer 4
            // SetSubtypesEffect scoped to every nonbasic Land on the
            // battlefield (PR #151) — combined with EffectiveManaAbilities
            // (PR #155) the affected lands lose their printed abilities and
            // tap for {R}. Lifecycle wiring (RetypeLandsStaticEffect ↔
            // CardMovedEvent) requires the live ContinuousEffectsService +
            // EventBus and goes through
            // BloodMoonFactory.Create(owner, effects, eventBus). The
            // single-arg dispatcher path here produces the correct card
            // shape without a live Layer 4 effect (suitable for test /
            // shape-only use).
            "Blood Moon" => BloodMoonFactory.Create(owner),

            // Creature — Human Wizard {2}{R} 2/2 (MagusOfTheMoonFactory).
            // Same "Nonbasic lands are Mountains" Layer 4 effect as Blood
            // Moon (CR 305.6 / 613.1d), wired via the shared
            // RetypeLandsStaticEffect binder. Lifecycle requires the live
            // ContinuousEffectsService + EventBus via
            // MagusOfTheMoonFactory.Create(owner, effects, eventBus); the
            // single-arg dispatcher path here produces the correct card
            // shape only.
            "Magus of the Moon" => MagusOfTheMoonFactory.Create(owner),

            // Creature — Wizard {1}{U} 2/2 (HarbingerOfTheSeasFactory).
            // "Nonbasic lands are Islands" — same scope as Blood Moon,
            // retypes to {Island} instead of {Mountain}. Printed creature
            // type "Merfolk Wizard" — Merfolk not yet in CardSubtype, so
            // only Wizard is assigned. Full lifecycle via
            // HarbingerOfTheSeasFactory.Create(owner, effects, eventBus).
            "Harbinger of the Seas" => HarbingerOfTheSeasFactory.Create(owner),

            // Enchantment — {2}{W}{W} (ConversionFactory).
            // "All Mountains are Plains." Scope: any Land whose subtype
            // set contains Mountain (basic or nonbasic). Retypes to
            // {Plains}. The original card's upkeep "sacrifice unless you
            // pay {W}{W}" clause is deferred — only the Layer 4
            // type-change ships here. Full lifecycle via
            // ConversionFactory.Create(owner, effects, eventBus).
            "Conversion" => ConversionFactory.Create(owner),

            // Legendary Land — Planar Chaos (UrborgTombOfYawgmothFactory).
            // "Each land is a Swamp in addition to its other types."
            // CR 305.7 / 613.1d. Implemented as a Layer 4
            // AddSubtypeToPermanentsEffect scoped to every Land on the
            // battlefield, additively granting {Swamp}. Combined with
            // EffectiveManaAbilities' additive-vs-replacement detection,
            // each affected land keeps its printed mana ability AND gains
            // a {T}: Add {B}. Urborg self-applies (no printed mana →
            // taps for {B} via the granted Swamp subtype). Lifecycle
            // wiring (GrantLandSubtypeStaticEffect ↔ CardMovedEvent)
            // requires the live ContinuousEffectsService + EventBus via
            // UrborgTombOfYawgmothFactory.Create(owner, effects,
            // eventBus). The single-arg dispatcher path here produces
            // the correct card shape only.
            "Urborg, Tomb of Yawgmoth" => UrborgTombOfYawgmothFactory.Create(owner),

            // Legendary Land — Dominaria United (YavimayaCradleOfGrowthFactory).
            // "Each land is a Forest in addition to its other types."
            // Same Layer 4 additive grant machinery as Urborg, only the
            // granted subtype differs ({Forest} instead of {Swamp}).
            // Full lifecycle via
            // YavimayaCradleOfGrowthFactory.Create(owner, effects, eventBus).
            "Yavimaya, Cradle of Growth" => YavimayaCradleOfGrowthFactory.Create(owner),

            // Enchantment — Aura {1}{U} (SpreadingSeasFactory).
            // "Enchant land. When Spreading Seas enters, draw a card.
            //  Enchanted land is an Island and has '{T}: Add {U}'."
            // CR 303.4 / 305.6 / 613.1d — Layer 4 retype scoped to the
            // aura's single attachment target, via
            // AttachedAuraRetypeStaticEffect. PR #155's
            // EffectiveManaAbilities derives {T}: Add {U} from the granted
            // Island subtype. Full lifecycle via
            // SpreadingSeasFactory.Create(owner, effects, eventBus). The
            // single-arg dispatcher path here produces the correct card
            // shape only. Cast-time targeting + attach flow is deferred —
            // tests manually AttachTo() the bearer.
            "Spreading Seas" => SpreadingSeasFactory.Create(owner),

            // Enchantment — Aura {U} (SeasClaimFactory).
            // "Enchant land. Enchanted land is an Island."
            // Same retype machinery as Spreading Seas, no ETB draw.
            "Sea's Claim" => SeasClaimFactory.Create(owner),

            // Creature — Human Wizard {1}{U} 2/1 (SnapcasterMageFactory).
            // Flash keyword + ETB trigger that grants flashback (CR 702.33)
            // to target instant/sorcery in controller's graveyard until EOT;
            // granted cost = the target's printed mana cost. The single-arg
            // dispatcher path here produces the correct card shape without
            // bus-driven EOT-cleanup wiring (suitable for test / shape-only
            // use). Use the (owner, eventBus) overload to enable automatic
            // grant expiration on the next Cleanup step.
            "Snapcaster Mage" => SnapcasterMageFactory.Create(owner),

            // Creature — Lhurgoyf {1}{G} (TarmogoyfFactory).
            // CR 604.3 / 613.2 — Layer 7a characteristic-defining P/T.
            // Power = number of distinct card types across all graveyards;
            // toughness = power + 1. Wired via
            // TarmogoyfFactory.Create(owner, effects, eventBus, graveyardSource)
            // when runtime services are available; the single-arg dispatcher
            // path here produces the correct card shape only (printed 0/1
            // seed, no live CDA).
            "Tarmogoyf" => TarmogoyfFactory.Create(owner),

            // Creature — Human Wizard {1}{B} 2/1 (DarkConfidantFactory).
            // Upkeep trigger: reveal top of controller's library, put it
            // into hand, lose life equal to its mana value. The single-arg
            // path produces the correct card shape without bus-driven
            // trigger registration (suitable for shape tests). Use the
            // (owner, bus, triggers) overload for fully-wired behavior.
            "Dark Confidant" => DarkConfidantFactory.Create(owner),

            // Legendary Planeswalker — Liliana {1}{B}{B} loyalty 3
            // (LilianaOfTheVeilFactory). +1 each-player-discards-a-card,
            // -2 target-player-sacs-a-creature, -6 ultimate (deferred).
            // The single-arg path uses no allPlayersResolver so the +1/-2
            // effects no-op (loyalty change still applies); use the
            // (owner, allPlayersResolver) overload to enable full
            // multi-player effects.
            "Liliana of the Veil" => LilianaOfTheVeilFactory.Create(owner),

            // Legendary Planeswalker — Wrenn {R}{G} loyalty 3
            // (WrennAndSixFactory). +1 return land card from graveyard
            // to hand (auto-pick), -1 lands-ping (deferred no-op), -7
            // retrace emblem (structural shell).
            "Wrenn and Six" => WrennAndSixFactory.Create(owner),

            // Sorcery — {7}{U} (TreasureCruiseFactory).
            // CR 702.66 — Delve. "Delve" marker keyword wired; the cost
            // mechanic itself lives in DelveCost + SpellCastFlow. Resolve
            // effect ("Draw three cards") is built on demand via
            // TreasureCruiseFactory.BuildResolveEffect — the shell here
            // omits it so the dispatcher path produces a shape-only card.
            "Treasure Cruise" => TreasureCruiseFactory.Create(owner),

            // Instant — {6}{U}{U} (DigThroughTimeFactory).
            // Same Delve marker pattern as Treasure Cruise. Resolve
            // effect ("Look at 7, hand 2, bottom 5") wired via
            // DigThroughTimeFactory.BuildResolveEffect with a deterministic
            // default selector (first-two-to-hand); production code can
            // supply a custom selector when agent-driven choose-2 ships.
            "Dig Through Time" => DigThroughTimeFactory.Create(owner),

            // Legendary Planeswalker — Teferi {1}{W}{U} loyalty 4
            // (TeferiTimeRavelerFactory). Printed static "Each opponent
            // can cast spells only any time they could cast a sorcery"
            // (CR 117.1a) — wired via SorcerySpeedRestrictionEffect when
            // the runtime (owner, opponentResolver, targetResolver,
            // effects, eventBus) overload is used. -3 bounce target
            // artifact/creature/enchantment + draw a card (v1 auto-pick).
            // +1 cast-sorceries-as-flash deferred (no controller-keyed
            // cast-time speed modifier yet). The single-arg dispatcher
            // path here produces the correct card shape only.
            "Teferi, Time Raveler" => TeferiTimeRavelerFactory.Create(owner),

            // Creature — Human Wizard {1}{U} 1/2 (LibrarySurveyorFactory).
            // Synthetic Surveil keyword fixture. ETB: surveil 2 (CR 701.42).
            // No printed Modern-legal Surveil card had a clean enough
            // isolated trigger to ship inside the v1 keyword scope; this
            // synthetic creature exercises the surveil_self effect path
            // alongside the existing Underground Mortuary / Thundering
            // Falls / Elegant Parlor surveil-lands.
            "Library Surveyor" => LibrarySurveyorFactory.Create(owner),

            // Creature — Bird Advisor {1}{U} 1/3 (LedgerShredderFactory).
            // Flying keyword wired. Two triggered abilities surfaced on the
            // card for shape: (1) "whenever you cast the second spell each
            // turn, surveil 1" — predicate increments a per-turn closure
            // counter and matches on the exact 2nd cast; (2) "whenever
            // Ledger Shredder surveils, put a +1/+1 counter on it" — chained
            // inline off trigger 1's effect (no SurveilEvent on the bus yet).
            // The single-arg dispatcher path here produces the correct card
            // shape without bus-driven turn reset or trigger-manager wiring.
            // Use the (owner, bus, triggers) overload for fully-wired behavior.
            "Ledger Shredder" => LedgerShredderFactory.Create(owner),

            // Creature — Human Rogue {1}{U} 1/1 (TestConniverFactory).
            // Synthetic Connive keyword fixture. ETB: connive (CR 701.50).
            // Draws + discards + +1/+1 counter if nonland was discarded.
            // No printed Modern-legal Connive card had a clean enough
            // isolated trigger — Ledger Shredder's second-spell-per-turn
            // and Raffine's combat riders go beyond the v1 keyword scope.
            "Test Conniver" => TestConniverFactory.Create(owner),

            // Creature — Zombie {1}{B} 1/1 (LazotepRecruitFactory).
            // Synthetic Amass keyword fixture (loosely modelled on the
            // War of the Spark Lazotep cycle). ETB: amass Zombies 1
            // (CR 701.49) — creates a 0/0 black Zombie Army token when
            // the controller has no Army, then places a +1/+1 counter on it.
            "Lazotep Recruit" => LazotepRecruitFactory.Create(owner),

            // Instant — {3}{U}{U} (ForceOfWillFactory).
            // "You may pay 1 life and exile a blue card from your hand
            //  rather than pay this spell's mana cost. Counter target spell."
            // Card shape only here; the counter-target SpellDefinition is
            // attached via ForceOfWillFactory.BuildDefinition at the
            // SpellCastFlow resolver wire-up site. Pitch alt-cost (CR 118.9)
            // = exile a blue card from hand + 1 life when it's not your
            // turn (PitchAlternativeCost). Bot probe via PitchAltCostProbe.
            "Force of Will" => ForceOfWillFactory.Create(owner),

            // Instant — {1}{U}{U}{U} (CrypticCommandFactory).
            // CR 700.2d — modal "Choose two —" with 4 printed modes
            // (counter spell / bounce permanent / tap-all-opponents-creatures /
            // draw a card). The single-arg dispatcher path produces the
            // correct card shape; the bound SpellDefinition is built on
            // demand via CrypticCommandFactory.BuildDefinition(caster,
            // targetResolver, stack). Multi-pick relies on the caller
            // populating ChosenSpellParams.ModeIndexes — the runtime
            // honours either the list or the scalar ModeIndex shape.
            "Cryptic Command" => CrypticCommandFactory.Create(owner),

            // Instant — {1}{U}{U} (ForceOfNegationFactory).
            // "If it's not your turn, you may exile a blue card from your
            //  hand rather than pay this spell's mana cost. Counter target
            //  noncreature spell."
            // Same shape as Force of Will minus the life rider, and the
            // counter is gated to noncreature spells (CR 608.2b illegal-
            // target check at resolution).
            "Force of Negation" => ForceOfNegationFactory.Create(owner),

            // Instant — {B/P} (SurgicalExtractionFactory).
            // "Choose target card in a graveyard other than a basic land
            //  card. Search its owner's graveyard, hand, and library for
            //  any number of cards with the same name as that card and
            //  exile them. Then that player shuffles."
            // Phyrexian-mana alt cost (2 life) via
            // PhyrexianManaAlternativeCost. Card shape only here; the
            // resolve effect is built on demand via
            // SurgicalExtractionFactory.BuildDefinition at the
            // SpellCastFlow resolver wire-up site.
            "Surgical Extraction" => SurgicalExtractionFactory.Create(owner),

            // Sorcery — {2}{R} (RiftBoltFactory). 3 damage to any target;
            // Suspend 1—{R} (CR 702.62). Spell-def and suspend alt cost
            // built on demand via RiftBoltFactory.BuildSpellDefinition /
            // BuildSuspendCost — caller wires them through SpellCastFlow
            // and SuspendedCardRegistry.
            "Rift Bolt" => RiftBoltFactory.Create(owner),

            // Instant — {R} (UnholyHeatFactory). Modern Horizons 2.
            // "Unholy Heat deals 2 damage to any target. Delirium —
            //  Unholy Heat deals 4 damage to that target instead if
            //  there are four or more card types among cards in your
            //  graveyard." CR 702.105. Card shape only here; the
            // resolve-time SpellDefinition (with the delirium gate
            // sampling the controller's graveyard) is built on demand
            // via UnholyHeatFactory.BuildSpellDefinition. Reuses
            // TarmogoyfFactory.CountDistinctCardTypes for the type count.
            "Unholy Heat" => UnholyHeatFactory.Create(owner),

            // Artifact — {1} (PithingNeedleFactory).
            // "As Pithing Needle enters, choose a card name. Activated
            //  abilities of sources with the chosen name can't be
            //  activated unless they're mana abilities." (CR 602.5c / 605.)
            // Wired via PithingNeedleStaticEffect when the runtime
            // (owner, nameSelector, eventBus) overload is used. The
            // single-arg dispatcher path here produces the correct card
            // shape only.
            "Pithing Needle" => PithingNeedleFactory.Create(owner),

            // Creature — Kor Artificer {1}{W} 1/2 (StoneforgeMysticFactory).
            // ETB tutor: search library for an Equipment card → hand
            // (deterministic first-match; shuffle deferred). Activated
            // {1}{W}, {T}: put an Equipment card from hand directly onto
            // the battlefield, then attach to a creature you control
            // (CR 113.6c / 117.1a — alt-zone "cast"). The single-arg
            // dispatcher path here produces the correct card shape; the
            // activated ability uses raw zone moves (no ZoneService).
            // Use the (owner, zoneService, eventBus, triggers) overload
            // for full ETB-replacement / trigger-firing wiring.
            "Stoneforge Mystic" => StoneforgeMysticFactory.Create(owner),

            // Creature — Ooze {1}{G} 2/2 (ScavengingOozeFactory).
            // Activated {G}: exile target creature card from a graveyard;
            // if you do, put a +1/+1 counter on Scavenging Ooze and gain
            // 1 life. The single-arg dispatcher path scans only the
            // controller's graveyard; use the (owner, allPlayersResolver)
            // overload to scan every player's graveyard. Real "target
            // creature card from a graveyard" prompt deferred — v1 picks
            // the first creature card deterministically.
            "Scavenging Ooze" => ScavengingOozeFactory.Create(owner),

            // Creature — Dragon {3}{U}{U} 3/3 (MurktideRegentFactory).
            // Flying + Delve marker keywords wired. ETB trigger: exile target
            // instant or sorcery card from a graveyard; enters with +1/+1
            // counters = delve-exiled-count + (ETB exile ? 1 : 0) per CR 122.1g.
            // The delve count is plumbed via Card.PendingDelveExiledCount,
            // stamped by SpellCastFlow when DelveCost is paid and consumed
            // by the ETB effect.
            "Murktide Regent" => MurktideRegentFactory.Create(owner),

            // Enchantment — {1}{U} (DressDownFactory). Flash. CR 613.6 + 613.7b:
            // "Creatures lose all abilities and have base power and toughness
            // 1/1." End-step sacrifice trigger wired (CR 500.4 / CR 603.1).
            // The single-arg dispatcher path here attaches the Flash keyword
            // and the end-step trigger to the card shape; live continuous-
            // effects + TriggerManager wiring requires the (owner, effects,
            // eventBus, triggers, creaturePoolSource) overload. Candidate-pool
            // snapshot semantics — creatures entering AFTER Dress Down are
            // not scoped by v1.
            "Dress Down" => DressDownFactory.Create(owner),

            _ => new Card(name, ""),
        };

        card.SetOwner(owner);

        if (card is Land && card.HasSupertype(CardSupertype.Basic))
        {
            AttachBasicLandMana(card, owner);
        }
        return card;
    }

    private static Land Land(string name, CardSubtype subtype) =>
        new(name, new[] { CardSupertype.Basic }, new[] { subtype });

    private static void AttachBasicLandMana(ICard land, Player controller)
    {
        var color = land.HasSubtype(CardSubtype.Mountain) ? "R"
                  : land.HasSubtype(CardSubtype.Forest)   ? "G"
                  : land.HasSubtype(CardSubtype.Plains)   ? "W"
                  : land.HasSubtype(CardSubtype.Island)   ? "U"
                  : land.HasSubtype(CardSubtype.Swamp)    ? "B"
                  : land.HasSubtype(CardSubtype.Wastes)   ? "C"
                  : null;

        if (color != null)
        {
            land.AddAbility(new ManaAbility(land, controller, ManaCost.Parse(color)));
        }
    }
}
