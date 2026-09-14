# Module development

## Required module structure

Each module should follow this shape:

```text
Modules/<ModuleName>/
├── AlegacyWebPanel.<ModuleName>.csproj
├── module.json
├── Contracts/       # DTOs and cross-layer contracts
├── Endpoints/       # HTTP coordination and response mapping
├── Exceptions/      # Module domain exceptions
├── Infrastructure/  # DI registration and external adapters
├── Persistence/     # Repository interfaces and persistence adapters
├── Services/        # Service interfaces and business logic
└── Tests/
    ├── Unit/
    └── Feature/
```

The module project must reference `Core/AlegacyWebPanel.Core.csproj` and must exclude its `Tests/` directory from production compilation. The host project references the module project and remains responsible for composition and endpoint activation. Keep module-specific NuGet packages in the module project that uses them.

Folders may be omitted only when a module genuinely has no responsibility in that area.

## Module manifest

Every module must contain `module.json` at its root:

```json
{
  "name": "Example",
  "description": "Short responsibility-focused description.",
  "version": "1.0.0"
}
```

`name` is the stable module name, `description` states the module’s responsibility, and `version` describes the module contract and implementation version.

## Layer responsibilities

### Endpoints

Endpoints are thin coordinators. They may read transport DTOs, validate transport shape, call service interfaces, translate domain exceptions into `HttpException` values, and map service results to HTTP results.

Endpoints must not call repositories directly, access EF Core, Identity, SSH.NET, or filesystem APIs, implement business decisions, or construct concrete service implementations.

### Services

Services contain use-case and business behavior. They accept application commands or values, return application DTOs/models, and depend on abstractions. Services must not reference ASP.NET request/response types.

### Repositories

Repositories are responsible only for reading and writing data or retrieving configured definitions. They map persistence models into application-facing models and should not decide HTTP status codes or business policy.

### Persistence and infrastructure

Persistence adapters implement repository interfaces. Infrastructure adapters implement external-system interfaces such as SSH connections, secret readers, or authentication sessions. Their concrete types are registered in the module’s registration class.

### DTOs

DTOs cross layer boundaries. Keep transport DTOs separate from persistence models. Do not expose `IdentityUser`, EF entities, SSH.NET objects, or configuration option objects through HTTP responses.

### Exceptions

Business code throws module domain exceptions. It must not throw HTTP-specific exceptions. Endpoint coordinators convert known domain exceptions into `HttpException` values. Core exception middleware converts `HttpException` values into problem responses.

## Registration and boundaries

Each module should expose one registration method from `Infrastructure`, such as `AddAuthenticationModule` or `AddRemoteOperationsModule`, and one endpoint-mapping method from `Endpoints` (e.g. in `AuthenticationRoutes.cs`), such as `MapAuthenticationModule`. Endpoint handler implementations reside in `*Endpoints.cs` classes (e.g. `AuthenticationEndpoints.cs`), separating route binding from endpoint logic. `Program.cs` calls the registration and mapping methods and does not register module internals individually.

Prefer explicit interfaces and DTOs between modules. Do not reach into another module’s persistence, services, or internal models. Put a contract in Core only when it is genuinely shared.
