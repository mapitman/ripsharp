# Copilot Instructions

## General

- Use clear, concise C#; prefer small, focused methods.
- Match existing naming and formatting conventions.
- Keep console output user-friendly and consistent with existing messages.
- Always add or update unit tests for new or changed behavior.
- Prefer targeted tests over broad integration tests unless required.
- Use `async`/`await` for I/O-bound operations.
- Avoid unnecessary dependencies; prefer built-in libraries.
- Write XML documentation comments for public APIs.
- Follow SOLID principles and best practices.
- Use meaningful commit messages that describe the changes made.

## Changes and Validation

- Avoid breaking changes to public interfaces unless requested.
- If you change progress display behavior, add tests that lock formatting or timing.
- Run relevant tests when asked or after non-trivial changes.
- Run `dotnet format` before submitting changes.
- Follow `.editorconfig` settings when adding new code or modifying existing code.
