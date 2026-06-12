# 6. The Coupling JSON Contract

[← Authoring](05-authoring-rolling-stock.md) · [Wiki home](README.md) · [Next: Commands →](07-commands.md)

A focused reference for **exactly what a vehicle must provide** so it couples and follows
correctly. If your cart's entity JSON satisfies everything here, it will couple.

## TL;DR checklist

A vehicle couples correctly when **all** of these are true:

- [ ] `"class": "EntityTrain"` in the entity JSON.
- [ ] A unique `"code"` (not shared with any other entity def).
- [ ] The `attributes` block with `mountAnimations` is present.
- [ ] The shape has an attachment point named exactly **`Seat-driver`**.
- [ ] A `creaturecarrier` behavior with a seat whose `apName` is `Seat-driver`, on both
      `client` and `server` behavior lists.
- [ ] `passivephysicsmultibox` with `gravityFactor: 0.0`.
- [ ] The vehicle is **placed on the track network** (spawned onto a segment), not free
      in the world.

You do **not** add any "coupling" block — coupling is behavior of the `EntityTrain` class,
not a JSON-declared feature.

## The state the system reads

Coupling is driven entirely by two synced attributes that `EntityTrain` manages at
runtime. You normally never set these in JSON — they are written by the coupling commands
— but understanding them explains the contract:

| Attribute key | Type | Default | Meaning |
|---------------|------|---------|---------|
| `vrrLeader` | long | `0` | Entity id of the vehicle this one follows. `0` = standalone / lead (reads throttle). |
| `vrrGap` | double | `6.0` | Metres kept behind the leader, measured **along the track**. |

And the underlying position state every vehicle has (also synced), which coupling reads
from the leader and writes on the follower:

| Attribute key | Type | Meaning |
|---------------|------|---------|
| `vrrSegId` | long | Which network segment the vehicle is on. |
| `vrrDist` | double | Metres along that segment. |
| `vrrSpeed` | double | Metres/second along the track. |

### How the follower uses them, each server tick
1. If `vrrLeader != 0`, look up the leader entity by that id.
2. If the leader is gone, set `vrrLeader = 0` and stop (auto-uncouple).
3. Otherwise walk the network **backwards** from the leader's `(vrrSegId, vrrDist)` by
   `vrrGap` metres, crossing nodes with the normal traversal rules, and set the
   follower's own `vrrSegId` / `vrrDist` to the result.
4. Copy the leader's `vrrSpeed` (for model pitch/animation only).
5. Skip throttle and skip the normal speed-advance — the follower is positioned purely by
   the offset.

This is why a follower needs no physics and no driver: its entire motion is "be N metres
behind the leader on the track."

## Why each JSON requirement exists

- **`class: EntityTrain`** — supplies steps 1–5 above plus the `vrr*` attribute
  accessors. A different class has none of this.
- **`attributes` + `mountAnimations`** — `EntityTrain` extends `EntityBoat`; the boat init
  reads these and throws without them, so the entity would never finish loading (and thus
  never couple).
- **`Seat-driver` AP + `creaturecarrier`** — seat creation runs during entity init for
  this class. A missing or mismatched seat AP NREs at spawn, so the vehicle never exists
  to be coupled. (Even an undriven cart needs the valid seat.)
- **`gravityFactor: 0.0`** — the follower's `(x,y,z)` is overwritten from the track every
  tick; gravity would briefly fight that and cause jitter.
- **Placed on the network** — `vrrSegId` must point at a real segment. A vehicle dropped
  in open world with no segment has nothing to offset along.

## Gauge

A consist should stay on **one gauge**. The gap walk follows the connected segments
regardless of gauge, but mixing gauges in a single physical line is not something the
coupling system validates — keep leader and followers on track of the same gauge.

## What you can tune

- **`vrrGap`** is the only coupling value worth tuning, and it is set at couple time from
  the vehicles' spacing (or defaults to 6 m). If you want a fixed coupler length per car
  type later, that would be a small `EntityTrain` change to read a default gap from the
  entity's `attributes` — not currently wired, but the attribute slot (`vrrGap`) is ready.

---

[← Authoring](05-authoring-rolling-stock.md) · [Wiki home](README.md) · [Next: Commands →](07-commands.md)
