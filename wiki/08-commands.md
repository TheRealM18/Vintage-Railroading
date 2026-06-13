# 8. Commands Reference

[← Coupling JSON Contract](07-coupling-json-contract.md) · [Wiki home](README.md) · [Next: Troubleshooting →](09-troubleshooting.md)

All commands require the chat privilege and a player.

| Command | What it does |
|---------|--------------|
| `/vrrgauge` | Cycle the active gauge used for newly laid track (narrow / metre / standard / broad). |
| `/vrrnode` | Place track via command: run once to set point A, move and run again to lay a segment A→B. (The Track Layer item is the preferred, survival-friendly way — see [Laying Track](02-laying-track.md).) |
| `/vrrsnap` | Toggle endpoint snapping on/off. Off = lay track close together without auto-joining. |
| `/vrrswitch` | Stand within 4 blocks of a junction node and cycle its through-route to the next branch. |
| `/vrrcouple` | Couple the two nearest **rail vehicles** (locomotive or cargo car) within 12 blocks; the nearer becomes the follower of the farther. |
| `/vrruncouple` | Detach the nearest coupled rail vehicle from its leader. |
| `/vrrtrain` | Legacy train spawn (superseded by the placer items). |

> `/vrrcouple` and `/vrruncouple` operate on the `IRailVehicle` interface, so they see and
> link **any** mix of locomotives and cargo cars — there is no separate "couple cargo"
> command.

## Typical mixed-consist session

```
# lay a straight test track with the Track Layer first, then:
/vrrgauge            # (optional) pick a gauge before laying track
# spawn a loco with the Baldwin placer on the rail
# spawn a log car (or coal cart / fluid tanker) a few blocks behind it
# stand between the loco and the car:
/vrrcouple           # links them; nearer one follows the farther
# (repeat: place another car behind, stand between car+newcar, /vrrcouple again for a chain)
# sit in the loco, drive with W/S; the cars trail it
/vrruncouple         # when you want to separate the nearest pair
```

## Loading cargo

Cargo is not handled by a command — **right-click a cargo car with an empty hand** to open
its storage GUI, then move items (or a filled liquid container, for a tanker) in and out.
See [Cargo Cars & Storage](05-cargo-and-storage.md).

---

[← Coupling JSON Contract](07-coupling-json-contract.md) · [Wiki home](README.md) · [Next: Troubleshooting →](09-troubleshooting.md)
