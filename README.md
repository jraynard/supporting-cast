# Embersong Vale

*(working title; the code, repo and save folder still say DragonWorld)* A survival / dragon village sim prototype in the spirit of RimWorld, set in a fantasy world: your villagers arrive
with a dragon egg, and grow into a village of dragons and humans living together. Simple graphics,
detailed interacting systems, emergent stories.

Built in C# on the [BRUTAL](https://github.com/ahwootech/BRUTAL) framework (GLFW, Vulkan, Dear ImGui).

## Running

Requires the .NET 10 SDK and a Vulkan 1.3 GPU. The engine, [Narsil](external/narsil), is a git
submodule: clone with `--recurse-submodules`, or run `git submodule update --init` once.

```sh
dotnet run --project src/DragonWorld                       # title screen (add --no-splash to skip the logo in Debug)
dotnet run --project src/DragonWorld -- --new-game         # skip the title, straight into a new village (no raids)
dotnet run --project src/DragonWorld -- --new-game --difficulty hard --skip-hours 80   # raids on (peaceful|normal|hard); first one around day 4
dotnet run --project src/DragonWorld -- --seed 42          # new village on a different map
dotnet run --project src/DragonWorld -- --demo --skip-hours 13   # slice orders pre-placed, jump to nightfall
dotnet run --project src/DragonWorld -- --dragon-demo      # an adult dragon soaring overhead, a grove marked to burn
dotnet run --project src/DragonWorld -- --prologue --name Arin # Adventure's egg-bearer story start (the village), skipping the creator
dotnet run --project src/DragonWorld -- --adventure --name Arin # Adventure mode at a new home with an egg, skipping the prologue
dotnet run --project src/DragonWorld -- --adventure pact --name Arin --dragon-name Ashwing   # a dragon start (escape|pact; egg = --prologue); --stage site skips the forest or camp
dotnet run --project src/DragonWorld -- --creator dragon   # title screen, straight into the dragon creator (screenshots)
dotnet run --project src/DragonWorld -- --raid-demo        # combat slice: two bandits attack (add --auto-combat to watch the AI play both sides)
dotnet run --project src/DragonWorld -c Release -- --bench # headless sim benchmark (one game day)
dotnet run --project src/DragonWorld -- --render-test --stress   # 50k-sprite renderer stress test
dotnet run --project src/DragonWorld -- --art-preview      # every animated sprite sheet, playing (see Art & assets)
dotnet run --project src/DragonWorld -- --check-art        # headless: every sprite and animation the game needs exists
dotnet run --project src/DragonWorld -- --load quicksave   # straight into a save (a slot name or a path)
dotnet run --project src/DragonWorld -- --check-save quicksave   # headless: load a save, play an hour, print its hash
dotnet run --project src/DragonWorld -- --host --name Arin       # co-op: host a new Pact at the tower's site (or --host <save>), port 47040
dotnet run --project src/DragonWorld -- --join 127.0.0.1 --player-id bea --name Bea   # co-op: join it (a second copy, as someone else)
dotnet run --project external/narsil/src/Narsil.Relay -- --port 47050   # a local relay, for online co-op by join code
dotnet run --project src/DragonWorld -- --host --relay 127.0.0.1 --name Arin   # host through it (--join <CODE> --relay 127.0.0.1 joins)
dotnet test                                                 # headless simulation and game-logic tests
```

A bare launch opens the title screen: **Adventure** picks a story, then your character (and
dragon), then how often raids should come (Peaceful, Normal, Hard); **Sandbox Village** starts the
vertical slice (the seed-1234 map) with the raid difficulty you pick,
**Continue** loads the newest save, **Load Game** lists them,
**Combat Demo** and **Dragon Demo** load the two demos below, **Options** opens the settings, **Exit**
quits, and Credits is a placeholder for now. **Menu** in the in-game top bar goes back to it,
**Save** / **Load** there manage named saves, and **Options** opens the same settings.

Saves live in `~/.config/Dragon_World/saves/*.dwsave`: named ones, the quicksave (F6, F9 to load)
and three rotating autosaves, made every dawn and just before a fight starts. Fights themselves
can't be saved. Creatures' current jobs aren't saved either: after a load everyone puts down what
they were carrying and decides what to do next.

Settings (display mode, resolution, monitor, VSync, frame rate limit, UI scale, pause on danger,
camera pan speed) are saved to `~/.config/Dragon_World/settings.json`, next to `imgui.ini`. Display
changes ask for confirmation and revert after 15 seconds. On Wayland, full screen can't change the
screen's resolution, so it runs at the desktop resolution like borderless. Any game flag (`--demo`, `--seed`, `--dragon-demo`, ...) skips the title screen, and
`--menu-pick "Dragon Demo"` presses a menu entry after three seconds (handy for screenshots).

Debug builds enable Vulkan validation if `VK_LAYER_KHRONOS_validation` is installed
(Arch/CachyOS: `sudo pacman -S vulkan-validation-layers`). Set `DW_PROFILE=1` to log frame-phase timings.

### Controls

| Input | Action |
|---|---|
| WASD / arrows, middle-drag | Pan (Shift = faster) |
| Q / E | Rotate the view 90° |
| Mouse wheel | Zoom at cursor |
| Left click / drag | Select, or apply the current tool to an area |
| Right click, Esc | Cancel tool / deselect / move a drafted villager or a mounted dragon |
| Space, Ctrl+1–3 | Pause, speed 1×/3×/6× (also on the clock, top right) |
| Tab | Next toolbar category (Orders / Build / Zones); in Adventure mode, the inventory |
| Letter keys | The open category's tools, shown on each button (C chop, M mine, B burn, U hunt, F fish, Z stockpile, ...) |
| F1 / F2 / F3 / F4 | Temperature overlay / rooms overlay / paths / night darkness |
| F6 / F9 | Quicksave / quickload (press F9 twice if there's unsaved progress) |
| F5 | Reload all art from disk |
| F7 | Recompile shaders (from the checkout's Narsil shader folders) |
| F8 | Reload defs (also automatic when a `data/defs` file changes, in Debug builds or with `DW_WATCH=1`) |
| `[` / `]` (PageDown / PageUp), Home | Cutaway level down / up, show everything |
| R | While placing a building: turn it (beds, tables, chests...) |
| F10 | Toggle the HD-2D look (tilt-shift, glowing fire, vignette, warmer colours; tune in Options → Interface) |

The HUD: the toolbar along the bottom, villager and dragon portraits along the top (click to
select, double-click to find; badges flag the downed, sleeping, drafted and those with an urgent
need), the clock and speed controls top right with new messages popping up below it, and the
inspector bottom left. Little bubbles over villagers show what they're doing. The full message
log, work priorities (**Work**), debug tools (**Debug**, including UI scale) and the licence
notices (**Credits**) are in the top bar.

The world is drawn in 3D from an isometric angle. The map has height: hills rise a few levels
above the landing site, reached by natural slopes or wooden stairs, and the ground can be dug into.
The cutaway hides everything above a level, so you can look inside buildings and hills; rock cut
by it shows as darker caps, and Dig on a block face tunnels in on that level.

Buildings can have upper storeys. Wooden floors are platforms that must be within two cells of
ground or a wall below (so they can also bridge water). Hold Ctrl while placing or dragging a build
order to put it one level above what you point at (on top of walls, or a floor up from the ground). Ladders climb straight up, stairs onto a neighboring cell one level up.
Blueprints can rest on other blueprints; villagers build bottom-up. Deconstruct takes buildings
and floors apart (top-down: nothing may be left hanging) and returns half their materials. Floors
can't close off a stairwell. Wooden floors burn, and whatever stood on them falls.

Debug flags: `--yaw <degrees>` and `--cutaway <level>` set the starting view, `--ignite-forest`
sets the nearby trees alight, `--breath-demo` has a young dragon breathe fire at them, `--dragon-demo --mounted`
starts with the keeper on the adult's back, `--no-autopause` keeps danger messages from pausing, `--no-splash`
skips the logo in Debug builds.

## Adventure mode

**Adventure** first asks which story:

- **Escape** (combat on): a dragon rider taken prisoner with your dragon breaks free and has to
  survive in the wild by crafting, gathering and hunting.
- **The Sorcerer's Pact** (peaceful): you and your dragon owe a sorcerer a tower, built far from
  home. Raids hold off until it stands.
- **The Last Egg**: the slow road, below.

Then the character creator (name, body, short or long hair, hair/eye/skin colour, traits) and,
for the two dragon starts, the dragon creator: its name and scale, belly, wing, horn and eye
colours. Your dragon is an adult that has flown with you for years: it always listens, you can
ride it straight away (**Ride** in your panel calls it down to you, or F on it beside you; right-click
to go somewhere, **Ctrl+right-click** to fly there, or tick **Fly on every move**; **Breathe fire**,
then click a target, for a blast from the saddle), and when nobody
has left it meat it hunts wild game for itself. **The Sorcerer's Pact** opens in the enchanted forest: pick six moonpetal (pale blue flowers
among the trees: F to pick, then pick up the bloom). Their owner, a sorcerer, comes for you; hear
him out (F beside him) and he trades your village's cure for a tower. Then ride your dragon (Ride
in your panel) and **Fly away on** it to where he points. There a ley stone marks the site, and a
checklist under the clock shows what the tower needs: three storeys of enclosed rooms stacked by
the stone, mostly stone, brick or cut-stone walls, with a bed, a table, a chest and a campfire
inside. Raids hold off until it stands. 

**Escape** opens in a captors' camp at dusk: you're in a cage, your dragon in chains across the
camp, your gear in a chest by the tents. At supper (19:00) the jailer opens the gate and comes in:
F on him knocks him down and his club and chain key fall at his feet, but the camp is roused and
the fight is on: pick them up (time units, see below). Get beside your dragon and free it (F, or **Free** in the combat panel), then **Fly
away** from beside it, before more of them arrive from the east. Go down and you'll wake back in
the cage; there's always another supper (the next evening, not the same one). There's a straw pallet
in the cage to sleep on (F on it), and anywhere you can lie down on the ground with **N** (or **Sleep
here** in your panel): a worse night than a bed. F and clicks never put you to sleep on the ground. The jailer carries the
key, and it falls wherever he goes down; if he's laid up, a guard brings supper (and keeps the key)
instead. Your chest holds **bandages**: one binds a wound, stopping the bleeding (the item's menu out
of a fight, or **Bandage** in the combat panel for TU), on yourself or your dragon beside you. You land in a wild valley with only what you carried,
raids on, and the objective line suggests what to see to first (a fire, a roofed bed, food, a tool). Co-op (**Host New Village**, `--host`) is
the Pact at the tower's site, and friends who join make a dragon of their own to fly in on.

**The Last Egg** starts in **Ashford**, a village that isn't yours: you're its youngest
hand. Old Orla, its dragon keeper, is dying in the hall and has sent for you (F beside her bed). She
entrusts you with the village's last dragon egg and her packed wagon. With the egg in your arms,
F anywhere near the wagon sets out; a few days on the road later you arrive alone at a new valley
(raids only start there) with the egg and your wagon, which holds the starting kit (16 stacks; F on
it to store what you hold or open it and take something out; companions eat and build from it too).
Once it's empty, its window offers **Take it apart**: mark it, then hold F at it for 30 oak wood:

| Input | Action |
|---|---|
| WASD | Walk (relative to the screen); the camera follows you |
| Left click | Walk there; hold the button and the walk follows the cursor. Click something F could act on (a tree, an item, a blueprint, a bed, the wagon) to walk over and do it; holding the button works it like F. Shift+click (or clicking a creature) selects it for the inspector |
| Right click | On food lying in reach: its menu, to eat it there |
| F (hold) | Work on what's under the cursor within reach, else in front of you: chop, mine, harvest, build, pick up, deliver to a blueprint (up to 2 cells off), feed or relight a fire with the wood you hold, sleep in a bed, open a workbench or work the craft ready at it, beat out a fire, shoot or strike at a wild animal (a bow reaches 14 cells; bare hands or a spear only reach beside you), butcher a carcass in reach. Work only progresses while F is held; keep holding it and building (handing over what you carry) and firefighting carry on to the next blueprint or burning cell in reach. |
| Z (hold) | Dig the block under the cursor or in front of you |
| X | Put down what you hold (under the cursor within reach, else in front of you, else at your feet) |
| Tab | Inventory: what you wear, hold and carry; take off, wear or wield, hold, eat, drop; the recipe book. Click an item's slot (here or on the hotbar) for what you can do with it |
| 1–4 | Take that pack stack in hand (what you held goes into the pack); an empty slot's key puts what you hold away |

**Hunting** (a first pass): point at a wild animal and hold F to loose arrows at it while it's in
bow range and sight (or swing at it from beside it). Game bolts when you come within its flee
range (a hare 9 cells, a deer 12), so creep up to just outside that; a wounded animal may limp off
out of range, and a boar turns on you. You don't move while shooting: walk after it and press F
again. Once it's down, hold F at the carcass to butcher it into meat and hide. Each shot and kill
trains Hunting (better butchering yields).

What you hold stays in your hands until you put it down with X (F never drops it). You also start
with a **satchel** on your back that carries 25 kg (every item has a weight: wood 1 kg, stone
blocks 2 kg). F picks things up into it first and into your hands when it's full; building and
refuelling take from it when your hands don't hold the right thing ("Deliver 12 oak wood
(pack)"). F never eats: pick **Eat** on the food itself (its slot's menu, the inventory, the
wagon's window, or right-click it on the ground). F on armor, a weapon or a pack lying in reach puts it straight on when that slot is empty;
otherwise it goes in the pack, and the inventory swaps it in (what it replaces goes into the pack).
A pack you take off or drop keeps what's in it, and opens like the wagon. Companions get satchels
too and haul and build with them, bringing several stacks per trip. You can't give work orders while you're
alone: build a walled, roofed room with a bed you don't sleep in, and within a day someone comes
to live there. Sleeping in a bed makes it yours. Once someone has joined, the Orders tab and Work
priorities unlock, and their inspector lets you ask them to **Follow me** or **Guard the egg** (they
switch to guarding your dragon once it hatches) or go back to their own work. They still eat and
sleep when they must. In a fight you only take your own turn (and your dragon's, once its bond with
you reaches 0.5); everyone else fights on their own. **Sandbox Village** is the original game:
three settlers, all under your orders.

## Co-op (alpha)

**Co-op → Host New Village** makes your character and founds a village (Adventure start, peaceful:
raids are off in co-op until fights are networked); **Host a Saved Village** opens one of your
saves. Friends pick **Join a Game**: villages on the local network are listed, or type the host's
address (port 47040 TCP; discovery uses UDP 47041). A newcomer makes a character and arrives by the
wagon with a dragon egg of their own, which hatches to them. Coming back to the same village gives
you the same character; while you're away it lives as an ordinary villager. Everyone can give
village orders; you steer only your own character and dragon. The host sets the speed and saves.
Everyone must run the same build (and defs).

Over the internet without port forwarding, set a relay server (Options → Network, or `--relay`):
the host's Players window then shows a join code, and friends type the code instead of an
address. If an online host vanishes, the next player in line takes the session over. The
characters you make are kept (with their dragon's name and look) and offered the next time you
host or join.

### Alpha test builds

Testers download a package from the repository's **Releases** page (Windows x64 zip, Linux x64
tarball, macOS Apple Silicon `.app`; self-contained, no .NET needed). Each package carries `README-ALPHA.txt`
([docs/alpha/README-ALPHA.md](docs/alpha/README-ALPHA.md)): requirements, hosting over Tailscale
or a forwarded TCP 47040, known limits and how to report bugs. To make one:

```sh
tools/package.sh                       # dist/: all three packages + SHA256SUMS.txt, checked and smoke-tested
```

CI (`.github/workflows/alpha-release.yml`) packages every push to `main` and, when the Windows
and macOS checks pass, publishes it as the prerelease `v<Version>`: until `<Version>` changes,
each green build replaces that release's packages and moves its tag to the new commit. The
tests run first (`dotnet test -c Release`): a red test publishes nothing.

On a LAN, the script also copies the newest build to `/srv/dragonworld` when that folder exists
(serve it read-only with Samba; `DW_SHARE_DIR` picks another folder, `DW_NO_SHARE=1` skips it).

Bump `<Version>` in `Directory.Build.props` (that starts a new release) and add a section to
[docs/alpha/CHANGELOG.md](docs/alpha/CHANGELOG.md) for each alpha. The game writes its logs, crash
reports and co-op desync dumps (`desync-<turn>-host/-mine.dwsave`) to the `logs` folder beside the
saves (Options → Interface → Open logs folder); `--version` prints the version and the co-op build tag.

## The vertical slice

Three villagers, a dragon egg, a few days' food, a hunting bow and a pile of pine planks. The egg only incubates between 28–80°C, and
nights drop to around 0°C. Chop trees, build a walled shelter (enclosed rooms are auto-roofed and
hold heat) with a campfire, keep the fire fed, and the egg hatches after ~1.5 days into a hatchling
that imprints on its keeper. The `--demo` orders build a wooden hall: campfire, stairs and a stone
dragon nest below (the stone comes from a boulder and a tunnel dug into the nearest hill), and once
the fire is lit, a second storey with beds. Hatchlings need meat, warmth and attention — and they sneeze fire,
more often when lonely or hungry. A sneeze scorches the grass and plants around them, but not
buildings, so the hall is safe from its own nest. Once grown into a
juvenile, a dragon can fly: over water and cliffs to its keeper, and up to hilltops to perch. It
can also breathe fire on command (inspector → "Breathe fire at..."), if it trusts you enough to
obey: the flames pour along a cone, stop at stone, and splash across whatever they hit. The
"Burn (dragon)" order marks an area to clear with fire: dragons old enough to breathe fly over
and torch it until nothing there will burn, and villagers only fight the flames that spread
beyond it. Burned trees are left as charred dead trees, marked for felling, so the land gets
cleared. Adults also take to the air for the fun of it, circling high over their keeper.

### Feeding a dragon

A grown dragon eats about 23 meat a day, far more than the village brought, and meat rots (fast in
the warm, slowly in the cold, not at all frozen; the inspector shows spoilage). Herds of deer,
hares and boar wander in from the map edge, graze a day or so and move on, keeping clear of people.
The **Hunt** tool (U) marks animals; anyone with a bow and Hunt work stalks them to bow range and
shoots, then butchers the carcass (a deer is 30 meat). Boar turn on whoever hurts them. The
**Fish** tool (F) marks bank cells next to water; each stretch of water fishes out for a few days,
so spread the spots. Hunting and fishing come last at equal priority (Work tab), after walls,
fires and wood.

### Growing up

Dragons work too, as their age allows (they're in the Work tab under the villagers): juveniles take
burn orders, and adults also haul (four stacks a trip) and fell trees. They roll obedience each
time, and a hungry dragon eats before it works. An adult that trusts its keeper (affinity 0.5+)
can be ridden: select the keeper and **Ride**, then right-click to send the pair, walking or
flying. The dragon minds the reins better with its keeper aboard. In a fight they stay mounted: the
dragon moves and breathes, the rider shoots from its back over low cover, and a blow aimed at the
rider sometimes lands on the dragon. Neglect costs trust: a lonely or starving dragon's affinity
slips, and with it obedience, work and riding.

### Caring for a dragon

A dragon needs looking after, a hatchling most of all. In Adventure mode, F at your own dragon
opens the care window: scratch it, feed it by hand, play, groom, comfort it when it's upset, train
it, or sleep beside it. The same thing again soon counts for less, so vary it. Now and then it
asks for something (a bubble over its head): F answers it, and a want left unanswered costs a
little trust. Hatchlings imprint in their first day (trust comes twice as fast) and are fed by
hand, or from a **feeding trough** their keeper keeps stocked. In a village, keepers do all of
this on their own.

Trust has named levels, each slower to reach than the last: wary, accepting (comes when you call),
trusting (lets you ride it, and takes your orders in a fight), devoted (brings you a share of its
kills, and strikes at anyone who steps up to you in a fight) and soulbound (its fire never touches
you, and it flies at whoever hurts you). Training teaches tricks over a few sessions:
come, stay, hold fire (fewer sneezes), steady, fire on command, fetch and carry. Neglect a dragon
for long and it sulks, refusing work and riders until it's won back; one that never trusted its
keeper leaves for good. The inspector's Bond section shows the level, what it wants and the care
it's had lately.

Forests grow in groves of oak, pine and birch. Each tree gives its own wood, wood stacks by kind,
and anything built shows the wood that went into it: a wall raised mostly from birch is a pale
"birch wall". Any wood pays for anything, but the kind matters: oak is sturdy and slow to work,
pine light and quick but quick to burn, birch in between. Stone comes in kinds too (sandstone,
limestone, granite, slate, basalt), and a granite wall stands up to far more than a sandstone one.

### Crafting

Workbenches are built from the Build tab like anything else, and things are made there in two
ways: **bills** for the village (select the station; "Add a bill…"; do it N times, until you have
N, or forever; pick which wood it may use, pause it, reorder), worked by anyone with Craft or Cook
work; or by **you**, in Adventure mode: F at a station opens its recipes, **Make** brings what you have
of the ingredients from your hands, pack and any chest or wagon in reach, and once it's all there
you hold F to work. What's been brought stays at the station, so a craft can be finished later or
by someone else; "Call it off" gives it back. Some things are made **by hand** anywhere (a club, a
satchel): from the recipe book, standing still.

- **Sawhorse**: logs into planks (oak stays oak). Plank walls and floors keep heat in better than
  log ones and cost less wood per wall.
- **Carpenter's bench**: hunting bows. **Mason's bench**: cut stone for the warmest walls.
- **Tanning rack**: butchered animals now give hides too; tan them into leather for caps,
  jerkins and a 40 kg leather pack, or sew shed dragon scales into heat-proof scale armor.
- **Campfire**: cooks meat, fish or berries into meals (lit fires only). People reach for a meal
  first, enjoy it, and leave the raw meat for the dragon.
- **Kiln** and **forge**: fired only while there's work for them. The kiln bakes charcoal,
  bricks from the clay banks by the water, quicklime from limestone, and smelts copper on a wood
  fire, bronze and silver once it's fed charcoal (haulers bring the fuel a craft needs). The forge
  makes tools, weapons and armor, and melts gold on coal. Iron and steel want more heat than coal
  alone gives: build **bellows** beside the forge (the smith pumps them while working), or have a
  juvenile or adult dragon with **Stoke** work breathe into it. Dragonforged gear needs the dragon.
- **Tools** (smith's hammer, tongs, saw, chisel, pick) count when they're on the worker or within
  two cells of the station (a chest beside it is a tool rack). Some recipes need one, others just
  go faster; an axe or a pick on you fells trees and breaks rock faster.

### Mining and the tool ladder

Gear climbs a ladder: **stone → copper → bronze → iron → steel → dragonforged**. A pick's tier
decides what rock it breaks: stone picks handle sandstone, limestone and the shallow ores (copper,
tin, coal), a copper pick opens granite, slate, iron and silver, bronze reaches gold, iron the deep
basalt, and steel the ember crystal in it. Hover over rock to see what it takes. Ore is hidden in
the rock until a face of it is opened, so prospect by digging: rock stained green, rust or gold
hints at a vein behind it, and broken boulders sometimes hold a little ore. Raiders come with gear
a step past yours, so a bronze village meets bronze spearmen, not knights.

Silver and gold are struck into coin at a **goldsmith's bench** and made into rings and torcs (worn
at the neck, for a little finery). Treasure draws raiders, but dragons love it: a dragon sleeps
happily on a hoard, and giving your dragon gold (hold it, F at your dragon) wins it over.

Gear comes out crude, plain, fine or masterwork by the maker's skill (it scales armor and weapon
damage), and people think better of whoever made what they wear. You can **mend** worn gear at the
station that makes it. The **recipe book** (Recipes in the menu bar, or the inventory's button)
lists what you know: recipes unlock at a skill level (the village knows one once anyone reaches it;
you once you do) or are discovered by handling something new (a hide, iron ore, clay), and
companions who know one you don't will show you.

## The combat slice (`--raid-demo`)

Two bandits with a club and a short sword walk in from the east. Of your three villagers one has a
hunting bow, one a woodcutter's axe, and the third won't fight but keeps a young dragon that can,
when it listens.

Combat is turn-based (old X-COM / Xenonauts style) and starts when hostiles come within sight.
Each unit has **time units** (TU, 60 a turn) and spends them in any order, as long as they last:
walking costs about 4 TU a cell (more on rough ground or with wounded legs, less for fast or
flying creatures), a melee blow 35% of its TU, a bow's **snap shot** 30% (less accurate) or
**aimed shot** 55% (more accurate), breathing fire 50%, picking up something in reach 15%,
dragging a body out of the way 20% (bodies block the cell they lie in: **Drag**, then click a
highlighted cell beside it), taking something out of a chest beside you 15% (a weapon is wielded,
gear put on, the rest goes in your hands or pack). WASD moves the camera during a fight.
Standing still, an axeman swings twice. TU you don't spend are kept for **reactions** in the
enemy's turn: a snap shot at an enemy moving in range, or a swing at one stepping out of reach;
and when an enemy attacks (anyone), everyone who can hit it answers, a swing if it's in reach or a
snap shot if it's in sight. Each is paid from what's left, once per enemy action (the tooltip over
an enemy lists who'd answer your attack). Tick **Reserve TU for reactions** to stop moves
eating into that. Click a cell to move (blue: you'll have TU left to attack; amber: you won't), an
enemy to attack with the chosen attack and mode (hover it first to see the odds and the cost),
Ctrl+click to have a flyer hover in the air (melee can't reach it there, arrows can), Tab for the
next unit, Enter to end the turn. The dragon rolls obedience on its first order each turn; if it
refuses, it acts on its own instincts. Between rounds the world moves on a few seconds: fires
spread, wounds bleed.

An attack rolls to hit (accuracy, range, cover, height, target size, the attacker's own wounds),
then to actually hurt (glancing blows, and dragon scales turn weak weapons), then for where it
lands. Wounds are per body part: a cut arm spoils aim, a torn wing grounds a dragon, a head wound
can knock someone out. Untended wounds bleed; blood loss or pain makes a creature collapse, and it
dies only if a vital part is destroyed or it bleeds out. After the fight, villagers with Doctor
work carry the downed to bed and bandage everyone (the inspector lists each wound), and how well
depends on their Medicine. Badly tended wounds can fester: an infection races the body's immunity
(rest in bed helps, a fresh dressing a day helps more), and deep wounds can leave lasting scars.

People have skills (Melee, Shooting, Medicine, Construction, Mining, Plants, Social, Crafting,
Cooking, Hunting) that grow with practice: they work faster, hit more often and bandage better (inspector → Skills). Armor
(leather jerkins and caps, iron helms, chain shirts) protects the parts it covers: a blow on a
covered part may be turned, or blunted, and wears the armor down. Raiders come wearing it and drop
it when they fall; villagers put on what they find lying around.

Downed raiders are no longer left to bleed: once the fight is over, doctors stabilize them and they
limp off. Tick **Capture** in a downed raider's inspector (and mark a bed "For prisoners") and a
doctor carries them there instead. Prisoners stay in their bed, fed by villagers with Warden work.
Set them to **Recruit** and a warden talks with them daily (Social helps) until they join, or
**Release** them and they walk away.

Humans and dragons have a mood (inspector → Mood): hunger, cold, a night on the ground or in a
bed, a death, the egg hatching all count, and a long face slows work while a happy one speeds it
up; a dragon's mood sways how well it obeys. People nearby chat, have deep talks, trade slights or
comfort each other (speech bubbles), and remember who rescued, tended or fought beside them
(inspector → Social: what they think of everyone, and why). Everyone has up to two traits
(optimist, hardy, abrasive...), and you pick your own in the character creator. Companions aren't
obliged to follow you: how they feel and what they think of you decides whether they agree.

In a normal game the storyteller sends raids from the map edge: after a few days' grace, every few
days, sized by how many days you've lasted, how many people (and weapons) you have, and how far
your dragon has grown. The debug panel's Storyteller section shows when the next one is due.

## Layout

```
external/narsil/          Narsil engine (git submodule): window, input, game loop, Vulkan core, sprites, atlas, ImGui,
                          the voxel quad renderer (Narsil.Voxel) and the sim's PRNG, DetMath and grid nav (Narsil.Sim)
src/DragonWorld.Sim/      headless simulation: ECS, defs, map, AI/jobs, environment, dragons
src/DragonWorld/          the game: world rendering, tools, UI, debug
tests/DragonWorld.Sim.Tests/  xUnit tests incl. determinism and full slice and raid playthroughs
tests/DragonWorld.Tests/      game-side tests: sprite sheets, animation choice, the art audit, co-op sessions, save slots
data/defs/*.json          content: terrain, things, creatures, materials, needs, bodies, weapons (later files override earlier)
data/textures/**/*.png    sprites (+ .anim.json for animated sheets), packed into an atlas at startup
data/fonts/               UI font (Fira Sans)
art/                      Aseprite sources, exported into data/textures
tools/                    art generators (placeholder_*.py, norse_*.py), the Aseprite exporter, packaging
docs/                     roadmap, design notes, art pipeline and style guide (ART.md), feature plans (plans/), tester docs (alpha/)
```

Key ideas:

- **The sim never touches rendering.** It runs at a fixed 60 ticks per game second and is fully
  deterministic for a given seed (`Simulation.StateHash`), so the whole game can be tested headless.
- **Data-driven content.** Add a creature, building or item by editing JSON; references between
  defs are resolved and validated at load.
- **Jobs are coroutines.** Each job (`Ai/Jobs.cs`) is a C# iterator yielding once per tick, with
  helpers for walking, reserving targets and failing cleanly. Brains pick needs first, then work by
  each villager's priorities (`Ai/WorkGivers.cs`).
- **Environment emerges from tiles.** Heat diffuses per tile through walls by insulation, roofs
  slow the leak to outdoors, fires feed on flammable contents and spread; rooms, warmth and danger
  all fall out of those rules.

## Art & assets

All art lives in `data/textures/` as PNGs, named by path (`things/oak.png` is the sprite
`things/oak`). Defs refer to sprites by those names, and the whole folder is packed into one atlas
when the game starts, so **replacing a PNG with real art of the same name is all it takes**.
[docs/ART.md](docs/ART.md) is the reference for everything else: scale (32 px = one cell, 64 px =
one level up), variants and accent patches, materials, ramps, animated sheets and the tags the
game asks for, box models, the layered looks of people and dragons, and the Norse HD-2D style guide.

Everything in `data/textures` except the BRUTAL logo is drawn by Python scripts (Pillow is the
only dependency), deterministic, so re-running gives identical files:

```sh
python3 tools/gen_placeholder_art.py     # regenerate everything (it runs the placeholder_*.py and norse_*.py modules)
```

**The generator overwrites files.** Once a sprite has real art, remove it from the generator (or
stop running that part) so it isn't replaced.

Real art comes from Aseprite sources in `art/`, mirroring `data/textures/`. Exports are committed,
so building the game never needs Aseprite:

```sh
python3 tools/export_art.py              # export what changed (--all for everything, --check to list stale exports)
python3 tools/export_art.py --watch --also-copy-to src/DragonWorld/bin/Debug/net10.0/data/textures
```

With `--watch` running, saving in Aseprite updates the running game: Debug builds (or
`DW_WATCH=1`) reload art whenever a texture changes, and F5 reloads by hand. `--art-preview` shows
every sheet's animations side by side, and `--check-art` (also a test) confirms nothing a def or
the renderer asks for is missing.

## Credits & licensing

Built with BRUTAL, a game development framework by Ahwoo Limited, used under the BRUTAL License v1.0.
BRUTAL game development framework is Copyright (c) 2024 Ahwoo Limited.

The license applies to every distributed copy (commercial or not), and the build takes care of it:

| BRUTAL License clause | How it's met |
|---|---|
| 1.0 license text with the software | `licenses/BRUTAL-LICENSE.txt` copied to build and publish output (`external/narsil/build/Narsil.Licensing.targets`) |
| 2.1 copyright notice in credits | in-game **Credits** window (top bar), read from `NOTICE.txt` |
| 2.2 BRUTAL logo before gameplay | 2.5 s splash in Narsil's `GameHost` with the official logo (shipped by Narsil into `data/textures/branding/`), unaltered except integer scaling. `--no-splash` works in Debug builds only |
| 2.3 `NOTICE.txt` alongside the product | copied next to the executable |
| 4.0 third-party component licenses | each BRUTAL package's `LICENSES/*.txt` (GLFW, Dear ImGui, Vulkan, VMA, shaderc, stb, ...) copied to `licenses/` |

The UI font is [Fira Sans](https://github.com/mozilla/Fira) (SIL Open Font License 1.1), bundled in
`data/fonts/`; its license ships as `licenses/FiraSans-OFL.txt` and is credited in `NOTICE.txt`.

If you add a BRUTAL package, its notices are picked up automatically. If you add other third-party
code or assets, add their licenses to `licenses/` too.
