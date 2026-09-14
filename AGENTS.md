# AlegacyWebPanel contributor guidance

Before changing this project, read the documentation in [`Docs/`](Docs/README.md). The documentation is the shared reference for architecture, module boundaries, testing rules, security, configuration, and operations.

Keep the documentation current. When a change affects behavior, module boundaries, request flow, configuration, security, testing conventions, deployment, or operational procedures, update the relevant file under `Docs/` in the same change.

Important project rules:

- Keep `Program.cs` as the composition root. Register modules there; keep feature behavior inside modules.
- Every module has a `module.json` manifest and owns its endpoints, services, repositories, persistence adapters, configuration, exceptions, DTOs, and tests.
- Keep shared, cross-module primitives in `Core`. Do not put feature-specific business logic in `Core`.
- Follow `HTTP request -> endpoint -> service interface -> service -> repository -> persistence/configuration`.
- Endpoints coordinate transport and HTTP concerns only. Services contain business logic and must not depend on HTTP requests, responses, or controllers.
- Controllers/endpoints depend on interfaces, never concrete service implementations.
- Use domain exceptions in business logic and translate them to HTTP exceptions at the endpoint boundary.
- Never commit credentials, private keys, local secret files, databases, build output, or generated test output.

## Build and test environment

- `dotnet build`
- Run tests with `dotnet test`.
