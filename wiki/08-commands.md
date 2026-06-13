# 8. Commands Reference

[← Coupling JSON Contract](07-coupling-json-contract.md) · [Wiki home](README.md) · [Next: Troubleshooting →](09-troubleshooting.md)

All commands require the chat privilege and a player.

| Command | What it does |
|---------|--------------|
| `/vrrgauge` | Cycle the active gauge used for newly laid track (narrow / metre / standard / broad). |
| `/vrrnode` | Place track via command: run once to set point A, move and run again to lay a segment A→B. (The Track Layer item is the preferred, survival-friendly way — see [Laying Track](02-laying-track.md).) |
| `/vrrsnap` | Toggle endpoint snapping on/off. Off = lay track close together without auto-joining. |
| `/vrrswitch` | Stand within 4 blocks of a junction node and cycle its through-route to the next branch. |
| `/vrrdebug` | Toggle verbose `[vrr]` debug logging to the server/client log. Off by default; turn on only when diagnosing something. |
| `/vrrtrain` | Legacy train spawn (superseded by the placer items). |

> **Coupling is no longer a command.** The old `/vrrcouple` and `/vrruncouple` were removed
> in favour of the **Coupler tool** — see [Coupling & Consists](04-coupling.md). Hold a
> Coupler, right-click a leader then a follower to link them; sneak + right-click to
> detach.

## Typical mixed-consist session

```
# lay a straight test track with the Track Layer first, then:
/vrrgauge            # (optional) pick a gauge before laying track
# spawn a loco with the Baldwin placer on the rail
# spawn a log car (or coal cart / fluid tanker) a few blocks behind it
# hold a Coupler tool:
#   right-click the loco  -> selects it as leader
#   right-click the car   -> couples it behind the loco (costs 1 durability)
# (repeat with more cars to build a chain: select leader, click next car)
# sit in the loco, drive with W/S; the cars trail it
#   sneak + right-click a car with the Coupler -> uncouples it
```

## Debugging

`/vrrdebug` toggles verbose logging. It's **off** by default so the logs stay clean in
normal play; turn it on, reproduce the issue, and the mod prints `[vrr]`-prefixed
diagnostic lines (entity init, tick/advance state, render projection, interaction
results) to the server or client log. Turn it back off when you're done.

## Loading cargo

Cargo is not handled by a command — **right-click a cargo car with an empty hand** to open
its storage GUI, then move items (or a filled liquid container, for a tanker) in and out.
See [Cargo Cars & Storage](05-cargo-and-storage.md).

---

[← Coupling JSON Contract](07-coupling-json-contract.md) · [Wiki home](README.md) · [Next: Troubleshooting →](09-troubleshooting.md)
