# 3. Spawning & Driving a Train

[← Laying Track](02-laying-track.md) · [Wiki home](README.md) · [Next: Coupling →](04-coupling.md)

This page is about **locomotives** (`EntityTrain`) — the vehicles you sit in and drive.
For seatless cargo cars, see [Cargo Cars & Storage](05-cargo-and-storage.md).

## Spawning

Hold the **Baldwin 2-8-0 Locomotive** placer item and **right-click directly on a rail**.
The locomotive spawns at that point on the track, rotated and centred on the rails.

You must aim reasonably close to a rail (within ~3 blocks of an actual rail point) or the
placer will tell you the nearest rail is too far away.

The same placer item type also spawns cargo cars — each cargo car has its **own** placer
item that points at its entity code. The placer accepts any rail vehicle, locomotive or
cargo, so the spawning step is identical; only what you get differs.

## Driving

1. **Right-click** the locomotive to sit in the cab. The camera locks to the driver
   seat.
2. Hold **W** to accelerate forward, **S** to reverse.
3. **Release** the key and the train coasts smoothly to a stop (the throttle ramps speed
   toward zero).

Driving is **seated-only** and only locomotives have a seat. (An older `/vrrgo` command
was removed; if a spawn message still mentions it, ignore that line.)

The train rides the network: when it reaches the end of a segment it continues onto the
connected segment, carrying its speed. At a true dead-end it stops. At a junction it
follows the switch setting or the smoothest branch (see
[Laying Track → Junctions](02-laying-track.md#junctions--switches)).

On grades the locomotive noses up and down to match the slope.

## Picking a train back up

Hold any **wrench** (Vintage Engineering's wrench, or any modded item whose code contains
"wrench") and **right-click** the locomotive. It is removed from the world and a placer
item returns to your inventory.

- If you are **seated** when you wrench it, you are safely ejected first (you will not get
  stuck in the seat).
- If your inventory is **full**, the placer drops as an item entity at the train so it is
  never lost.
- Left-click (attack) does **not** pick it up — only right-click with a wrench.

Cargo cars are picked up the same way (wrench + right-click), and **their stored cargo is
dropped into the world** so it is not lost — see
[Cargo Cars & Storage](05-cargo-and-storage.md#picking-up-a-loaded-cargo-car).

> Note: picking up a vehicle that is part of a consist currently drops it out of the
> consist; cars behind it will auto-uncouple rather than re-link. See
> [Coupling](04-coupling.md) for the current limitations.

---

[← Laying Track](02-laying-track.md) · [Wiki home](README.md) · [Next: Coupling →](04-coupling.md)
