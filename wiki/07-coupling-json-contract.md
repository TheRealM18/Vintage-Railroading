# 7. The Coupling JSON Contract

[← Authoring](06-authoring-rolling-stock.md) · [Wiki home](README.md) · [Next: Commands →](08-commands.md)

A focused reference for **exactly what a vehicle must provide** so it couples and follows
correctly. If your vehicle's entity JSON satisfies everything here, it will couple.

## TL;DR checklists

Coupling is a behavior of the **`IRailVehicle`** interface, which both `EntityTrain` and
`EntityCargo` implement. You never add a "coupling" block — you just use a rail-vehicle
class and place it on the network.

**A cargo car (`EntityCargo`) couples when all of these are true:**

- [ ] `"class": "EntityCargo"` in the entity JSON.
- [ ] A unique `"code"`.
- [ ] `passivephysicsmultibox` with `gravityFactor: 0.0`.
- [ ] It is **placed on the track network** (spawned onto a segment), not free in the world.

That's it — **no seat, no `creaturecarrier`, no `Seat-driver` AP, no `mountAnimations`.**

**A locomotive (`EntityTrain`) couples when all of these are true:**

- [ ] `"class": "EntityTrain"` in the entity JSON.
- [ ] A unique `"code"`.
- [ ] The `attributes` block **with `mountAnimations`** is present.
- [ ] The shape has an attachment point named exactly **`Seat-driver`**.
- [ ] A `creaturecarrier` behavior with a seat whose `apName` is `Seat-driver`, on both
      `client` and `server` behavior lists.
- [ ] `passivephysicsmultibox` with `gravityFactor: 0.0`.
- [ ] It is **placed on the track network**.

The extra locomotive items are all **seat** requirements — they exist because
`EntityTrain` extends `EntityBoat` and creates a driver seat at spawn. `EntityCargo` has
no seat, so it drops every one of them.

## The state the system reads

Coupling is driven entirely by two synced attributes that both classes manage at runtime.
You normally never set these in JSON — they are written by the coupling commands — but
understanding them explains the contract:

| Attribute key | Type | Default | Meaning |
|---------------|------|---------|---------|
| `vrrLeader` | long | `0` | Entity id of the vehicle this one follows. `0` = standalone / lead. |
| `vrrGap` | double | `6.0` | Metres kept behind the leader, measured **along the track**. |

And the underlying position state every vehicle has (also synced), which coupling reads
from the leader and writes on the follower:

| Attribute key | Type | Meaning |
|---------------|------|---------|
| `vrrSegId` | long | Which network segment the vehicle is on. |
| `vrrDist` | double | Metres along that segment. |
| `vrrSpeed` | double | Metres/second along the track. |

All five are exposed through the `IRailVehicle` interface, which is why a follower can
read them off **either** an `EntityTrain` or an `EntityCargo` leader without caring which.

### How the follower uses them, each server tick
1. If `vrrLeader != 0`, look up the leader entity by that id (any `IRailVehicle`).
2. If the leader is gone, set `vrrLeader = 0` and stop (auto-uncouple).
3. Otherwise walk the network **backwards** from the leader's `(vrrSegId, vrrDist)` by
   `vrrGap` metres, crossing nodes with the normal traversal rules, and set the
   follower's own `vrrSegId` / `vrrDist` to the result.
4. Copy the leader's `vrrSpeed` (for model pitch/animation only).
5. Skip throttle and skip the normal speed-advance — the follower is positioned purely by
   the offset. (A cargo car has no throttle to skip; a coupled locomotive ignores its own.)

This is why a follower needs no physics and no driver: its entire motion is "be N metres
behind the leader on the track."

## Why each JSON requirement exists

- **A rail-vehicle `class`** (`EntityTrain` or `EntityCargo`) — supplies steps 1–5 above
  plus the `vrr*` attribute accessors via `IRailVehicle`. A non-rail class has none of it.
- **`gravityFactor: 0.0`** — the vehicle's `(x,y,z)` is overwritten from the track every
  tick; gravity would briefly fight that and cause jitter.
- **Placed on the network** — `vrrSegId` must point at a real segment. A vehicle dropped
  in open world with no segment has nothing to offset along.
- **(Locomotive only) `attributes` + `mountAnimations`** — `EntityTrain` extends
  `EntityBoat`; boat init reads these and throws without them, so the entity never finishes
  loading (and thus never couples).
- **(Locomotive only) `Seat-driver` AP + `creaturecarrier`** — seat creation runs during
  init for `EntityTrain`. A missing/mismatched seat AP NREs at spawn. `EntityCargo` skips
  this entirely.

## Gauge

A consist should stay on **one gauge**. The gap walk follows the connected segments
regardless of gauge, but mixing gauges in a single physical line is not something the
coupling system validates — keep leader and followers on track of the same gauge.

## What you can tune

- **`vrrGap`** is the only coupling value worth tuning, and it is set at couple time from
  the vehicles' spacing (or defaults to 6 m). If you want a fixed coupler length per car
  type later, that would be a small change to read a default gap from the entity's
  `attributes` — the attribute slot (`vrrGap`) is already there.

---

[← Authoring](06-authoring-rolling-stock.md) · [Wiki home](README.md) · [Next: Commands →](08-commands.md)
