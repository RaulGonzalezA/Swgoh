# Daily Farming Planner

The daily farming planner translates the aggregate investment farming plan into a short list of actions for the current day.

## Data boundary

The public player provider does not expose current player energy, live shipments, crystal budget, unequipped gear, or shard inventory. The planner therefore:

- never claims to know current energy;
- never recommends paid refreshes without a future explicit crystal-budget model;
- treats store/event availability as a check, not a guaranteed purchase;
- keeps Fleet energy unassigned when no exact relic-material node is modeled;
- labels normal-energy scavenger farms as guided feedstock rather than direct relic drops.

## Verified source mappings

Verified on 2026-09-18 against SWGOH Wiki:

| Resource | Route | Cost |
| --- | --- | ---: |
| Fragmented Signal Data | Cantina 8-C | 16 Cantina Energy |
| Incomplete Signal Data | Cantina 8-F | 16 Cantina Energy |
| Flawed Signal Data | Cantina 8-G | 16 Cantina Energy |
| Carbonite Circuit Board feedstock | Light Side 1-C Normal | 6 Energy |
| Bronzium Wiring feedstock | Light Side 7-B Normal | 10 Energy |
| Corrupted Signal Data | Scavenger conversion | n/a |

References:

- https://swgoh.wiki/wiki/Fragmented_Signal_Data
- https://swgoh.wiki/wiki/Incomplete_Signal_Data
- https://swgoh.wiki/wiki/Flawed_Signal_Data
- https://swgoh.wiki/wiki/Cantina_Battles%3A_8-C
- https://swgoh.wiki/wiki/Cantina_Battles%3A_8-F
- https://swgoh.wiki/wiki/Cantina_Battles%3A_8-G
- https://swgoh.wiki/wiki/Beginner_Guide/Kidori%27s_Scavenger_Guide
- https://swgoh.wiki/wiki/Light_Side_Battles%3A_7-B_%28Normal%29
- https://swgoh.wiki/wiki/Corrupted_Signal_Data

## Free-energy baselines

The UI shows theoretical free daily energy, assuming regeneration is not capped and all listed bonus energy is claimed:

- Normal: 240 regeneration + 135 bonus = 375.
- Fleet: 240 regeneration + 45 bonus = 285.
- Cantina: 120 regeneration + 45 bonus = 165.

These are planning baselines, not the player's live energy balance.
