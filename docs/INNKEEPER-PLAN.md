# Innkeeper playtest: travellers, bedtimes, order buttons, keys, markers, mini-games, kegs, the upper floor

Suggested order (each a commit): upper floor → keys into hand → markers → order card buttons + take-it-to-them → keg balance → mixing cues →
staggered bedtimes → travellers → mouse cleaning mini-games.

## Context

Playtesting the innkeeper, letting rooms is feast or famine. The code says why:
- `PartySystem.Present` (`Adventurers/PartySystem.cs:79`) allows one party in town at a time: 2–4 guests for six rooms.
- `ShopSystem.WantsARoom` (`Shops/ShopSystem.cs:250`) opens at 18:00 for everyone at once, so the whole party rushes the board together.
- A party out on a quest leaves the inn with no lodgers at all.

Fix: (1) **travellers**, lone non-adventurers (pedlar, pilgrim, drover, minstrel) who come in the late
afternoon, eat, drink, take a room for a night or two and move on, never questing and never holding
parties back; (2) **staggered bedtimes**, each guest asking for a room at an hour of their own.

## Approach: a traveller is an `Adventurer` of a one-member `PartyDef` flagged `traveller`

Reusing the component gets the hatch, the bar, lodging, directions, rumours, satisfaction, tastes,
the festival and the save codec for free. Quest offers already need the party to have a Quest order
open (`ExpeditionSystem.Offer`, `QuestOrder`), so one guard at the source stops quests everywhere.

### Defs: `Defs/AdventurerDefs.cs`, `data/defs/adventurers.json`
- `PartyDef`: add `Traveller` (bool) and `NightsMin`/`NightsMax` (default 1/1; validate in `Resolve`).
- New kinds: `Pedlar`, `Pilgrim`, `Drover`, `Minstrel`. Thin purses (around 6–14), `spend` below 1,
  their own `tastes`, a plain outfit, no weapon (a drover might carry a club). Low skills.
- New one-member parties with `"traveller": true`, `"wagonChance": 0`, label "a pedlar" etc., and a few names each.

### Arrivals: `Adventurers/PartySystem.cs`
- `NextParty` and the party schedule pick only non-traveller defs. `Present` ignores travellers, so
  parties still come while a traveller is lodged.
- A second schedule, `NextTravellerTick`, on its **own** `SimRandom` stream (`seed ^ …`) so it doesn't
  reshuffle party rolls. Constants: `TravellerFromHour = 14`, `TravellerUntilHour = 19`,
  `MaxTravellers = 2` in town at once, roughly one a day (`HoursBetweenTravellers` ±30%). None during
  the festival or in practice (the existing early return covers both).
- `Send` takes the traveller path for a traveller def: `LeaveTick` is the morning after their last
  night (about 8–10h, `Nights` rolled), not `StayHours`; a word of their own ("A pedlar comes up the
  road."), not "ride in … wanting everything at once"; no wagon. `RoadWorn` still applies (no weapon
  to dull, now and then a scrape for the empath).
- Save: write `NextTravellerTick` and the new stream state. Bump `SaveGame.FormatVersion` to 42 and
  read them behind `r.Version >= 42`.

### Behaviour guards
- `Shops/ShopSystem.cs:189`: no `ServiceKind.Quest` want when `a.PartyDef.Traveller`.
- `Director/Reckoning.AdventurerLeft`: a traveller's word counts for less. Add a weight parameter,
  0.5 for travellers (`TravellerWord` const), from both callers in `PartySystem.SendOff` and
  `FestivalSystem`/`FinalCount`.
- Check the remaining `Adventurer` readers by hand: `Feasts` (only a party back from a quest gets a
  feast, so no change expected), `FestivalSystem` toasts (a party of one averages fine), `BotPlayer`
  quest offering (it goes through `Offer`, so it already fails).

### Staggered bedtimes: `Shops/ShopSystem.WantsARoom`
- Each guest has a bedtime derived from their entity and the night, never stored:
  `Bedtime(who) = LodgingFromHour + Spread * hash01(who.Index, Lodging.Tonight(sim))`, with `Spread = 3.5`,
  so bedtimes fall between 18:00 and 21:30, before `AiQueries.IsNight` (22:00) sends them to sleep.
  Use `SimRandom.Hash` as `InnRooms.FitLocks` does. They don't ask before their bedtime. The small-hours path
  (`SmallHours` → `WantsARoom`) is past every bedtime, so it's unchanged.
- No codec change, and it's deterministic on every peer.

### Order card: a check to take it, and a button to take it to them (`src/HeroesNotIncluded/VillageGame.Shop.cs:76-114`, `DrawOrderCard`)
- "I'll do it" becomes a green check: `Gui.IconButton` with the existing `ui/icon_good` (a green tick, now used only by `Hud.cs:307`),
  tooltip "I'll do it". Same `TakeOrderCommand`. Do the same on favour cards (`VillageGame.Favours.cs:129`) so the two match.
- Beside it, once the order's yours (`e.MineToDo`) and it's a Drink or MixedDrink: a "take it to them" icon button showing **what they
  ordered** (`ServiceIcon(kind)`, e.g. `items/ale`). It's enabled when you carry a drink that fills the order (in hand or pack, the same test as
  `AvatarSystem.Supply`, `:563`); otherwise it's greyed out with the tooltip "Pour it first". It issues `SeeToCustomerCommand`. Repair, Unlock and Appraise
  keep their text `DrawErrandButton`, now shown as the same icon button for consistency (`ui/work/hand.png` is the fallback icon).
- Sim: `ShopSystem.HandsOver` (`ShopSystem.cs:855`) grows to include Drink and MixedDrink. `SeeToCustomerJob` (`Shops/SeeToCustomerJob.cs`)
  takes a drink branch: find the poured drink on the pawn (as `Supply` does), `GoToEntity(e.Poster)`, then hand it over through
  `ShopSystem.Received` (`:1107-1121`: serve, pay, `Table.Begin`). `CommandRules.Refusal` already checks the order is yours.
- Test: a player with a poured ale and a taken drink order from a customer sat across the room issues `SeeToCustomerCommand`.
  The customer is served and paid, and the job fails cleanly if the ale's been drunk or set down.

### Finding your customer: markers for every order you've taken (`VillageGame.Errands.cs:16-31`, `UpdateErrandPointers`)
- Now: an order's bubble is shown only while it's open (`Shop.cs:342`), and edge-of-screen `Pointers` only for Repair, Unlock and Appraise. A
  room guest who's paid and wandered off, or who's waiting on their key, can't be found.
- Fix: a `Pointer` (head tag, or an edge arrow when off screen) for **every** order you've taken that's still to be done (`MineToDo`), each
  labelled with what's owed ("Their key: room 3", "Their ale"). The lodging steps get theirs: signed in the book but key not yet given → a pointer
  to the guest; a guest sent to the wrong room (`WalkIn`) → a pointer once they're back down.
- The room book (`VillageGame.Rooms.cs`) also lists tonight's guests by room (from what the player was told and wrote, not hidden state:
  the book holds what was written). Clicking a name sets a pointer to them for a while.

### Keys off the board go into your hand (`src/HeroesNotIncluded.Sim/Core/SimCommands.cs:143-185`, `TakeFromStorageCommand`)
- A room key (`InnRooms.IsRoomKey`) taken from the board goes straight into your empty hand (`Carrying.Item`); if your hand's full, it
  goes into the pack as now. The inspector's "Hand over the key" (`Shop.cs:397`) then works without a trip to the inventory.
- Test: an empty-handed avatar taking a key from the board holds it; with something in hand, the key goes into the pack.

### Keg balance: the cask should need changing
- Now: `AleCask` holds 60 servings (`data/defs/furniture.json:93`) and starts full. My estimate is about 40–55 drink orders a season,
  about 65% of them ale, so one cask lasts the season and the 3 storeroom kegs (`VillageRound.StoreroomKegs`) are never touched.
- Fix: cask `servings` 60 → **24**, so with travellers' drinks a season wants about 2 kegs; `StoreroomKegs` stays 3. Confirm a mixed
  drink draws from the cask (`MixSession`: check it calls `PourSession.Draw`; make it draw if not).
- Re-check `InnUpkeepTests` refill bounds: they're written as `60 - SprayPerMiss`, so they should read the def's servings. Add a count of
  ales poured and kegs tapped to the season sweep's report (`Debug/SeasonSweepRun.cs`) so this can be tuned from the numbers.

### Mixing mini-game: readable cues (`src/HeroesNotIncluded/VillageGame.Bar.cs:349-386`, `DrawMix`)
- Now: each cue's key letter is `Theme.Text` (cream) on a filled `Theme.Accent` (ember) disc at 85%, which is low contrast, and ember
  everywhere goes against the brand's "keep ember rare".
- Fix: a dark disc (`Theme.Oak`) with a `Theme.Barrel` ring, and the letter in `Theme.Parchment` (bright on dark), drawn larger (about 1.4×
  through the font scale) and centred properly. The cue that's due next is ringed in `Theme.Good` as it nears the mark, and missed or hit cues flash
  `Theme.Bad`/`Theme.Good` as they pass. Make the bar taller (90 → about 110) so the bigger tokens fit. Gamepad glyphs come from `Controls.Cue` as now.
- Check it in a screenshot of the mixing panel at a couple of UI scales.

### Mouse in the cleaning mini-games (`WashSession`, `ScrubSession`)
- Pointer input: append `WorkInputKind.Point` (last, since kinds go over the wire by number). Its `Value` packs the pointer's place in the panel as
  two bytes (u, v quantised 0–255). It's stamped and batched like every other input, so `WorkPredictor` and the sim replay it
  identically (no side command like `AimThrowCommand`). `WorkSession` gets a virtual `Pointed(float u, float v)`.
  `VillageGame.UpdateWork` sends a Point when the mouse is over the mini-game's area and has moved a quantum, and the left button
  counts as `Held`. Keyboard and pad keep working as now.
- Wash (`Work/BarWork.cs:103`): with the mouse, the brush goes straight to the pointer; hold the left button to scrub. Same grime grid and rules.
  `DrawWash` (`VillageGame.Bar.cs:21`) reports the piece's square so the UI can map the pointer to (u, v); the hints show "drag to scrub" when
  using the mouse.
- Scrub (`Hospitality/MessSystem.cs:205`): rework it from mashing A/D into rubbing the stain out. The stain becomes a blot grid like the wash
  (its starting total from the mess `Amount`). The mop or rag lifts dirt only while it's **moving** across a blot (the distance rubbed
  under it, faster with a mop: `WithMop`/`ByHand` become per-distance rates). Mouse: drag over it. Keyboard and pad: A/D sweeps the
  mop back and forth along a row and W/S moves between rows, so it keeps its "back and forth" feel but has to cover the stain.
  `Finish`/`Interrupted` still apply the partial clean. Redraw it in `DrawScrub` (`VillageGame.Bar.cs:227`) with blots instead of one fading sprite.
- Tests: in the existing WashSession/ScrubSession tests (or a new `CleaningTests`), inputs given as Point+Hold clean a piece; holding still
  over a stain doesn't clean it but rubbing does; and a predicted and a replayed session given the same batches agree.

### Upper floor: missing floorboards along every wall
- Cause, in the renderer only (the sim floors all 168 cells): `WorldMesher.AddSlab` (`src/HeroesNotIncluded/WorldMesher.cs:533-596`)
  clips an upper-storey wall cell's boards to stop at the cell's middle, wherever a neighbour isn't `Inside`. In a straight wall run,
  both neighbours along the run are walls, so the slab is zero-width and nothing is drawn. The strip between the thin timber
  wall and the cell edge shows through. 9af8e3b fixed this only for door cells (`door && IsWallAt`).
- Fix: on that axis, `Run` is true for a neighbour that's `Inside` **or** a wall cell (any wall, not just doors). The boards then run along
  the wall line and stop only at the building's outer edge (a neighbour with no floor), which keeps fc96bb3's no-jutting intent.
  Check corners and T-junctions in the cutaway.
- Test: pull the extent maths out into a static helper and assert a non-zero area for every upper wall cell of the inn
  (pattern: `tests/HeroesNotIncluded.Tests/CutawayWallTests.cs`, building from `StoreyTests.TheInnStandsWithItsUpperFloorStairAndRoof`).

### Docs
- `docs/DESIGN.md`, Innkeeper "Rooms": a line on travellers and on guests turning in at their own hour.
- `docs/HISTORY.md`: an entry once it's built, with new bench baselines (rounds change, so `--bench --round` hashes will change; plain `--bench` shouldn't, because no round is live).

## Tests: `tests/HeroesNotIncluded.Sim.Tests/` (new `TravellerTests.cs`; follow `PartyTests` and `InnRoomsTests`)
- A traveller arrives only between 14:00 and 19:00 in a live round, and never more than `MaxTravellers` at once.
- A traveller never posts a Quest order, and `Expeditions.Offer` to one returns false.
- A traveller asks for a room in the evening, is let one, and leaves the morning after their last night (checked out, bed unmade).
- A party is sent while a traveller is present (`Present` ignores travellers).
- Bedtimes: two guests in the same evening ask at different hours, and both have asked by 21:30.
- Save round-trip with a traveller in town and `NextTravellerTick` pending.

## Verification
- `dotnet build HeroesNotIncluded.sln` (stays warning-free); `dotnet test -c Release`.
- `dotnet test --filter SeasonScenario --logger "console;verbosity=detailed"`: travellers in the log, the season still reaches the reveal.
- `dotnet run -c Release --project src/HeroesNotIncluded -- --bench --seed 1234` and `--seed 99`: plain hashes unchanged.
- `--season-sweep --seeds 8`: compare Renown/Dread against the baseline, to see the travellers' added word.
- Play: `--new-game --trade Innkeeper --name Arin`. Watch a party go off on a quest and a traveller
  still turn up for the evening, and keys asked for across 18:00–21:30 rather than all at once.
