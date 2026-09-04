# Contributing to Tokenmon

Thanks for helping improve Tokenmon.

## Development setup

1. Install the .NET 10 SDK on Windows.
2. Fork and clone the repository.
3. Restore dependencies with `dotnet restore Tokenmon.slnx`.
4. Run the test suite with `dotnet test Tokenmon.slnx`.
5. Start the app with `dotnet run --project Tokenmon.App`.

## Pull requests

- Keep each pull request focused on one change.
- Add or update tests when changing game rules, parsers, or persistence behavior.
- Run the full test suite before opening the pull request.
- Do not commit generated files from `bin`, `obj`, `artifacts`, or `TestResults`.
- Describe user-visible changes and any manual verification performed.

When reporting a bug, include the Windows version, .NET SDK version, affected usage provider, expected behavior, and observed behavior. Remove prompts, project paths, tokens, and other private data from logs before sharing them.
