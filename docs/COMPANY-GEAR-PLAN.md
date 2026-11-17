# Grey wizard hat and robe, and gear for the rare companies

## Context
The two Tolkien-inspired rare companies in `data/defs/adventurers.json` borrow outfits that don't fit them:
- **GreyWanderer** wears the völva `Seer` outfit (kaftan and skirt).
- The dwarves (**ExiledHeir**, **Graybeard**, **AxeHand**) wear `Wanderer`: the blue-haired preset's heather poncho and rose scarf.
- **Burrower** (the halfling) wears `Farmhand`.
- **Ranger** and **ElfArcher** wear the generic `Hunter`.

Looks are random too (`HumanLook.Derived`), so the grey wanderer can turn up young, beardless, with blue hair.

The goal: a grey wizard hat and robe, a dyed dwarf hood, an outfit for each company kind, and an optional fixed look per adventurer kind.

## 1. New garments (defs): `data/defs/outfits.json`
- **WizardRobe**: Tunic slot, `materialsOf: Cloth`, covers Torso and both arms, ankle-length, `worn: apparel/wizard_robe`, `sprite: items/wizard_robe`, light armour and heat values like the kaftan.
- **WizardHat**: Head slot, `garment: true` (so outfits accept it), `materialsOf: Cloth`, `hidesHair: false`, small armour values, `worn: apparel/wizard_hat`.
- **DwarfHood**: Head slot, `garment: true`, `materialsOf: Cloth`, a close hood with a long tasselled tail, `worn: apparel/dwarf_hood`.
- No new dye: `Ash` ("cool grey") is the wizard's grey. If it reads too blue in the preview, add a warmer `Grey` dye (`Undyed` darkened) at the end of `materials`.

The renderer already draws a Head-slot `_dye` layer (`HumanLook.AddWorn` in `src/HeroesNotIncluded/Rendering/HumanLook.cs:529`), so the game side needs no change for dyed hats.

## 2. Art: `tools/chibi_people.py` (worn layers) and `tools/norse_items.py` (icons)
- **`g_wizard_robe(G, F)`**: modelled on `g_kaftan` (line 2056) for the body and sleeves, and `g_skirt` (line 2144) for the hem.
  - Full-length to about y 55, flaring at the hem, with wide bell sleeves (`sleeve(F, s, ...)` widened at the cuff).
  - A rope belt, and a darker trim band (`G.over`) at the hem and cuffs.
  - Cloth goes on `G.dye` so it tints.
- **`g_hat(G, F, kind)`**, for both head pieces:
  - **wizard_hat**: a wide floppy brim, then a cone that bends over to one side (back views: mirrored).
    - Headroom limit: the head's top is at y≈5 + `F.hy` and the frame starts at 0, so the cone can't stand straight up.
    - Instead, the cone climbs about 4 units above the head, then its tip droops sideways past the head's edge.
    - Every pose's `head_dy`/`body_dy` must stay inside the frame. The horned helm (tip at -0.4+hy) is the bound to match.
    - Cloth on `G.dye`; a darker band above the brim on `G.over`.
    - Skipped on sleep frames like the helms: add it to the `HEAD_ITEMS` tuple.
  - **dwarf_hood**: a cowl hugging the crown and cheeks, the face open, and a long tail falling behind one shoulder ending in a tassel.
    - Fully dyed. A small undyed trim edge on `G.over`.
- Register the pieces in three places:
  - `GARMENTS` (line 2492): `wizard_robe`, `wizard_hat`, `dwarf_hood`.
  - `HEAD_ITEMS` (line 2518): `wizard_hat`, `dwarf_hood`. The `figure()` composer (line ~2809) must also tint the `_dye` layer of head items (it only draws `worn[item][""]` today).
  - The `slots` order list (line 2800): add `wizard_robe`.
- Add a preview look to the `--preview` set (near line 2996): grey robe and hat, long grey hair and a full beard. Add a dwarf in a hood too.
- `tools/norse_items.py`: add `wizard_robe` (a variant of `tunic(c, long_=True)` with bell sleeves), `wizard_hat` and `dwarf_hood` icons to `DYED` (line 298), so `items/<name>_<dye>` and the plain `items/<name>` are generated.
- Regenerate:
  - `python3 tools/chibi_people.py --only wizard_robe,wizard_hat,dwarf_hood`, or the whole run if `--only` doesn't fit.
  - Then `tools/chibi_builds.py`, so the dwarf and halfling builds get the new sheets.
  - Then `tools/norse_items.py`.
- Update docs/ART.md's "Today's set" with the robe, hat and hood.

## 3. Company outfits: `outfits.json` "Story people" section, not presets
| Outfit | Pieces |
|---|---|
| `GreyWizard` | WizardRobe Ash, WizardHat Ash, Turnshoes, Scarf Ash (or a Walnut Mantle; judge in the preview) |
| `DwarfLord` (ExiledHeir) | Tunic Woad, Trousers IronBlack, FurBoots, FurMantle |
| `DwarfKin` (AxeHand, Graybeard) | Tunic, `dyes` [Walnut, Madder, Woad, Lichen, Weld], Trousers [Walnut, IronBlack], FurBoots, DwarfHood, `dyes` [Madder, Woad, Lichen, Weld, Orchil, Walnut, Ash]: each dwarf rolls a colour |
| `Burrowfolk` (Burrower) | Tunic [Weld, Undyed, Madder] as the waistcoat colours, Trousers Walnut, **no shoes** (barefoot), HoodedCloak Lichen |
| `Ranger` (Ranger) | Tunic Walnut, Trousers IronBlack, FurBoots, HoodedCloak, `dyes` [Walnut, Lichen] |
| `ElfWarden` (ElfArcher) | Kaftan Lichen, Trousers Ash, Turnshoes, Cloak Ash (the grey-green elven cloak) |

Adventurer-kind updates in `data/defs/adventurers.json`:
- `outfit` set on each of the six kinds above.
- **Graybeard**: the IronHelm stays (DwarfKin's hood is a Head garment too, and the helm, being armour, is worn after the clothes and takes the slot).
  - Check `sim.Wear` replaces a Head garment. If so, give Graybeard its own outfit without the hood (a `DwarfElder` with a Kaftan Walnut instead of the Tunic) and keep the helm.
  - ExiledHeir and AxeHand keep the IronHelm. AxeHand drops it so the hoods show, keeping the ChainShirt.
- **GreyWanderer**: no armour, as now. Weapon: the Staff stays.

## 4. A fixed look per adventurer kind (sim and renderer)
- `AdventurerKindDef` (`src/HeroesNotIncluded.Sim/Defs/AdventurerDefs.cs`): optional `Look { Body, Hair, Beard, HairColor }` (strings parsed into `BodyType`, `HairStyle`, `BeardStyle` and a hex colour, validated in `Resolve`).
- **`Kin`** (`Components.cs:1073`): extend it with optional overrides: `BodyType? Body`, `HairStyle? Hair`, `BeardStyle? Beard`, `uint HairColor` (0 = derived). Its doc: the cosmetic look of someone without an `Appearance`.
  - Codec (`ComponentCodecs.cs:738`): write the new fields, read them only when `r.Version >= 52`.
  - Bump `SaveGame.FormatVersion` to 52.
- **`PartySystem.Spawn`** (`PartySystem.cs:392`): add `Kin` when the kind has a `Folk` or a `Look`. Put the helper on `AdventurerKindDef` or `PartySystem` (`KinOf(kind)`) so both spawns share it.
- **`ExpeditionSystem.Respawn`** (`ExpeditionSystem.cs:775`): re-add `Kin` from `m.Kind`. Today a returning company loses its folk.
- **`Clothing.DerivedBody`**: callers that know the entity use a new `Clothing.BodyOf(sim, e)`, which honours `Kin.Body`. That's the `Clothing.cs:131` call.
- **`HumanLook.Of`** (`HumanLook.cs:176`): after `Derived(...)`, apply the `Kin` overrides (body, hair, beard, hair colour).
- Data:
  - GreyWanderer: `"look": { "body": "Male", "hair": "Long", "beard": "Full", "hairColor": "B8B8B4" }`.
  - Dwarves: `"beard": "Full"` or `"Braided"`, so none comes beardless.
  - Halflings and elves: no change.

## 5. Body poses for the eight emotes (same art pass, so one regeneration covers everything)
The emotes landed in `8801df6` with only a bubble. The game already asks for the frames:
- `CreatureAnimator.Pose` swaps idle for `EmoteTag(kind)`, i.e. `emote_<kind>`, and `Directional` looks for `emote_<kind>_<view>`.
- `Fallback` sends a missing tag to idle. A point turns the figure toward its cell.

So this is art only, in `tools/chibi_people.py`, plus the roadmap line (`docs/ROADMAP.md:153`).

- **`sequence()`** (line 61): append, after the `sit_*` block so every existing frame keeps its index:
  - `emote_<kind>_<view>` for the 8 kinds × 4 `VIEWS`, about 4 frames each.
  - An alias `emote_<kind>` → `emote_<kind>_down`, like idle and walk.
  - Wave, laugh, angry and weep loop. Bow, shrug, cheer and point play once (`loop=False`) and hold their last frame. The animator falls back to idle when a one-shot ends, so the timings must add up to `Emotes.Duration` (2–4 s); hold the last frame long.
  - Update the docstring's frame count.
- **`Pose`**: add `face=None` (an expression: `laugh`, `weep`, `angry`, `lifted`) and a `shoulders=0` lift for the shrug.
- **`Fig.arm`** (line 134): one branch per emote, in the style of `crossed`, `hip` and `coin`:

| Emote | Arms (role 1 = the working hand) | Body and head | Face |
|---|---|---|---|
| wave | working hand raised beside the head, swinging out and in across 4 frames, front | still | as is |
| bow | both hands together at the waist (or the working hand on the chest) | `head_dy` +1 → +3 → hold → +1 (head dips; no torso tilt in this rig) | eyes closed on the hold |
| cheer | both hands up over the head, fists | a small hop (`body_dy` −0.8 and legs lifted). The wizard hat's droop must still fit at the hop's height. | `laugh` |
| laugh | free hand on the belly, front | head bobbing back and forth (`head_dy` −0.5/+0.25) | `laugh`: eyes as happy arcs, mouth open |
| point | working arm out at shoulder height: full-length sideways in the `*side` views, foreshortened toward the viewer (hand before the chest, high) in down and up | still | as is |
| shrug | elbows out, palms up at the waist | shoulders lifted 1 unit, head tipped | `lifted` brows |
| weep | both fists to the eyes in front views (face in hands); hands at the face, unseen, from behind | head down +1, shoulders shaking (`body_dy` 0/0.3) | `weep`: eyes shut, a tear |
| angry | fists clenched stiff at the sides, the working fist shaking up by the shoulder | a slight shake | `angry`: brows slanted down to the nose, mouth flat |

- **Expressions**:
  - `eye()` and `eyes_layers()` (lines 298 and 322): the face and eyes layers read `F.pose.face`: closed arcs (laugh, weep), a tear drop (weep), and the open mouth for laugh.
  - `brows()` (line 1875; drawn in every hair layer): slant for angry, raise for lifted.
  - Back views need nothing.
- **Arms over or in front of the head** (wave, cheer, weep):
  - `front_arms()` and the skin layer put the forearm over the face. Check that hair, helm and hat layers (drawn after) don't cover a fist raised before the face. If they do, add an "arm over head" mask drawn into the head-item and hair layers' cut, like `front_arms`.
  - Weapons can't be drawn while emoting (`Emotes.Problem`), but a held tool (the wizard's staff, a smith's hammer) is still in hand: check the `held_*` layers follow the arm in the new frames, or hide them there.
- Builds: `chibi_builds.py` warps every frame, so dwarves and halflings get the poses for free. Check that the raised hands survive the head drop.
- **Atlas cost**: about 128 new frames on top of the current sequence, across every person layer and both builds. Check the atlas page count before and after (`--check-gpu`, or the atlas log at start-up).
  - If it grows too much: 3 frames per emote, and identical up/upside frames for the face-only emotes. Directional falls back from `upside` to `side`, not to down, so every view still needs its own tag.
- `--preview`: add a contact sheet of every emote in every view (`preview(..., tags=[...])`) to `.cache/people/emotes.png`.
- Docs: the people section of docs/ART.md lists the emote tags; strike the "body poses" half of the roadmap line (ghosts' emotes stay); record it in docs/HISTORY.md and the changelog.

## Tests
- `tests/HeroesNotIncluded.Sim.Tests/CompanyTests.cs`:
  - The grey wanderer comes dressed in the robe and hat (`Clothing.In(sim, e, ApparelSlot.Tunic)` is a WizardRobe of Ash), with `Kin` carrying the long grey hair and beard.
  - Dwarves wear hoods (or helms) and keep their `Kin` after a quest's return (`Respawn`).
- A codec round-trip covers `Kin`'s new fields (the existing save tests should pick it up; add a case if not).

## Verification
1. `dotnet build HeroesNotIncluded.sln`, warning-free.
2. `dotnet test -c Release`.
3. `--bench` hashes for seeds 1234 and 99:
   - Dressing changes item entities, so the hashes may move. That's expected for a content change.
   - Note the new values; they're not a pure refactor.
4. `dotnet run --project src/HeroesNotIncluded -- --check-art`: every new sheet, frame, tag and dyed icon exists, including the `_dwarf` and `_halfling` builds.
5. `python3 tools/chibi_people.py --preview`, then look at `.cache/people/` for the hat's droop and headroom in every view (and in walk, sit and attack frames). Check the robe hem in walk frames.
6. In game, `--new-game --screenshot shot.png --screenshot-at 5`:
   - Spawn the Long Road company via its rare day, or extend an existing test hook. Easiest: run `SeasonScenario` with the company's chance forced in a scratch def edit, not committed.
   - Eyeball the wizard, the halflings and the hooded dwarves. Also check `--art-preview`'s look list.
7. Emotes in game:
   - Run `--new-game --name Arin` and use the emote wheel.
   - Check each emote in all four views by turning the camera with the eighth-turn keys.
   - Check them on a dwarf (`--folk Dwarf`) and while holding a tool.
   - `--check-art` confirms every layer, the builds included, still has the base sheet's frame count.
