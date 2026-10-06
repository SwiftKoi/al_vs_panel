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

Shared-kernel tests live under `Core/Tests`. They test Core primitives and shared behavior, not module business rules. This includes the audit filter (`AuditEndpointFilterTests`: actor, role and IP capture, failure recording, a failing trail never breaking the action, no trail registered) and what it records (`AuditRequestDescriberTests`: server and target extraction, removal of secrets and file contents, console-command redaction, size limits).

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
dotnet test Modules/ServerLogs/Tests/Unit/AlegacyWebPanel.ServerLogs.UnitTests.csproj
dotnet test Modules/Users/Tests/Unit/AlegacyWebPanel.Users.UnitTests.csproj
dotnet test Modules/Users/Tests/Feature/AlegacyWebPanel.Users.FeatureTests.csproj
dotnet test Modules/Analytics/Tests/Feature/AlegacyWebPanel.Analytics.FeatureTests.csproj
dotnet test Modules/Audit/Tests/Unit/AlegacyWebPanel.Audit.UnitTests.csproj
dotnet test Modules/Audit/Tests/Feature/AlegacyWebPanel.Audit.FeatureTests.csproj
```

Run the complete test suite in the isolated SDK test image:

```sh
docker compose --profile test run --rm --build tests
```

The test image copies the repository into the container and does not bind-mount the workspace, so Docker-generated `bin/` and `obj/` files cannot change host permissions.

Before submitting a change, run the relevant Unit and Feature suites, `dotnet build`, and `git diff --check`.

RemoteOperations adapter-focused unit tests may inspect process start information and generated SSH command text, but they must not open a real process or network connection. ServerManagement unit tests replace RemoteOperations and cover operation mapping, status, metrics, and connections parsing, command validation, lifecycle conflicts, failure handling, and safe stream mapping. Feature tests invoke endpoint coordinators directly and verify request/result mapping and domain-to-HTTP exception translation.

Logging unit tests cover the writer (level filtering, structured-state serialization with secret redaction and size bounds, drop counting), the query service (filter validation and DTO mapping), the SQLite repository (write, filtered query, pagination, level counts, sources, delete, prune, and storage-failure translation) and the background worker's persistence and shutdown flush. Logging feature tests invoke the endpoint coordinators with a fake service and verify filter binding, error-feed mapping, and domain-to-HTTP exception translation.

Audit unit tests cover the SQLite repository (write, every filter, paging, facets, pruning, storage-failure translation), the trail service (timestamping, swallowing storage failures) and query validation. Audit feature tests verify filter binding, error translation and that every route is read-only and admin-only.

Users unit tests cover role assignment on create, role listing, role changes (security-stamp rotation, self-change and last-admin refusal) and last-admin delete protection. ServerManagement and Analytics feature tests include a route-policy test that maps the module and asserts exactly which routes carry the moderator (`Staff`) policy and that all others use the admin default. Core tests cover the panel policies.

AutomationApi unit tests cover API-key validation (including the separate mods key scope) and authentication-handler outcomes. ModManager unit tests include the public mods catalog rules (side normalisation, private/unreadable/unchecked mods, completeness, URL form). AutomationApi feature tests invoke the endpoint coordinators with fake FileManager and ServerManagement services and verify argument mapping, multipart upload handling, streamed downloads, and domain-to-HTTP exception translation.
