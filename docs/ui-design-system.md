# SWGOH Assistant UI Design System

The Blazor UI uses one shared visual language defined in `wwwroot/design-system.css` and reusable Razor components under `Components/Ui`.

## Ownership

- `design-system.css` owns colors, typography, spacing, radii, elevation, controls, shared surfaces and state patterns.
- Feature styles (`ux-v*.css`, `ux-conquest.css`) may own feature-specific layout while legacy screens are progressively migrated.
- Feature styles must consume design-system variables and must not introduce a new palette, generic button style, generic card style or generic page-header style.
- `design-system.css` is loaded last intentionally so shared visual decisions are authoritative while feature layout remains backwards compatible.

## Shared components

Use these instead of recreating the pattern in a page:

- `UiPageHeader`: page title, kicker, description, back link and page actions.
- `UiCard`: normal/accent/success/warning/danger surfaces with consistent header and actions.
- `UiMetric`: KPI/summary values.
- `UiStatusPill`: status labels with optional state dot.
- `UiEmptyState`: no-data/not-configured state.
- `UiLoadingState`: page or section loading state.
- `UiAlert`: info/success/warning/error feedback.
- `RichUnitPortrait`: SWGOH unit recognition surface (portrait, relic, speed, mods and datacron verification state).

## Tokens

Prefer `--ds-*` variables for new styles. Compatibility aliases (`--bg`, `--surface`, `--line`, `--text`, `--muted`, `--accent`, etc.) remain for existing feature CSS.

Token groups:

- Color: `--ds-bg`, `--ds-surface*`, `--ds-border*`, `--ds-text*`, `--ds-accent*`, `--ds-success`, `--ds-warning`, `--ds-danger`.
- Spacing: `--ds-space-1` through `--ds-space-9`.
- Radius: `--ds-radius-xs` through `--ds-radius-xl`.
- Elevation: `--ds-shadow-sm`, `--ds-shadow-md`, `--ds-shadow-lg`.
- Typography: `--ds-text-*`, `--ds-title-*`.
- Interaction: `--ds-touch-target`, `--ds-transition`.

## Rules for new UI

1. Do not hardcode a new generic color, radius or shadow if an existing token expresses the intent.
2. Do not add `ux-v10.css`, `ux-v11.css`, etc. for generic visual changes. Extend `design-system.css` or create a genuinely feature-specific stylesheet.
3. Do not duplicate `.primary-button`, card, metric, status, loading or empty-state implementations in a page.
4. Keep feature styles focused on layout and domain-specific visualizations.
5. Desktop and mobile must share the same component semantics; responsive CSS changes layout, not meaning.
6. A datacron warning must communicate uncertainty (`DC?` / verify), never imply applicability that the data cannot prove.
7. Prefer portraits over unit-name lists for GAC/Conquest recognition; keep names available as captions/tooltips/details.

## Migration strategy

Legacy pages can keep their existing class names while they are migrated. `design-system.css` contains adapters for the existing high-level surfaces so the product remains visually consistent during the transition. When touching a legacy page for functional work, prefer replacing its duplicated generic markup with the shared component at the same time.
