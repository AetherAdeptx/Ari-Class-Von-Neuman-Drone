# Navigation design

The first conservative flight planner is active when `Free_Move` is enabled.
It uses ship geometry, world-space observations, scan ages, state control, and
radio map merging. Its current route stages are described below.

## 1. Route hierarchy

1. Read the ship's world position and `Target_Destination`.
2. Use the low-resolution solar-system map to choose broad travel corridors.
3. Refine the next corridor against the world-anchored near collision BSP.
4. Inflate blocked or unknown regions by the ship collision radius plus a
   configurable safety margin.
5. Search for a waypoint chain through cells with sufficient clearance.
6. Remove intermediate waypoints when a swept-volume line test proves that a
   direct segment is safe.
7. Feed only the next safe waypoint to a separate flight controller.

Keeping global routing, local avoidance, and physical control separate lets
each stage run at a different resolution and frame budget.

## 2. Cell meaning and traversal cost

The near BSP uses `0` for unknown, `1` for confirmed empty, and `2` for
confirmed matter. Current traversal rules are:

- confirmed empty: base movement cost;
- unknown: base cost plus 30 percent;
- confirmed matter: impassable;
- stale observations: gradually approach unknown cost based on scan age.

The 30 percent preference encourages known corridors without making unexplored
space impossible when it is the only route.

## 3. Clearance

Pathfinding must plan for the ship volume rather than its center. The existing
six extrema, local bounding box, and bounding radius provide a conservative
first implementation. A candidate segment is accepted only if the radius plus
safety margin remains clear along the full swept path. A later implementation
can replace the sphere with a tighter oriented box or convex approximation.

## 4. Search and simplification

The near planner uses bounded A* over six neighboring cells. Work is
incremental: the open set, closed set, and parent links remain in memory, and a
configured number of nodes are expanded per frame. If the route contains too
many waypoints, simplification tests the farthest visible later waypoint first
and removes every intermediate point only when the swept-volume test succeeds.

## 5. Replanning

The planner should restart or repair its route when:

- a forward ray detects a new obstruction;
- the target changes;
- the ship deviates beyond a route tolerance;
- a synchronized map supplies newer geometry;
- clearance changes because the construct gains or loses blocks.

`Velocity_Vector_Encounter_Detected` can request immediate braking or evasion
before the slower planner finishes a replacement route.

## 6. Flight control boundary

The planner outputs world-space waypoints and desired arrival speeds. A later
controller converts those into attitude error, gyro commands, acceleration,
and directional thruster overrides. `Hard_Stop`, `Emergency`, `Docking`, and
`Docked` remain higher-priority safety states and may always suppress motion.
