# Testing

## Test categories

Every feature module owns separate Unit and Feature test projects under its own `Tests/` directory.

### Unit tests

Unit tests verify behavior implemented by services. They instantiate the service under test, replace repositories and other service interfaces with fakes or mocks, and verify business decisions, validation, orchestration, and domain exceptions.

Unit tests do not test ASP.NET, EF Core, Identity, SSH.NET, filesystem, network, or vendor behavior.

### Feature tests

Feature tests verify endpoint coordination. They invoke endpoint coordinators with fake service interfaces and verify DTO-to-command mapping, service-result-to-HTTP mapping, and domain-failure-to-`HttpException` translation.

Feature tests do not retest ASP.NET routing, model binding, cookie internals, EF Core, or SSH.NET.

## Core tests

Shared-kernel tests live under `Core/Tests`. They test Core primitives and shared behavior, not module business rules.

## Naming

Use behavior-focused names:

```text
Authenticate_rejects_invalid_credentials
Execute_rejects_operations_not_returned_by_repository
Login_translates_domain_failure_to_http_exception
```

Each test should verify one meaningful behavior and avoid incidental implementation details.

## Running tests

```sh
dotnet build
dotnet test Core/Tests/AlegacyWebPanel.Core.Tests.csproj
dotnet test Modules/Authentication/Tests/Unit/AlegacyWebPanel.Authentication.UnitTests.csproj
dotnet test Modules/Authentication/Tests/Feature/AlegacyWebPanel.Authentication.FeatureTests.csproj
dotnet test Modules/RemoteOperations/Tests/Unit/AlegacyWebPanel.RemoteOperations.UnitTests.csproj
dotnet test Modules/RemoteOperations/Tests/Feature/AlegacyWebPanel.RemoteOperations.FeatureTests.csproj
dotnet test Modules/ServerManagement/Tests/Unit/AlegacyWebPanel.ServerManagement.UnitTests.csproj
dotnet test Modules/ServerManagement/Tests/Feature/AlegacyWebPanel.ServerManagement.FeatureTests.csproj
dotnet test Modules/FileManager/Tests/Unit/AlegacyWebPanel.FileManager.UnitTests.csproj
dotnet test Modules/AutomationApi/Tests/Unit/AlegacyWebPanel.AutomationApi.UnitTests.csproj
dotnet test Modules/AutomationApi/Tests/Feature/AlegacyWebPanel.AutomationApi.FeatureTests.csproj
dotnet test Modules/Logging/Tests/Unit/AlegacyWebPanel.Logging.UnitTests.csproj
dotnet test Modules/Logging/Tests/Feature/AlegacyWebPanel.Logging.FeatureTests.csproj
```

Run the complete test suite in the isolated SDK test image:

```sh
docker compose --profile test run --rm --build tests
```

The test image copies the repository into the container and does not bind-mount the workspace, so Docker-generated `bin/` and `obj/` files cannot change host permissions.

Before submitting a change, run the relevant Unit and Feature suites, `dotnet build`, and `git diff --check`.

RemoteOperations adapter-focused unit tests may inspect process start information and generated SSH command text, but they must not open a real process or network connection. ServerManagement unit tests replace RemoteOperations and cover operation mapping, status and metrics parsing, command validation, lifecycle conflicts, failure handling, and safe stream mapping. Feature tests invoke endpoint coordinators directly and verify request/result mapping and domain-to-HTTP exception translation.

Logging unit tests cover the writer (level filtering, structured-state serialization with secret redaction and size bounds, drop counting), the query service (filter validation and DTO mapping), the SQLite repository (write, filtered query, pagination, level counts, sources, delete, prune, and storage-failure translation) and the background worker's persistence and shutdown flush. Logging feature tests invoke the endpoint coordinators with a fake service and verify filter binding, error-feed mapping, and domain-to-HTTP exception translation.

AutomationApi unit tests cover API-key validation and authentication-handler outcomes. AutomationApi feature tests invoke the endpoint coordinators with fake FileManager and ServerManagement services and verify argument mapping, multipart upload handling, streamed downloads, and domain-to-HTTP exception translation.
