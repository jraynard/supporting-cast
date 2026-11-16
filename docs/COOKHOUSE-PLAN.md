# The cook supplies the inn (Phase 5), and what Reedmere still wants

Suggested order (each a commit): provisions order → the hamper → the inn's warming shelf → the AI
and the bot → Reedmere follow-ups.

## Context

Phases 1–4 are done (docs/HISTORY.md, 2026-11-15):
- **Town roles.** Workplaces and services go by town role (`Defs/TownRole.cs`,
  `ShopSystem.BuildingFor` / `WorkplaceOf`).
- **New buildings.** The `Cookhouse` and the kitchenless `LandingInn` exist.
- **Building on water.** Decks on piles carry buildings over water.
- **Reedmere** (`tools/lake_map.py`) is the second town. It puts the cook in a cookhouse on the
  shore and the innkeeper in an inn at the far landing.

Reedmere plays clearly harder than Millbrook. Over 8 seeds of
`--season-sweep --map Reedmere`:

| | Reedmere | Millbrook |
|---|---|---|
| Renown, mean | 29.5 | 51.9 |
| Wins | 0 | 2 |
| Dread, peak mean | 71 | 68 |

Most of the gap is the walk. Parties loiter at the inn (their haunt is the Tavern), and every
meal is ordered at the cookhouse's hatch, across the square and the causeway. Meals lapse, lines
go unserved, and parties leave unhappy.

The design chosen for the split is **supplier**:
- The cook still sells meals over their own hatch.
- The cook also supplies the inn, so the inn's guests eat without the trek.
- The two trades depend on each other, which gives a traitor (M4) something to spoil.

Everything here applies **only where the Tavern and the Kitchen are different buildings**. On
Millbrook both are the inn, and none of it may switch on. Check that with
`BuildingFor(Tavern) != BuildingFor(Kitchen)`.

## Approach

### 1. Provisions: an order from the inn to the cookhouse
- **The service.** Add `ServiceKind.Provisions` (`Defs/ServiceDefs.cs`; add at the end of the enum)
  and an entry in `data/defs/services.json` with `"at": "Kitchen"`. It's a trade price per hamper,
  a little under the meals it holds.
- **Who orders it.** The innkeeper (whoever's trade has the Tavern role) posts it at the cookhouse,
  using `ShopSystem.Post` with `where:` the cookhouse.
  - A player does it with F at the cookhouse hatch, as a meal is ordered (`ShopSystem.PlayerOrder`).
  - It is paid from the innkeeper's own purse (`Coin`), like any order. Making a till for the inn
    is a bigger change; leave it.
- **Refusals.** `CommandRules.Refusal` must refuse it on Millbrook, and from anyone who isn't the
  inn's keeper. It must also hold up in `CommandTests.HostileCommandsDoNoHarm`.
- **The cook's side.** It is an ordinary `Order`, so the hatch line stays derived from open orders.
  The cook sees it on their sign and order cards (`VillageGame.Shop.cs`, `Serves`). Add
  `Provisions` to the Cook's `serves` in `trades.json`.

### 2. The hamper
- **The def.** A new item, `Hamper` (`data/defs/` items, a storage item like a pack), holds
  dishes: bowls of stew, pies, bread or rations.
- **Packing.** The cook fills it at the prep table, reusing what packing rations does
  (`Work/Rations.cs`, `PackSession`). Either:
  - give it a hamper mode, or
  - add a `HamperSession` that moves finished dishes in from the hatch shelf or the pot (one
    ladle a bowl, `Vessels`).

  The order is done when the full hamper is handed over at the hatch (`Hatch` with `Fits`).
- **Hidden state.** Anything hidden about it (a dish going off, later a traitor's tampering) is read
  only through `Perception`, as `Ration.LooseWrap` is. Never show it in the UI directly.
- **Carrying.** The innkeeper fetches it, or the labourer does it as a favour (`Favours/`, an errand
  "bring the hamper to the inn"). It's bulky, so it's carried in both arms (`Carrying`).

### 3. The inn's warming shelf
- **The furniture.** A `WarmingShelf` in `LandingInn` (`buildings.json`), behind the bar, marked
  `kept` like the `BarShelf`, so it isn't eaten from by passers-by (`Simulation.InKept`). Unpacking
  a hamper onto it is F at the shelf.
- **What draws on it:**
  - **Lodgers' board.** A lodger (`Lodging`, `LodgeStay`) eats a dish from it at supper and
    breakfast, handed over by the innkeeper like a drink (`BarService`, `ServeJob`), with no meal
    order to the cookhouse.
  - **Quest parties.** They set out with rations from it if there are any (`ExpeditionSystem`,
    where rations are taken now).
  - **Ordinary meals.** On a split map, a Meal order from someone at the inn could be served from
    the shelf by the innkeeper before falling back to the cookhouse. Decide in playtest whether that
    takes the cook's trade away; at most allow it for lodgers.
- **An empty shelf.** Lodging is worth less to adventurers (scale `worthMin/Max` in
  `ShopSystem.ChooseShop` for Lodging) and parties leave without rations. The resolver already
  handles a party with none.

### 4. The AI and the bot
- **AI innkeeper.** A `WorkGivers` entry posts a provisions order when the shelf is low and none
  is open. A `HaulJob` (or a new `FetchHamperJob`) brings a packed hamper from the cookhouse hatch
  and unpacks it.
- **AI cook.** Serves provisions orders front first like meals (`ServeJob` / `Hatch`). Packing is
  done the plain way, without the game, as `LayOnHands` does for the empath.
- **Bot** (`Bots/BotPlayer.cs`, commands only). As innkeeper it orders, fetches and unpacks. As
  cook it packs and hands the hamper over.

### 5. Reedmere follow-ups (independent; any order)
- **The carter.** `Repairs.RoadCut` is global, so a cut causeway stops the carter even at the
  cookhouse on the shore, and the message says "over the river". The carter should still reach
  shore buildings; only what's across the breach waits. Word the message by what's actually cut.
- **Feasts.** On a split map the feast is called when a returning party walks into the Kitchen,
  but parties loiter at the Tavern (`Hospitality/Feasts.cs`). Call it at the Tavern and serve it
  from the warming shelf, or have the party come over to the cookhouse.
- **The festival** is on the shore green, and a party stranded out on the water (causeway down,
  decks burnt) misses it and the Renown it's worth. Hold the festival on the deck square once the
  bonfire is safe on planks, or accept it as the lake town's risk.
- **Falling in.** Deep water is impassable, and a burnt-out deck strands whoever was on it
  standing over water. Follow-ups:
  - knockback off the walks;
  - swimming and breath;
  - fishing someone out;
  - a body sunk in the lake for the traitors (M4).
- **Art.**
  - The boardwalk's rails read as low solid walls. Thinner rails with gaps (`BridgeLook.Thickness`)
    or posts only would look better.
  - Reeds along the shallows (a plant def) and a dipping post with a proper sprite
    (`tools/` art generators).
  - A reference painting in `art/reference/` for the layout, like Millbrook's.
- **Tuning.** Re-run `--season-sweep --seeds 8 --map Reedmere` after Phase 5 and compare it with
  the numbers above. Reedmere may stay a little harder than Millbrook on purpose.

## Tests

- **Provisions:** ordered by the innkeeper at the cookhouse, packed, carried, unpacked and served
  to a lodger. On Millbrook it can't be ordered at all.
- **Warming shelf:** an empty shelf lowers what lodging is worth. A quest party takes rations from it.
- **Commands:** the new commands are in `CommandTests.HostileCommandsDoNoHarm`.
- **Bot season:** `SeasonScenario` on Reedmere.
- **Hashes:** `--bench` hashes for seeds 1234 and 99 and `--round` are unchanged on Millbrook.
  - Seed 1234: `57F4CB524D2257FB`.
  - Seed 99: `C1BB56A18110F5C7`.
  - `--round` 1234: `A5FCA9767F06D3FA`.
