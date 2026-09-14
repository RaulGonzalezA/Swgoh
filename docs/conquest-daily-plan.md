# Conquest daily battle plan

The daily planner turns the current Conquest feat configuration into a projected battle sequence. It is a preview and never records projected wins as real results.

## Sequential state

Each projected battle updates an in-memory planning state before the next battle is selected:

- feat progress advances only for feats matched by the proposed team;
- stamina decreases for the five proposed units using the configured per-battle cost;
- completed feats leave the pending set;
- data-disk loadouts are reevaluated against the newly selected team and remaining feats;
- the next team is calculated from the resulting state rather than from the original plan.

The planner stops when the configured battle limit is reached, all starting pending feats are projected complete, no viable team remains, the energy budget cannot fund another battle, or the configured reward target is reached.

## Energy budget

Energy planning is intentionally configurable instead of assuming one universal node cost:

- `AvailableEnergy` is optional. When it is not configured, energy does not limit the sequence.
- `EnergyCostPerBattle` is configurable and defaults to 20 as a convenience value.
- each planned step exposes its energy cost, cumulative energy spent and, when a budget exists, the resulting energy remaining.

The current model assumes the configured cost is constant for the projected sequence. Route-specific node costs, passive energy regeneration and purchased refreshes are not yet modeled.

## Reward / crate target

The plan can also store:

- current reward points;
- target reward points;
- an optional target label such as `Caja roja`.

Reward projection is conservative. A feat's configured point value is added to projected reward points only when that feat becomes fully complete in a projected battle. Partial feat progress never creates partial reward points.

When a reward target is active, teams that can unlock actual feat-completion points in the current battle are prioritized before the existing feat-efficiency, stamina and tactical tie-breakers. The sequence stops immediately once the projected total reaches the target.

`RewardPointsPerEnergy` is reported for the generated sequence as:

`projected reward points gained / energy spent`

This is a planning efficiency metric, not an assertion about the game's internal scoring formula.

## Output

Each step exposes:

- team and portrait metadata;
- stamina before and after the projected battle;
- recommended disk preset and capacity usage;
- feat progress before and after the battle;
- reward points unlocked by feats completed in that step;
- energy cost and cumulative energy spent;
- projected reward total and points-per-energy efficiency;
- whether the team or disk preset changes from the previous battle;
- reserve-risk warnings.

The result also identifies characters projected below the configured stamina reserve floor so the UI can suggest recovery priority.

## What the planner does not yet model

The planner still does not claim knowledge of:

- the currently selected Conquest node or enemy squad;
- node difficulty or boss mechanics;
- route branching and keycards earned from clearing individual nodes;
- failed battles or retries;
- passive energy regeneration during the day;
- consumables;
- live equipped data disks from an authoritative source.

Because of those limitations, the result should be treated as an energy-aware feat farming plan, not an exact simulation of every possible route to a crate.

## Execution safety

Generating a daily plan saves the current manual Conquest configuration first so that the calculation uses the same feat, stamina, disk, energy and reward state shown in the UI. The generated steps remain preview-only: projected feat progress, stamina consumption, energy consumption and reward gains are not persisted as completed gameplay.
