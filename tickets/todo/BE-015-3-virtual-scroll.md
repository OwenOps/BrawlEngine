# BE-015-3 — Virtual scroll (only if needed)

| Field | Value |
| --- | --- |
| Folder | todo |
| Blocked by | BE-015-2; only if the DOM is still slow |

## Expected feature

If Load more creates hundreds of cards and scroll stutters, render only visible cards (CDK virtual scroll or equivalent). Skip this ticket if 50–100 cards are already fine.

## Tasks

- [ ] Measure first
- [ ] Virtualize Maps (and Musiques if same grid)

## Acceptance

- [ ] Smooth scroll with a large list, **or** ticket closed as “not needed”
