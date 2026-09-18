# Daily Farming Planner

The daily farming planner translates the aggregate investment farming plan into a short list of actions for the current day.

## Data boundary

The public player provider does not expose current player energy, live shipments, crystal budget, unequipped gear, or shard inventory. The planner therefore:

- never claims to know current energy;
- only recommends paid refreshes inside the explicit daily crystal budget selected by the player;
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

### Smart Signal Data routing

Sector 9 adds exact dual-Signal-Data nodes at 20 Cantina Energy per attempt:

| Active deficits | Preferred route while both remain open | Cost |
| --- | --- | ---: |
| Fragmented + Incomplete | Cantina 9-B | 20 Cantina Energy |
| Fragmented + Flawed | Cantina 9-D | 20 Cantina Energy |
| Incomplete + Flawed | Cantina 9-F | 20 Cantina Energy |

The planner keeps the legacy Sector 8 node when only one Signal Data type is needed. When two compatible deficits are active it uses the corresponding Sector 9 dual node. If all three types are missing, it pairs the two highest-priority deficits and leaves the remaining resource on its dedicated Sector 8 node.

This is deliberately a routing heuristic rather than a claim about guaranteed drops. The dual-node rewards and energy costs are verified from the live SWGOH.GG campaign database. Community tracking published in June 2026 used roughly 28,000 observations across two studies and supports Sector 9 as a useful aggregate farm while Sector 8 remains appropriate for a single specific short-term deficit; those rates are empirical, not official guarantees.

Sector 9 references:

- https://swgoh.gg/campaigns/cantina-battles/M09/
- https://www.reddit.com/r/SWGalaxyOfHeroes/comments/1u3s6tt/cantina_sector_9_drop_rates_final_version_and/

Original route references:

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


## Crystal budget and paid energy refreshes

Verified on 2026-09-18 against SWGOH Wiki:

- Normal Energy refreshes grant 120 energy. The first three cost 50 crystals each; the next three cost 100 each, then the price continues to escalate.
- Fleet Energy refreshes grant 120 energy and use the same initial 50 / 50 / 50, then 100 / 100 / 100 structure.
- Cantina Energy refreshes grant 120 energy. The first three cost 100 crystals each; subsequent refreshes cost more.
- The application contains the documented escalation tables, but treats the configured crystal amount as a maximum rather than a spending target.

References:

- https://swgoh.wiki/wiki/Light_Side_Battles%3A_1-E_%28Normal%29
- https://swgoh.wiki/wiki/Fleet_Battles%3A_2-B_%28Normal%29
- https://swgoh.wiki/wiki/Cantina_Battles%3A_2-B

### Budget profiles

The Blazor UI stores the selected daily budget locally per ally code:

- F2P: 0 crystals/day.
- Ahorro: 50 crystals/day.
- Eficiente: 150 crystals/day.
- Acelerado: 300 crystals/day.
- Custom: 0-5,000 crystals/day.

The planner only allocates crystals to Normal, Cantina or Fleet when that channel has an actionable material farm with a real resource dependency. Fleet guardrails and other informational actions are intentionally ineligible. Unused crystals remain explicitly unspent.

Refresh allocation is marginal: each additional refresh on the same channel has diminishing planning priority. This allows a constrained budget to diversify between a critical Cantina farm and a useful Normal farm instead of spending the entire budget on one channel by default.


## Target ETA projection

The planner now projects a conservative completion date for each active investment target when the remaining blockers can be modeled safely.

### Signal Data basis

The ETA engine deliberately uses the dedicated Sector 8 nodes as a conservative baseline even when the daily route uses a dual Sector 9 node:

- Fragmented Signal Data: 1.35 expected units per attempt on Cantina 8-C.
- Incomplete Signal Data: 0.90 expected units per attempt on Cantina 8-F.
- Flawed Signal Data: 0.65 expected units per attempt on Cantina 8-G.
- Each dedicated attempt costs 16 Cantina Energy.
- Planned daily Cantina Energy is the free-energy baseline plus the refresh energy actually allocated by the current crystal budget.

These planning rates are intentionally slightly below the long-running empirical community averages commonly cited around 1.37 / 0.93 / 0.66. They are expectations over many attempts, not guaranteed drops.

Current campaign data confirms the dedicated Sector 8 Signal Data nodes and their 16-energy cost:

- https://swgoh.gg/campaigns/cantina-battles/M08/

Empirical references:

- https://www.reddit.com/r/SWGalaxyOfHeroes/comments/ugtcwo/
- https://www.reddit.com/r/SWGalaxyOfHeroes/comments/15r7ae6/

### Conservative portfolio scheduling

Signal Data deficits are projected in the same portfolio priority order as the aggregate farming plan. The calculation does not credit the simultaneous second Signal Data drop from Sector 9. Therefore a smart 9-B / 9-D / 9-F route may finish earlier than the displayed ETA.

The June 2026 Sector 9 study collected roughly 28,000 observations and supports using dual nodes for aggregate value, but those empirical rates are not treated as guaranteed constants in the ETA engine:

- https://www.reddit.com/r/SWGalaxyOfHeroes/comments/1u3s6tt/cantina_sector_9_drop_rates_final_version_and/

### Normal-energy feedstock ETA

Carbonite Circuit Board and Bronzium Wiring now also contribute conservative ETA data through their existing recommended feedstock farms:

- Carbonite Circuit Board: Light Side 1-C Normal, 6 Energy per attempt, modeled at 0.70 CCB per attempt.
- Bronzium Wiring: Light Side 7-B Normal, 10 Energy per attempt, modeled at 0.20 BW per attempt through Mk 5 Fabritech Data Pad conversion.
- Normal Energy uses the same free-energy baseline plus only the refresh energy allocated by the current crystal budget.
- Carbonite and Bronzium share the Normal Energy channel, so their expected energy is accumulated in portfolio priority order rather than allowing both to spend the same daily energy.
- Normal Energy and Cantina Energy progress in parallel, so a target blocked by both channels uses the later modeled completion date.

The Carbonite rate is intentionally rounded below the roughly 0.72 CCB per attempt observed in the classic LS 1-C sample. The Bronzium rate is intentionally rounded below the roughly 0.22 BW per attempt implied by community observations of Mk 5 Fabritech drops on LS 7-B and the current Scavenger point conversion. These remain planning expectations, not guaranteed drops.

References:

- https://swgoh.wiki/wiki/Scavenger_Guide
- https://www.reddit.com/r/SWGalaxyOfHeroes/comments/flyayi/
- https://www.reddit.com/r/SWGalaxyOfHeroes/comments/1gqbd6s/
- https://swgoh.gg/campaigns/light-side-battles/

### Complete vs partial ETA

A target receives a complete ETA only when every aggregate blocker that affects it has a modeled cadence.

- Signal Data, Carbonite Circuit Board, and Bronzium Wiring can currently contribute a modeled ETA.
- A target whose tracked materials are already covered is marked as ready now.
- Other Scavenger conversions, live stores/events, credits, advanced relic materials, gear, and shard/star progress remain without a time estimate unless their acquisition cadence becomes explicitly modeled.
- If some resources are modeled but another blocker is not, the UI shows a partial ETA for the known bottleneck and keeps the final completion date unset.
- Shared inventory is evaluated at portfolio level, so the ETA does not let multiple targets spend the same materials virtually.

The date remains a planning estimate: RNG, bonus/double-drop events, missed bonus energy, energy caps, extra purchases, and manual changes to the active target list can move the actual completion date.


## Budget what-if simulator

The daily plan now evaluates the same active farming portfolio under several crystal-budget scenarios without changing the player's saved preference.

Scenarios always include:

- F2P: 0 crystals/day.
- 50 crystals/day.
- 150 crystals/day.
- 300 crystals/day.
- The current custom budget when it is not already one of those presets.

Each scenario reuses the same refresh allocator and conservative ETA engine as the live plan. It reports the crystals actually spendable under the current farming actions, unused budget, refresh count, extra energy, number of targets with a complete ETA, the modeled portfolio horizon, and days saved relative to F2P.

The modeled portfolio horizon is the latest completion date among targets that have a complete ETA. It is not presented as the completion date of targets that still depend on shards, live stores, unmodeled Scavenger conversions, credits, or other unknown acquisition rates. Carbonite Circuit Board and Bronzium Wiring are exceptions because their normal-energy feedstock routes now have explicit conservative planning rates.

A higher configured budget is never treated as mandatory spending. For example, a 50-crystal scenario with only a Cantina Signal Data farm keeps all 50 crystals unspent because the first Cantina refresh costs 100. Similarly, crystals allocated to Normal Energy may be useful to the daily farming plan without shortening a Signal-Data-only ETA.

The UI keeps simulations read-only until the player explicitly selects a scenario. Choosing a scenario then stores that daily budget through the existing player preference flow and recalculates the plan.
