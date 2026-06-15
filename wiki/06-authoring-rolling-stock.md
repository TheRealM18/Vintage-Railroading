# 6. Authoring a New Cart or Locomotive

[← Cargo Cars & Storage](05-cargo-and-storage.md) · [Wiki home](README.md) · [Next: Coupling JSON Contract →](07-coupling-json-contract.md)

This is the page you want if you are **building new rolling stock**. It covers **both**
vehicle classes and, critically, **what makes a vehicle work with the coupling system**.

## First decision: locomotive or cargo car?

> **Pick the class by whether the vehicle is driven.**
>
> - **Driven, has a cab, reads W/S** → **`EntityTrain`**. Needs a seat (`creaturecarrier`
>   + a `Seat-driver` attachment point) and the `mountAnimations` attributes block.
> - **Hauled, carries cargo, no driver** → **`EntityCargo`**. **No seat, no
>   `creaturecarrier`, no `Seat-driver` AP, no `mountAnimations`.** Add a storage
>   behavior instead.

Both classes ride the network identically and both implement `IRailVehicle`, so both
couple. You make a new vehicle of either kind with **data, not code** — a new entity JSON
(its own `code` and shape) that sets the right `class`, plus a placer item that names it.
No new C# class per vehicle.

The shipped examples to copy:

- Locomotive → `entities/train.json`
- Log car → `entities/logcar.json` (`genericstorage`, filtered to `wood`)
- Coal cart → `entities/coalcart.json` (`fuelstorage`)
- Fluid tanker → `entities/fluidcar.json` (`fluidstorage`)
- Bulk gondola → `entities/orecar.json` (`genericstorage`, filtered + capacity)
- Freezer → `entities/freezercar.json` (`genericstorage` + `freezer` power)
- Livestock → `entities/livestockcar.json` (`EntityTrain` with animal seats, no inventory)
- Passenger → `entities/passengercar.json` (`EntityTrain`, seats, `maxSpeed: 0`)

## Multi-type system (how it works)

Two data hooks make many types share two classes:

- **The placer is data-driven.** `ItemTrainPlacer` reads `attributes.entityCode` from the
  *item's* JSON and spawns `vintagerailroading:<entityCode>`. It accepts any
  `IRailVehicle`, so one placer class spawns locomotives **and** cargo cars — you just
  point each placer itemtype at a different entity code.
- **Per-type tuning via entity attributes.** `EntityTrain` reads `attributes.maxSpeed`
  (m/s) from the *entity's* JSON. Cargo cars take their speed from the leader, so they do
  not need it.

### To add a cargo type "flatcar" you need exactly:
1. `entities/flatcar.json` — copy `logcar.json`, set `"code": "flatcar"`, keep
   `"class": "EntityCargo"`, point `shape.base` at your model, choose a storage behavior.
2. `shapes/entity/flatcar.json` — your model. **No seat AP required** for cargo.
3. `itemtypes/flatcarplacer.json` — `"class": "ItemTrainPlacer"` with
   `"attributes": { "entityCode": "flatcar" }`.
4. `lang/en.json` — `"item-flatcarplacer"` and `"entity-flatcar"` display names.

That's the whole recipe. No registration, no new class.

## Standard car dimensions (fit the default track)

> **Use these numbers for any new car.** They are the **tank car's** dimensions, which are
> the reference fit for the default-gauge track — every shipped car (coal, log, freighter,
> tank, ore, dirt, stone, organics, freezer, livestock, passenger) now uses them, so a
> consist of mixed car types lines up cleanly on the rail.

| Property | Value | Notes |
|----------|-------|-------|
| `hitboxSize` | `{ "x": 3.5, "y": 3.0 }` | also set `deadHitboxSize` to the same |
| `collisionBoxes[0]` | `{ "x1": -1.5, "y1": 0.0, "z1": -1.75, "x2": 1.5, "y2": 2.5, "z2": 1.75 }` | the box that actually decides track fit |
| `eyeHeight` | `2.0` | (cargo) |
| `gravityFactor` | `0.0` | the network drives position, not physics |

**Model envelope (shape JSON), matching that collision box.** Build the body inside roughly:

- **Length (along the track):** the chassis runs long on the model's **X** axis,
  about `-42 … +42` for the underframe and `-39 … +39` for the body.
- **Width (across the track):** about `-15 … +15` on **Z**.
- **Floor height:** body sits on top of the underframe at **Y = 6**; keep wheels/trucks
  below that (the tank's trucks sit around Y `-12 … 0`).

The simplest way to get a correctly-fitting model is to **copy the tank car's chassis**
(its `coupler_*`, `wheel_*`, `truck_front`, and `underframe` elements from
`shapes/entity/tankcar.json`) and build your car's body on top. Every shipped car was made
this way.

> The body's long axis is **X**, not Z — the entity applies the track-facing rotation, so
> matching the tank's element coordinates is what guarantees the fit. Don't eyeball new
> wheel positions; reuse the tank's.

## Cargo car entity JSON (the common case)

This is the **full** shape of a cargo car, annotated. It is deliberately simpler than a
locomotive — no seat, no mount animations.

```jsonc
{
  "code": "flatcar",              // <- MUST be unique
  "class": "EntityCargo",         // <- seatless cargo class (rides rails, couples, no driver)
  "tags": [ "inanimate", "vehicle" ],

  "attributes": {
    "shouldSwivelFromMotion": false,
    "speedMultiplier": 1,
    "swimmingOffsetY": 0
    // NOTE: no mountAnimations — EntityCargo does not extend EntityBoat and has no seat.
  },

  "hitboxSize":     { "x": 3.5, "y": 3.0 },   // <- standard track-fit size (see table above)
  "deadHitboxSize": { "x": 3.5, "y": 3.0 },
  "eyeHeight": 2.0,
  "canClimb": false,

  "behaviorConfigs": {
    "passivephysicsmultibox": {
      "collisionBoxes": [ { "x1": -1.5, "y1": 0.0, "z1": -1.75, "x2": 1.5, "y2": 2.5, "z2": 1.75 } ],
      "groundDragFactor": 1, "airDragFallingFactor": 0.5,
      "gravityFactor": 0.0          // <- the network drives position, not physics
    }
    // NOTE: no creaturecarrier, no selectionboxes-on-seat — there is no seat.
  },

  "client": {
    "renderer": "Shape",
    "shape": { "base": "vintagerailroading:entity/flatcar" },
    "textures": {
      "iron":  { "base": "game:block/metal/plate/iron" },
      "dark":  { "base": "game:block/stone/rock/basalt1" },
      "metal": { "base": "game:block/metal/plate/iron" },
      "wood":  { "base": "game:block/wood/planks/pine1" }
    },
    "behaviors": [
      { "code": "repulseagents" },
      { "code": "passivephysicsmultibox" },
      { "code": "interpolateposition" },          // client-only; smooths position between ticks
      { "code": "genericstorage", "quantitySlots": 16 }  // <- the cargo behavior (pick one)
    ]
  },

  "server": {
    "behaviors": [
      { "code": "repulseagents" },
      { "code": "passivephysicsmultibox" },
      { "code": "genericstorage", "quantitySlots": 16 }  // <- same behavior, server side
    ]
  }
}
```

Swap the storage line for the cargo you want:

```jsonc
{ "code": "genericstorage", "quantitySlots": 16 }                          // accepts everything
{ "code": "genericstorage", "quantitySlots": 16, "acceptCategories": ["ore"] }   // filtered
{ "code": "fuelstorage",  "quantitySlots": 16 }   // any burnable
{ "code": "fluidstorage", "capacityLitres": 200 } // any liquid (litres)
```

> **`genericstorage` is the general-purpose behavior** and is what every solid-cargo car
> now uses (the log car included). It accepts everything by default; add a filter and/or a
> capacity override through optional attributes:
>
> | Attribute | Effect |
> |-----------|--------|
> | `acceptCategories` | whitelist by convenience group: `wood`, `ore`, `stone`, `dirt`, `organic`, `food`, `perishable` |
> | `acceptCodes` | whitelist by exact code or wildcard, e.g. `["game:ore-*"]` |
> | `maxStackSize` | per-slot capacity override (0 = item default) |
>
> With no filter attributes it behaves exactly like a plain accept-all store, so existing
> cars are unaffected. `fuelstorage` (burnables) and `fluidstorage` (litres) remain
> separate specialised behaviors. The older `woodstorage` behavior still exists for back-
> compat but new cars should use `genericstorage` with `acceptCategories: ["wood"]`.
>
> **Freezer/food car:** pair `genericstorage` (filtered to `food`/`perishable`) with the
> `freezer` behavior, which draws fuel from a coupled coal car to keep perishables cold.
> See [Cargo & Storage](05-cargo-and-storage.md).

> Put the storage behavior in **both** the `client` and `server` lists. The server holds
> the authoritative inventory; the client opens the GUI. `interpolateposition` is
> **client-only** (smoothing) — do not add it server-side.

### Cargo: what to change, what to keep

- **Change freely:** `code` (unique), `shape.base`, `textures`, hitbox/collision sizes,
  the storage behavior and its capacity.
- **Keep:** `"class": "EntityCargo"`, the `attributes` block (minus mount animations is
  fine), `gravityFactor: 0.0`, and the storage behavior on **both** sides.
- **Do not add (cargo):** `creaturecarrier`, a `Seat-driver` AP, `mountAnimations`, or a
  `selectionboxes` block that references a seat. They are seat machinery a cargo car does
  not have.

## Locomotive entity JSON (driven)

A locomotive is the heavier case because it carries a driver. Start from `train.json`.
The differences from a cargo car are exactly the seat parts:

```jsonc
{
  "code": "flatloco",
  "class": "EntityTrain",         // <- driveable; extends EntityBoat for the seat machinery
  "tags": [ "inanimate", "vehicle" ],

  // EntityTrain extends EntityBoat, which REQUIRES this attributes block (mountAnimations)
  // or boat init NREs.
  "attributes": {
    "shouldSwivelFromMotion": false,
    "speedMultiplier": 1,
    "swimmingOffsetY": 0,
    "maxSpeed": 6.0,              // <- per-type top speed (m/s)
    "mountAnimations": { "idle": "sitflooridle", "ready": "", "forwards": "", "backwards": "" }
  },

  "hitboxSize":     { "x": 3.0, "y": 4.5 },
  "deadHitboxSize": { "x": 3.0, "y": 4.5 },
  "eyeHeight": 2.5,
  "canClimb": false,

  "behaviorConfigs": {
    "passivephysicsmultibox": {
      "collisionBoxes": [ { "x1": -1.0, "y1": 0, "z1": -1.5, "x2": 1.0, "y2": 2.5, "z2": 1.5 } ],
      "groundDragFactor": 1, "airDragFallingFactor": 0.5, "gravityFactor": 0.0
    },
    "creaturecarrier": {
      "seats": [ { "apName": "Seat-driver", "controllable": true, "bodyYawLimit": null, "eyeHeight": 1 } ]
    },
    "selectionboxes": { "selectionBoxes": [ "Seat-driver" ] }
  },

  "client": {
    "renderer": "Shape",
    "shape": { "base": "vintagerailroading:entity/flatloco" },
    "textures": { "iron": {"base":"game:block/metal/ingot/iron"}, "dark": {"base":"game:block/stone/rock/basalt1"}, "metal": {"base":"game:block/metal/plate/iron"} },
    "behaviors": [
      { "code": "repulseagents" },
      { "code": "passivephysicsmultibox" },
      { "code": "interpolateposition" },
      { "code": "selectionboxes" },
      { "code": "creaturecarrier" }
    ]
  },
  "server": {
    "behaviors": [
      { "code": "repulseagents" },
      { "code": "passivephysicsmultibox" },
      { "code": "selectionboxes" },
      { "code": "creaturecarrier" }
    ]
  }
}
```

### Locomotive must-keep (or mounting/coupling breaks)

- `"class": "EntityTrain"`.
- The `attributes` block **with** `mountAnimations` — boat init reads it and NREs without.
- `creaturecarrier` + a shape attachment point named exactly **`Seat-driver`** (see
  Step: shape), on both behavior lists.
- `gravityFactor: 0.0`.

### Can a locomotive also carry cargo?

Yes — add a storage behavior to a `EntityTrain` too. Because the loco has a seat, the
storage behavior opens on an **empty-hand, non-sneaking** right-click and suppresses the
seat for that click (it sets the interaction to "handled"); **sneak + empty hand** then
sits you. For this to work the storage behavior must be listed **before**
`creaturecarrier` in the behavior arrays. On a cargo car this ordering does not matter —
there is no seat.

## The shape JSON

Your model lives at `shapes/entity/<code>.json`.

- **Locomotive:** must contain an attachment point named exactly **`Seat-driver`** on the
  element that should be the seat/origin (on the Baldwin it is the cab body). Both the
  seat behavior and the selection box reference it by name; missing it NREs at spawn.
  The attachment point must be on an **element** (a lowercase `attachmentpoints` array
  inside an element), **not** at the root of the shape file.
- **Cargo car:** **no attachment point is required.** A cargo car has no seat, so you can
  use any model. (You may still add APs for cosmetic mounts/particles if you like.)

The shape's **texture keys** must match the keys your entity `client.textures` declares.

## Registering the class

Both `EntityTrain` and `EntityCargo` are already registered by the mod, as are the three
storage behaviors (`woodstorage`, `fuelstorage`, `fluidstorage`). So when you reuse them
you register **nothing** — the game maps your `"class"` and behavior `"code"` strings
automatically. You only add a registration if you write a brand-new C# class or behavior.

## Spawning your vehicle

- **Add a placer itemtype** that points at your entity code via
  `attributes.entityCode`, using `"class": "ItemTrainPlacer"`. Right-click rail to spawn.
- Or spawn via the game's creative entity tools for quick tests.

Then, for a cargo car, spawn it behind a locomotive and link it with a Coupler tool
(see [Coupling](04-coupling.md)).

## Lang entries

```json
{
  "item-flatcarplacer": "Flatcar",
  "entity-flatcar": "Flatcar"
}
```

## The crafting tree

> Recipes use **Vintage Engineering** for mechanical parts — gears are
> `vinteng:metalgear-iron` — plus vanilla metal (`game:metalplate-iron`, `ingot-*`,
> `stick`, `plank-*`, `glass-*`, `charcoal`). VE is the intended industrial backbone, so
> it is a load-bearing dependency for crafting (the vehicles still *work* without it; you
> just can't craft them without the gears).

Components (3×3 unless noted) consume gears + plates + ingots:

| Component | Key ingredients |
|-----------|-----------------|
| **Boiler** | iron plates, VE iron gears, copper ingot |
| **Firebox** | iron plates, steel ingots, gear, charcoal |
| **Cab Frame** | iron plates, glass, planks |
| **Steam Piston** | iron plates, copper, gears, stick |
| **Iron Wheel Set** | gears, iron plates, iron ingot |

Final vehicles combine the components with extra wheel sets and gears. Every car shares an
iron-plate + VE-iron-gear + iron-wheelset chassis and adds one signature material:

| Vehicle | Key ingredients |
|---------|-----------------|
| **Baldwin 2-8-0** | cab frame, boiler, firebox, steam pistons, wheel sets, gear |
| **Coal Cart** | iron plates, steel, wheel set, gear |
| **Logging Flatcar** | planks, iron plates, gear, wheel set |
| **Tank Car** | iron plates, copper, wheel set, gear |
| **Freight Car** | iron plates, gear, wheel sets |
| **Ore Car** | steel ingots (reinforced) + chassis |
| **Dirt Car** | fire bricks (cheapest) + chassis |
| **Stone Car** | andesite stone + chassis |
| **Organics Car** | planks (flatbed) + chassis |
| **Freezer Car** | glass + copper (insulation/coils) + chassis |
| **Livestock Car** | planks + stick (slatted pen) + chassis |
| **Passenger Car** | glass + copper (windows/trim) + chassis |

> All car recipes are checked to be **conflict-free**: no two share the same grid pattern
> and ingredient mapping, so each is uniquely craftable.

Tools:

| Tool | Pattern | Key ingredients |
|------|---------|-----------------|
| **Track Layer** | tool layout | steel, iron plate, gear, stick |
| **Coupler** (×4 tiers) | `MM_ / MGH / P__` | metal ingot ×3 (copper/tin-bronze/iron/steel), VE iron gear, stick, iron plate |

### The Coupler tool tiers

The Coupler is a durability tool with four metal variants that differ only in how many
coupling actions they perform before breaking:

| Tier | Durability (coupling actions) |
|------|-------------------------------|
| Copper | 80 |
| Tin-bronze | 160 |
| Iron | 320 |
| Steel | 640 |

Each couple **or** uncouple costs 1 durability. The variants share one itemtype
(`couplingtool` with a `metal` variantgroup) and one item class (`ItemCouplingTool`); the
durability and texture are set per-variant in the JSON. See
[Coupling](04-coupling.md) for how the tool is used in play.

---

[← Cargo Cars & Storage](05-cargo-and-storage.md) · [Wiki home](README.md) · [Next: Coupling JSON Contract →](07-coupling-json-contract.md)
