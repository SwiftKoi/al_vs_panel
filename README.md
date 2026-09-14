# Alegacy Web Panel

A legacy server management dashboard providing remote operations, console commands logging, metrics, and file manager features.

## Prerequisites

- .NET 10.0 SDK
- Node.js & npm (v18+)
- Docker & Docker Compose (for testing and isolated runtimes)

---

## Launching the Project in Development Mode

You need to run both the C# backend server and the React/Vite frontend development server.

### 1. Launch the Backend Server
From the root workspace directory, run:
```bash
dotnet run
```
* Runs the composite composition root hosting the Minimal APIs.
* Default local listener: `http://localhost:5053` under `Development` environment settings.

### 2. Launch the Frontend Dev Server
From the root workspace directory:
```bash
cd frontend
npm install
npm run dev
```
* Starts the Vite dev server at: `http://localhost:5173`
* Proxies `/auth`, `/api`, and `/health` requests to the C# backend on `http://localhost:5053`.

### 3. Alternative: Launch Using Docker Compose
The Docker Compose setup is fully automated via the `manage.py` utility script:

```bash
# 1. Initialize environments, detect GIDs/UIDs, generate passwords and SSH keypairs
python3 manage.py setup

# 2. Build and start the compose stack
python3 manage.py dev
```
* The application runs locally on `http://localhost:3000`.
* The setup script automatically creates the `secrets/` directory with `700` permissions, generates credentials, and assigns group ownership `1654` so the non-root container user can read them securely.
* **Note**: If you want to run the frontend locally with hot-reloading targeting the Docker backend, run `npm run dev:docker` from the `frontend/` directory instead of `npm run dev`.


---

## Running the Verification Suite

### Module Unit Tests
Run local unit tests directly for development feedback:
```bash
dotnet test Modules/FileManager/Tests/Unit/AlegacyWebPanel.FileManager.UnitTests.csproj
```

### Full Integration Containerized Tests
Execute the full module validation suite using isolated Docker Compose profile runners:
```bash
python3 manage.py test
```


---

## Architecture & Code Guidelines

Please reference the detailed guidance docs in the [Docs/](Docs/README.md) directory for details on structure, configuration, and modules conventions.