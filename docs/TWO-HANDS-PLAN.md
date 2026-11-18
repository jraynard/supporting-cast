# Two pairs of hands: the rest

_Saved 2026-11-17 for later. The framework and the striker are built (HISTORY.md, "The theme: behind every hero, and two pairs of hands"); this is what's left._

## Context

The game is about working people, ordinary people, facing danger together (DESIGN.md, "The theme under them"). Two pairs of hands plays to it: some work is too much for one person. Three rules make it meaningful rather than a shortcut:
- **Help opens up work one pair of hands can't do.** It never makes ordinary work easier. The usual work stays solo and unchanged.
- **The helper plays a game of their own,** with its own skill and its own way to fail. Bad help is worse than none.
- **The lead calls and the helper answers,** so the two have to keep time and talk.

Anyone can lend a hand: another trade, the labourer, or an AI villager who heard the call and thinks well enough of you. The helper is named on the work (and so in "Behind every hero") and learns a little of the trade.

## What's built to follow

- **`Work/Pairs.cs`:**
  - `HelperSession`: the helper's half.
    - `Follows(lead)`: it carries on into the lead's next session.
    - `LeadAtIt`: it puts the tool down when the work's done.
    - `InPlace`: the helper is where the work is.
  - `Pairs.Join`, `Relink`, `Exchange`, `AtWork`, `Willing`.
  - `AskForHandCommand`: "Help!" from the quick-say wheel while at work for two.
  - `HelpJob` and `Helping`: an AI helper. Its answer comes a moment after each `Call` (`Pairs.AiBlow`).
- **`WorkSession`:**
  - `Partner`.
  - `Send(BeatKind, value)` from play.
  - `Received(Beat)`: a beat handed over by the sim between steps.
  - `Revision`: the `WorkPredictor` re-clones and replays when it changes.
  - `TakesHelper`, `HelperFor`, `AskForHand`.
  - `BeatKind` is saved by number: append only.
- **The work key** offers `AvatarActionKind.LendAHand` at a station whose session wants a hand (`Pairs.WantsHandAt`). The sim recomputes it, so no new command is needed.
- **Credit:**
  - `Crafted.Helper`, `WorkPiece.Helper`, `GearRecord.Helper` and `DeedKind.Struck`.
  - A new kind of help may want its own `DeedKind`, appended.
- **The pattern to copy:** `SledgeSession` and `HammerSession.Received`/`Heavy`/`TapShare`, and `PairTests` (driven through two predictors, `SmithyKit.Drive(alongside:)`).

## 1. The keeper at the Calling back

The empath's rite to bring the dead back becomes a rite for two.

- **Refused alone:** `Revival.CallProblem` (`Afterlife/Revival.cs`) refuses to begin it alone: "Someone must hold them while you call."
  - This leaves a gap. With the keeper's session joined only once the empath's has started, the rite needs a way to wait for one. Either:
    - (a) the empath begins and the weave stays locked till a keeper joins; or
    - (b) a keeper kneels first, and the empath's start checks for them.
  - Prefer (a): it matches the anvil, where the lead's session exists first.
- **The empath's session:** `CastSession` with `callingFor` (`Healing/CastSession.cs`) gets `TakesHelper` while it calls back, and `HelperFor` gives a `HoldSession` at the body.
- **The keeper's game (`HoldSession`):**
  - The grave pulls in waves: a `Call` beat with each rune the empath keys.
  - The keeper braces (holds F as each pull comes and eases between), timed to the pull. Bracing too early tires the grip; too late, the hold slips.
  - A slipped hold sends a `Slip` beat (append to `BeatKind`), and the empath's weave loses that rune's worth. The weave's quality flows into `Revival.Call`: blood back, and the grave's chill on the empath. At worst the calling fails and its mana is spent.
- **Where the keeper stands:** kneeling at the body (`Revival.BodyBeside`); `InPlace` checks it.
- **AI:**
  - The AI empath's `CallBackJob` (`Afterlife/CallBackJob.cs`) waits for a keeper. It calls for one itself, with the AskForHand reasoning applied to AI leads, or gives up after a while.
  - An AI keeper (`Helping`) answers each pull with a steady hold, like `AiBlow`.
- **Credit:** the reveal's row for the returned shows "called back by Dara, held by Arin". `RevealRow` would need a field (and a summary format bump).
- **Ghosts:** check nothing in `Afterlife/` assumes the caster is alone at the body: errands, `Hauntings`, and the ghost answering (`AnswerProblem`).
- **Tests:**
  - The Calling is refused alone and works with a keeper.
  - A slipped hold lowers the weave's quality.
  - An AI keeper answers the empath's call.
  - Existing revival tests (`AfterlifeTests` and friends) bring a keeper.
- **The practice yard:** the empath's lesson that teaches the Calling gets a keeper (an AI one).

## 2. The pitman under the carpenter's saw

- **What it opens up:** a great beam, for the bridge timbers or a bigger plank yield. It becomes a `twoHanded` recipe at the woodshop, or a "great log" variant of `SawSession`'s pit-saw phase.
- **The lead and the helper:** `SawSession` is already written from the top sawyer's view (`Carpentry/SawSession.cs`, :18-22). The pitman below pulls on the downstroke: the top sawyer's stroke is the `Call`.
- **How the pitman fails:** out of rhythm binds the saw, and the stroke is lost (and the kerf may wander).
- **Credit:** `Crafted.Helper` on the planks or beam is awkward, since planks stack. It may need to be credited on the bridge brace or in a message instead. Decide when building.
- **Validity:** `StillValid` requires `bench.By == Worker` (:158): the lead's check stays; the pitman's is its own.

## 3. The lifter at a party's wagon

- **What it opens up:** axle and broken-wheel repairs, a worse kind of wagon damage than a wheel out of true (`Tinkering/Wagons.cs`).
- **The lead and the helper:** the tinker trues at `TrueSession`; the lifter holds the wagon level against its sway.
- **How the lifter fails:** a drop jolts the spokes already set.
- **Credit:** `Wagon.MendedBy` gains a helper; the quest's tale credits it (the wagon is `ex.Hauls`).

## 4. Around all of them

- **A "Lend a hand" lesson** in the practice yard (`trades.json` top-level `basics`, `Scenarios/Lessons`): an AI smith at a great piece, and the player takes up the sledge. Steps are derived from `Perception`, never a cursor.
- **Bots** (`Bots/BotPlayer`): they answer a call for a hand and take up the sledge. Bot smiths might also make great pieces and ask for a hand.
- **AI smiths** never make `twoHanded` recipes (`Crafting.NextForVillager`). They could, with an AI striker free nearby.
- **Art and sound:**
  - The greatsword borrows the longsword's sprite (`items/steel_sword`).
  - The sledge has no sounds of its own (`WorkSounds`, docs/AUDIO.md).
  - The striker has no swing pose.
- **Playtest the striker over the internet:**
  - Is the call's window (`SledgeSession.CallSteps` × tolerance) wide enough at a real round trip? The helper sees the call a round trip late, after the predictor's resync.
  - Does the smith wait for the striker's heave?
  - Tune `TapShare` (0.1), `StrikeShaping`, `OffbeatCrack`.
- **Mood:** working beside someone is a good memory about them (`mood.json` opinion), so neighbourliness pays.

## Verification

- `dotnet test -c Release`, with `PairTests`, `QuestTaleTests` and the new tests for each helper.
- `SeasonScenario` and `VillageDayScenario` still pass.
- Each new helper session goes in the `WorkSession.Read` switch, with a save format bump.
- Manual: `--host` and `--join`, one player as the lead and one as the helper, for each pair; `--die-at 5` with a keeper.
