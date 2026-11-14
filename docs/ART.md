# Art pipeline

Everything the game draws is a PNG under `data/textures/`, packed into one atlas at startup and
addressed by path without extension (`creatures/dragon_adult`). Defs name sprites in their `sprite`
fields. Any file can be replaced by real art with the same name; nothing else needs to change.

## Scale

- **32 pixels = one cell across, 64 pixels = one level up.** The camera draws height stretched ×2
  (`Rendering/WorldScale.LevelHeight`, Narsil's `Camera3D.VerticalScale`), so walls, doors, cliffs,
  stairs and furniture stand tall enough for the 56 px people. Tops (terrain, floors, the tops of
  blocks) are 32×32 and seen as before.
- **Vertical faces keep square pixels by stacking**: a 32×32 side texture repeats twice up a level
  (`WorldScale.TilesPerLevel`); art drawn 32×64 shows once. So block sides (`*_side`, walls) stay 32×32,
  while faces that must show once are drawn 32×64: doors and the cage gate, the kiln and forge fronts
  (with a 32×32 `<sprite>_top` for the block's top, which the mesher prefers), and every ramp's top and
  side. A side whose art has a band at its top (turf, topsoil, a water line) can give the lower tile
  of each level its own `<side>_low` art (`terrain/grass_side_low`), so the band doesn't repeat
  halfway down a cliff. Box-model sides stack the same way, their art's top row at the box's top.
- Creatures with an animated sheet are drawn at **native size** around their pivot. A 128×96 adult
  dragon covers 4×3 cells and a 40×60 person 1.25×1.875 (4 rows of it headroom over the hair). The `size` in a life stage still drives the sim
  (breath height, combat size) but not how big the sprite looks.
- Static sprites (items, plants) are still scaled by the def's `renderSize`, which is their height in
  cells; draw billboards at native size for it (the ladder is 32×96 at 3.0, the wagon 72×60 at 1.875).
- Filtering is nearest-neighbour, so draw crisp pixel art with no smoothing.

## Variants and patches

- **Variants:** `name_v2.png`, `name_v3.png`… next to `name.png` are alternatives. Ground tops
  (`terrain/grass`) pick one per cell and are mirrored at random, so make tiles seamless and avoid
  a strong "up". Upright things (`things/oak`) also pick a variant per cell and are mirrored. Plants
  are jittered in size and position and sway in the wind. All of this is cosmetic and stable per
  cell. **Keep the same average colour across a tile's variants**, or the ground shows a checkerboard.
- **Accent patches:** a block can name an `accent` texture (and a sparser `accentFringe`) in its def.
  It's shown where smooth noise rises above `accentAbove`, which is how grass gets wildflower
  meadows. Each patch uses one accent variant, so give each variant its own colour scheme.
- **Trees** are drawn at 32 px per cell and stand on the bottom row of the image, with the trunk base
  centred. A def's `renderSize` is the height in cells (pixel height / 32); width follows the
  image's aspect. Species are separate defs (`Oak`, `Pine`, `Birch`, all flagged `isTree`); the map
  generator grows them in single-species groves. Sizes: pine (a spruce) 56×96 (`renderSize` 3.0), oak 72×80 (2.5),
  birch 56×88 (2.75), and the burnt snag `things/dead_tree` 44×64 (2.0). Each tree has 2–3 variants.

## Ramps: slopes and stairs

A ramp's top sprite has its **high edge at the top row**. Natural slopes pick `<sprite>_<ground>`
(`terrain/slope_grass`, `terrain/slope_stone`...) from the block they sit on, and fall back to
`terrain/slope`. The side faces use the top sprite's name + `_side` (`terrain/slope_grass_side`,
`buildings/stairs_wood_birch_side`). Both are 32×64 (a ramp climbs a level, which looks 64 px tall).
The side's opaque part is the wedge, high end on the **left**, with the slope surface running from the
top-left corner to the bottom-right (stairs: a stepped profile, four steps 8 px across and 16 down). Pixels outside it must be transparent (alpha below 0.5). Without a side sprite the
ramp's sides stay open.

A stair def with `steps` (`WoodStairs`, `InnStair`: 4) isn't drawn as a slope at all: the mesher
builds solid steps (`WorldMesher.AddStairs`), each a box with a plank tread and riser between dark
timber stringers, turned to the climb. Solid to the floor, it leaves no holes in a stairwell. A whole
level in one cell is steep, so when the cell at its foot is clear the flight starts half a cell out
into it (`Rendering/Stairs`, half as many steps again). The sim lifts a walker only as they step off
the stair, so `WorldRenderer.DrawPos` draws them on the tread under their feet (`Stairs.FloorAt`).
Its sprite is then only the toolbar icon.

## Materials (wood types)

Items and buildings can be made of a material (`materials` in the defs: Oak, Pine, Birch, all kinds
of `Wood`). A thing made of a material draws `<sprite>_<material>` (e.g. `buildings/wall_wood_birch`,
`items/wood_pine`) when that file exists. Otherwise it draws the generic sprite tinted with the
material's `color`. Anything that costs wood needs a sprite for each wood, and `--check-art` warns
about any that are missing. A new wood type is one def in `materials`, a `material` on the tree's
harvest yield, and its sprites.

## Animated sheets

A sheet is a PNG plus a sibling `.anim.json`. The usual way to make one is Aseprite:

1. Save the source as `art/<path>.aseprite`, e.g. `art/creatures/dragon_adult.aseprite`.
2. Run `python3 tools/export_art.py` (stale files only; `--all` exports everything). It writes
   `data/textures/<path>.png` and `.anim.json`. Commit both, so the build never needs Aseprite.
3. For a live loop while drawing, run
   `python3 tools/export_art.py --watch --also-copy-to src/HeroesNotIncluded/bin/Debug/net10.0/data/textures`.
   The game (Debug build, or with `DW_WATCH=1`) reloads art whenever those files change. F5
   reloads by hand.
4. `dotnet run --project src/HeroesNotIncluded -- --art-preview` shows every sheet with each tag looping
   side by side, plus pivot and point markers and a flip toggle. Run
   `dotnet run --project src/HeroesNotIncluded -- --check-art` to confirm every def's sprite and every
   needed animation exists. A test runs the same check.

### Aseprite conventions

| What | How |
|---|---|
| Animation names | Frame **tags**. A tag name ending in `!` plays once and holds its last frame (`takeoff!`). Others loop. |
| Timing | Frame durations as set in Aseprite. |
| Facing | Draw everything **facing right**. The game mirrors sprites for left. |
| Feet / anchor | A slice named `pivot`, either its pivot point or its centre. Default: bottom-centre. |
| Named points | Any other slice becomes a point. `mouth` is where fire breath leaves the dragon. |
| Frame events | Tag user data `event=N` marks frame N of that tag (e.g. the hammer strike that kicks up dust). |

Exports are trimmed and packed; the manifest records each frame's offset, so trimming never moves
the sprite.

### Animation tags the game asks for

The animator picks a tag from what the creature is doing. A missing tag falls back:
`glide → fly → idle`, `takeoff/land → fly`, `breathe → breathe_windup → idle`, `work/walk/sleep → idle`,
`downed → sleep`.

| Tag | When |
|---|---|
| `idle` | Standing around. Every sheet needs this. |
| `walk` | Moving on the ground. |
| `work` | Humans building, deconstructing, chopping, mining or digging, while at the site. Mark the strike frame with `event=N`. |
| `sleep` | Asleep. **Humans are drawn flat, top-down, head at the top** (the pillow end of `buildings/bed`), and `buildings/bed_blanket` is drawn over them in bed. Dragons stay upright, curled up. |
| `downed` | Downed or dead. Flat for humans, like `sleep`. |
| `fly` | Flapping flight (looping). |
| `glide` | Wings held out while soaring. |
| `takeoff!`, `land!` | One-shots at the start and end of a flight. |
| `breathe_windup` | Lining up a fire breath (looping). |
| `breathe!` | One puff of flame (about 0.4 s). |

The flight and breath tags are the season's dragon's (below, "The dragon").

**Four-way people (JRPG style).** Human sheets are 40×60 frames of people about 3 heads tall (pivot at
the feet, (20, 58); the top 4 rows are headroom, so a big crown lifted by an idle breath isn't cut off), with `idle`, `walk` and `work` drawn four ways: `_down` (facing the viewer), `_up`
(facing away), and the three-quarter `_downside` (turned toward screen right and the viewer) and
`_upside` (turned right and away), which the game mirrors for leftward. The turned views are the
front and back designs drawn through a turn (`Fig.T`, `Fig.TURNS`, the "angled" one, about 40°):
the head rotates on a cylinder (the face slides toward the far cheek, a full mass of hair behind it
on the near side), the body is foreshortened on its far half, and the far leg steps out. There is no profile: the
animator (`CreatureAnimator.ViewOf`) picks a three-quarter view for anything with a real sideways part
(older sheets with a `_side` profile still work as the fallback). The plain `idle`/`walk`/`work` alias
the `_down` frames. Standing about, a person shows their **stance** (`Appearance.Stance`):
`idle_<view>_crossed`, `_hip` and `_coin` (a toss, a catch, a beat holding it) beside the plain idle.
Sat on a stool with a drink or a bowl, `sit_<view>`: the body drops onto the seat (`SIT_DROP`), the
knees spread, and the cup is held at the lap and lifted for a sip on the third frame. The game draws
the cup at that hand (`WorldRenderer.LapHand`/`SipHand`, in the same design units).
`sleep` and `downed` stay top-down, centred in the frame, and are drawn flat at native scale. Faces have
big eyes (a lash line, a two-tone iris, a catch-light) and no mouth. Dragons and animals keep side-on
sheets.

Human sheets need `work`. Flying life stages need the flight tags, and dragons need the breath tags.
`--check-art` lists any that are missing.

### Hand-written manifests

Any tool works if it writes this format. The placeholder generator writes it directly:

```json
{
  "frameSize": [96, 80],
  "pivot": [46, 77],
  "points": { "mouth": [91, 24] },
  "frames": [ { "x": 0, "y": 0, "w": 96, "h": 80, "ox": 0, "oy": 0, "ms": 220 } ],
  "tags": { "idle": { "from": 0, "to": 3, "loop": true, "event": -1 } }
}
```

`x, y, w, h` give where a frame's pixels are in the PNG. `ox, oy` give where they sit inside the
full `frameSize` frame.

## Box models (furniture and item piles)

Beds, campfires, tables, stools, chests and item piles (wood, stone, food) are drawn as a
few textured boxes, listed in their def's `model` (see `ModelBox` in `Defs.cs`):
`{ "min": [x, y, z], "max": [x, y, z], "top": ..., "side": ..., "front": ..., "minCount": n }`.

A building with a `footprint` wider than a cell (the wagon) is drawn once, from its centre
cell: its boxes use coordinates past 0..1 (a 3x3's run from -1 to 2) to cover the cells round it.

- **Coordinates:** 0..1 inside the cell with z up (boxes may stand taller than 1). The front is
  +Y (south), which faces the viewer at the default camera. R turns a building while you place it.
- **Face textures** (`props/*`, from `tools/placeholder_props.py`) are 32×32 tiles drawn as if
  they covered a whole cell. A face shows only the part matching its size, so thin boxes keep the
  pixel scale instead of squashing the art.
- **Wood:** wood faces are tinted by the material (oak, pine, birch), or use `<name>_<wood>` art if
  it exists.
- **Item piles:** `minCount` makes them grow, with a box appearing once the stack reaches that
  count.
- **Sprites still needed:** the def's own `sprite` is still used for its toolbar icon and the
  inspector.
- **Set in a wall** (`isWall` with a `model`: the fireplace): drawn at full height, not squashed
  like furniture. Boxes starting at 1 or higher (a chimney stack) go with the roof: hidden when
  roofs are, and left out under another storey or while you're inside. `modelCutAt` leaves out the
  boxes starting that high while the walls beside it are cut down (the serving hatch's posts, the
  fireplace's chimney breast). Kit walls run straight on into such a model (`BuildingKit`).
- **Fire and smoke:** a fuelled model draws its flames at `fireAt` (model units; the cell's middle
  by default), `fireSize` times as big, and `smokeAt` sends a wisp up from a chimney's top.
- **Lamps:** a building's `lamps` (plan cells) hang its `lamp` def's model from the beams, with a
  small steady flame at its `fireAt` (the inn's lanterns). They're not structures, so they take no
  floor, and they light once the building's finished, without fuel.
- **Wall torches:** a lamp with a `facing` is mounted on the wall behind its cell (its back, the
  model's -Y edge, to the wall; the plan must have a wall there), and a lamp's own `def` overrides
  the building's `lamp` (`WallTorch`: an iron bracket and a pitch-wrapped head). It's left out while
  the cutaway cuts its wall down to a stub.
- **Set-in walls and the cutaway:** a plan gap filled by something built as a wall counts as wall,
  so the walls beside a fireplace aren't cut down for it.

## Building kits (preset buildings)

Preset buildings (`data/defs/buildings.json`) aren't drawn as cubes. Each look names a **kit** (`"kits"` in the same file; `Rendering/BuildingKit.cs`).

**Walls** are box models like furniture's, made for a wall running along X through the cell, with
y 0.5 as its middle. The mesher pieces each wall cell together from its plan neighbours:

- `span`: the whole cell, for a straight run.
- `corner` plus one `arm` (the cell's middle to its +X edge) towards each neighbour: where walls
  meet, turn or end.
- `post`: a `P` cell.
- `door`: the frame round a door. The leaf still swings on its own.

The pieces are turned as needed, cut down to stubs by the cutaway like any wall, and the
material picks the art (`wall_wood_birch`...). Use the tiling wall art for the long faces, so
pixels stay square up a level. On a second storey (`upperLayout`, the inn's) the same pieces are
drawn without their footing: any box wholly below `BuildingKit.FootingHeight` is left off.

The village's look is `timber_plaster` (limewashed panels in dark oak, straw thatch) and
`timber_shingle`, drawn by `tools/village_art.py` with the rest of the village's scenery (lanes,
garden soil, the well, the bridge, fences, crates, cabbages). It writes only its own files,
so it can be re-run alone, and makes the wood and stone versions of its art itself.

**The roof** is generated over the plot. Its ridge runs along the long side. It rests on the side
walls' middles at `pitch` levels of rise per cell, reaches `eave` past them, and reaches
`overhang` past the gable walls. These parts take sprites:

- `sprite`: the slopes.
- `trim`: the eaves' edges and the barge boards, which cross `horns` past the ridge.
- `ridge`: the beam along the ridge.
- `gable`: an alpha-cut triangle, opaque below the diagonal with its high side on the right. It's
  mirrored for the other slope.
- `gableFill`: the boards under a wide gable's triangle.

Each roof cell draws only its own share, so chunks mesh independently. Everything is flagged as
roof, so `\` hides it and the outline shows instead.

The slopes pick a variant per cell (`roof_turf_v2`...) and are mirrored at random. Corner and
post pieces are turned per cell, so their end grain doesn't repeat. A roof's `eaveTrim` (with
`lip`) hangs along the eaves in place of `trim`: turf roofs use it for their grass lip.

`tools/norse_kit.py` makes the kit's own art from the existing textures:

- the gable triangles (`gable_boards[_<wood>]`, from `wall_planks`);
- the carved barge board (`trim_carved`);
- the weathered post tops (`post_top`, from `props/log_end`);
- the carved doors (`door_carved[_<wood>]`);
- the turf lip (`turf_edge`);
- the roofs' rolled and mirrored variants.

The art audit (`--check-art`) checks every kit sprite.

## Bridges

A floor def with a `bridge` look (`BridgeLook`: the stone bridge in `village.json`) is dressed by
the mesher (`WorldMesher.AddBridge`) from the run of the span through each cell; the sim never sees
any of it.

- **Parapets** with coping along each side whose neighbour isn't part of the span; the ends, where
  the road comes on, stay open.
- **Abutments** at the span's ends, down to the bank; a **pier** every `pierEvery` cells, buttressed
  out past an open side; between them a stepped **round arch** with a ring of paler voussoirs.
- **Broken:** cells of a breach (`Repairs`) still count as span, so the arches keep their places.
  Beside the gap the parapets stop short and ragged, the vault's edge is torn, and there's rubble in
  the water.
- Art: `buildings/bridge_parapet`, `_coping`, `_pier` (and their stone variants), drawn by
  `tools/village_art.py`. A bridge reads best square to its river: Millbrook's crosses a straight
  reach (`tools/village_map.py`).

## People: layered looks

Humans are drawn in layers so the character creator can recolour and dress them, mixing and
matching every part (each layer has the base sheet's exact frame layout: the game matches frames by
index). Everything comes from `tools/chibi_people.py` (drawing helpers in `tools/chibi_art.py`): big-
headed chibi Norse people, about two heads tall, drawn at **four times the detail** (160×240 frames,
`"density": 4`; see "Fine sprites" below). `--preview` writes contact sheets to `.cache/people/`,
`--only hair` regenerates just the matching sheets. One shared skeleton per frame (`Fig`: shoulders,
elbows, hands, legs, the head's drop) drives every part, so sleeves, bracers and armor follow the arms
through every stance.

| Sheet | What | Drawn |
|---|---|---|
| `creatures/person_<body>` | linen underclothes (a chemise; a shirt and braies), the base every layer matches; the coin | as is |
| `creatures/person_<body>_skin` | the whole head, ears, arms with fists, legs | skin colour |
| `creatures/person_<body>_skin_elf` | the same with an elf's long swept-back ears | skin colour |
| `creatures/person_<body>_ears_elf` | an elf's ears again, what shows past the head, drawn over the hair | skin colour |
| `creatures/person_<body>_eyes_<style>` | the iris, per eye style (`round`, `narrow`, `sleepy`, `bright`) | eye colour |
| `creatures/person_<body>_face_<style>` | whites, lids, lashes, catch-lights, nose, mouth, blush | as is |
| `creatures/person_hair_<style>` | hair in front (from behind: all of it) and the brows; for both bodies | hair colour |
| `creatures/person_hair_<style>_back` | long hair behind the body, drawn before it all (cut under the base) | hair colour |
| `creatures/person_beard_<style>` | beards (`stubble`, `short`, `full`, `braided`) | hair colour |
| `apparel/<worn>_<body>` | a garment or piece of armor: trim, leather, metal (whatever lies over its cloth is cut from the dye) | as is |
| `apparel/<worn>_dye_<body>` | its dyed cloth, greyscale (main shade grey 200) | the dye |
| `apparel/<cloak>_back_<body>` (+ `_dye`) | a cloak's back panel, cut under the base | first |
| `apparel/held_<shape>_<body>` (+ `_metal`, `_back`, `_back_metal`) | a weapon or tool in the working hand, along the forearm through every pose and swing (front views over the body, back views behind it); the metal is tinted by the item's tier (`HumanLook.MetalTint`) | in hand |

**Folk** (`Appearance.Folk`, `HumanLook.Folks`: each its label, swatches, how often bearded and its build):
humans, our dwarves (ruddy, bearded, the dwarf build), our halflings (warm browns, the halfling build), and our elves, whose skin sheet has long ears swept up and
back past the head (`elf_ear`) and whose ears are drawn again over the hair (`ears_layer`), so they
stand out through any style. A new folk is a name in `FOLK` and its own skin (and ears) sheets;
the game falls back to the human skin when a folk's is missing. Elves run to their own swatches
(`HumanLook.ElfSkinTones` and friends) and grow no beards.

**Builds** (`tools/chibi_builds.py`, run last): dwarves and halflings have bodies of their own,
warped from every finished people sheet so all their layers stay aligned. The head drops (a dwarf's
5 design units, a halfling's 7), the body below the neck (31.5) is squashed to the feet (which stay
on the pivot) and a dwarf's broadened (×1.24, eased in below the neck). Each build has its own
`person_<body>_<build>[_skin|_eyes_*|_face_*]`, `apparel/<worn>_<body>_<build>` and, moved down
with its head, `person_hair_<style>[_back]_<build>` and `person_beard_<style>_<build>`. The game picks
them by folk (`HumanLook.FolkOf(...).Build`, `BodyKey`) and maps points such as the seated cup's hand
with the same numbers (`HumanLook.BuildPoint`). The builds about double the people's atlas pages.

Bodies are `male` and `female` (shown as Masculine and Feminine). Hair styles (`HairStyle`, new ones go at the end of the
enum) are `short`, `shaggy`, `long`, `ponytail`, `bun`, `sidebraid`, `crownbraid`, `pigtails` (two braids),
`topknot` (shaved sides), `swept` (layered, the fringe over one eye), `curlyshort`, `curlylong` and `none`.
The game lifts the hair tint (`HumanLook.HairLift`) so the chosen colour sits near the main shade. Every style is built **lock by lock**, as swept and the braided ponytail were first (`hwisp`,
`wave_lock`, `dutch_plait`, `coils` in chibi_people.py): a soft crown, then tapered locks of varied
width and length merged and lined as one mass, strand lines down each (a shadow along its fold, a
sheen at its root), strays breaking loose, curls as coils and ringlets of every size, braids woven
strand by strand, a bun wound round itself; each with a back view of its own. Beards follow
`art/reference/shortbeard.webp` and `fullbeard.webp`: the beard is the jaw, in smooth curves (its outer edge the
face's, round the chin and a little past, softening into tapered wisps along the bottom; beside the
eyes it keeps to the side of the face, never near them, then curves in along the jaw to the moustache
under the nose; a notch under the lip), and the full one hangs to a ragged row
of points under a moustache of its own whose tips curl up. `python3 tools/chibi_people.py --hair-preview [styles]`
draws every style (or those named) in four views beside its game size, then every beard, into
`.cache/people/hair.png`. Braids hanging from the hair are drawn before it, so the hair lies over their
roots with locks gathered into them; braids lying on the head (`dutch_plait(..., soft=True)`) are shaded
into it with no outline, only a mid-tone where each strand tucks under the next, and the braided
ponytail's is fed by locks off the crown. The curly styles follow `art/reference/curly-hair.webp` (a
wide, airy bob built of curl clumps, each a short springy lock with its sheen and spiral tip, painted
back to front over a dark depth: `clump`, `bob_clumps`, `paint_clumps`) and `curly-hair-long.webp` (big
S-waves wrapping the head and fanning out, each lock painted on its own with its sheen and spiral tip:
`s_path`, `paint_waves`; in front, the braided ponytail's sweep across the brow from a deep side parting). Big hair is
kept within a dome over the head (`under_volume`) rather than the skull's outline.

**A true three-quarter turn (a test, not in the sheets yet):** `--turn-test [styles]` draws each
style's turned views beside a head turned for real: drawn facing us and from behind, wrapped round
a cylinder per row (as wide as the head and hair there) and turned 30° (150° away), so the face slides
and foreshortens and the hair comes round the near side (`turned_figure`, `turn_warp`). The turn eases
off down the face to a third at the chin, so the jaw stays over the neck. Into
`.cache/people/turn.png`. Adopting it would turn the face sheets and every hair layer the same way. Hair is drawn as **locks**: tapered strands
with a parting or a cut fringe, and a broken band of the lightest step across the crown (the Octopath
sheen), never one shaded blob. People are about three heads tall: an 18 px head with a jaw and a neck,
sloped shoulders, a waist, and arms that hang beside the body and swing when walking.

The game draws, in order (`HumanLook.Layers`): a cloak's back panel, the hair's back half, skin, eyes
and face, clothes from the legs up (Legs, Feet, Tunic), body armor, packs, the cloak, the beard, the
hair (unless a helm with `hidesHair`), an elf's ears, head armor. `creatures/human` (the def's own sprite) is a copy
of the male base. Hand-drawn replacements must keep the same frames and tags.

**Clothes** (`data/defs/outfits.json`) are apparel in the Tunic, Legs, Feet and Cloak slots. Cloth ones
are made of a dye (`materials` of `Cloth`: undyed, woad, madder, weld, walnut, lichen, orchil,
iron-black), and the game tints their `_dye` layer with its colour (`HumanLook.Dye` scales it so the
main shade lands on the dye). Trim, metal, leather and fur stay in the plain layer. Their inventory
icons come once per dye (`items/tunic_woad`; the plain `items/tunic` is undyed, `tools/norse_items.py`).
Today's set: tunic, kaftan, apron dress (hangerok with tortoise brooches and beads), leather vest,
trousers with leg wraps, skirt, turnshoes, fur-topped boots, cloak (pinned at the right shoulder),
hooded cloak (hood down as a cowl), fur mantle, a short mantle (over the near shoulder and the front,
the far arm free), a leather vest over the tunic (a `garment` in the Body slot, with bracers) and a
scarf (a `garment` in the Neck slot, wound over the cloak or mantle).

**Worn armor** is a layer the same way: an apparel def's `worn` names a sheet (`apparel/iron_helm`); the
game uses `<worn>_<body>` if it exists, else `<worn>`. Badly worn items
(under 30% HP) are drawn darker. Today's armor: a laced leather jerkin, a ringed byrnie, a fur-brimmed
hood-cap, a spangenhelm (bronze bands, nasal guard, mail at the nape) and a leather bag with a knotwork
flap; the metal tiers are recoloured from them (`placeholder_mining.worn_layers`). `--check-art`
checks every layer's frames and tags against each look, and `--art-preview` ends with every look
dressed in each armor set and outfit.

### 3D to sprites

People can come from a rigged 3D model instead of the generator (parked for now): every view, animation and stance is a camera angle and an action, and each hair style
or garment is one more mesh rendered as its own layer. Two steps, per character (the Wanderer's
config is `art/3d/wanderer.json`):

```sh
blender -b art/3d/wanderer.blend --python tools/blender/render_layers.py -- art/3d/wanderer.json
python3 tools/sprites_from_renders.py art/3d/wanderer.json --aseprite
```

The first renders each layer, view and frame at 8× the frame size into `.cache/renders/<name>/`; the
second shrinks them to crisp pixels, snaps them to pixel-art colours, outlines the figure and writes
the sheets (and, with `--aseprite`, `art/creatures/<model>.aseprite` to touch up and export as above).

**A model is its own figure in the game.** Its config's `model` (`wanderer3d`) names its sheets:
`creatures/wanderer3d` (the base: underclothes and outline), `_skin`, `_eyes`, `_face`, `_hair`
(and an optional `_beard`), and a garment's `apparel/<worn>_wanderer3d` (`_dye`, `_back` as usual).
A look with `Appearance.Model` set (an outfit's `"model"`, the Wanderer preset's) is drawn from
those instead of the generated body and hair sheets; its colours still tint them, and the creator's
body and hair pickers switch back to the drawn figure. Clothes stay separate layers: the character
wears whatever has a sheet for the model, and a garment without one isn't drawn on it (no
fallback, since another body's sheet wouldn't line up). So each garment is its own mesh in its own
collection, rendered whole under the layers drawn after it, and the body is whole under them all.
A layer nothing was rendered for gets no sheet. `--check-art` checks every model a preset names.

**Fine sprites.** A sheet whose manifest says `"density": 4` (`write_sheet(..., density=4)`) has
four sheet pixels per world pixel; the game divides every sheet measurement by it
(`SpriteSheet.Density`), so it stands the same size as anything else with sixteen times the pixels.
Such sprites are drawn with `Quad.Smooth`: shrunk on screen, the shader box-filters them
(`quad.frag`'s `sampleSmooth`) instead of dropping texels, so a zoomed-out view stays steady. The
detail shows from Adventure's starting zoom in (`VillageGame.AdventureZoom`, 72), fully at the
closest zoom, in the creator and portraits. Shapes are designed in 40×60 world pixels and scaled up;
eyes, brows, strands, stitching and rivets are placed in sheet pixels. Outlines are 2 sheet pixels
(half a world pixel) so they survive the shrink. The atlas packs identical frames once, which with
trimming keeps every person's layers to a few pages.

**Making the model** (free tools):

1. **VRoid Studio**: build the character (hair, face, clothes). Export a VRM (Export > VRM, any
   version; the polygon reduction options don't matter at this size).
2. **Blender** (5.x) with the **VRM add-on** (Edit > Preferences > Get Extensions, search "VRM"),
   then let the setup script import it and sort it into layers:

   ```sh
   blender -b --python tools/blender/setup_vrm.py -- ~/model.vrm art/3d/wanderer.json   # -> art/3d/wanderer.blend
   ```

   It splits the meshes by material and files each piece into the collection of its layer by the
   config's `materials` rules (parts of VRoid's material names: `_SKIN` → `Body`, `EyeIris` →
   `Eyes`, `Tops` → `Vest`, ...). Whatever no rule matches lands in `Unsorted`, left out of the
   renders and listed: drag it into a layer in Blender, or add a rule. The collections are named as
   the config's `layers` say: `Underclothes`, `Body` (the skin), `Eyes`, `Face` (whites, lashes,
   brows), `Trousers` and `Trousers cloth`, `Shoes`, `Vest`, `Poncho` and `Poncho cloth`, `Scarf`
   and `Scarf cloth`, `Hair`. A garment's dyed cloth is its own collection (`... cloth`); its trim,
   straps and buckles stay in the plain one.
3. **A first test needs no animation**: a tag whose action is missing renders the pose the rig is
   in (the VRM's rest pose), with a warning. Then add **one action per animation**, named as the
   config's tags say (`Idle`, `Walk`, `Work`, `IdleCrossed`, `IdleHip`, `IdleCoin`, `Sleep`,
   `Downed`), and list the frames to sample (4 for a walk: contact, passing, contact, passing).
   Give each action a **fake user** (the shield icon), or Blender drops the unused ones on save.
   Animations come from posing by hand, VRM animation files (`.vrma`, imported by the add-on), or
   **Mixamo**: upload the model's FBX there once (its bones keep VRoid's `J_Bip_*` names), then
   download each animation as FBX, *Without Skin*, 30 fps (the walk *In Place*) into `art/3d/anims/`,
   list them under the config's `animations` (`"Walk": "anims/Walk.fbx"`) and bake them onto the rig:

   ```sh
   blender -b art/3d/wanderer.blend --python tools/blender/retarget_mixamo.py -- art/3d/wanderer.json [Walk ...]
   ```

   It matches bones by name (`bone_map` for others) in world space, so differing bone rolls and
   motion Mixamo put on the armature object (lying down) carry over; the actions get fake users.
   Then set each tag's `frames` to sample the action (the script prints every action's range).
   Lying tags (`"camera": "top"`) are turned and moved to lie head up, centred on `lying_center`.
4. **Materials**: VRoid's MToon shading is already toon-like and works. Whatever the game tints
   (skin, hair, irises, dyed cloth) is best **white or light grey**, so only the shading is left; a
   coloured one has its brightness stretched onto the grey steps (`"levels": false` on the layer
   keeps it as is). Everything else in its real colour. A sun from the top left (the setup script
   adds one if there's none).
5. **Scale and placement**: the armature's origin at the feet, on the world origin, the character
   facing −Y (toward the camera the script adds; VRMs import that way). The config's `root` is the
   object turned per view.

**The config**: `frame`, `pivot` and `pixels_per_meter` set the size (31 px/m makes a 1.75 m person
about 54 px tall in a 40×60 frame); `elevation` is the camera's angle above the horizon; `views` turns
the character for each view (degrees about Z: 0 faces the camera, 40 is the three-quarter view,
180 the back); each layer has a `role` (`skin`, `hair`, `eyes`, `beard`, `dye` are tinted by the game
and snap to its grey steps; `plain` snaps to a palette of `colors`; `base` is plain and takes the
outlines on tinted layers' edges), and a dye layer's `outline_to` is its garment's plain sheet. A layer's `cover` (0.45) is how much of a pixel's block must be
covered to keep it: lower for small details (the eyes 0.2). `bone_scale` enlarges bones on every
frame (`J_Bip_C_Head: 2` makes VRoid's ~7-heads figure read closer to the game's ~3), and
`rest_pose` turns bones (degrees about the world axes) for tags with no action yet, to bring a
VRM's T-pose arms down. Proportions set in VRoid itself (head size, eye size, leg length) look
better than `bone_scale`. A layer's `"shade": "toon"` renders it white in constant light steps
instead of its own texture (only the texture's transparency is kept): VRoid's hair textures are
painted dark at the roots, which from the game's high camera turns the crown into a dark patch.
The renders use their own light (`light`, from the upper left by default), not the scene's.

**What to expect**: renders shrunk this far lose detail; eyes in particular are best touched up by
hand (or drawn as a fixed overlay per view). The sheets the pipeline writes win over the generator's
(the `.aseprite` it writes claims them, `placeholder_sheets.hand_drawn`).

### Painting a look by hand

`python3 tools/make_look_aseprite.py` gathers a character's look into one Aseprite file to paint
over (today: the Wanderer preset, written to `art/creatures/wanderer.aseprite` when you run it). It has one layer per sheet
the game draws them from, bottom to top in the game's order and named after the sheet
(`apparel/scarf_dye_male`), every frame and tag (160×240 frames: paint at the 4× detail), the `pivot` slice, a palette of the tint
greys and clothing ramps, and a hidden `ref composite` layer showing the character in colour.

- Layers the game tints (`_skin`, `_hair`, `_eyes_*`, `_dye`) stay **greyscale**: paint in the
  palette's greys (white is the chosen colour, darker greys shade it). Everything else is drawn as is.
- Keep every frame and tag: the game matches look layers by frame index. A layer may be empty in a
  frame (the eyes from behind).
- `python3 tools/export_art.py` exports each sheet-named layer to its own sheet (untrimmed, so
  empty frames keep their place) and adds the plain `idle`/`walk`/`work` aliases; the reference
  layer isn't exported. Needs the `aseprite` CLI.
- Once a file in `art/` provides a sheet, `gen_placeholder_art.py` leaves it alone
  (`placeholder_sheets.hand_drawn`). Remove the layer (or the file) to hand a sheet back to the generator.
  To refresh a file nobody has painted yet from newer generated art: `DW_REGEN_HAND_DRAWN=1
  python3 tools/gen_placeholder_art.py`, then rerun `make_look_aseprite.py`.
- Sheets are shared: `apparel/scarf_male` is every man's scarf and `creatures/person_hair_swept`
  everyone's swept hair, so painting them changes everyone; the female variants stay generated.
  Only create the file when you mean to paint: while it exists, the generator leaves its sheets alone.
- `export_art.py` keeps the `"density"` the generator gave a sheet, so painted people stay their size.

Layers sort with their figure: `WorldRenderer.BillboardFrame` tells the shader where a trimmed
layer's feet are (`Quad.FeetInAo`), and each layer is nudged a hair in front of the one before.

Layer frames may be **trimmed** to their opaque pixels (the manifest's `ox`/`oy` give the
offset, as Aseprite's `--trim` export writes); everything that draws a layer places it by that
offset. The placeholder layers are trimmed to save atlas space; base sheets aren't.

## Nature, animals and piles

The animals (deer, boar, hare, wolf, bear, and the village's pig) come from `tools/chibi_animals.py` in the people's
style, untinted, on the side-on creatures' canvas (`tools/chibi_canvas.py`, shared with the dragon
and nature).

Trees, bushes, boulders, the standing and ley stones and the wild plants (`things/*`, variants
`_v2`/`_v3` and `_bare` included) come from `tools/chibi_nature.py`: puffy outlined foliage lobes (a
tone darker behind), flat cel shading lit from the upper left, at four times the detail on their old
canvases. They're static PNGs without an `.anim.json`; the game sizes them by the def's `renderSize`
and the sprite's aspect, and `WorldRenderer.DrawVariedBillboard` draws them with `Quad.Smooth`. It
runs last in `gen_placeholder_art.py`, replacing the older `norse_nature`/`norse_plants`/`norse_stone`/
`norse_glow` drawings of the same sprites.

**Stations and props of their own** come from `tools/station_art.py`: the inn's cask, keg, shelf,
larder, stew pot, wash tub, counter, serving hatch, fireplace and lantern, the village's barrel,
crate and barn, the whetstone, the meditation stone and herbs, and the pyre (toolbar and inventory
icons), plus the faces the cask's hoops, the pot and the wash tub wear (`props/iron_band`,
`props/iron_pot`, `props/washtub_side`, `props/suds`). Nothing borrows another thing's icon: a new
station gets its own there.

The Wood item's pile is a box model (`props/woodpile_top`/`_side`/`_end`, `norse_buildings.py`). Box
faces show the top-left of their texture at the face's size, they don't stretch it, so each log is
0.25 across and 0.125 levels tall (square on screen, as levels look twice as tall) and its textures
repeat every 8 px: one log end, one log's curve of bark.

An item def's `held` names its shape in hand (`sword`, `longsword`, `knife`, `axe`, `pick`, `hammer`,
`mace`, `club`, `spear`, `bow`, `rod`, `tongs`, `saw`, `chisel`, `brush`, `staff`, `crossbow`). A person holds the tool their
job works with (`Job.ToolKind`, found by `Crafting.ToolInHand`), else their weapon.

## The dragon

The season's last crisis is a wild dragon (`creatures/dragon_adult`, side-on, with the flight and
breath tags above), drawn by `tools/chibi_dragon.py` at four times the detail ("density": 4, so
it's drawn smooth like the people) to the design below; `--preview` writes `.cache/dragons.png`
with the mouth point marked. It has one life stage, the adult: the egg, the young, the second breed and their recolouring went with
DragonWorld. The dragon's breath is the orange `fx/breath` jet with the white `fx/breath_core`
(`norse_fx.py`) over its yellow core; ordinary fire (`fx/fire`, `fx/ember`, burning ground) is all orange.

## UI chrome (carved wood and iron)

The interface is drawn by Narsil's UI kit (external/narsil/docs/UI.md) from sprites made by
`tools/norse_ui.py`, named in `Ui/Theme.cs`:
- **Nine-sliced** (border 6 px, drawn at 2× the UI scale; the corners stay whole-pixel, the edges and middle tile):
  - `ui/frame_panel`: oak planks in an iron rim with riveted corners.
  - `ui/frame_title`: a carved title plate.
  - `ui/frame_inset`: a dark well.
  - `ui/bar_trough`: an iron channel for bars.
  - `ui/keycap`: a key.
  - Keep anything decorative inside the outer 6 px, so the middle tiles cleanly.
- **Toast and errand icons** (24×24, like the order icons): `ui/icon_coin`, `ui/icon_quest`, `ui/icon_wound`, `ui/icon_good`.
- **The hammering scene:** `ui/work/anvil`, `ui/work/hammer` (handle left, head right; the game swings it) and `ui/work/billet`. The billet is drawn pale, and the game tints it by heat.

- **The other scenes** (`tools/work_ui_art.py`): the whetstone and a blade (pale, tinted by its heat),
  the stew pot and salt crock, a bed and a sheet, a hand, a forearm and a turn of bandage, a
  strongbox's front and the probe, a curio and the loupe, a patient on a pallet, the span's dressed
  stone and spirit level, the rasp, and the cask's head, its tap and the mallet (pouring, tapping). `VillageGame.WorkScenes` draws them as a small scene over each
  game's gauges, moved and tinted from the session in hand. `--work-scenes` opens every scene in
  one panel, played by stand-in values, to look the art over without playing each game.

Picking a lock, winding, fitting, mixing, washing up and meditating stay drawn from the palette on
purpose: they're diagrams (a lock in section, a cue lane, a scrubbing grid) whose shapes are the game.

## Placeholder art

`python3 tools/gen_placeholder_art.py` regenerates all programmer art: tiles, plus the animated
sheets in `tools/placeholder_sheets.py`. **It overwrites files in `data/textures`, including hand-made
art with the same names.** Once real art exists for a sprite, remove that sprite from the generator.
It needs Pillow (`pip install pillow`) and is deterministic, so a rerun leaves git clean.

The Norse art pass (below) lives in its own modules, which the generator calls: `norse_art.py` (palette
ramps, the shaded-shape painter, the selective outline, the Nordic grade), `norse_people.py` (people and
worn gear), `norse_items.py` (the gear's inventory icons), `norse_ground.py` (grass, flowers, dirt, sand and water tops),
`norse_nature.py` (spruce, oak, birch and the burnt snag), `norse_stone.py` (every rock's top and strata
side, ore seams, clay, slopes and their sides, the earth side faces, moss, boulders) and `norse_glow.py`
(everything that glows: campfire, kiln, forge, ember crystal, the standing and ley stones, moonpetal), `norse_buildings.py` (walls of wood and every rock, floors, doors, stairs, ladders, beds,
the box models' face textures in `props/`, furniture and workstation icons, the wagon), `norse_gear.py` (tools
and weapons in every metal tier, bars, coins, jewellery, ores, fuel, building stock, logs and planks, food,
hides), `norse_animals.py` (deer, boar, hare), `norse_plants.py` (berry bushes, wild flax, emberroot) and
`norse_ui.py` (the toolbar icons, `fx/fire`, `fx/ember`), `norse_roofs.py` (turf, thatch and shingle
roofs) and `norse_fx.py` (the selection brackets, the rune-cut designation mark, the shield-shaped need bubble,
the chatter bubble, the breath's core). These run after the older placeholder modules and replace their drawings of the same
sprites; the old modules still draw the small white effects (`fx/zzz`, `fx/glint`)
and the soft `fx/smoke` puff (also the contact shadow under everyone). They draw a silhouette as a
mask, shade it from a blurred copy of itself lit from the top left, then add texture and detail by
hand. Preview with `norse_art.contact_sheet`.

## Style guide

The target is **HD-2D with a Norse hand**: Octopath Traveler's look (richly shaded pixel sprites with
real proportions, lit scenes, depth blur and bloom) in a cold Nordic world of wool, linen, iron and
spruce. **Redrawn from scratch: the dragon, the four trees, the grass (with
Nordic wildflowers), dirt, sand and water tops, every rock, ore, slope and side face, boulders, people and
everything they wear (sheets and inventory icons), everything that glows (campfire, kiln, forge, ember
crystal, ley and standing stones, moonpetal), every building, model texture and
furniture icon, every item, the animals, the forage plants, the UI icons, the roofs (turf over enclosed
rooms) and the selection, designation and bubble art.** Only the small white effects (`fx/zzz`,
`fx/glint`) and the smoke puff keep their older drawing. The renderer's
colour grade is cool and slightly muted by default (`VisualStyle`).

### Norse HD-2D (new art follows this)

- **Palette:** muted Nordic colours: moss and blue-green spruce, slate, weathered wood, wool dyes
  (woad blue `#3e5c98`, madder red `#9c3c34`, undyed oat `#e0d0b0`, walnut brown `#6c5038`), bronze,
  iron. Build each material from a **hue-shifted ramp** (`norse_art.ramp`): 4–6 steps whose shadows
  drift toward blue-violet and lose value, and whose highlights drift toward yellow and lose a little
  saturation. Never "colour, then the same colour darker".
- **Light and shading:** lit from the top-left. Round forms get their step from the slope against the
  light (`shade_shape`), then a deliberate texture on top: scale lattice, bark furrows, wool weave,
  bough ribs. Texture is ordered-dither or a fixed pattern, never random noise, so a frame that moves
  a pixel doesn't shimmer.
- **Outline:** *selective*. The silhouette gets a dark, violet-tinted version of the colour it borders
  (`selective_outline`), not flat black; limbs that overlap the body get a dark contour where they
  meet it. No black lines inside a form.
- **Proportions:** people about 3 heads tall (40×60 with headroom; the generator designs them on a 32-wide grid and
  broadens the body and legs through `Fig.T`): a readable face (3 px eyes, brows, a jaw), a neck, a
  wide easy stance, arms that hang clear of the body with a seam against it, small hands and feet. The
  dragon is 128×96 and keeps the 1.65:1 height ratio to a person it always had.
- **Norse details:** woven braid at hems, tortoise brooches and bead strings on the apron dress,
  leg wraps (winingas) and turnshoes, spangenhelms with a nasal guard (**no horned helmets**),
  knotwork on leather. Buildings (later batches) take turf roofs, carved gables and stave-style
  uprights.
- **Glow colours** (embers, eyes, fire, runes) stay separate and bright, outside any tinted layer, so
  bloom catches them and a later "unlit" pass keeps them visible at night. Use `norse_glow`'s `GLOW`,
  `HOT`, `CORE` (fire) and `RUNE`, `RUNE_CORE` (ley light), and pass them as `keep` to
  `selective_outline`. The Nordic grade leaves glow pixels alone (`norse_art.is_glow`: near-full red with
  a saturated orange-to-amber hue, or near-full blue), so graded art keeps its fire too. Only quads the
  renderer flags `Quad.Glow` (fire effects, defs with `glow`, such as moonpetal) actually bloom; the
  kiln's mouth, forge coals and ley runes are bright pixels on an ordinary texture.
- The people's greyscale tint layers and `ui/`, `fx/` art stay neutral because the game tints them. Do not run Norse art through `world_finish` (it is already in palette).

### Older placeholder drawings

- `nordic_finish` (via `placeholder_nature.world_finish`) still grades what the older modules save, and the
  Norse terrain modules author in the pre-grade space and save through it. Glow pixels pass through it
  untouched. Everything else Norse is drawn straight in the final palette and saved as it is.

### The dragon (original design)

Our dragon is its own creature, not any franchise's monster: upright on two legs and leaning
forward, a long neck curving like a carved prow into a wedge head with swept-back horns, wings wider
than its body, a ridge of bone plates down the back like a ship's ribs, rust scales (`#9e402e`) with
a bone belly and horns, and glowing **ember veins** stepped at right angles like runes. Keep to that
if it's redrawn; avoid a bright-orange body with a cream belly and teal wings.
