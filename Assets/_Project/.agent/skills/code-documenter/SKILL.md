---
name: code-documenter
description: Generate concise but comprehensive code documentation. Use when documenting source code files, classes, methods, APIs, or entire codebases. Supports C#, TypeScript, Python, and other languages. Produces XML doc comments, JSDoc, docstrings, or markdown documentation as appropriate.
---

# Code Documenter

Generate clear, concise documentation that captures essential details without verbosity.

## Documentation Principles

1. **Concise ≠ Incomplete**: Include all necessary information, eliminate filler words
2. **Intent over mechanics**: Explain *why* and *what*, not obvious *how*
3. **Audience-aware**: Assume competent developers; skip basic explanations
4. **Scannable**: Use consistent structure; frontload the purpose

## Documentation Workflow

```
Analyze Code → Identify Purpose → Write Summary → Document Parameters → Add Examples (if complex)
```

### Step 1: Analyze

- Identify what the code does and its role in the system
- Note edge cases, constraints, and dependencies
- Understand caller/callee relationships

### Step 2: Document

Apply language-specific format, following patterns below.

---

## Language Patterns

### C# (XML Documentation)

```csharp
/// <summary>
/// Brief one-line description of purpose.
/// </summary>
/// <param name="paramName">What this parameter represents.</param>
/// <returns>What is returned and when.</returns>
/// <remarks>Non-obvious behavior, constraints, or usage notes.</remarks>
/// <exception cref="ExceptionType">When this exception is thrown.</exception>
public ReturnType MethodName(ParamType paramName)
```

**Guidelines:**
- `<summary>`: One sentence, imperative mood ("Calculates...", "Returns...")
- `<param>`: Start with lowercase, describe the role not the type
- `<returns>`: Describe the value, including edge cases (null, empty, etc.)
- `<remarks>`: Only for non-obvious details; omit if unnecessary
- `<exception>`: Document explicitly thrown exceptions

**Class example:**
```csharp
/// <summary>
/// Manages pooled audio sources for performant sound playback.
/// </summary>
/// <remarks>
/// Pre-allocates sources on initialization. Automatically expands pool when exhausted.
/// </remarks>
public class AudioPool { }
```

---

### TypeScript/JavaScript (JSDoc)

```typescript
/**
 * Brief description of function purpose.
 * @param paramName - What this parameter represents
 * @returns Description of return value
 * @throws {ErrorType} When this error occurs
 * @example
 * const result = functionName(value);
 */
function functionName(paramName: Type): ReturnType
```

**Guidelines:**
- First line: purpose in present tense
- `@param`: Use hyphen after name, describe role
- `@example`: Include for non-trivial APIs

---

### Python (Docstrings)

```python
def function_name(param_name: Type) -> ReturnType:
    """Brief one-line description.

    Longer description only if the one-liner is insufficient.

    Args:
        param_name: What this parameter represents.

    Returns:
        What is returned and under what conditions.

    Raises:
        ExceptionType: When this exception is raised.
    """
```

**Guidelines:**
- Use Google-style docstrings
- First line stands alone as summary
- Omit sections with no content

---

## Documentation Levels

### File/Module Level
- Purpose of the file/module
- Key exports or entry points
- Dependencies (if non-obvious)

### Class Level
- What the class represents
- Key responsibilities
- Thread safety, lifecycle, or initialization requirements

### Method Level
- What it does (not how)
- Parameters and return values
- Side effects and exceptions
- Preconditions/postconditions (if any)

### Property/Field Level
- Only document if purpose is non-obvious from name
- Include valid ranges or constraints

---

## Quality Checklist

Before finalizing documentation:

- [ ] Every public member has documentation
- [ ] Summary explains purpose, not implementation
- [ ] Parameters describe role, not just type
- [ ] Return values describe meaning, including edge cases
- [ ] Non-obvious behavior is noted in remarks
- [ ] No filler phrases ("This method is used to...")
- [ ] Consistent tense (imperative for summaries)

---

## Anti-Patterns to Avoid

| ❌ Bad | ✅ Good |
|--------|---------|
| "This method calculates the sum" | "Calculates the sum" |
| "Returns an integer" | "Returns the total item count, or -1 if unavailable" |
| "param1: The first parameter" | "param1: Source collection to merge" |
| Documenting obvious getters/setters | Skip trivial properties |
| Copying method name as summary | Explain the purpose |

---

## Batch Documentation

When documenting multiple files:

1. Start with public APIs and interfaces
2. Document in dependency order (dependencies first)
3. Cross-reference related classes/methods
4. Maintain consistent terminology across files
