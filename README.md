# Von Neuman Space Engineers Scripts

This is an MDK2 C# workspace for Space Engineers programmable-block scripts.
It is configured for the local Steam/Proton installation on this computer.

## Build and check

```bash
dotnet build
```

The build runs the Space Engineers whitelist analyzer, combines the C# source
files, lightly minifies them to remain below the programmable block's
100,000-character limit, and writes the finished script into the game's local
script browser.

In Space Engineers, open a programmable block, choose **Edit**, then
**Browse Scripts** (the folder icon) and select the built script.

## Project files

- `Program.cs` is the programmable block entry point.
- `ShipRuntime.cs` owns the `init`, `reinit`, `main`, `save`, and `close`
  lifecycle and coordinates the other modules.
- `BlockCatalog.cs` scans membership during initialization, tracks the
  grid-local minimum/maximum Z blocks, and crawls one known block per tick to
  refresh its position dictionaries.
- `SeatMonitor.cs` detects the edge when a controller seat is entered.
- `ThrusterClassifier.cs` creates the six translation and six differential-
  rotation thruster groups from in-game orientation and center of mass.
- `GyroArray.cs` stores gyroscopes nearest-to-farthest from center of mass.
- `SolarTracker.cs` hill-climbs combined solar output using only rotors tagged
  `[Solar]`, alternating trackers while unattended.
- `Supervisor.cs` owns the operating state and repeating function-plus-
  parameters task queue.
- `ShipState.cs` contains the shared collections (the C# equivalent of a
  declarations header).
- `TelemetryScheduler.cs` runs exactly two named telemetry jobs per game tick.
- `ShipStatsTelemetry.cs` calculates mass, motion, power, average power, and
  instantaneous and averaged energy/economy efficiency statistics.
- `FuelTelemetry.cs` tracks battery, hydrogen, oxygen, uranium, and ice from
  zero to 100 percent capacity.
- `ResourceTelemetry.cs` tracks each raw ore amount, target shortfall, net
  change rate, and positive production rate.
- `ResourceTargets.cs` loads desired ore reserves from programmable-block
  Custom Data.
- `TelemetryState.cs` holds the statistics, fuel, resource, projection, rate,
  and update-name dictionaries.
- `DistanceSensorArray.cs` treats cameras as ranging sensors, creates six
  directional lists, and sorts each list front-to-back, top-to-bottom, then
  left-to-right in controller-local coordinates.
- `BspSpatialMap.cs` maintains world-anchored one-cubic-kilometer BSP trees on large grids and
  proportionally scaled 200-meter trees on small grids. Each leaf represents a
  4x4x4 block region: `0` is unknown, `1` is observed empty space, and `2`
  is observed matter.
- `SpatialBufferMonitor.cs` samples programmable-block movement every tick,
  retains three direction vectors, publishes estimated BSP memory statistics,
  and purges trailing slabs at the 64 MiB per-script spatial-map budget.
  Emergency cleanup rechecks after every slab,
  preserves only the programmable block's current slab and its forward slab
  when necessary, and latches `Hard_Stop=1` if those still exceed 64 MiB.
- `ProgramSpatialApi.cs` exposes cache set/get, coordinate lookup, and map
  encode/decode functions.
- `ProgramControlApi.cs` exposes callable rotation-factor setters/getters while
  keeping the main loop small.
- `RuntimeRegistry.cs` publishes rotation factor and spatial cache distance.
- `GlobalTemporalBsp.cs` is a sparse 32,768 km solar-system scan-date map with
  256 km leaves and a fixed 16 MiB node-pool budget. Each timestamp is stored
  as two 32-bit words forming a 64-bit Unix-seconds value; zero is unknown.
- The registry also publishes total battery capacity and charge in MWh, the
  six extreme block names, controller-local collision bounds and radius,
  solar-tracking output, and radio-network state.
- `RadioNetwork.cs` receives IGC heartbeats and future-position plans for 24
  drone slots and 64 relay slots across seven assignable drone colors.
- Add more `.cs` files when separating larger features; MDK2 combines them.
- `mdk.ini` contains portable packaging settings.
- `mdk.local.ini` contains this computer's game paths and is intentionally
  ignored by Git.

The in-game scripting API is documented at:
https://github.com/KeenSoftwareHouse/SpaceEngineersModAPI

## Runtime arguments

- `init` starts the runtime and performs a fresh scan.
- `reinit` or `rescan` forces a fresh scan.
- `close` stops periodic updates until `init` is run again.
- `status` prints the latest cached telemetry values.
- `save-data` writes a bounded persistent snapshot immediately.
- `load-data` reloads that snapshot immediately.
- `rotation-factor 0.5` sets the clamped rotation-thruster cutoff factor.
- `spatial-cache 4` keeps 64 trees. On a large grid this is a 4x4x4 km cache
  of one-kilometer trees. On a small grid the installed game's exact 1:5
  linear scale is applied: the cache is 0.8x0.8x0.8 km with 200-meter trees.
  Both modes therefore have the same 400x400x400 maximum region count.
- `home-base X,Y,Z` sets the probe's home-base world coordinates.
- `assigned-dock X,Y,Z` sets its assigned dock world coordinates.
- `mother-ship ENTITY_ID` selects the preferred daily map-sync partner.
- `identity Drone16-Green` assigns a persistent 1–32 character node identity.
- `target X,Y,Z` sets the world-space navigation destination.
- `free-move on|off` arms or disarms autonomous movement.
- `free-mode on|off` is an alias for `free-move`.
- `cruise-speed 15` sets the danger/unknown-space speed cap.
- `travel-speed 100` sets the confirmed-clear-space speed cap.
- `task-lines 2` sets interpreted command lines per task object per frame.
- `send-task ADDRESS|COMMAND;COMMAND` sends a task object to a trusted node.
- `radio-free-mode ADDRESS on|off` sends the encrypted `FREE_MODE` control.
- `radio-key SHARED_SECRET` changes the persistent shared encryption key.
- `radio-kind drone|relay` selects the local node table.
- `radio-slot 1..24|64` selects the local slot.
- `radio-color red|orange|yellow|green|blue|indigo|violet` selects its color.
- `mode discovery|broadcast|monitor|off` selects radio behavior.
- `beacon on|off` permits beacon activation on emergency or unknown exposure.
- `sync-maps` requests an immediate collision/temporal-map sync.
- `auto-tracking on|off` controls automatic solar-rotor movement.
- `auto-tracking-level 60` sets the battery percentage below which automatic
  solar searching and charging begin.
- `tasks-per-frame 2` changes the supervisor queue budget from 1 through 32.
- `state normal|in_use|evasion|emergency|solar_charge|return_to_base|track_radio|search|docking|docked`
  selects a supervisor state.

`Docking` requests map synchronization, preferring the configured mother ship,
and becomes `Docked` when a connector reports connected. `Docked` disables
thrusters, gyros, reactors, hydrogen engines, and gas generators, and puts all
gas tanks into stockpile mode. Map sync is retried at least once per day while
docking or docked; if the preferred mother ship does not answer within one
minute, the request falls back to nearby broadcast peers. Temporal-map
snapshots are split into bounded IGC packets. Overlapping world-space collision
trees merge confirmed-empty regions, while temporal cells keep the newest
timestamp.

Discovery uses eight tag-based IGC channels (`VON.NEUMAN.DISC.0` through `.7`),
not numeric radio frequencies. Discovery mode sends an encrypted presence every
15 seconds and listens for 60 Update1 frames. Broadcast mode sends every four
frames. Monitor mode opens all eight listeners every 24 frames for a 60-frame
window. An encrypted presence from an unregistered address is treated as an
unidentified contact and updates `Enemy_Exposure_Time`; IGC cannot prove that a
sender belongs to an enemy faction. Beacons are enabled only when the beacon
flag is on and exposure or emergency conditions are active.

Entering any cockpit or control seat automatically performs `reinit` and makes
that controller the reference for forward, backward, up, down, left, and right.

Camera raycasting is enabled automatically. The current velocity vector is
raycast to 2 km, or 15 km above 25 m/s, whenever a suitably oriented camera has
enough scan charge. Samples alternate between the centerline and four offsets
one map region around it. The other five controller-relative directions are
raycast to 1 km every four seconds. Clear samples record empty regions and
surface hits record matter. Resolution is 10 meters on a
large grid and 2 meters on a small grid.

Clear ray segments are sampled every 100 meters into the collision BSP and a
hit endpoint is recorded as matter. Navigation advances toward a distant
destination in 1.5 km planning horizons, tries a direct swept-clearance route,
and otherwise expands a bounded six-neighbor A* search over 100 meter cells.
Unknown cells cost 30 percent more than confirmed-empty cells. Planning and
waypoint simplification use the ship's collision radius plus five meters.
Unknown space or a forward encounter limits flight to `Cruise_Speed`; a
confirmed-clear segment may use `Travel_Speed`. `Free_Move` defaults off.

The navigation-history ring records position, destination, timestamp, and
supervisor-state string every 15 seconds. Its 2,880 entries cover 12 hours and
occupy approximately 184,320 bytes (180 KiB), plus a few shared state strings.
Entries reference the shared strings rather than allocating 2,880 copies.

Radio task objects contain a string phrase split into command lines. The
interpreter processes up to `Tasks_Per_Frame` objects and
`Task_Object_Lines_Per_Frame` lines from each object per frame. Only predefined
commands are callable, and remote objects are accepted only from the configured
mother ship or an assigned drone/relay address.

Map synchronization and task/control packets use authenticated XTEA-CTR
envelopes with a keyed block MAC and per-packet nonce. Every cooperating node
must have the same `radio-key`. The included `VonNeuman-Change-Me` bootstrap
key is only for initial setup and should be replaced. This compact custom layer
is suitable for in-game privacy and packet rejection; it is not a substitute
for an audited modern cryptographic protocol. A bounded runtime nonce cache
rejects recently replayed encrypted packets.

`Velocity_Vector_Encounter_Detected` is `1` when the latest completed
velocity-vector scan hit an entity and `0` when it completed clear. If no
camera can presently scan, the last confirmed value is retained.
`Velocity_Vector_Scan_Valid` remains `0` until the first velocity scan succeeds,
and `Velocity_Vector_Scan_Distance_Meters` reports the active 2 or 15 km range.

Radio heartbeat packets use tag `VON.NEUMAN.NET` and
`MyTuple<int,int,int,double,Vector3D>`: node kind (`0` drone or `1` relay),
one-based slot, color `0..6`, antenna range in meters, and current position.
Prediction packets carry kind and slot plus five vectors for +15 minutes,
+30 minutes, +1 hour, +1 day, and the ultimate location.

The supervisor begins with camera and radio work as two repeating tasks.
`Normal`, `Docking`, and `Docked` dispatch up to `Tasks_Per_Frame` housekeeping
queue entries each tick.
`In_Use` is selected while the reference controller is occupied.
`Solar_Charge` puts batteries in recharge mode when productive solar is
available and stored charge is below 60 percent. Low battery charge, hydrogen,
or reactor uranium selects `Emergency` after a five-second startup grace
period; the default low thresholds are 20 percent charge and 15 percent fuel.
Evasion, return-to-base, track-radio, and search are reserved operational
states whose behaviors can be added independently.
`Auto_Tracking_Receicing_Side` reports the strongest panel's ship side:
`-1` unknown, then `0..5` for forward, backward, up, down, left, and right.
The registry also reports `Velocity_Vector_Scan_Age_Seconds` and the most
recent `Velocity_Vector_Encounter_Distance_Meters` (`0` for a clear scan).

`Save()` writes a bounded versioned snapshot containing persistent registry
settings, radio records, the local collision map, and the global temporal map.
Because programmable-block storage is limited, both maps use sparse bounded
encodings rather than raw 80 MiB memory images. The local encoding starts with
`BSP6|distance|regionsPerTreeAxis|treeSizeMeters|`. Each remaining record is a world tree coordinate
plus a preorder token stream: `0` is uniform unknown, `1` is uniform observed
empty, `2` is uniform observed matter, and `3` is a split followed by low/high
children. Matching uniform children collapse automatically. Ship-relative
`BSP2`–`BSP5` geometry is intentionally rejected because it cannot be safely
reinterpreted as world-space data without the original scan transform.

Projected resource needs are configured in the programmable block's Custom
Data. Amounts are ore kilograms; omitted entries default to zero:

```ini
[resource-targets]
Iron=100000
Nickel=20000
Cobalt=10000
Ice=50000
```
