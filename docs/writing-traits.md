# Writing bastle traits

This covers what most content mods need: stat traits, attacks, abilities, diets, combat effects, procs, looks, auras, drawbacks, materials and exclusivity. Nearly all of it is XML. Traits with behaviour of their own (rising from death, splitting, blinking) need a small C# class; that is covered at the end, along with the API for making bastles from your own code.

`Defs/Traits/` in Bastle Framework is the best reference: every shape below is used by at least one of its 87 traits. `docs/bastle-traits.csv` lists them with their numbers.

## Setup

A trait is a `BastleFramework.BastleTraitDef`. Put your traits in any file under `Defs/`.

Your mod needs Bastle Framework loaded first. For a hard dependency, add `saintwacko.BastleFramework` to `<modDependencies>` and `<loadAfter>` in your About.xml. For a soft one, put the trait files in a folder that only loads when the framework is active:

```xml
<!-- LoadFolders.xml -->
<loadFolders>
  <v1.6>
    <li>/</li>
    <li IfModActive="saintwacko.BastleFramework">Mods/Bastle</li>
  </v1.6>
</loadFolders>
```

and keep `<loadAfter>` so the framework's defs are there when yours load.

Once a trait has shipped, keep its `defName`. Saved creatures refer to their traits by defName.

## A minimal trait

```xml
<Defs>
  <BastleFramework.BastleTraitDef>
    <defName>MyMod_Longstrider</defName>
    <label>longstrider</label>
    <description>Its legs are longer than they should be. It covers ground quickly.</description>
    <rumor>outruns anything on open ground</rumor>
    <namePrefix>striding</namePrefix>
    <scalesWithRarity>true</scalesWithRarity>
    <discoverWhen>
      <li>Attacks</li>
      <li>JoinsColony</li>
    </discoverWhen>
    <stage>
      <statFactors>
        <MoveSpeed>1.1</MoveSpeed>
      </statFactors>
    </stage>
  </BastleFramework.BastleTraitDef>
</Defs>
```

Traits work on any pawn: animals, humanlikes, mechanoids and anything else, unless the trait says otherwise. There is nothing to patch onto races. The info card lists the trait's effects from its stage and its other fields, so `description` is flavour text: say what it looks or feels like, with the numbers a player would want.

## Common fields

| Field | Default | What it does |
|---|---|---|
| `label` | | The trait's name, in lower case ("razorclawed"). The card shows it with its rarity once known; before that it is part of the creature's single "???" entry. |
| `description` | | Shown on the card once the trait is known. |
| `rarity` | `Common` | `Common`, `Uncommon`, `Rare`, `Epic`, `Legendary`. With `scalesWithRarity` it is the lowest rarity the trait rolls at. |
| `scalesWithRarity` | false | The trait can also roll at higher rarities, with its numbers scaled up (see below). |
| `rarityOverrides` | | Changes at a higher rarity: a new label, a better attack, an ability (see below). |
| `pointCost` | from rarity | Generation cost at the trait's own rarity. Unset uses the settings table: Common 1, Uncommon 2, Rare 3, Epic 5, Legendary 8. Negative makes a drawback. 0 means free, which is meant for material traits. |
| `commonality` | 1 | Weight among the traits that can roll at the same rarity. 0 means it never rolls and can only be added by code or a material. |
| `healthScaleFactor` | 1 | Multiplies the creature's health, on top of the health every trait point gives. Scales with rarity. Applies while the trait is still hidden. |
| `drawSizeFactor` | 1 | Draws the creature bigger (1.3 is 30% larger). Drawing only: body size and hit box don't change. Not applied to humanlikes. |
| `tags` | | Free-form strings ("Fire", "Deep", "Boss") your code can filter rolls on. `Utility` is special, see below. |
| `heritable` | true | Whether newborns can inherit it. |
| `inheritChance` | 0.5 | Chance per carrying parent. With both parents carrying it, the chances compound. |
| `marketValuePercent`, `marketValueOffset` | unset | Replace the price the trait would get from its points: a fraction of the creature's own value (1.0 doubles it), or flat silver. |

### Who can get it

| Field | Default | |
|---|---|---|
| `allowAnimals`, `allowHumanlikes`, `allowMechanoids` | true | By race kind. |
| `allowOther` | true | Anything that is none of those (Anomaly entities, many modded races). |
| `requiresFlesh` | false | Flesh races only. Use it for anything about blood, pain, toxins or digestion. |
| `disallowConstructs` | false | Not on creatures made of a material (see Materials). Use it for meat, leather, blood, pain and rot. |
| `minBodySize`, `maxBodySize` | 0, 9999 | By the race's base body size. |
| `allowedRaces`, `disallowedRaces` | | Lists of race ThingDefs. |

A trait with `products` (wool or milk) only rolls on races that are shorn or milked.

### Points and rarity

Generation gives each creature a point target and adds traits until it reaches it. A trait's points are what it costs against that target. Set `pointCost` when a trait is clearly stronger or weaker than others of its rarity (the utility trait Luminous costs 0.5: a light is worth less than the other utility traits' meat, wool or lower hunger, and at half a point it can fit beside another one in the small utility budget). Each trait point also adds 10% health and some market value by default; both are in the settings. Points are never shown to the player.

The roller picks a rarity by its weight, then a trait at that rarity by commonality. A trait much cheaper than the target per remaining slot is made less likely, so a big target buys rarer traits instead of filling the trait cap with Commons. A trait that would leave the target out of reach of the slots left is skipped. A trait that would overshoot is held back in proportion to how far over it goes. The target is a minimum, so the last trait may go over.

With `scalesWithRarity`, rolled higher, the trait's stage numbers are scaled by the rarity strength table in the settings (by default 1, 1.25, 1.5, 2, 3 from Common to Legendary), relative to its own rarity, and its points follow the points table the same way. Offsets are multiplied; factors move further from 1 in the direction they already point (x0.8 becomes x0.6 at double strength). `healthScaleFactor`, `drawSizeFactor`, `retaliation`, `lifesteal`, `deflect`, scaled attacks and proc amounts follow too. A rarity is skipped once a factor would fall below 0.05, so a strong trait may top out before Legendary. Without `scalesWithRarity`, the trait only rolls at its own rarity.

### Rarity overrides

```xml
<rarityOverrides>
  <li>
    <minRarity>Epic</minRarity>
    <label>virulent</label>
    <description>Its bite carries a virulent poison that overwhelms the victim within moments.</description>
    <hediffs>
      <li><hediff>BF_VirulentFangs</hediff></li>
    </hediffs>
  </li>
</rarityOverrides>
```

From `minRarity` up, every field the override sets replaces the trait's own; the rest stay. Lists are replaced whole, so list every ability, not only the new one. With several overrides, the highest one that applies wins. An override's stage is scaled from its own `minRarity`, so write it with the numbers it should have at exactly that rarity.

An override can set `label`, `description`, `namePrefix`, `nameSuffix`, `rumor`, `stage`, `hediffs`, `abilities`, `aiThinkTree`, `diet`, `overlays`, `effecters`, `flames`, `surfaces`, `glow`, `afterimages`, `eventVisuals`, `procs`, `healthScaleFactor`, `drawSizeFactor`, `retaliation`, `lifesteal`, `products`, `deflect`, `marketValuePercent` and `marketValueOffset`. Stormborn's "thunderclap" override adds a proc, more arcs and a brighter light at Legendary. It needs `scalesWithRarity`, and its `minRarity` must be above the trait's own.

Overrides also fill gaps: Flaming's fire resistance can't scale past Uncommon, so it rolls at Uncommon or, through its "infernal" override, at Legendary.

## Discovery

A bastle's traits start hidden. The trait's effects still apply, and the creature's name doesn't mention it. The card, the hediff tooltip and the inspect pane list the known traits followed by a single "???" while any are hidden, and the health tab reads "N known, more unknown", so the player never learns how many are left to find.

```xml
<discoverWhen>
  <li>TakesDamage</li>
  <li>Dies</li>
</discoverWhen>
```

| Trigger | Reveals the trait when the creature |
|---|---|
| `UsesGrantedVerb` | attacks with an attack one of this trait's hediffs gives |
| `UsesGrantedAbility` | casts one of this trait's abilities |
| `Attacks` | uses any attack or ability |
| `TakesDamage` | takes damage |
| `Dies` | dies |
| `Dodges` | dodges a melee blow |
| `EatsAddedFood` | eats something only this trait lets it eat |
| `StarvesNearRefusedFood` | is starving and passes up food this trait refuses |
| `JoinsColony` | joins the player: tamed, bought, recruited or rescued |
| `Manual` | never on its own: your code calls `Discover()` |

Every trigger but `JoinsColony` only counts while the player can see the creature. A trait with a `diet` and no `discoverWhen` gets `EatsAddedFood` and `StarvesNearRefusedFood`. A trait that is not visible and has no trigger logs a config error, unless it lists `Manual` or one of its procs discovers.

Some effects reveal their trait the first time the player sees them, with no trigger listed: a proc going off (see Procs), a shot turned aside by `deflect`, and an aura affecting someone. Proc traits usually list only `Dies` as a fallback.

`visible` true makes the trait known from the start. Use it only when seeing the trait is knowing it: a light that is all the trait does (luminous), a body far bigger than its kind's (colossal). A look that only hints (flames could be flaming or infernal) is better left hidden; see Looks and light. `discoveryMessage` false skips the message when it is found.

The framework also reveals traits on its own: bastles in a pack share what the player learns, newborns and Hydra young know what their parent's pack knows, humanlike colonists know their ability traits, and traders sometimes sell a bastle with some traits already known. None of that needs anything from the trait.

## Names and rumors

```xml
<namePrefix>venomous</namePrefix>
<nameSuffix>the firebreather</nameSuffix>
<nameOrder>0</nameOrder>
<namePriority>0</namePriority>
<rumor>bites with a poison that brings down a grown man</rumor>
```

Known traits add words to the creature's name: "venomous swift wolf the firebreather". `nameOrder` places them, lower further from the kind. When a creature knows more naming traits than the "Most traits in a name" setting allows (3 by default), higher `namePriority` wins a place first, then more points.

`rumor` is what survivors say about a creature with the trait. The hunt and outbreak quests use it to describe the creature they send you after: "Those who escaped it say it [rumor]." Write a verb phrase in the third person, starting in lower case, with no full stop. A trait without one is named by its label instead. Drawbacks are never used as rumors.

## Stage

```xml
<stage>
  <painFactor>0.5</painFactor>
  <hungerRateFactor>1.2</hungerRateFactor>
  <statOffsets>
    <MeleeDodgeChance>8</MeleeDodgeChance>
  </statOffsets>
  <statFactors>
    <MeleeDamageFactor>1.2</MeleeDamageFactor>
  </statFactors>
  <damageFactors>
    <Blunt>0.76</Blunt>
  </damageFactors>
  <capMods>
    <li><capacity>Sight</capacity><offset>-0.3</offset></li>
  </capMods>
</stage>
```

A vanilla `HediffStage`. Every trait's stage on a creature is merged into one hidden hediff: factors multiply, offsets add, and lists combine per stat. Most stage fields work, including `naturalHealingFactor`, `regeneration`, `totalBleedFactor`, `restFallFactor`, `disablesNeeds` and `makeImmuneTo`.

Fields that don't merge, and so do nothing in a trait stage: death, vomit and mental break MTBs, mood overrides and thought nullification, `lifeThreatening`, part-only fields and labels. Put those on a hediff in `hediffs` instead.

## Hediffs and attacks

```xml
<hediffs>
  <li><hediff>MyMod_IronJaw</hediff></li>
  <li><hediff>MyMod_Gills</hediff><part>Neck</part><severity>0.5</severity></li>
</hediffs>
```

The hediffs are added with the trait and removed with it. `part` puts the hediff on the first matching body part the creature still has; without that part, it is skipped. Any vanilla or modded hediff comp works.

A hediff that would show in the health tab gives the trait away, and logs a config error. Make it a child of `BF_TraitHediffBase`, which hides it; discovering the trait shows it.

For an attack, give the hediff a `HediffCompProperties_VerbGiver` with tools, and add `BastleScaledAttack`:

```xml
<HediffDef ParentName="BF_TraitHediffBase">
  <defName>MyMod_IronJaw</defName>
  <label>iron jaw</label>
  <modExtensions>
    <li Class="BastleFramework.BastleScaledAttack"><factor>1.3</factor></li>
  </modExtensions>
  <comps>
    <li Class="HediffCompProperties_VerbGiver">
      <tools>
        <li>
          <label>iron jaw</label>
          <capacities><li>Bite</li></capacities>
          <power>12</power>
          <cooldownTime>2</cooldownTime>
          <chanceFactor>1.5</chanceFactor>
        </li>
      </tools>
    </li>
  </comps>
</HediffDef>
```

A fixed `power` can't suit every body: fangs written for a wolf make a squirrel bite harder than a cobra. With `BastleScaledAttack`, the hediff's strongest tool hits as hard as the creature's own strongest natural attack times `factor`, times the trait's rarity strength. The natural attack is capped first by the scaled attack cap setting (12 by default, a cobra bite), so a rarer trait always hits harder. Other tools on the hediff keep their power relative to the strongest; `extraMeleeDamages` scale with it; a tool's `armorPenetration` stays as written if set and follows the damage if not. `power` only applies to a race with no attacks of its own.

## Abilities

```xml
<abilities><li>BF_Leap</li></abilities>
<aiThinkTree>BF_LeapAtTarget</aiThinkTree>
```

Any AbilityDef works. The creature gets the abilities while it has the trait.

Wild and hostile bastles cast them on their own. The framework's ability AI runs every 30 ticks and tries, in order: each trait's `aiThinkTree`; in combat, an offensive ability (`ai_IsOffensive`) at the creature's current enemy; in combat, a self-targeted ability. Every cast still goes through vanilla's checks (`aiCanUse`, cooldown, range, each comp's AICanTargetNow), so leave `aiCanUse` on. Use `aiThinkTree` when an ability needs its own conditions, such as a jump at the creature's melee or hunting target; `Defs/ThinkTreeDefs/BF_ThinkTrees.xml` has the leap's tree.

For the player:
- Ability buttons are hidden until the trait is known.
- Animals need the "trait abilities" training before the player can use the buttons. Humanlikes and mechanoids don't.
- Whether a known, trained ability is cast on its own is a setting.
- A humanlike in the player's faction knows its ability traits from the start.

With Ability AI active, Ability AI decides when trait abilities are cast. To tune how it uses your ability, patch an Ability AI extension onto the AbilityDef; `Mods/AbilityAI/Patches/BF_AbilityAI.xml` shows the framework's own, wrapped so they only load with Ability AI.

## Combat effects

```xml
<retaliation>
  <damageDef>Stab</damageDef>
  <amount>3</amount>
  <armorPenetration>0.1</armorPenetration>
</retaliation>
<lifesteal>
  <fraction>0.17</fraction>
</lifesteal>
<deflect>
  <chance>0.35</chance>
</deflect>
```

| Block | Fields (defaults) | What it does |
|---|---|---|
| `retaliation` | `damageDef`, `amount` (5), `armorPenetration` (0), `chance` (1), `igniteChance` (0), `meleeOnly` (true) | Damages whoever hits the creature. With `meleeOnly` false, shooters are hit too, wherever they stand. `igniteChance` can set the attacker on fire, on the next tick. |
| `lifesteal` | `fraction` (0.25), `meleeOnly` (true), `fleshOnly` (true) | Heals the creature by part of the damage it deals. |
| `deflect` | `chance` (0.25, capped at 0.9), `rangedOnly` (true), `reflectChance` (0.4) | The hit does nothing. A deflected shot flies back at the shooter at `reflectChance` (not scaled), and otherwise glances off in a random direction and can hit whatever is in its path. |

The retaliation `amount`, lifesteal `fraction` and deflect `chance` scale with rarity. Retaliation and other extra damage never trigger more retaliation, so two thorned creatures can fight. A deflection the player sees reveals the trait.

## Procs

A proc is "when this happens, do that": a stun on a hit, a frost blast when struck, a heal when downed.

```xml
<procs>
  <li>
    <on>Hit</on>
    <attack>Melee</attack>
    <chance>0.08</chance>
    <rarityScales>Chance</rarityScales>
    <adaptSeconds>12</adaptSeconds>
    <adaptFactor>0.5</adaptFactor>
    <toOther>true</toOther>
    <stunSeconds>2</stunSeconds>
    <text>staggered</text>
  </li>
</procs>
```

The checks run when the event happens; the effects run on the next tick, since the event arrives in the middle of vanilla's damage and hediff code. A proc going off where the player can see it reveals its trait (`discovers`, on by default). `Defs/Traits/BF_Traits_Procs.xml` has fourteen examples.

| `on` | Goes off when the creature | The other pawn |
|---|---|---|
| `Hit` | deals damage that gets through to a pawn | the pawn hit |
| `TakeDamage` | takes damage that gets through (armor stopping all of it doesn't count) | the attacker |
| `Kill` | kills a pawn | where the pawn fell (it is a corpse by the time the effects run) |
| `Downed` | is downed | none |
| `Eat` | finishes eating something | none |

When it goes off:

| Field | Default | |
|---|---|---|
| `attack` | `Any` | `Hit` and `TakeDamage`: `Melee` (from an adjacent cell, not a shot or a blast) or `Ranged` (everything else). |
| `hostileOnly` | true | The other pawn (for `TakeDamage`, the attacker) is hostile to the creature. Off: anyone, a hunted animal or a tamer included. |
| `healthBelow` | 0 | `TakeDamage`: only under this fraction of health after the hit. |
| `minDistance` | 0 | The other pawn is at least this many tiles away. |
| `chance` | 1 | |
| `cooldownSeconds`, `cooldownHours` | none | A range (`15~20`); set one. Saved with the creature. |
| `rarityScales` | `None` | What a higher rarity does besides the amounts: `Chance` multiplies the chance by the rarity strength (for offensive procs), `Cooldown` divides the wait by it (for defensive ones and lifesavers). |
| `adaptSeconds`, `adaptFactor` | 0 | With `toOther`: a repeat on the same pawn within `adaptSeconds` of the last is `adaptFactor` as strong, and once that would fall under a quarter the pawn is immune until the time runs out. `adaptFactor` 0 makes it immune at once. Keeps a stun on every bite from pinning an attacker for good. |

Who it reaches: the creature itself by default. `toOther` sends the effects to the other pawn instead. A `radius` sends them to every pawn `targets` picks within that many tiles of `center` (`Self` or `Other`), behind no wall if `blockedByWalls` (on). `targets` is `Enemies` by default and takes a list such as `Self, Allies`; allies are the creature's own faction and its allies, and for a wild creature, wild creatures of its kind. `toOther` doesn't go with a radius, and a `Kill` proc can't use `toOther` (centre a radius on `Other` instead). Damage, fire, mental states, pushes and skips need `toOther` or a radius: on the creature itself they would be a drawback.

| Effect | |
|---|---|
| `hediff`, `severity`, `hediffSeconds` | Adds the hediff, or refreshes it. `severity` below 0 keeps the hediff's own. `hediffSeconds` sets how long it lasts and needs a `HediffComp_Disappears`. |
| `heal` | Hit points healed, worst injuries first. |
| `food` | Fraction of the food need filled. |
| `stunSeconds` | |
| `damageDef`, `damage`, `armorPenetration` | Extra damage. |
| `mechanoidDamageDef`, `mechanoidDamage` | Extra damage to mechanoids only, such as EMP. |
| `igniteChance` | Sets the pawn on fire. |
| `mentalState`, `animalMentalState`, `mentalStateChance`, `psychic` | Starts a mental state (the animal one on animals, when set). `psychic` multiplies the chance by the pawn's psychic sensitivity. Never on mechanoids, entities, the downed or a pawn already in one. |
| `pushDistance`, `pushMaxBodySize` (2) | Throws the pawn straight away from the centre, stunned 1 to 3 seconds where it lands. Not the downed or anyone carrying someone. |
| `skipDistance`, `skipMaxBodySize` (3.5) | Skips the pawn to a random spot this many tiles away, as a chaos skip. |
| `smokeRadius` | A cloud of blinding smoke at the centre. |
| `casts` | Abilities, see below. |
| `fleck`, `fleckScale`, `sound` | Played at the centre. `fleckScale` below 0 is sized from the radius. |
| `text` | Floating text over each pawn reached ("staggered"). |
| `visuals` | Event visuals (below) played when it goes off. Their `on` is ignored, and the spot `Affected` means each pawn reached. They show whether the trait is known or not. |

Amounts are written for the trait's own rarity and multiplied by its rarity strength and the Procs strength setting: hediff severity and seconds, heal, food, stun, both damages and the mental state chance. Ignite chance, push and skip distance and the smoke radius don't scale. If nobody is reached and nothing else happens, the cooldown isn't spent. Damage a proc deals never sets off another proc, thorns or lifesteal, kills included.

```xml
<casts>
  <li><ability>MyMod_FrostNova</ability><target>Self</target></li>
</casts>
```

A cast fires the ability at once, with no job, warmup, range or cost, through a throwaway copy that is never added to the creature, so its own abilities and cooldowns are untouched. Only the ability's effect comps run, never its verb, so no projectile and no jump. `target` is `Self`, `Other` or `OtherCell` (a `Kill` proc needs `OtherCell`). An ability with a cooldown group is refused, casts don't go with a radius, and a `Hit` or `TakeDamage` proc with casts needs a chance below 1 or a cooldown.

The Procs settings on the Traits tab turn procs off and scale every chance (0 to 300%), cooldown (25 to 400%) and strength (25 to 300%).

## Diet

```xml
<diet>
  <addFoodTypes>Meat, Corpse</addFoodTypes>
  <removeFoodTypes>VegetableOrFruit, Plant, Tree, Seed</removeFoodTypes>
  <hunts>true</hunts>
</diet>
```

| Field | |
|---|---|
| `addFoodTypes`, `removeFoodTypes` | Food type flags, as on a race. Added types override the race's `willNeverEat`, except special trees such as the anima tree. |
| `refuseFoods` | ThingDefs it never eats. |
| `extraFoods` | ThingDefs it also eats, each with a `nutrition`. Ordinary food is eaten the normal way. Anything else (stone chunks, steel) is eaten by the framework's own job and gives `nutrition` per unit, which is required for those. |
| `hunts` | true makes it hunt like a predator, false stops it. Unset leaves it as the race has it. Removing `Corpse` always stops hunting. |

```xml
<diet>
  <extraFoods>
    <li><thing>ChunkGranite</thing><nutrition>0.5</nutrition></li>
  </extraFoods>
</diet>
```

Diets merge across traits and the creature's material. A refused food is never picked; a creature with nothing it can eat starves, as an ordinary animal would. Mutants with a diet of their own (ghouls) ignore trait diets. Creatures that carry their food to eat it (tool users) never pick living plants.

## Looks and light

```xml
<overlays>
  <li>
    <color>(1.0, 0.4, 0.05, 0.35)</color>
    <pulseSpeed>1.3</pulseSpeed>
    <pulseMin>0.5</pulseMin>
  </li>
</overlays>
<effecters><li>BF_Effecter_Embers</li></effecters>
<flames><li><count>3</count></li></flames>
<glow>
  <radius>5</radius>
  <color>(240,130,50,0)</color>
</glow>
```

- `overlays` redraw the creature's own body (and head, for humanlikes) in a colour with a glow shader, so they work on any body with no art. `color` alpha is the strength. `scale` above 1 makes a halo, `layer` below 0 draws behind the body, `pulseSpeed` is pulses per second (0 is steady) and `pulseMin` the low point of a pulse. `shaderType` Transparent gives a flat tint. Overlays follow flying, swimming and lying sprites and are hidden on corpses.
- `effecters` are EffecterDefs kept running on the creature. `scaleEffectersWithBodySize` (on) sizes them to the body.
- `flames` draws flickering fire sprites over the body: `count`, `size`, `color`, `texFolder` (vanilla fire by default).
- `glow` is real light on the map, as a lamp's, that follows the creature. The colour is a ColorInt like a lamp's `glowColor`. A creature with several glowing traits casts the largest light.
- `surfaces` are moving effects drawn on the body, masked to its outline (below).
- `afterimages` leaves faded copies of the creature behind as it runs: `intervalTicks` (5), `lifeTicks` (22), `color`. Swift's Epic override uses it.
- `eventVisuals` play once when something happens to the creature (below).

A trait's looks (overlays, flames, surfaces, effecters, afterimages, event visuals) show before the trait is known, as a hint, while the setting "Looks hint at unknown traits" is on (the default). Showing them reveals nothing: the trait stays "???" until it is discovered the usual way. `visualsBeforeIdentified` on the def overrides the setting for all its looks (`false` keeps them secret until discovery, `true` shows them whatever the setting), and `beforeIdentified` on a single surface or event visual overrides both for that entry. The older `showVisualsWhenHidden` still works as `visualsBeforeIdentified` true, but its `false` can't opt out: use `visualsBeforeIdentified` false for that. Proc visuals always show, since a proc that goes off in view identifies its trait. Auras are the exception: their glow is the aura's reach drawn on the ground, so every look of an aura trait waits until the aura is known (by default the first time the player sees it affect something); only an explicit `true` on the def or entry shows it earlier. A light always shows. Make a trait `visible` only when seeing it is knowing it (a creature that glows, one twice its kind's size); for one whose look only hints at it (flames: flaming or infernal?), leave it hidden and let the look be the hint.

### Surfaces

```xml
<surfaces>
  <li Class="BastleFramework.BastleSurface_Flow">
    <color>(1, 0.45, 0.08, 0.5)</color>
    <threshold>0.5</threshold>
    <coreThreshold>0.78</coreThreshold>
    <riseAbove>0.1</riseAbove>
  </li>
</surfaces>
```

A surface is made at runtime from whatever texture the body shows, so it needs no art and fits modded animals, flying frames and swimming sprites. Each one is built once per texture the first time it shows. Surfaces aren't drawn on portraits, on invisible creatures, or on zoomed-out humanlikes (which the game draws as a cached image). The setting "Moving body effects" turns them all off.

Every surface takes `layer` (relative to the body), `includeHead` (on: humanlikes get it on the head too) and `beforeIdentified`.

| Class | Fields (defaults) | |
|---|---|---|
| `BastleSurface_Flow` | `color`, `coreColor`, `threshold` (0.5), `coreThreshold` (0.78), `cellsAcross` (7), `cellsUp` (4), `evolveCells` (3), `loopTicks` (360), `scrolls` (2), `frames` (32), `pulseSpeed`, `pulseMin` (1), `additive` (true), `riseAbove` (0) | Patches of colour crawling up the body: flames, frost, smoke. Noise above `threshold` shows, and above `coreThreshold` turns to `coreColor`. More cells across than up makes taller patches. Keep `frames` at least 16 x `scrolls`, or the cross-fade shows double images. `additive` false blends instead of adding light, which is what frost and smoke need to show on a white coat. `riseAbove` (a fraction of the texture height, up to 0.5) lets the patches lick above the outline. |
| `BastleSurface_Sheen` | `color`, `periodTicks` (240~420), `sweepTicks` (45), `width` (0.16), `angle` (30), `frames` (16) | A bright band sweeping across the body, for polished scales or metal. Additive, so it barely shows on a white coat. |
| `BastleSurface_Glints` | `color`, `count` (2), `periodTicks` (90~200), `glintTicks` (24), `size` (0.13~0.21), `texPath` | Short star-shaped twinkles at random spots on the body. |
| `BastleSurface_Arcs` | `color`, `count` (2), `intervalTicks` (30), `chance` (0.6), `arcTicks` (5), `width` (0.14), `minLength` (0.15), `maxLength` (0.5), `texFolder` | Lightning crackling between points on the body. Lengths and width are fractions of the body's height, so a rat's sparks fit a rat. |

### Event visuals

```xml
<eventVisuals>
  <li Class="BastleFramework.BastleVisual_Flecks">
    <on>Heal</on>
    <fleck>BF_Fleck_HealGlow</fleck>
    <scale>0.9~1.2</scale>
    <minIntervalTicks>180</minIntervalTicks>
  </li>
</eventVisuals>
```

`on` is `Hit` (its damage got through to a pawn), `Struck` (it took damage from someone), `Heal` (one of its injuries healed), `Dodge` (it dodged a melee blow) or `Deflect` (it turned a hit aside). Each entry also takes `at` (`Self` or `Other`: the pawn hit, the attacker or the shooter), `chance` (1), `minIntervalTicks` (0, at most once per that many ticks per creature), `attack` (`Hit` and `Struck` only), `otherFleshOnly` and `beforeIdentified`. They only play on the map the player is looking at, at spots that aren't fogged. The setting "Event effects" turns them off, along with running afterimages; procs still work.

| Class | Fields (defaults) | |
|---|---|---|
| `BastleVisual_Flecks` | `fleck`, `count` (1), `scale` (0.5~0.7), `angle` (0~360), `speed` (0), `rotationRate`, `rotation`, `spread` (0), `color` | A burst of flecks at the spot. |
| `BastleVisual_Effecter` | `effecter`, `toward` (`Other`) | A vanilla effecter, triggered once. `toward` aims sprays. |
| `BastleVisual_Arcs` | `count` (2~3), `reach` (0.4~0.9), `width` (0.18), `arcTicks` (6), `arcColor`, `to`, `segments` (3), `jag` (0.3), `fleck`, `fleckScale`, `texFolder` | Lightning zaps around the spot, or with `to`, a jagged bolt from the spot to each `to` spot. |
| `BastleVisual_Stream` | `fleck`, `from` (`Other`), `to` (`Self`), `count` (10~14), `scale`, `color`, `lead` (0.5), `spread` (0.2), `speed` (7~9) | Flecks flowing from one spot to the other. |
| `BastleVisual_Afterimage` | `count` (3), `distance` (0.8~1.1), `lifeTicks` (30), `color`, `horizontal` (true) | Faded copies of the pawn at the spot sliding away from the other spot. |
| `BastleVisual_Ribbons` | `from` (`Self`), `to` (`Other`), `color`, `strands` (3), `bulge` (0.14), `maxBulge` (0.8), `turns`, `spin`, `chaos`, `width` (0.32), `coreWidth`, `coreWhiten`, `speed` (14), `minTravel`, `tail` (0.3), `stagger` | Glowing strands writhing along the line between two spots: life drawn out of a victim, madness poured into one. |

Stream and Ribbons use `from` and `to` in place of `at`. Put the visuals of a proc in the proc's own `visuals`, not in `eventVisuals`.

## Products

```xml
<products>
  <woolDef>Gold</woolDef>
  <woolAmountFactor>0.5</woolAmountFactor>
</products>
```

`woolDef`, `woolAmountFactor`, `milkDef`, `milkAmountFactor` change what shearing and milking give.

## Auras

```xml
<traitClass>BastleFramework.BastleTrait_Aura</traitClass>
<modExtensions>
  <li Class="BastleFramework.AuraParams">
    <radius>6.9</radius>
    <hediff>BF_Rallied</hediff>
    <affectsAllies>true</affectsAllies>
    <onlyInCombat>true</onlyInCombat>
    <ringColor>(1.0, 0.8, 0.35, 1)</ringColor>
  </li>
</modExtensions>
```

Every second (`intervalTicks` 60) the creature gives the hediff to everyone in range it can see, and refreshes it on those who have it. The hediff needs a `HediffComp_Disappears` that lasts longer than `intervalTicks` + 15, so it fades soon after a pawn leaves; 400 ticks works. With `severityPerDay` above 0 the hediff builds up instead, like toxic fallout, cut by each of `resistStats`.

| Field | Default | |
|---|---|---|
| `affectsSelf`, `affectsAllies`, `affectsNeutrals`, `affectsEnemies` | false | By relation to the creature. A wild creature's allies are wild creatures of its own kind. |
| `affectsHumanlikes`, `affectsAnimals` | true | |
| `affectsMechanoids`, `affectsEntities` | false | |
| `targetSameRace` | false | Only its own race. |
| `targetTraits` | | Only bastles with one of these traits. |
| `targetsAsleepOnly` | false | Only pawns asleep (the drawback Noisy). |
| `whileDowned` | false | |
| `whileAsleep` | true | |
| `onlyInCombat` | false | Only while drafted, in an aggressive mental state, holding an enemy target, or within 20 seconds of a fight. |
| `blockedByWalls` | true | |
| `discoverOnEffect` | true | Reveals the trait when it affects someone the player can see. |
| `showRing`, `ringColor` | on, white | The range ring, shown while selected. |
| `showGlow`, `glowColor`, `glowStrength`, `showLinks`, `linkColor` | on, ring, 0.125, off, ring | With Ideology: a ground glow sized to the radius, and lines to each pawn it affects. |
| `pulseFleck`, `pulseIntervalTicks`, `pulseScale` | none, 150, from the radius | A fleck on the creature while active. |

An aura's looks wait until the trait is known, whatever the hint setting says, since the glow is its reach drawn on the ground. An aura with a negative `pointCost` is a drawback like any other; Noisy is one, with `showGlow` off.

## Drawbacks

A trait with a negative `pointCost` is a drawback. Generation only gives drawbacks to some creatures (30% by default, at most one per roll), never as the first trait, and spends the points it gives back on more traits, so a drawback buys a stronger creature.

```xml
<BastleFramework.BastleTraitDef>
  <defName>MyMod_Skittish</defName>
  <label>skittish</label>
  <description>It startles at every sound, and flees fights it could win.</description>
  <pointCost>-1</pointCost>
  <exclusionTags><li>MyMod_Nerve</li></exclusionTags>
  <discoverWhen><li>TakesDamage</li></discoverWhen>
  <hediffs><li><hediff>MyMod_SkittishNerves</hediff></li></hediffs>
</BastleFramework.BastleTraitDef>
```

## Utility traits

Tag a trait `Utility` when it is worth something mainly once the creature is tamed: meat, wool, hauling, hunger, sleep. Utility traits are kept out of the main roll. After it, with the utility chance in the settings (40%), a second, smaller roll picks from utility traits alone, in the slots that are left. So a utility trait never costs a creature combat strength, and its points add no health. A drawback tagged `Utility` rolls in that second roll, so it can only buy utility traits.

## Exclusivity

`exclusionTags`: two traits sharing any tag never share a creature. The framework's own tags are `BF_Element`, `BF_FireAffinity`, `BF_Speed`, `BF_Toughness`, `BF_Healing`, `BF_Hunger`, `BF_Rest`, `BF_Size`, `BF_Sight`, `BF_Death`, `BF_Stun` (one stunning blow), `BF_MeleeAnswer` (one answer to being struck in melee), `BF_Blood` (bleeding faster or slower), `BF_HeatTolerance` and `BF_ColdTolerance` (the comfortable temperature range), and `BF_TempDrawback` (at most one temperature drawback). Reuse them when your trait overlaps one of the framework's (a second fire body should take `BF_Element` and `BF_FireAffinity`), and make your own prefix for the rest.

A trait that only grants abilities isn't added where another trait already grants all of them.

## Materials

A bastle can be made of a material: any metal, stone or wood. A material creature has no pain, blood or rot, is drawn in the material's colour, gets armor and damage from the material, and always has the material's traits. Material traits are ordinary traits with `commonality` 0, `pointCost` 0 and `heritable` false, and usually `visible`.

To give a material traits or tune it, add `BastleMaterialExtension` to the stuff:

```xml
<Operation Class="PatchOperationAddModExtension">
  <xpath>Defs/ThingDef[defName="Jade"]</xpath>
  <value>
    <li Class="BastleFramework.BastleMaterialExtension">
      <traits><li>MyMod_Jadeheart</li></traits>
      <commonality>0.5</commonality>
    </li>
  </value>
</Operation>
```

| Field | Default | |
|---|---|---|
| `traits` | | Traits every creature of this material has. |
| `allowRolling` | true | false keeps it out of random rolls; code can still set it. |
| `forceAllowed` | false | Lets a stuff that isn't metal, stone or wood be rolled. |
| `commonality` | 1 | Multiplies the stuff's own commonality. |
| `armorFactor`, `damageFactor`, `butcherYieldFactor` | 1 | Multiply what the material gives. |
| `tags` | | Free-form, like a trait's. |

Several mods can each add one to the same stuff. They are merged at startup: traits and tags combine, factors multiply.

## Hydra young

When a hydra-blooded creature splits, its young take the parent's mental state and group. For a hediff that keeps a creature in a state of its own (as Scaria does), add `<li Class="BastleFramework.BastleSplitInherited" />` to the HediffDef's `modExtensions` and the young get it too.

## World bastles

Bastles turn up among wild animals, animal events, faction groups and trader stock without anything from your mod. To change how often for your own defs:

| Extension | On | Fields |
|---|---|---|
| `BastleIncidentExtension` | IncidentDef | `chance` (below 0 uses the setting), `pointFactor`, `onePack` (one leader, the rest copies in one pack) |
| `BastleFactionExtension` | FactionDef | `groupChance`, `maxAnimals`, `pointFactor` |
| `BastleTraderExtension` | TraderKindDef | `chance`, `pointFactor` |

Only incidents with the extension convert their animals. The framework adds it to vanilla's manhunter ambush, herd migration, thrumbo passing, farm animals wandering in, alphabeavers and Anomaly's frenzied animals.

The incident workers `IncidentWorker_BastleSighting`, `IncidentWorker_BastlePack` and `IncidentWorker_BastleRampage` are public, so your own IncidentDefs can reuse them. Quests can use the site parts by tag: `BF_BastleLair` and `BF_BastleOutbreak`.

## DLC and other mods

Put `MayRequire="Ludeon.RimWorld.Biotech"` (or the mod's packageId) on any trait that names a def from that DLC or mod, and on the defs that go with it. Without the DLC the trait isn't loaded and nothing else changes. Firebreather and Acid-spitting (Biotech), Dread (Odyssey) and Void-touched (Anomaly) do this.

## Translation

`label`, `description`, `namePrefix`, `nameSuffix` and `rumor`, the same fields in `rarityOverrides`, and each proc's `text`, are injected like any def's, under `Languages/<Language>/DefInjected/BastleTraitDef/`.

## Testing

Load errors name the trait: a hidden hediff that shows in the health tab, an override that can never apply, a rumor in the wrong form, a diet that hunts without corpses, a proc with no effect or a field its event doesn't have, afterimages with a `lifeTicks` or `intervalTicks` below 1. Fix every one; most mean the trait would never roll, would give itself away, or would do nothing.

In dev mode, the Bastle Framework category of the debug actions has "Add trait..." (variable traits ask for a rarity), "Roll traits..." (pick a point target), "Spawn bastle..." (an animal of a kind you pick or a random one, rolled at a point target, wild or manhunter, with its traits known), "Set material...", "Discover all traits", "Spawn linked pack (3 copies)", "Log discovery group" and "Clear bastle". The bastle events fire from the vanilla incident menu (`BF_BastleSighting`, `BF_BastlePack`, `BF_BastleRampage`) and the quests from the dev quest generator (`BF_BastleHunt`, `BF_BastleOutbreak`).

## Traits with code

For behaviour XML can't describe, subclass `BastleFramework.BastleTrait` and name it in `traitClass`. Put tuning numbers in a `DefModExtension` on the trait def so other mods can reuse your class with their own numbers; the framework's classes do this (`UndyingParams`, `PhasingParams`, `HydraParams` and others in `Source/BastleTraitClasses.cs`). Two of them need no code to reuse: `BastleTrait_Shedding` with `SheddingParams` (`filth`, a filth ThingDef, hair by default; `intervalTicks`, 5000~10000) and `BastleTrait_Sweltering` with `SwelteringParams` (`heatPerSecond` 6, where a heater is 21; `maxTemperature` 35). Sweltering heats rooms only, never from a doorway.

| Virtual | Called |
|---|---|
| `Stage` | Override to change the stage, for example only in light (`BastleTrait_Lightsick`). Call `pawn`'s bastle `StageChanged()` when the answer changes. |
| `PostAdded()` | After the trait is added, not on load. |
| `CopyStateFrom(source)` | When `CopyBastle` copies the trait onto another creature. |
| `PreRemoved()` | Before the trait or the whole bastle is removed. |
| `PostDiscovered()` | After the trait is revealed. Also runs on packmates that learn it quietly, who may be off the map or dead. |
| `TickInterval(delta)` | Only called when your class overrides it, so traits that don't tick cost nothing. |
| `Notify_PawnPostApplyDamage`, `Notify_PawnUsedVerb`, `Notify_PawnDamagedThing` | Combat events. To deal damage from one of these, queue it with `BastleScheduler.NextTick` and use `BastleCombatUtility.DealExtraDamage`, which never triggers retaliation. |
| `Notify_PawnKilled()` | Just before death, while the mental state and lord are still there. |
| `Notify_PawnDied(dinfo)` | After death. |
| `TipStringExtra` | Extra lines on the card once known. |
| `ExposeData()` | Save your state; call the base. |
| `ShowsVisual(entryBeforeIdentified)` | Whether a look shows now. Override to change when looks show before discovery (auras do). |

Use `Discover()` to reveal the trait yourself, after checking `BastleUtility.PlayerCanObserve(pawn)`. Changing a def's `traitClass` later is safe for saves.

## Making bastles from code

`BastleFramework.BastleUtility`:

| Method | |
|---|---|
| `RollTraits(pawn, request)`, `RollTraits(pawn, points, request)` | Rolls traits. Roll after the pawn has its faction. |
| `AddTrait(pawn, def, discovered, force, rarity)`, `RemoveTrait`, `CanAddTrait` | One trait. |
| `GetBastle`, `IsBastle`, `GetOrMakeBastle`, `ClearBastle` | The creature's bastle hediff. |
| `CopyBastle(from, to, includeDiscovery, linkDiscovery)` | Copies every trait and the material. `linkDiscovery` puts both in one pack, which shares what the player learns. |
| `LinkDiscovery(pawns)`, `DiscoveryGroupOf(pawn)` | Packs. |
| `InheritTraits(child, mother, father)` | Already called for births; call it for your own offspring. |
| `GetMaterial`, `SetMaterial`, `RollMaterial`, `IsConstruct` | Materials. |
| `DiscoverAll`, `PlayerCanObserve` | Discovery. |

`BastleRollRequest` changes one roll; every field left null uses the settings: `points`, `maxTraits`, `drawbackChance`, `maxDrawbacks`, `rarityWeights`, `allowedRarities`, `validator` (a filter, for example `d => d.tags != null && d.tags.Contains("Fire")`), `overshootStrictness`, `utilityChance`, `utilityBudget`. A request is never modified, so you can keep one and reuse it.

`BastleWorld.MakeBastle(pawn, traitPoints, request)` rolls a creature the way world bastles are rolled, and `BastleWorld.TraitPointsFor(threatPoints)` turns raid points into trait points with the settings' strength curve.
