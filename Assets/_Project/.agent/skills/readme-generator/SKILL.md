---
name: readme-generator
description: "Generates industry-standard README.md files."
license: MIT
metadata:
  version: 1.0.0
---

# Readme Generator
Generates professional `README.md` files using industry best practices.
## Procedure

Analyze: Scan files (package.json, requirements.txt, go.mod) for stack, name, and setup. If unavailable, query user for Name, Description, Features, Stack, and Commands.

Draft: Follow references/TEMPLATE.md. Use professional tone, valid Markdown, and language-tagged code blocks.

Refine: Remove placeholders. Verify all setup steps are actionable.
## Rules
Structure: Strictly follow the Template order.

Badges: Add License/Version badges at the top.

Visuals: Suggest placeholders for screenshots/GIFs.

Goal: Enable zero-friction setup for new developers.
## Resources
- [Template](references/TEMPLATE.md)
