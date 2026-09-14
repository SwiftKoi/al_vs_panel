# Local secrets

This directory contains sensitive files that Docker mounts into the application container at runtime. It is intentionally ignored by Git. Never commit this directory's contents, paste them into an issue, or put them in `appsettings.json`, `.env`, or frontend code.

The Compose deployment expects two files:

| File | Purpose |
| --- | --- |
| `admin-password` | Password for the initial local `admin` account. The application reads it during startup when it seeds the authentication database. |
| `ssh-private-key` | Private SSH key used by the backend to authenticate to the configured remote server. It is not a Docker encryption key and it is not used to authenticate browser users. |

The application runs as the non-root container user with numeric GID `1654`. With regular local Docker Compose file-backed secrets, the host file permissions are preserved when the files are mounted. The secret files therefore need to be readable by group `1654`, but not by everyone:

```sh
sudo chgrp 1654 secrets/admin-password secrets/ssh-private-key
chmod 640 secrets/admin-password secrets/ssh-private-key
```

The `sudo` is needed because the host group ID may not exist as a named group on the deployment machine. Do not use `chmod 644`; that would expose the secrets to every local user.

## SSH key terminology

SSH authentication uses a key pair:

```text
This application/container                 Remote server
----------------------------               -----------------------------
ssh-private-key   -- SSH -->               ~/.ssh/authorized_keys
(keep secret)                                (contains matching public key)
```

The private key stays on the deployment machine and is mounted into the container as `/run/secrets/ssh_private_key`. The matching public key must be installed in the remote SSH account's `~/.ssh/authorized_keys` file. The private key must never be copied to the remote server.

The remote account should be dedicated to this panel and have only the permissions required for its configured operations. Do not use the remote server's root account.

## Option A: use an existing SSH key

If you already connect to the target server with SSH, check for an existing private key:

```sh
ls -la ~/.ssh
```

Typical private key names are `id_ed25519` and `id_rsa`. A private key normally has no `.pub` suffix; files ending in `.pub` are public keys and must not be used as `ssh-private-key`.

Copy the appropriate private key into this repository's secrets directory:

```sh
cp ~/.ssh/id_ed25519 secrets/ssh-private-key
sudo chgrp 1654 secrets/ssh-private-key
chmod 640 secrets/ssh-private-key
```

Only do this if the key already authenticates as the same remote user configured for the panel. If the key has a passphrase, the current non-interactive container setup cannot prompt for it; create a dedicated key as described below or add explicit secret/passphrase support before using it.

## Option B: create a dedicated panel key

Creating a separate key avoids giving the panel access to your personal SSH identity. Generate it on the deployment machine:

```sh
mkdir -p ~/.ssh
chmod 700 ~/.ssh
ssh-keygen -t ed25519 -f ~/.ssh/alegacy_webpanel -N '' -C 'alegacy-web-panel'
```

This creates:

- `~/.ssh/alegacy_webpanel` — the private key; keep it secret;
- `~/.ssh/alegacy_webpanel.pub` — the public key; this can be installed on the remote server.

Install the public key for the intended remote user:

```sh
ssh-copy-id -i ~/.ssh/alegacy_webpanel.pub remote-user@remote-host
```

If `ssh-copy-id` is unavailable, append the contents of `~/.ssh/alegacy_webpanel.pub` to the remote user's `~/.ssh/authorized_keys` using an already-working administrative connection. Do not append the private key.

Verify the new identity before giving it to Docker:

```sh
ssh -i ~/.ssh/alegacy_webpanel remote-user@remote-host
```

Then copy the private key into the Compose secrets directory:

```sh
cp ~/.ssh/alegacy_webpanel secrets/ssh-private-key
sudo chgrp 1654 secrets/ssh-private-key
chmod 640 secrets/ssh-private-key
```

The key is deliberately generated without a passphrase because the container starts non-interactively. Compensate by using a dedicated, least-privileged remote account, restrictive local permissions, and a host that is itself protected. Do not reuse this key for personal access.

## Create the application password

Generate a strong password for the initial `admin` account:

```sh
umask 077
openssl rand -base64 32 > secrets/admin-password
sudo chgrp 1654 secrets/admin-password
chmod 640 secrets/admin-password
```

The value in this file is the password used for the first seeded admin account. Keep it in a password manager. Do not put it in the frontend, Compose file, or application settings.

## Complete setup

From the repository root, run:

```sh
mkdir -p secrets
chmod 700 secrets
umask 077
openssl rand -base64 32 > secrets/admin-password
cp /path/to/your/ssh/private/key secrets/ssh-private-key
sudo chgrp 1654 secrets/admin-password secrets/ssh-private-key
chmod 640 secrets/admin-password secrets/ssh-private-key
```

Replace `/path/to/your/ssh/private/key` with either an existing private key or the dedicated key created above. Confirm that the files exist and have restrictive permissions:

```sh
ls -la secrets
```

The expected permissions are approximately:

```text
drwx------ secrets/
-rw-r----- secrets/admin-password
-rw-r----- secrets/ssh-private-key
```

Start the application after the files are present:

```sh
docker compose up -d --build
docker compose ps
docker compose logs -f web
```

## Remote configuration

The SSH key only proves the identity of the remote SSH user. The target connection still needs to be configured separately. Set the remote host, SSH port, username, host-key fingerprint, and allowlisted commands through `appsettings.json` or an environment-specific configuration source. For example, environment variables use the `__` separator:

```text
Remote__Targets__remote-main__Host=example.internal
Remote__Targets__remote-main__Port=22
Remote__Targets__remote-main__Username=alegacy-panel
Remote__Targets__remote-main__HostKeyFingerprintSha256=SHA256:...
```

The host-key fingerprint verifies that the application is connecting to the expected server. Do not disable host-key verification just to make a connection work.

## Troubleshooting

- **`bind source path does not exist`:** create both `secrets/admin-password` and `secrets/ssh-private-key` before running Compose.
- **`Access to `/run/secrets/...` is denied:** verify that both files have group `1654` and mode `640`; rerun the `sudo chgrp` and `chmod 640` commands above.
- **`Permission denied (publickey)`:** verify that the matching `.pub` key is in the configured remote user's `authorized_keys`, the username is correct, and the private key works with `ssh -i`.
- **Private key parsing or passphrase errors:** the container cannot answer an interactive passphrase prompt. Use a dedicated key without a passphrase or add supported passphrase handling.
- **Host-key fingerprint mismatch:** verify the configured SHA-256 fingerprint against the trusted remote server. Do not replace it blindly.
- **Authentication database or login problems after recreation:** make sure the `app_data` and `data_protection_keys` Docker volumes were retained.

If a private key is exposed, revoke its public-key entry from the remote user's `authorized_keys` immediately and create a replacement key.
