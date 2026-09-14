# SWGOH Assistant UI Design System

The Blazor UI uses one shared visual language defined in `wwwroot/design-system.css` and reusable Razor components under `Components/Ui`.

## Ownership

- `design-system.css` owns colors, typography, spacing, radii, elevation, controls, shared surfaces and state patterns.
- Feature styles are named for the surface they own (`gac-planner.css`, `home-assistant.css`, `app-navigation.css`, etc.). They may own layout, but must consume design-system tokens instead of introducing another generic visual language.
- Component/page-specific layout should prefer Blazor CSS isolation (`*.razor.css`) when it does not need to style markup owned by another component.
- Sequential versioned stylesheets (`ux-v2.css`, `ux-v3.css`, …) are no longer part of the application. New visual work must extend the design system, CSS isolation, or a clearly owned feature stylesheet.
- `design-system.css` is loaded last intentionally so shared visual decisions are authoritative while feature layout remains compatible.

## Shared components

Use these instead of recreating the pattern in a page:

- `UiPageHeader`: page title, kicker, description, back link and page actions.
- `UiCard`: normal/accent/success/warning/danger surfaces with consistent header and actions.
- `UiActionCard`: one dominant operational action with status, explanation and primary CTA.
- `UiMetric`: KPI/summary values.
- `UiStatusPill`: status labels with optional state dot.
- `UiEmptyState`: no-data/not-configured state.
- `UiLoadingState`: page or section loading state.
- `UiAlert`: info/success/warning/error feedback.
- `RichUnitPortrait`: SWGOH unit recognition surface (portrait, relic, speed, mods and datacron verification state).
- `RosterUnitCard`: roster-specific unit surface with isolated styling and a details action.

## Tokens

Prefer `--ds-*` variables for new styles. Compatibility aliases (`--bg`, `--surface`, `--line`, `--text`, `--muted`, `--accent`, etc.) remain only for feature CSS that has not yet been fully tokenized.

Token groups:

- Color: `--ds-bg`, `--ds-surface*`, `--ds-border*`, `--ds-text*`, `--ds-accent*`, `--ds-success`, `--ds-warning`, `--ds-danger`.
- Spacing: `--ds-space-1` through `--ds-space-9`.
- Radius: `--ds-radius-xs` through `--ds-radius-xl`.
- Elevation: `--ds-shadow-sm`, `--ds-shadow-md`, `--ds-shadow-lg`.
- Typography: `--ds-text-*`, `--ds-title-*`.
- Interaction: `--ds-touch-target`, `--ds-transition`.

## Rules for new UI

1. Do not hardcode a new generic color, radius or shadow if an existing token expresses the intent.
2. Do not create numbered or release-oriented CSS files for visual changes. Extend `design-system.css`, component-scoped CSS, or a genuinely feature-specific stylesheet.
3. Do not duplicate `.primary-button`, card, metric, status, loading, empty-state or primary-action implementations in a page.
4. Keep feature styles focused on layout and domain-specific visualizations.
5. Desktop and mobile must share the same component semantics; responsive CSS changes layout, not meaning.
6. A datacron warning must communicate uncertainty (`DC?` / verify), never imply applicability that the data cannot prove.
7. Prefer portraits over unit-name lists for GAC/Conquest recognition; keep names available as captions/tooltips/details.
8. Home is an assistant, not a navigation dashboard: one primary action should win, with only a short queue and minimum supporting context.
9. Do not leave page-specific CSS in the global bundle when CSS isolation can own it safely.

## Migration strategy

When touching a legacy page, replace duplicated generic markup with `Ui*` components at the same time. Existing feature classes can remain temporarily for cross-component/domain-specific layout, but their stylesheet must be feature-owned and consume design-system tokens. Remove compatibility aliases from a feature stylesheet when its last legacy selector is migrated.

The intended migration order is Home → Roster → GAC → Planner → Conquista.

- Home: migrated to the assistant-first `Ui*` pattern.
- Roster: migrated to `UiPageHeader`, `UiMetric`, `UiCard`, `UiStatusPill`, `UiLoadingState`, `UiAlert` and `UiEmptyState`. Page layout now lives in `PlayerRoster.razor.css`; unit cards live in `RosterUnitCard.razor` + isolated CSS. Legacy `player-roster.css` and `roster-collection.css` are removed. Shared GAC/player navigation lives in `player-gac-shared.css`, while War Room owns `war-room.css`.
- GAC / Planner / Conquista: next migration targets.
