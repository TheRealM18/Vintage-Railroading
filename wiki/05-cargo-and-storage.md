# 5. Cargo Cars & Storage

[← Coupling](04-coupling.md) · [Wiki home](README.md) · [Next: Authoring →](06-authoring-rolling-stock.md)

Cargo cars are the **haulage** half of the mod. This page explains the `EntityCargo`
class and the three **storage behaviors** that turn a car into a log car, a coal cart, or
a fluid tanker.

## The `EntityCargo` class

`EntityCargo` is a **seatless** rolling-stock class. Compared to the driveable
`EntityTrain` locomotive it:

- **has no seat** — right-clicking it opens its storage GUI instead of mounting you;
- **has no throttle** — it never sets its own speed;
- **only moves by coupling** — with no leader it parks; with a leader it trails the gap.

It still rides the network with the same 1D state, the same curve projection, the same
client-side smoothing (every render frame), and the same grade pitch as a locomotive. It
implements `IRailVehicle`, so the placer and the coupling commands treat it exactly like a
locomotive.

Because there is no seat, a cargo car needs **none** of the seat machinery a locomotive
needs — no `creaturecarrier` behavior, no `Seat-driver` attachment point, no
`mountAnimations`. That makes its entity JSON noticeably simpler (see
[Authoring](06-authoring-rolling-stock.md)).

## The three storage behaviors

You make a car carry something by adding a **storage behavior** to its entity JSON. All
three share the same shape — a persisted inventory opened by an **empty-hand right-click**
— and differ only in **what they accept** and **how capacity is expressed**.

| Behavior code | Carries | Capacity attribute | Default | Filter |
|---------------|---------|--------------------|---------|--------|
| `woodstorage`  | logs, planks, sticks, firewood, lumber, saplings… | `quantitySlots` (item slots) | `16` | code-token match (+ `isWood` override) |
| `fuelstorage`  | anything burnable | `quantitySlots` (item slots) | `16` | `CombustibleProps.BurnDuration > 0` |
| `fluidstorage` | any pourable liquid (water, oil, etc.) | `capacityLitres` (litres) | `200` | has `WaterTightContainableProps` |

All three:

- persist their contents in the entity's watched-attributes tree, so cargo survives
  save/reload (`vrrwoodinv`, `vrrfuelinv`, `vrrfluidinv` respectively);
- open with an **empty-hand right-click** on the car;
- **drop their contents into the world when the car is picked up**, so nothing is lost.

Each behavior must be registered in code (the mod already does this) **and** listed in the
car's entity JSON. The registered codes are `woodstorage`, `fuelstorage`, and
`fluidstorage`.

### Wood storage (`woodstorage`)

A 16-slot inventory that only accepts wood-related items. Because wood has no single
engine flag (unlike fuel or liquid), the slot matches on the item's **code path**: an item
is wood if its code contains a token like `log`, `plank`, `stick`, `firewood`, `lumber`,
`board`, `sapling`, `timber`, etc., **and** is not excluded by a "not wood" token
(`bowl`, `bucket`, `tool`, `axe`, `door`, `chest`, `barrel`, …). A collectible can force
the result either way with an `isWood` attribute (`true`/`false`), which wins over the
token match — handy for modded items.

JSON (client **and** server behavior lists):

```jsonc
{ "code": "woodstorage", "quantitySlots": 16 }
```

### Fuel storage (`fuelstorage`)

A 16-slot inventory that accepts **anything burnable** — the slot tests for
`CombustibleProps.BurnDuration > 0`, so coal, charcoal, coke, firewood, peat, and modded
fuels all qualify, while non-fuels are rejected. This is what the shipped **Coal Cart**
uses. Beyond storing fuel, a coal cart **acts as a tender**: when coupled behind a
locomotive it supplies the loco's firebox, so the train can only accelerate while a loaded
coal cart is in its consist (see [Driving → Fuel](03-driving.md#fuel)).

```jsonc
{ "code": "fuelstorage", "quantitySlots": 16 }
```

### Fluid storage (`fluidstorage`)

A single-slot **tank** measured in **litres**, accepting **any pourable liquid**. It is
modeled on Vintage Engineering's large-liquid slot: the slot extends the vanilla
liquid-only slot and raises its capacity to `capacityLitres × ItemsPerLitre`, recomputed
from the actual liquid so "200 litres" is correct whether the tank holds water or oil.
You fill it by dropping a **filled liquid container** (a bucket/jug of water, etc.) into
the tank slot in the GUI, the same way barrels take liquids; you draw liquid back out the
same way. The dialog title shows the capacity, e.g. **"Fluid Tank (200L)"**.

```jsonc
{ "code": "fluidstorage", "capacityLitres": 200 }
```

## Opening a cargo car's storage

**Right-click the car with an empty hand.** That is the whole interaction. There is no
seat to compete with on a cargo car, so the storage opens immediately. (On a *locomotive*
that also had a storage behavior, the behavior opens storage on an empty-hand,
non-sneaking click and suppresses the seat for that click; sneak + empty hand would let
you sit. Cargo cars have no seat, so this distinction does not arise for them.)

If you are holding an item, the right-click is **not** treated as "open storage" — that
keeps the wrench-pickup interaction (right-click with a wrench) working.

## Picking up a loaded cargo car

Wrench + right-click picks the car up just like a locomotive. The difference: **the car's
stored cargo is dropped into the world** at the car's position first, so loading is never
silently destroyed. For a fluid tanker, the liquid drops in its container-stack form (see
the note in the behavior — if you would prefer the liquid to spill/puddle instead, that is
a one-method change).

## The shipped cargo cars

| Entity | Class | Storage | Notes |
|--------|-------|---------|-------|
| `logcar`   | `EntityCargo` | `woodstorage` (16 slots)   | Hauls logs/planks/etc. |
| `coalcart` | `EntityCargo` | `fuelstorage` (16 slots)   | Hauls any fuel. |
| `fluidcar` | `EntityCargo` | `fluidstorage` (200 L)     | Tanker for any liquid. |

Each has a matching placer item (e.g. the Log Car item) that spawns it onto a rail. Spawn
one behind a locomotive and link it with a Coupler tool.

> Textures on the shipped cargo cars currently reuse placeholder maps (the log car/coal
> cart use stand-in textures such as basalt for dark surfaces, and the fluid car reuses
> the log-car shape) until dedicated models/textures are added. This does not affect
> behavior.

---

[← Coupling](04-coupling.md) · [Wiki home](README.md) · [Next: Authoring →](06-authoring-rolling-stock.md)
