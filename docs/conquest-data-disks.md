# Conquest data disks

Conquest plans can track a manual data-disk inventory and reusable loadouts. The application deliberately does not assume a live source for the player's equipped disks or an exact combat-stat formula.

## Model

- `DiskCapacityLimit` is configurable per Conquest plan.
- A data disk stores its display name, capacity cost, planner bonus, applicability rule and optional feat links.
- Applicability supports any team, faction coverage, or specific units.
- Loadouts reference inventory disk IDs and must fit within the configured capacity.
- The optimizer evaluates every valid loadout for every recommended team and selects the best compatible one.

## Scoring

Feat efficiency remains the primary ranking signal. Disk bonuses only affect the tactical score between teams with comparable feat coverage. A disk's planner bonus is explicitly a user-configured planning weight, not a claim about the game's underlying stat formula.

When a compatible disk is explicitly linked to a feat that the proposed team advances, the optimizer adds a small feat-synergy signal. This makes presets useful for farming objectives without pretending that a data disk directly grants a known amount of feat progress.

## Backward compatibility

Existing MongoDB Conquest documents that predate data-disk support load with a default capacity of 12 and empty inventory/loadouts. No destructive migration is required.
