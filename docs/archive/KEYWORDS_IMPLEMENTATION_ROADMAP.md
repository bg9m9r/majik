# Keywords Implementation Roadmap

## Executive Summary

**Total Keywords in Database**: 728  
**Official Magic Keywords (Rule 702)**: 189 (confirmed from rules)  
**Currently Implemented**: 50  
**Remaining Official Keywords**: 139  
**Card Names (to filter)**: ~400-500 (estimated)  
**Custom/Unknown**: ~50-100 (estimated)

## Implementation Priority

### Tier 1: High Priority (Week 1-2)
**Goal**: Implement all common, frequently-used keywords

**Static Abilities** (30 keywords):
- ✅ Already implemented: flying, haste, trample, vigilance, deathtouch, lifelink, hexproof, shroud, indestructible, defender, menace, skulk, fear, intimidate, horsemanship, shadow, reach, flash, infect, wither, toxic, decayed, protection
- ⏳ To implement: banding, phasing, cumulative upkeep, echo, fading, flanking, rampage

**Triggered Abilities** (20 keywords):
- ⏳ To implement: prowess, landfall, exalted, enrage, constellation, delirium, morbid, raid, revolt, threshold, hellbent, metalcraft, spell mastery, fateful hour, ferocious, formidable, battalion, celebration, coven, domain

**Activated Abilities** (15 keywords):
- ✅ Already implemented: cycling variants, equip
- ⏳ To implement: channel, flashback, kicker, multikicker, buyback, retrace, rebound, replicate, overload, dash, bloodrush, bestow, evoke, morph, ninjutsu

### Tier 2: Medium Priority (Week 2-3)
**Goal**: Implement remaining official keywords

**Complex Keywords** (50 keywords):
- Suspend, vanishing, delve, convoke, storm, cascade, annihilator, level up, undying, miracle, soulbond, scavenge, unleash, cipher, evolve, extort, fuse, tribute, dethrone, outlast, exploit, renown, awaken, devoid, ingest, myriad, surge, emerge, escalate, melee, crew, fabricate, partner, undaunted, improvise, aftermath, embalm, eternalize, afflict, ascend, assist, mentor, afterlife, riot, spectacle, companion, daybound/nightbound, disturb, cleave, training, compleated, reconfigure, blitz, casualty, enlist, read ahead, ravenous, squad, backup, bargain, craft, plot, saddle, spree

### Tier 3: Low Priority (Week 3-4)
**Goal**: Handle edge cases and custom keywords

- Custom/unofficial keywords
- Rare keywords
- Keywords requiring special handling
- Fallback system for unknown keywords

## Implementation Strategy

### Step 1: Categorization (Day 1-2)
1. Run `KeywordAnalyzer` on CSV file
2. Generate categorization report
3. Identify all official keywords
4. Filter out card names
5. Create implementation priority list

### Step 2: Template-Based Implementation (Day 3-7)
1. Create keyword implementation templates
2. Implement Tier 1 keywords using templates
3. Test each keyword
4. Register in KeywordRegistry

### Step 3: Complex Keywords (Day 8-14)
1. Implement Tier 2 keywords
2. Handle special cases
3. Integrate with existing systems
4. Comprehensive testing

### Step 4: Fallback System (Day 15-17)
1. Implement oracle text fallback
2. Generic keyword handler
3. Unknown keyword tracking
4. Manual review queue

### Step 5: Testing and Refinement (Day 18-20)
1. Test all keywords from database
2. Validate filtering accuracy
3. Performance optimization
4. Documentation

## Keyword Implementation Templates

### Template: Simple Static Ability
```csharp
Register("{keyword}", new KeywordInfo(
    KeywordType.Static,
    layer: 6,
    description: "{description from rules}",
    createAbility: (source, controller) => new StaticAbility(
        source,
        controller,
        "{Keyword}",
        isActiveCheck: () => source is Cards.Permanent p && p.Zone == Zones.ZoneType.Battlefield,
        applyEffect: () => { /* Handled by {system} */ }) as object));
```

### Template: Triggered Ability
```csharp
Register("{keyword}", new KeywordInfo(
    KeywordType.Triggered,
    layer: null,
    description: "{description}",
    createAbility: (source, controller) => 
    {
        var trigger = CreateTriggerCondition(...);
        var effect = CreateEffect(...);
        return new TriggeredAbility(source, controller, null, new[] { effect });
    }));
```

### Template: Activated Ability
```csharp
Register("{keyword}", new KeywordInfo(
    KeywordType.Activated,
    layer: null,
    description: "{description}",
    createAbility: (source, controller) => 
    {
        var cost = ParseCost(...);
        var effect = CreateEffect(...);
        return new ActivatedAbility(source, controller, null, new[] { cost }, new[] { effect });
    }));
```

## Estimated Timeline

- **Week 1**: Categorization + Tier 1 keywords (70 keywords)
- **Week 2**: Tier 2 keywords (50 keywords)
- **Week 3**: Tier 3 + fallback system (remaining)
- **Week 4**: Testing, refinement, documentation

**Total**: ~4 weeks to support all keywords

## Success Metrics

- ✅ 95%+ of official Magic keywords implemented
- ✅ 99%+ card name filtering accuracy
- ✅ 100% of keywords have some handling (even if placeholder)
- ✅ < 10ms keyword processing per card
- ✅ Comprehensive test coverage
