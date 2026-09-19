# Agent Guide for ZYC.Codex.Protocol

This file defines house rules for any AI/code assistant working in this repository.
It applies to the entire repository unless the user explicitly overrides it.

## Scope

- Applies to all directories under this repository root.
- Additional AGENTS.md files in subfolders may supplement or override these rules within their scope.

## Review Focus

- Prioritize API design, naming consistency, visibility, dependency boundaries, and correctness.
- Prefer minimal, surgical changes aligned with existing architecture and style.
- Keep explanations concise and action-oriented.

## Out-of-Scope Topics

- Do not suggest enabling or changing code analyzers, StyleCop/FxCop/Roslyn rules, or linting tools.
- Do not discuss or recommend modifying `RunAnalyzersDuringBuild`; treat its current value as a given.
- Do not propose changes to the build pipeline, CI, or solution-wide props/targets unless the user asks.
- Do not introduce large new dependencies or frameworks without an explicit request.

## Technology Boundaries

- Preserve the existing `net10.0` target frameworks.
- Preserve existing output paths and packaging structure.

## Testing Architecture

- Automated tests are maintained in `ZYC.Codex.Protocol.Generator.Tests` within this repository.
- Use the existing test project; do not add test projects unless the user explicitly asks.
- Avoid running tests or local builds unless the user explicitly asks for them.
- When reporting verification, distinguish between tests not being run for the current task and automated tests not existing.

## Naming and Coding Conventions

- Interfaces: `I` prefix and PascalCase, for example `IUpdateManager`.
- Methods: PascalCase; async methods returning `Task`, `Task<T>`, `ValueTask`, or `ValueTask<T>` must end with `Async`.
- Events and DTOs: PascalCase; avoid typos.
- Namespaces: file-scoped; match folder structure where practical.
- Do not use primary constructors on classes or structs. Declare constructors explicitly and store dependencies in get-only auto-properties.
- Avoid adding `sealed` unless required for correctness, an API contract, or an explicit user request.
- Unless necessary, do not use `readonly` fields. Prefer get-only auto-properties with PascalCase names. For example, replace `private readonly StringBuilder text = new();` with `private StringBuilder Text { get; } = new();`.
- XML documentation is optional unless explicitly required for the task.

## File Encoding

- New handwritten files must use CRLF line endings and UTF-8 with BOM.
- Generated files retain the generator's existing LF line endings and UTF-8 without BOM.

## Safety and Change Policy

- Do not proactively filter, mask, or redact sensitive parameters unless the user explicitly requests it.
- When renaming types, update file names and all references across the solution.
- Prefer additive and backward-compatible changes unless the user approves breaking changes.

## Response Style

- Be concise and specific; avoid filler.
- Use Mermaid `graph TD` for diagrams, including requested module dependency graphs.
- When listing files or paths in responses, use absolute or repository-root-relative paths.

## Exceptions

- If the user explicitly asks for analyzer, build, or CI discussion or changes, those topics are allowed for that request only.
