# 1. Getting Started

[← Wiki home](README.md) · [Next: Laying Track →](02-laying-track.md)

## Installing

Vintage Railroading is a code mod. Build it (`dotnet build -c Release`) and place the
resulting `VintageRailroading.dll` plus its `assets/` into a mod folder, then launch the
game. Because the mod registers its own asset domain (`vintagerailroading`) and custom
save attributes, **always test on a fresh world** — old saves from before a domain or
attribute change will not load placed track or trains.

## The items

Everything you need is craftable (see [Authoring](06-authoring-rolling-stock.md) for the
recipe tree) and also available in the creative inventory while you experiment. The mod
adds four creative tabs so things are easy to find:

- **VRR All** — every item and block the mod adds, in one place.
- **VRR Trains** — driveable locomotives (the Baldwin 2-8-0).
- **VRR Cargo** — cargo cars: Coal Cart, Logging Flatcar, Tank Car, Freight Car, Ore Car,
  Dirt Car, Stone Car, Organics Car, Freezer Car, Livestock Car, and Passenger Car.
- **VRR Other** — the Track Layer and Coupler tools, the Rail Node block, and all
  crafting intermediates (boiler, firebox, cab frame, piston, wheel set).

Items also stay in the default survival creative search.

| Item | What it does |
|------|--------------|
| **Track Layer** | The tool you use to lay track by clicking point-to-point. |
| **Coupler** (copper / tin-bronze / iron / steel) | Durability tool to link and unlink rail vehicles. Higher tiers last longer. |
| **Baldwin 2-8-0 Locomotive** (the "train placer") | Right-click rail to spawn a drivable locomotive (`EntityTrain`). |
| **Cargo car placers** (Coal, Log, Tank, Freight, Ore, Dirt, Stone, Organics, Freezer) | Right-click rail to spawn a seatless cargo car (`EntityCargo`) you couple behind a loco. |
| **Livestock & Passenger car placers** | Right-click rail to spawn a seat-based car (`EntityTrain`, top speed 0 — moves only when coupled). Livestock carries live animals; Passenger seats riders. |
| **Locomotive Boiler / Firebox / Cab Frame / Steam Piston / Iron Wheel Set** | Intermediate crafting parts that combine into the locomotive. |

## Your first train in five steps

1. **Lay some track.** Hold the Track Layer, right-click a block to set the start, then
   right-click farther away to lay a segment. Keep clicking to extend it. See
   [Laying Track](02-laying-track.md).
2. **Spawn a locomotive.** Hold the Baldwin placer item and right-click directly on the
   rail you just laid. A locomotive appears on the track.
3. **Drive it.** Right-click the locomotive to sit in the cab, then hold **W** to go
   forward and **S** to reverse. Release to coast to a stop. See [Driving](03-driving.md).
4. **Add a cargo car.** Spawn a log car, coal cart, or fluid tanker on the **same track**,
   a few blocks behind the loco. These are seatless — right-clicking one opens its
   **cargo storage**, not a seat. See [Cargo Cars & Storage](05-cargo-and-storage.md).
5. **Couple up.** Hold a Coupler tool, right-click the loco (selects it as leader), then right-click the car to link it. Now drive the
   loco and the car trails it. See [Coupling](04-coupling.md).

## Locomotives vs. cargo cars (the one thing to internalise)

- A **locomotive** (`EntityTrain`) is the only thing you **sit in and drive**.
- A **cargo car** (`EntityCargo`) has **no seat**. Right-clicking it **opens its storage
  GUI**. It does not move on its own — it must be coupled behind a leader.

If you right-click a vehicle expecting to sit and instead a storage window opens, that
vehicle is a cargo car, working as intended.

## A note on gauges

The mod supports multiple track gauges (narrow, metre, standard, broad). The active
gauge for new track is set with `/vrrgauge`. A vehicle's gauge comes from the segment it
is placed on. Mixing gauges in one physical line is allowed by the geometry but is not
something the coupling system tries to police — keep a consist on one gauge.

---

[← Wiki home](README.md) · [Next: Laying Track →](02-laying-track.md)
