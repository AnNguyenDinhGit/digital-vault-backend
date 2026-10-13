# Repository Guidelines

## Project Structure & Architecture

`LegacyVault.sln` contains four .NET 8 projects:

- `LegacyVault.API/`: ASP.NET Core controllers, authentication, middleware, dependency injection, and Swagger configuration.
- `LegacyVault.BLL/`: business services, DTOs, validation, OTP, and document security.
- `LegacyVault.DAL/`: scaffolded entities, EF Core context, repositories, and file storage.
- `LegacyVault.Tests/`: executable test harness, HTTP integration tests, and SQLite relational tests.

Preserve **API → BLL → DAL**. Controllers delegate business decisions to services; services access persistence through repositories. Keep scaffolded entity/context files intact; add model customization through partial files such as `WorkflowConcurrency.cs`. `docs/` contains API and Swagger instructions; `database/` contains database scripts. The root PNG is a project reference image.

## Build, Test, and Development Commands

Use SDK `9.0.315`, specified in `global.json`.

```powershell
dotnet restore LegacyVault.sln
dotnet build LegacyVault.sln --no-restore
dotnet run --project LegacyVault.Tests
dotnet run --project LegacyVault.API --launch-profile https
dotnet tool restore
```

These restore dependencies, build, execute tests, start the API, and restore the pinned EF Core CLI tool. Development Swagger runs at `https://localhost:7015/swagger`. If necessary, trust the local certificate with `dotnet dev-certs https --trust`.

## Coding Style & Naming

Use four-space indentation, nullable reference types, and file-scoped namespaces. Follow surrounding C# conventions: PascalCase types/methods, camelCase parameters/locals, and descriptive DTO names. Await asynchronous operations and propagate `CancellationToken` through layers. No dedicated formatting configuration is present; avoid unrelated reformatting, particularly generated files.

## Testing Guidelines

Tests use custom assertions in a console runner, not xUnit or NUnit; run them with `dotnet run`, rather than `dotnet test`. Name scenario files descriptively, such as `MainFlowTests.cs` or `BeneficiaryAssignmentTests.cs`. Cover authorization, invalid inputs, rollback, and concurrent updates when changing workflows. Use SQLite in memory and test doubles for external services. No numerical coverage threshold is configured; never create test accounts in the production database.

## Commits & Pull Requests

History uses short, action-oriented subjects, including `connect db` and `feature-API:register/login`; no strict commit standard is established. Write concise subjects describing the change. PRs should explain resulting behavior, affected endpoints, configuration requirements, and validation commands/results. Link relevant issues and update `docs/` when contracts change.

## Security & Configuration

Store database credentials, SMTP settings, encryption keys, and certificates in User Secrets or environment variables. Do not commit secrets or runtime `App_Data` files. Preserve server-controlled roles, resource ownership checks, OTP requirements, and signature validation. Schema changes require explicit review; do not automatically migrate or recreate the scaffolded database.
