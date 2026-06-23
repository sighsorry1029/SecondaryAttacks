# YAML Validation Normalization Goal

## Goal

After the YAML field layout is settled, make config validation predictable and consistent across all SecondaryAttacks YAML files.

## Desired Rules

- Missing field: inherit from the applicable parent/global fallback, then built-in default.
- Explicit invalid enum/string choice: use that field's built-in default and emit a warning.
- YAML syntax or type parse failure: reject the new snapshot and keep the previously applied configuration.
- External resource reference, such as prefab names, VFX names, `copyFrom`, or effect ids: do not silently replace with defaults; warn and skip only the affected feature when possible.
- Numeric ranges: clamp to valid runtime ranges; add warnings later for values that are probably mistakes.

## Candidate Fields

- `preset`
- `projectileSpinAxis`
- `qualityPreset`
- shield mode/option fields if new string choices are added
- effect `type`, `trigger`, `damageType`, `modifier`, and scalar modes

## Implementation Notes

- Keep inheritance semantics separate from invalid-value fallback.
- Prefer small shared helpers for normalized enum/string choices.
- Warning messages should include the YAML field name, invalid value, accepted values, and chosen default.
- Avoid accepting misspelled aliases; warnings should help users fix config rather than normalize typos silently.

## Non-Goals

- Do not change the YAML schema as part of this cleanup.
- Do not add automatic migration while field names are still in flux.
- Do not turn missing optional fields into warnings.
