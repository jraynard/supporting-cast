# Roadmap

What's built, what's under way and what's next, oldest first. The direction is in
[design.md](design.md); each big feature's design record is in `docs/plans/` (listed at the end).

## Done: foundation (M0–M5)

- **M0 Foundation** — solution (Engine / Sim / Game / Tests; the engine has since moved out into
  the Narsil submodule), GLFW window, input, game loop with frame-phase profiling
  (`DW_PROFILE=1`), logging.
- **M1 Vulkan 2D renderer** — instance/device/swapchain with resize, 2 frames in flight, ImGui via
  BRUTAL backends, runtime GLSL (shaderc), PNG atlas (stb), instanced `SpriteBatch` (50k sprites
  ≈1.5 ms), `Camera2D` + controller, overlay/line/rect drawing. Chunk-cached tile buffers were
  skipped: resubmitting visible tiles each frame is far below budget.
- **M2 Simulation core** — sparse-set ECS, deterministic RNG + state hash, clock, JSON defs with
  reference resolution, map layers, generator, regions, rooms + auto-roof.
- **M3 Villagers** — A* pathfinding, reservations, coroutine jobs, needs/work brains, work
  priorities, hauling, stockpiles, blueprints → delivery → construction, chop/mine/harvest.
- **M4 Environment** — per-tile heat diffusion with insulation, campfire fuel, fire spread/damage/
  burn-out + firefighting, day/night climate and lighting.
- **M5 Dragons** — egg incubation band and damage, hatching + keeper imprinting, life stages,
  feeding/bonding jobs, dragon brain (warmth-seeking, following keeper), fire sneezes.
- **Game layer** — rendering of all of the above, architect tools, inspector, work tab, messages
  (auto-pause on danger), debug panel with overlays and dev tools, `--demo`, `--bench`.
- **BRUTAL License compliance** — logo splash, Credits window, `NOTICE.txt` and third-party
  `licenses/` shipped with build and publish output.
- **Verification** — headless tests from the start (26 then, about 700 now) incl. 10k-tick
  determinism and a full slice playthrough on two seeds.

## Done: the 3D world

Timberborn-style view: Z levels, a rotatable ortho camera, multi-storey building, dragon flight,
3D heat/fire/smoke and fire breath. Phased so the slice tests stay green throughout.

1. ✅ Sim on 3D cells (`int3`, `Map.Block`, ground at z = 0, walk level z = 1); top-down view of one level.
2. ✅ Terrain height, digging, stairs/ramps in the walk graph; environment runs on active cells only.
3. ✅ Vulkan depth buffer, `Camera3D`, chunk meshes, billboards, voxel picking, layer cutaway, light volume.
4. ✅ Floors (own layer), ladders, support rules, blueprints on blueprints; real roofs from floors/rock above.
5. ✅ 3D heat (rises through floors and stairwells), fire climbing, smoke layer + puffs, 3D point lights.
6. ✅ Dragon flight (juveniles and adults): composed walk/fly routes, gliding, perching, ground shadows.
7. ✅ Fire-breath cone with splash: ordered from the dragon's inspector (obedience from affinity); sneezes reuse it.
8. ✅ Slice: a two-storey hall (upper storey ordered once the fire is lit), dig + mine for the nest's stone,
   a week of supplies, villagers leaving meat for the dragon; the scenario test ages the dragon to see
   it fly. Release bench: 0.045 ms/tick on the 160×160×12 map.
9. ✅ Furniture sized for the taller look: rectangular footprints that turn (1×2 beds, 2×1 workbenches,
   1×1/1×2/2×2 tables picked as shapes of one build button); built roofs (thatch, shingles) out to
   three cells from a wall, roofs drawn over enclosed rooms, a roof toggle (`\`, hidden by default)
   with an outline of what's sheltered.

## Done: combat slice (turn-based raid) and first-pass health

- Injuries on body parts (humanoid, dragon), bleeding, pain, capacities, collapse and death;
  Doctor work rescues the downed to bed and bandages wounds; fire and falls now cause injuries.
- Weapons (bow, axe, club, short sword) and natural attacks (fists, bites, claws), dragon scales,
  a hostile Raider faction that walks in and flees when broken.
- Turn-based encounters: move range from speed (flyers fly and may hover), one action per turn,
  hit → hurt → location rolls with a breakdown tooltip, breath that burns friend and foe, dragon
  obedience with instinctive action on refusal, world time advancing between rounds.
- `--raid-demo` (+ `--auto-combat`), HUD with unit list and log, `RaidScenarioTests` on two seeds.

## Done: combat follow-ups

- Skills (Melee, Shooting, Medicine, Construction, Mining, Plants, Social) that grow with practice
  and feed work speed, accuracy, tend quality and recruiting.
- Apparel and armor per body part (jerkin, cap, iron helm, chain shirt): turns or blunts blows,
  wears out; raiders wear it, villagers pick up what's lying around (weapons too).
- Tend quality, infections racing immunity, scars from deep wounds.
- Prisoners: downed raiders stabilized after a fight, or captured to prisoner beds; Warden work
  feeds them, recruits them (resistance, Social) or releases them.
- A storyteller (Peaceful / Normal / Hard, chosen when an Adventure starts; off in tests and demos) that sends
  raids from the map edge, sized by days survived, people, weapons and dragon growth.

## Done: combat follow-ups, part 2 (docs/plans/combat-follow-ups.md)

- Armor drawn on people: apparel defs name a `worn` layer sheet, drawn in the people's frame
  layout over the body (under the hair for body armor, over it for helms), darker once battered.
  Placeholder layers for the cap, helm, jerkin and chain shirt; `--check-art` validates them and
  `--art-preview` shows every look dressed in each set.
- Overwatch and reactions (`Combat/Reactions.cs`): a ranged unit can end its turn watching and
  shoots once at the first enemy moving into its line of fire; stepping out of a melee fighter's
  reach draws a free swing. Reactions hit at ×0.7, a downed mover stops where it falls. The move
  preview turns red where a move draws a reaction; the AI weighs those threats and archers with
  nothing to shoot go on overwatch. **Superseded by time units** (docs/plans/combat-time-units.md):
  reactions are paid from TU kept from the unit's own turn.
- Doctors triage the downed by who bleeds out soonest (overwatch made this matter in the raid).
- "Take off" orders that stick (`Outfit`, `GearOrderCommand`/`WearCommand`): a slot ordered bare
  stays bare until allowed again.
- Prisoner mood: imprisonment weighs on them, warden talks and meals lift it and their opinion
  of the warden, and a content prisoner listens better. Miserable, unwatched prisoners may break
  out (`PrisonBreakSystem`), grab a weapon and run; recaptured, they resist recruiting harder.
- Save format 4.

## Done: save/load (M6)

- Binary saves (`Sim/Saving`): Brotli body, header readable on its own for menus, defs by name.
  Jobs are dropped on load; a load is exactly the saved game with every job interrupted, and
  `SaveLoadTests` checks it plays on tick for tick from there.
- Named saves, F6/F9 quicksave, autosaves at dawn and just before a fight (fights can't be saved),
  Continue / Load Game on the title, `--load` and `--check-save`.

## Done: def and shader hot reload

- Editing `data/defs/*.json` moves the running village onto the new defs (a save round trip, so
  jobs restart; bad JSON or a removed def in use shows an error and changes nothing). F8 forces it.
- F7 recompiles the quad and sprite shaders from the checkout; a compile error keeps the old look.
- Save format 2 stores construction deliveries per cost entry, so cost-list edits keep them.

## Done: food chain for dragons

- Hunger: a life stage's hunger factor sizes the stomach (each unit fills less of the bar) instead
  of being applied twice; an adult eats ~23 meat a day and a full bar lasts the night.
- Wildlife (`WildlifeSystem`, own random stream): herds of deer, hares and boar walk in from the
  edge about every 20 h, graze, shy away from people and move on. Generated maps start with two.
- Hunting in real time (`RealtimeAttacks` reuses the combat odds and wounds): Hunt orders, bow
  stalking, butchering carcasses into meat; boar charge back. Fishing from marked bank cells, with
  per-patch stocks that thin out and recover.
- Spoilage (`RotSystem`): food and carcasses rot by temperature; stacks average it.
- Start kit 80 meat + a bow; `FoodChainScenarioTests` keeps an adult fed for five days.
- Food poisoning: food past half spoiled may make the eater ill (slower, works less, keeps little
  down; it passes, sooner in bed). People eat fresh food first; dragons rarely mind carrion;
  cooking halves rot, so a meal from turning meat is safe.
- Butcher spot: hunters carry carcasses there (a deer slows them, a hare doesn't), butcher them
  1.5x as fast with a tenth more yield, and the meat lands by home instead of out in the field.
- Ride pose: riders sit astride (knees out, hands on the reins, a bob) in all four views, facing
  the way the dragon goes, hips on the saddle. (The saddle point is one per sheet; following the
  wingbeat frame by frame needs per-frame points in Narsil's sheets.)
- Saddles: a dragon saddle (tanning rack: 10 leather, 3 wood) the keeper buckles on from the dragon's
  inspector. The rider aims 10% truer, the scales under it are a little tougher, and its saddlebags
  carry 60 kg; it travels with the dragon. Riding bareback plays as before. (Drawn for the Hearth
  adult; the Norse dragon's saddle art is still to do.)
- Hunting from dragonback: F shoots game from the saddle (from its height); right-click an animal
  and the dragon gives chase, rider aboard, leaving the kill (a new trick, Hunt with me, helps it
  obey). Fixed along the way: a diving dragon tried to bite from the air, which the odds refuse, and
  spent its cooldown on it, so stoops on running game hardly ever landed.
- Rescuing downed dragons: doctors carry a downed hatchling or juvenile to a nest; a downed adult
  is too big, so it's fed and tended where it lies and mends faster beside a lit fire. A devoted
  grown dragon carries its downed keeper home to bed. The player can pick up a downed hatchling (F)
  and lay it in a nest, or carry a carcass to the butcher spot.
- Breeding: dragons are male or female (save format 16). A grown pair of the village, fed, content
  and at least accepting of their people, with a nest free, court and she lays a clutch of one or
  two eggs a few days later, each of either parent's breed and element; then she rests for 20 days.
  The keeper button names the dragon when there are several; the Debug panel's "Add a dragon" gives
  a mate. Fixed along the way: a new egg's keeper defaulted to entity 0 (often a villager), not nobody.
- Animals in the storyteller: wolf packs and bears come for the village and are fought turn by
  turn like raiders (a pack runs at half strength); great herds migrate across the valley for a
  few hours' hunting; wild dragons visit, and one fed three meals from the stores settles in (of the
  other sex to your dragon: a breeding pair). Debug panel buttons send each. Save format 17.
- Follow-ups: the Norse dragon's saddle art; the saddle point following the wingbeat (per-frame
  sheet points in Narsil); picking a dragon's sex in the creator and keeping it in the co-op roster;
  claiming a clutch egg in co-op; keepers saddling up on their own; predators hunting the herds
  between raids; caring for a visiting dragon by hand (pet, groom) as well as feeding it.
- Hunting orders: per dragon (eat stores first / hunt first / don't hunt unless starving, bring
  kills home to the butcher spot, take boars, range from the nest) and for the village (keep N
  meat in store: the larder marks harmless game near home, never the last of a herd).

## Done: dragon progression

- Dragons take work through `WorkGivers`, gated per life stage (juveniles burn; adults burn, haul
  and fell trees) and by obedience; they're in the Work tab. Burned groves leave Chop-marked snags.
- Adults haul four stacks a trip (`BulkHaulJob`).
- Riding (`RidingSystem`): the keeper rides an adult that trusts them, overland and through the
  air; obedience +0.25 mounted. In combat the pair are two linked units: the dragon moves and
  breathes, the rider shoots from its back with the height, some blows land on the mount.
- Neglect (lonely or starving) wears affinity down, and with it obedience, work and riding.

## Done: mood & social

1. ✅ Mood: humans and dragons ease toward 50 plus their thoughts (`data/defs/mood.json`).
   Situational: hunger, rest and bond bands, cold/heat, pain, sitting in the dark, warm sleep,
   carrying the keeper. Memories: raw meat, where they slept, deaths (keeper and dragon hardest),
   the hatching, fights won or lost, a keeper's feeding and play. Mood scales human work speed
   (x1 between 35 and 65, x0.85 miserable, x1.1 elated; not the avatar) and dragon obedience
   (±0.1). Inspector: mood bar, Mood tab with every thought. No mental breaks yet.
2. ✅ Opinions and social interactions: memories about someone carry an opinion of them, plus a
   fixed per-pair compatibility; friends (25+) grieve harder, rivals (−20) hardly at all. People
   nearby chat, have deep talks, slight or comfort each other, and keepers pet their dragons
   (`SocialSystem`, own random stream), with speech bubbles and a Social tab. Opinions also come
   from rescues, tending, capture, recruiting, fighting side by side, and feeding a dragon.
3. ✅ Loyalty and traits: companions weigh their mood and opinion of you before agreeing to a
   request (the button's tooltip shows the odds and why); a refusal sticks for an hour, asking
   someone who's down costs goodwill, and a companion who turns against you drops a standing
   request. Humans roll 0–2 traits (optimist, gloomy, hardy, dragon friend, kind, abrasive,
   thick-skinned, glutton, loner; `mood.json`), the character creator picks the avatar's, and
   they travel with the party. Kept dragons rest, bask and perch where their keeper can walk
   (they had been sitting on unreachable plateaus, unfed), and come down when hungry or lonely.

Follow-ups: mental breaks, romance and family, room beauty and impressiveness, dragon traits
from incubation ("the egg remembers"), "old ties" opinions carried from Ashford, and a "burned me"
opinion once fire remembers who lit it. (Cooked meals came with crafting, prisoner mood with the
combat follow-ups.)

## Done: inventory and equipment, first pass (docs/plans/inventory-equipment.md)

1. ✅ Weight: every item def has a `mass` (kg per unit); a def without one fails to load.
2. ✅ Packs: a Back apparel slot; a satchel (25 kg) is the first `container` item. Contents are
   `Stored` in the pack, follow it (worn, dropped, hauled), rot as usual, spill if it burns, and
   are never taken by others' jobs. Swapping packs moves what fits across. Save format 6.
3. ✅ Player: F picks up into the pack, then the hands; building, refuelling and eating take from
   the pack; F on gear lying in reach puts it on when its slot is empty. Inventory window (Tab),
   hotbar (1–4), storage windows with your pack beside them; travel carries gear and pack.
   Game speed moved to Ctrl+1–3.
4. ✅ Companions: start with satchels, haul several stacks per trip and bring a site's whole cost
   at once; their pack only holds a job's load. The inspector's Gear tab shows packs and has a
   "Wear…" picker (`WearCommand`).

Follow-ups: crafting and upgrading packs (Crafting), encumbrance, raiders carrying packs and loot,
pinned hotbar slots, drag and drop.

## Done: crafting (docs/plans/crafting.md)

1. ✅ Workbenches and bills: stations hold bills (do N, until you have N, forever; material
   filter; pause; reorder) and the craft under way, whose ingredients are used up as they arrive,
   so it survives interruptions and saves. Craft and Cook work; the player picks a recipe at a
   station (F) and holds F. Planks keep their wood; plank walls and floors beat log ones. Skills
   Crafting, Cooking and Hunting (butchering), rolled without moving anyone's old skills. Save format 7.
2. ✅ Recipe books: skill unlocks (village: anyone's level; player: their own), discoveries from
   handling things (hides, clay, iron ore, iron bars, dragon scales) or building a station, and
   companions teaching the player (a social interaction). Hand recipes (club, satchel).
3. ✅ Leather and cooking: hides from butchering, tanning rack, leather gear and a 40 kg leather
   pack; cooked meals at a lit campfire, eaten first, with a mood boost.
4. ✅ Heat industry: clay banks and iron ore veins (kept off the landing), mason's bench and cut
   stone, kiln (charcoal, bricks) and forge (iron, tools, weapons, armor), fired only while there's
   work. Dragons Stoke a forge to iron heat, which charcoal alone can't reach in the open.
5. ✅ Quality (crude to masterwork, scales armor and damage; using a friend's work raises your
   opinion of them), tools that gate or speed recipes and chopping/mining, mending gear (player),
   and dragons shedding scales for heat-proof scale armor.

Follow-ups: villagers mending gear by bill, carrying tools to the work, a hearth and better meals,
room beauty from fine furniture, recipe books as items and
trade, the Ashford keeper teaching a recipe in the prologue, saddles, upgrading a worn pack in place.

## Done: mining and the tool ladder (0.1.0-alpha.4, docs/plans/mining-progression.md)

1. ✅ Tiers: named (stone, copper, bronze, iron, steel, dragonforged); a pick's tier decides what
   rock it breaks (`digTier`), better ones go faster; "needs a copper pick" on hover and in orders.
2. ✅ Materials that matter: wood and stone change HP, insulation, flammability, work and weight.
   Five kinds of stone in strata (sandstone, limestone, granite, slate, basalt), carried through
   cut stone and walls ("cut granite wall").
3. ✅ Ore hidden until a face is opened (stains on the rock hint at it), boulders sometimes hold
   some; copper, tin, coal, iron, silver, gold and ember crystal as data-driven veins. Kiln
   smelting (copper, bronze, silver), coal and fuel chosen by the heat a craft needs.
4. ✅ Copper and bronze tools, spears, swords, maces, a studded jerkin, bronze helm and cuirass;
   raiders come at most a tier past the village's best gear.
5. ✅ Steel: bellows the smith pumps, iron and steel on coal without a dragon; steel tools,
   longsword, sallet and plate (heavy: it costs time units). Dragonforged gear is folded steel.
6. ✅ Silver and gold: a goldsmith's bench for coin, rings and torcs (a Neck slot); treasure
   draws raiders, and dragons sleep happily on a hoard and love a gift of gold.
7. ✅ The deep tier: ember crystal in the basalt (steel picks; a dragon blade; a dragon's treat),
   lime mortar for the toughest stone walls.

## Done: dragon interactions and bond (0.1.0-alpha.5, docs/plans/dragon-interactions.md)

1. ✅ Interactions as defs (`dragons.json`): pet, hand-feed, play, groom, comfort, train, rest
   together, a gift of treasure, all through one rule (`Bonding.Interact`) for the player and AI
   keepers, with novelty (the same thing again soon counts less). Save format 10.
2. ✅ Adventure care: F at your own dragon opens the care window; F answers what it's asking for,
   or feeds it what you hold.
3. ✅ Wants (`DragonCareSystem`, own random stream): a dragon asks for things, with a bubble, and
   an ignored want costs a little trust; village keepers answer them too.
4. ✅ Hatchlings and trust: imprinting in the first day, hand-fed hatchlings and the feeding
   trough, named trust levels (wary, accepting, trusting, devoted, soulbound) with slower gains as
   it climbs, a Bond section in the inspector, Call. Long neglect ends in a sulk, and an
   untrusting dragon leaves.
5. ✅ Training: tricks (come, stay, hold fire, steady, fire on command, fetch and carry). A
   devoted dragon brings a share of its kills; a soulbound one's fire never touches you.

Left to do: the social Pet still adds bond directly instead of through `Bonding.Interact`; the
combat unlocks (a devoted dragon guarding its keeper unasked, a soulbound one answering whoever
hits them); pet / play / groom animation tags, a sound and the novelty text line.

## In progress: Adventure mode (player avatar; formerly Bearer mode)

The main campaign direction (see docs/design.md): one player-made character, WASD movement, hold F
to work, autonomous companions, turn-based combat where you only take your own turn.

1. ✅ Avatar core: `Avatar` component, `ISimCommand` queue, WASD stepping, hold F to work, Z to dig.
2. ✅ Character creator (Adventure): body type, short/long hair, hair/eye/skin colour, name; tinted
   sprite layers for everyone. ✅ Solo start: orders unlock when a settler moves into a free bed indoors.
3. ✅ Companions: "Follow me" / "Guard the egg" requests; AI-controlled companions in combat; the
   dragon commandable at high affinity. Companions weigh mood and opinion of you before agreeing (see mood & social).
4. ✅ Wagon: a 16-stack container holding the starting kit; companions eat and build from it.
   Next: haulers storing into it, pulling it to a new site.
5. ✅ Prologue: Ashford, an NPC village where you're the youngest hand; Orla, its dying keeper,
   entrusts you with the egg and her wagon; setting out carries a `TravelParty` to a new map.
   Next: an overworld where Ashford stays a place you can return to (save it as a site), the
   journey itself, pursuers, and old ties with the villagers.
6. ✅ Two dragon starts (docs/plans/adventure-mode.md): **Escape** (a captors' camp, combat on) and
   **The Sorcerer's Pact** (the enchanted forest, then a tower to build; peaceful until it
   stands), each with a bonded adult dragon from the dragon creator, which hunts for itself and
   carries you to the new map by air.
7. ✅ Hunting as the avatar: hold F to shoot or strike at game, butcher the carcass.

## In progress: drop-in co-op (alpha, docs/plans/coop-multiplayer.md)

Host-relayed lockstep over LAN or a typed address; everyone builds the same village and raises
their own dragon.

1. ✅ Engine: `Narsil.Net` (lockstep host/client, drop-in join by barrier + snapshot, checksum
   desync detection and resync, TCP and loopback transports, LAN discovery, player identity).
2. ✅ Every player action is an `ISimCommand` (overseer tools and inspector included), encoded by
   `CommandCodec`; `CommandRules` lets players steer only their own character and dragon.
3. ✅ Players: `PlayerCharacter` remembers whose a character is; joining gives it back (or opens
   the character creator, arriving with an egg that imprints on you); leaving hands it to the AI.
4. ✅ Game: Co-op menu (host new/saved village, join from a LAN list or address), players panel,
   host-only speed and saving, `--host`/`--join`/`--player-id`.
5. ✅ Since the first alpha: internet play through a relay (join codes; Options → Network or
   `--relay`), host migration when an online host vanishes, chat, reconnecting without a full
   snapshot, saved characters (`characters.json`), a host pump thread, 30 Hz turns and avatar walk
   prediction, macOS (Apple Silicon) packages.
6. ✅ Deterministic trig for mixed OS/CPUs (`Core/DetMath`; a test keeps `MathF`'s out of the sim).
7. Next: networked turn-based combat (raids are off in co-op until then), name labels over
   players, per-section desync dumps and a headless `--check-desync`. The lockstep checksum is
   still `StateHash(withMap: false)`; `FullHash(withMap: false)` would catch every divergence but
   costs ~1.6 ms on the bench village (15 ms with the map), about 1.5 times a second.

## In progress: Norse HD-2D art pass (docs/ART.md "Style guide")

- ✅ Redrawn: people (32×56, Octopath proportions) and their worn gear and gear icons; all three dragon
  stages (one rig, `tools/norse_dragon.py`); spruce, oak, birch and the burnt snag. Ground, buildings,
  items, story sprites and animals pass through the Nordic grade (`norse_art.nordic_finish`); doors, beds
  and plank walls have Norse details; the renderer's grade is cool and muted by default (`VisualStyle`).
- ✅ Redrawn since: grass and wildflower patches, dirt, sand and water tops (`tools/norse_ground.py`), the egg.
  Dragons now beat their wings properly (up, over, down and forward; the old flap never left the swept-back arc).
- ✅ Redrawn since: every rock, ore, slope and side face, boulders (`tools/norse_stone.py`); buildings,
  model textures and furniture icons (`norse_buildings.py`); items and gear in every tier (`norse_gear.py`);
  animals, forage plants, UI icons and everything that glows. ART.md's style guide has the full list.
- ✅ People, second pass: about three heads tall with a jaw, neck, free-swinging arms and hair drawn as
  locks with a sheen; eye styles, beards and five new hair styles; clothes as real items (Tunic, Legs,
  Feet and Cloak slots, dyed by their `Cloth` material, `data/defs/outfits.json`, `Systems/Clothing`):
  villagers, settlers, raiders and story people are dressed, and the character creator has archetype
  presets (Farmhand, Weaver, Shieldmaiden, Hunter, Skald, Smith's apprentice, Wanderer, Seer), tabs, dyes
  and a turnable walking preview. Save format 12.
- ✅ Redrawn since: the selection brackets, designation mark and bubbles (`norse_fx.py`); turf, thatch
  and shingle roofs (`norse_roofs.py`), with a new turf roof to build (warm, slow to burn) and turf drawn
  over every enclosed room.
- ✅ People, third pass (the Wanderer first): three-quarter views instead of profiles, big eyes and no
  mouth, hair as locks with sheen (the new Swept style), idle stances (arms crossed, hand on hip, coin
  flip), a scarf with a shoulder cowl, a vest over the tunic with bracers, feet that point where people
  face. Fixed: trimmed look layers sorted behind their figure (eyes and clothes never showed in game).
- ✅ People, fourth pass: 40×60 frames with headroom, lean proportions, bare arms (short-sleeved shirt),
  angled legs that stride when walking (contact and passing frames, not feet pumping in place), the
  Wanderer's poncho, banded scarf, leg wraps and gloves. `art/creatures/wanderer.aseprite` holds his
  layers to paint over (`tools/make_look_aseprite.py`; `tools/export_art.py` exports each layer).
- ⬜ Next for people: bring the other presets up to the Wanderer, and redo the remaining hair styles as locks.
- ⬜ **People art, the way forward.** The generator draws people from shapes in code, and that has gone
  about as far as it can; hand-made art is the next step. Three ways there, in the order we're trying them:
  1. **3D models rendered to sprites** (current). Build characters in 3D (VRoid Studio for anime-style
     hair and clothes, Mixamo to rig and animate, Blender to render) and render them to the game's
     layered sheets: every view, animation and stance comes from posing, not drawing, and each hair
     style or garment is one more mesh rendered as its own layer, so new craftable clothes cost one
     model instead of a hundred hand-drawn frames. Renders shrunk to 40×60 look soft unless toon-shaded,
     palette-snapped and outlined, with a light touch-up (the eyes especially); the pipeline does the
     first three (`tools/blender/render_layers.py` → `tools/sprites_from_renders.py`, docs/ART.md
     "3D to sprites"). Try it on the Wanderer first; if it doesn't read at game scale, fall back to 2 or 3.
     *So far:* a VRoid test figure goes VRM → Blender (`setup_vrm.py` sorts it into layers) → Mixamo
     animations (`retarget_mixamo.py`) → sheets, and the Wanderer preset is drawn from it as its own
     model (`Appearance.Model`, `wanderer3d`). Next: Norse clothes as separate meshes (the vest,
     mantle and scarf), work and arms-crossed animations, a lying-on-the-back sleep, eye touch-ups.
     *And 2D, once more:* everyone is now a hi-res chibi drawn in code (`tools/chibi_people.py`, after
     the Valkyrie test): both bodies, every hair style, beard, eye style and garment at four times the
     detail (`"density": 4` sheets, box-filtered when shrunk), Adventure starting closer (zoom 72).
     The 3D Wanderer is parked; the Wanderer preset is drawn like the rest. The dragons, egg, wild
     animals, held tools and weapons, and trees, bushes, rocks and wild plants (`chibi_nature.py`)
     followed in the same style; effects (fire, breath, smoke) are next, probably at 2×.
  2. **A bought pixel-art base.** Modular "paper doll" packs (e.g. Mana Seed: a body plus separate hair,
     outfits and colour ramps) fit the layer system well and look good fast, but are usually smaller and
     chibi (Stardew more than Octopath), Norse items still need drawing, and the licence must allow a
     commercial Steam release (avoid share-alike licences like LPC's CC-BY-SA for core art).
  3. **Commission an artist** (planned at some point regardless): a base body in four views with a walk,
     drawn to the layer conventions in docs/ART.md, then hair and garments edited onto that template.
     Likely the most quality for the money before a Steam release; the 3D renders or the generator's
     sheets make a good brief.
- ⬜ Left: carved gables and stave-style uprights (corner posts, carved door frames) for buildings; clothes
  to make (a loom and dye recipes: the dyes are materials already), and clothes' warmth.
- ⬜ Engine side (needs Narsil shader work, so a submodule commit first): flickering fire light feeding the
  light volume, depth haze, soft sprite shadows.

## Next

Follow-ups to a finished feature are listed in its section above; these are the rest. (The plans
in `docs/plans/` refer to this list's old item numbers: the table at the end maps them.)

- **Combat** — breath overwatch for dragons, suppression, escapees freeing other prisoners.
- **Save/load** — saving mid-fight (serialize the `Encounter` and the combat HUD modes), saving
  in-flight jobs if dropping them on load ever looks wrong (a hauler putting down a load), a
  save-format migration path (older formats load through version checks inside the codecs today).
- **Hot reload** — keep jobs across a def reload (patch defs in place) if restarting them gets in
  the way; reload art from the checkout's `art/` exports without `--also-copy-to`.
- **3D** — collapse when supports are removed, raymarched smoke and heat haze, sun shadows,
  directional dragon sprites, gas types beyond smoke (poison breath), raising the build plane more
  than one level at a time.
- **Visuals** — chibi size vs. 1-cell furniture, more box props (wagon, wall trim), real art
  replacing the placeholders.
- **Farming** - planting, growing, harvesting, and processing crops.
- **Storyteller / event director** — wanderers joining, weather events, more kinds of event.
- **UX** — wall-drag line placement, forbid/allow items, bed assignment, organize and declutter 
  the UI better, categorize orders and buildables. make this legible  on a steam deck screen.
- **Tutorial / New player experience** - add a light tutorial in adventure mode, and refine the 
  starting experiences.
- **Engine** (the shared Narsil submodule; its own roadmap is `external/narsil/docs/ROADMAP.md`) —
  ImPlot system profiler, optional texture arrays when the atlas fills up, frame limiter for
  mailbox mode. Performance pass done (Nov 2026): tick interpolation and an after-tick follow
  camera, hitch logging and per-system peaks, background autosave (capture ~14 ms, was ~31 ms
  blocking), time-budgeted remeshing, co-op host pump thread, adaptive client buffer, 30 Hz turns
  and avatar walk prediction with stop-at-cell. Next if measured in play: the structure-change
  rebuild ("Derived", ~5 ms per change on the 160×160×12 map: regions ~3.6 ms, path costs ~1.9,
  rooms ~1.1) made incremental, and save capture (~14 ms) made cheaper. Regions can't simply
  re-flood locally: one outdoor region covers most of the map and nearly every change touches it,
  so it needs RimWorld-style chunked regions (regions bounded to map sections, plus a graph
  between them). Region ids are only ever compared for equality, so their numbering may change.
- **More dragon types** — the framework is in (docs/plans/dragon-types.md): breeds (Hearth, and
  the Norse dragon back from the old art) and five working elements. Next: more breeds (a
  serpentine wyrm, a small swift drake) as defs plus art scripts, per-breed eggs, wild eggs of a
  rolled breed and element, element-specific care and tricks.
- **Dragon breeds** - new breeds, a quicker more agile dragon, an eastern style serpent, and maybe one inspired by elden ring
- **Mining follow-ups** — spear reach, arrowheads, room beauty from materials.
- **Customization** — more options in the character and dragon creators: more hair, facial hair
  and face shapes for people; for dragons to be decided (more dragon types are part of it).
- **Dragon mode** — you are the dragon, and have to grow.
- **Trade** — a trader caravan or Ashford's market, priced in the coin minted from silver and gold.
- **Audio** - sound effects, music, etc
- **Map Size** - Map size increase
- **World Map** - wider world, travel, other settlements or dragon habitats
- **Quests** - quests, both procedural and story focused, from people in towns or other sources in adventure mode.
- **Progression tuning** - making sure the game is fun throughout and adjusting as needed.
- **Sleep in multiplayer** - speed up recovery or skip forward when everyone's asleep

## Known issues

- On this KDE/Wayland setup, FIFO presentation sometimes paces at ~20 fps while the window is
  occluded; CPU+GPU work is < 1 ms/frame, so it's compositor throttling, not engine cost.
- Several villagers may deliver to the same construction site at once and over-deliver slightly;
  extra material is dropped next to it.
- Light from campfires passes through walls sideways (the light grid has no occlusion on a level).
- Burned buildings aren't rebuilt automatically. (Hatchling sneezes no longer light buildings.)
- The scenario tests take ~80 s in Debug (the sim isn't optimized there); `dotnet test -c Release` ~11 s.

## Design records

Each plan opens with its status and what changed from the plan while building it.

| Plan | Status | Was "Next" item |
|---|---|---|
| [combat-follow-ups.md](plans/combat-follow-ups.md) | Implemented | 0 |
| [combat-time-units.md](plans/combat-time-units.md) | Implemented (replaces the overwatch rules above) | |
| [visual-improvements.md](plans/visual-improvements.md) | Implemented; the palette pass became the Norse art pass | 5 |
| [inventory-equipment.md](plans/inventory-equipment.md) | Implemented | 9 |
| [crafting.md](plans/crafting.md) | Implemented | 10 |
| [coop-multiplayer.md](plans/coop-multiplayer.md) | Alpha; networked combat is next | 12 |
| [mining-progression.md](plans/mining-progression.md) | Implemented | 13 (Trade was 17) |
| [dragon-interactions.md](plans/dragon-interactions.md) | Mostly built; see its "Left to do" | 14 |
| [adventure-mode.md](plans/adventure-mode.md) | Implemented (Escape, Pact) | |
