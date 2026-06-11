# 1. Getting Started

[← Wiki home](README.md) · [Next: Laying Track →](02-laying-track.md)

## Installing

Vintage Railroading is a code mod. Build it (`dotnet build -c Release`) and place the
resulting `VintageRailroading.dll` plus its `assets/` into a mod folder, then launch the
game. Because the mod registers its own asset domain (`vintagerailroading`) and custom
save attributes, **always test on a fresh world** — old saves from before a domain or
attribute change will not load placed track or trains.

## The items

Everything you need is craftable (see [Authoring](05-authoring-rolling-stock.md) for the
recipe tree) and also available in the creative inventory while you experiment:

| Item | What it does |
|------|--------------|
| **Track Layer** | The tool you use to lay track by clicking point-to-point. |
| **Baldwin 2-8-0 Locomotive** (the "train placer") | Right-click rail to spawn a drivable locomotive. |
| **Locomotive Boiler / Firebox / Cab Frame / Steam Piston / Iron Wheel Set** | Intermediate crafting parts that combine into the locomotive. |

## Your first train in four steps

1. **Lay some track.** Hold the Track Layer, right-click a block to set the start, then
   right-click farther away to lay a segment. Keep clicking to extend it. See
   [Laying Track](02-laying-track.md).
2. **Spawn a locomotive.** Hold the Baldwin placer item and right-click directly on the
   rail you just laid. A locomotive appears on the track.
3. **Drive it.** Right-click the locomotive to sit in the cab, then hold **W** to go
   forward and **S** to reverse. Release to coast to a stop. See [Driving](03-driving.md).
4. **(Optional) Add cars.** Spawn a second vehicle on the same track, stand between the
   two, and run `/vrrcouple`. See [Coupling](04-coupling.md).

## A note on gauges

The mod supports multiple track gauges (narrow, metre, standard, broad). The active
gauge for new track is set with `/vrrgauge`. A vehicle's gauge comes from the segment it
is placed on. Mixing gauges in one physical line is allowed by the geometry but is not
something the coupling system tries to police — keep a consist on one gauge.

---

[← Wiki home](README.md) · [Next: Laying Track →](02-laying-track.md)
