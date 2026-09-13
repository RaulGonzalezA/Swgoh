# Conquest daily battle plan

The daily planner projects a sequence of Conquest battles from the current saved plan. It reuses the same planning signals as the one-battle optimizer: feat value, roster strength, stamina opportunity cost and data-disk loadout synergy.

## Sequential simulation

For every projected battle the planner:

1. rebuilds pending feats from the progress produced by previous projected battles;
2. recalculates the current stamina of every candidate character;
3. selects the best five-character team for that projected state;
4. chooses the best compatible saved data-disk loadout;
5. applies projected feat progress and stamina cost in memory;
6. repeats until the requested battle limit is reached, all pending feats are completed, or no viable team remains.

This means battle 2 is evaluated against the simulated result of battle 1 rather than against the original snapshot.

## Preview semantics

Generating a daily plan saves the current Conquest configuration first, but the generated sequence is a preview. It does not persist projected wins, projected feat progress or projected stamina depletion. Real execution/result capture is intentionally a separate future workflow.

## Output

Each step exposes:

- team and portrait metadata;
- stamina before and after the projected battle;
- recommended disk preset and capacity usage;
- feat progress before and after the battle;
- whether a feat is projected to complete;
- whether the team or disk preset changes from the previous battle;
- reserve-risk warnings.

The result also identifies characters projected below the configured stamina reserve floor so the UI can suggest recovery priority.
