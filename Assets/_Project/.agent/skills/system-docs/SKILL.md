---
name: system-docs
description: Document business logic, architectural decisions, and system design. Use when creating or updating project documentation including ADRs (Architecture Decision Records), system overviews, domain models, and workflow documentation. Ideal for onboarding, knowledge transfer, or capturing "why" behind code decisions.
---

# System Documentation

Generate high-quality technical documentation that captures business logic, architectural decisions, and system relationships.

## Documentation Structure

Create documentation in the project's `docs/` folder:

```
docs/
├── README.md              # Project overview and navigation
├── architecture/
│   └── adr/               # Architecture Decision Records
│       ├── 0001-record-template.md
│       └── ...
├── domain/                # Business domain documentation
│   ├── glossary.md        # Domain terminology
│   └── models.md          # Domain model descriptions
├── systems/               # System-specific documentation
│   └── [system-name].md   # Per-system deep dives
└── workflows/             # Process and flow documentation
    └── [workflow-name].md
```

## Documentation Types

### 1. Architecture Decision Records (ADRs)

Use ADRs to capture significant architectural choices with their context and rationale.

**When to write an ADR:**
- Technology/framework selection
- Significant structural changes
- Trade-off decisions with alternatives considered
- Breaking changes or deprecations

**ADR Format:**
```markdown
# [NUMBER]. [TITLE]

Date: YYYY-MM-DD
Status: [Proposed | Accepted | Deprecated | Superseded by ADR-XXXX]

## Context

[Describe the forces at play: technical constraints, business requirements,
team capabilities, existing patterns. Keep it objective and factual.]

## Decision

[State the decision clearly. Use imperative: "We will...", "The system will..."]

## Consequences

[List both positive and negative outcomes. Be honest about trade-offs.]

### Positive
- [Benefit 1]
- [Benefit 2]

### Negative
- [Drawback or risk 1]
- [Complexity introduced]

## Alternatives Considered

### [Alternative A]
- Pros: ...
- Cons: ...
- Why rejected: ...
```

See [references/adr-examples.md](references/adr-examples.md) for complete examples.

---

### 2. System Documentation

Document each major system/module with:

```markdown
# [System Name]

## Purpose
One paragraph explaining what this system does and why it exists.

## Key Concepts
- **Term**: Definition and context
- **Term**: Definition and context

## Architecture
[Diagram or description of major components and their relationships]

## Responsibilities
- What this system owns
- What it explicitly does NOT handle

## Dependencies
- **Internal**: Other systems this depends on
- **External**: Third-party services, libraries

## API / Public Interface
Document public methods, events, or contracts other systems use.

## Configuration
Required settings, environment variables, or Unity Inspector fields.

## Common Patterns
Code patterns and conventions specific to this system.
```

---

### 3. Domain Documentation

**Glossary**: Define domain terms to ensure consistent understanding.

```markdown
## Glossary

| Term | Definition | See Also |
|------|------------|----------|
| Actor | Game entity with team affiliation | Health, Damageable |
| Affiliation | Team identifier for friend/foe detection | Actor.Affiliation |
```

**Domain Model**: Describe entity relationships and business rules.

```markdown
## Domain Model: Combat System

### Entities
- **Actor**: Any entity that can participate in combat
- **Health**: Tracks damage and death state
- **Damageable**: Damage receiver with multipliers

### Relationships
- Every Actor has exactly one Health component
- Damageable forwards damage to the nearest Health in hierarchy

### Business Rules
1. Damage below zero triggers death callback exactly once
2. Self-damage applies a reduction multiplier
```

---

### 4. Workflow Documentation

Document complex processes and state machines:

```markdown
# [Workflow Name]

## Overview
Brief description of the process.

## States / Steps

### 1. [State/Step Name]
- **Entry conditions**: What triggers entry
- **Actions**: What happens in this state
- **Exit conditions**: What triggers transition

### 2. [Next State]
...

## Diagram
[Mermaid or text-based state diagram]

## Error Handling
Document failure modes and recovery strategies.
```

---

## Documentation Principles

1. **Purpose over mechanics**: Explain *why* before *how*
2. **Living documents**: Update docs when code changes
3. **Discoverable**: Include navigation and cross-references
4. **Audience-aware**: Write for future team members
5. **Concise**: Respect reader's time; frontload key information

## ADR Numbering

- Use sequential 4-digit numbers: `0001`, `0002`, etc.
- Never reuse numbers, even for superseded ADRs
- Reference related ADRs by number in the text

## Documentation Workflow

When documenting:

1. **Identify scope**: System, feature, or decision to document
2. **Gather context**: Review code, commits, and existing docs
3. **Choose format**: ADR, system doc, or workflow based on content
4. **Write draft**: Follow templates above
5. **Cross-reference**: Link to related docs and code
6. **Review**: Validate accuracy with code or team

## Quick Reference

| What to Document | Use |
|------------------|-----|
| Technology choice | ADR |
| Breaking change | ADR |
| Module overview | System doc |
| Entity relationships | Domain model |
| Multi-step process | Workflow doc |
| Term definitions | Glossary |
