# Vintage Railroading — Wiki

Free-form, multi-gauge railroads for Vintage Story 1.22.x. This wiki covers both
**playing** with the mod (laying track, driving, coupling, hauling cargo) and
**authoring** new rolling stock — locomotives *and* seatless cargo cars — whose JSON is
set up correctly to work with the coupling and storage systems.

> Status: the mod is feature-complete enough to build and run a working railroad with
> mixed consists of locomotives and cargo cars. A few systems (interaction-based
> coupling, coupling persistence) are still command-driven or in progress — those caveats
> are called out where they apply.

## Two kinds of rolling stock

Vintage Railroading has **two vehicle classes**, and which one you use depends on whether
the vehicle is *driven*:

- **`EntityTrain`** — a **driveable locomotive**. It has a seat, reads the throttle
  (W/S), and rides the track under its own power. The Baldwin 2-8-0 is one.
- **`EntityCargo`** — a **seatless cargo car** (log car, coal cart, fluid tanker). It has
  **no seat and no throttle**: it only moves by being **coupled behind a leader**. Its job
  is to carry cargo, which it does via a **storage behavior** with a GUI.

Both implement a shared interface, **`IRailVehicle`**, so everything that operates on
rolling stock — the placer, `/vrrcouple`, `/vrruncouple` — accepts **either** class.
A locomotive and any number of cargo cars couple together into one consist.

## Pages

1. [Getting Started](01-getting-started.md) — install, the items, and your first train.
2. [Laying Track](02-laying-track.md) — the Track Layer, snapping, grades, junctions.
3. [Spawning & Driving a Train](03-driving.md) — placing, mounting, controls, pickup.
4. [Coupling & Consists](04-coupling.md) — how to couple cars and how following works.
5. [Cargo Cars & Storage](05-cargo-and-storage.md) — the `EntityCargo` class and the
   **wood / fuel / fluid** storage behaviors, with full JSON examples.
6. [Authoring Rolling Stock](06-authoring-rolling-stock.md) — **the JSON guide** for both
   classes: entity files, shapes, seats (locos) vs. no seats (cargo), storage hookup.
7. [The Coupling JSON Contract](07-coupling-json-contract.md) — a focused reference for
   the attributes and conventions a vehicle must satisfy to couple correctly.
8. [Commands Reference](08-commands.md) — every `/vrr*` command.
9. [Troubleshooting](09-troubleshooting.md) — common breakages and how to spot them.

## Core idea (read this first)

Every vehicle in Vintage Railroading is described by a tiny **1D state** rather than free
3D physics:

- `SegmentId` — which track segment the vehicle is on.
- `Distance` — how many metres along that segment it sits.
- `Speed` — metres/second along the track.

Each server tick the vehicle advances `Distance` by `Speed`, then projects that 1D
position onto the segment's curve to get a real `(x, y, z)`, heading, and pitch. When it
runs off the end of a segment it continues onto the connected one at the shared node.

A **locomotive** (`EntityTrain`) sets its own `Speed` from the throttle. A **cargo car**
(`EntityCargo`) never sets its own speed — with no leader it sits still; with a leader it
is positioned purely by the coupling offset (below).

**Coupling is built directly on this model.** A coupled car does not do its own physics —
it simply asks: *"where is the point N metres behind my leader, along the track?"* and
places itself there every tick. That is why coupling follows curves, grades, and
junctions automatically: it is the same path-walking the leader already uses. Because
this offset is the *only* thing that moves a cargo car, a cargo car with no leader simply
parks.

Understanding this 1D model makes the rest of the wiki — especially the authoring
pages — much easier to follow.
