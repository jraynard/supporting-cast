# The candlemaker: open questions

Built in one pass by a fresh coding agent from the skills alone (docs/MODDING.md, the skills' test). A first
playable pass: each F dips the wick (a layer); a layer takes 1.5 s to set, shown only as colour (warm amber
fading to pale); dipping before it's set leaves a lump; eight layers make a candle, any lump a lumpy one.
Try it: `--new-game --trade Candlemaker --mods candlemaker --drill candlemaker`.

To settle by playing it:
1. Is 1.5 s a layer right? Is 8 layers too many or too few for one candle?
2. Is the colour fade enough of a cue, or should there be a sound as a layer sets?
3. Should one early dip ruin the candle, or should a candle carry how lumpy it is (its `Crafted` quality)?
   Should waiting too long cost anything (wax too cold to take a layer)?
4. Should it need a place (a wax vat, a chandlery) and buy tallow or wax, rather than working anywhere?
5. Who buys candles, and what does a lumpy one do differently: sell for less, burn shorter?
6. Should a skilled hand get a little leeway on dipping early (`Hands.Tolerance`)?
