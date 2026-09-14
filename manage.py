#!/usr/bin/env python3
import os
import sys
import shutil
import secrets
import argparse
import subprocess
import grp
import pwd
import tempfile
from pathlib import Path

# Group ID for ASP.NET Core container non-root user (app)
CONTAINER_GID = 1654

RESTART_SERVICE_NAME = "vintagestory-restart.service"
RESTART_TIMER_NAME = "vintagestory-restart.timer"

# Server ZIP archive downloaded by Server/install.sh on first start.
DEFAULT_SERVER_ARCHIVE_URL = "https://fplay.org/get/vs?v=stable&s=vs_server_linux*.zip"


def read_env_value(path, key, default):
    if os.path.exists(path):
        with open(path, "r") as f:
            for line in f:
                if line.startswith(f"{key}="):
                    return line.strip().split("=", 1)[1]
    return default


def systemd_exec_argument(value):
    """Quote one systemd ExecStart argument."""
    return '"' + value.replace("\\", "\\\\").replace('"', '\\"') + '"'

def check_prerequisites():
    print("Checking prerequisites...")
    docker_ok = shutil.which("docker") is not None
    compose_ok = False
    
    if docker_ok:
        try:
            # Check if 'docker compose' (v2 plugin) is available
            res = subprocess.run(["docker", "compose", "version"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            if res.returncode == 0:
                compose_ok = True
        except Exception:
            pass
            
        if not compose_ok:
            # Fallback to check docker-compose (v1)
            compose_ok = shutil.which("docker-compose") is not None
            
    ssh_keygen_ok = shutil.which("ssh-keygen") is not None
    
    if not docker_ok:
        print("[-] Warning: 'docker' is not installed or not in PATH.")
    if not compose_ok:
        print("[-] Warning: 'docker compose' (or 'docker-compose') is not available.")
    if not ssh_keygen_ok:
        print("[-] Warning: 'ssh-keygen' is not installed or not in PATH.")
        
    return docker_ok and compose_ok

def setup_secrets():
    print("\nSetting up secrets...")
    
    # 1. Ensure secrets folder exists with restrictive permissions
    secrets_dir = "secrets"
    os.makedirs(secrets_dir, exist_ok=True)
    os.chmod(secrets_dir, 0o700)
    
    # 2. Generate admin-password if not present
    admin_pwd_path = os.path.join(secrets_dir, "admin-password")
    if not os.path.exists(admin_pwd_path):
        pwd = secrets.token_urlsafe(32)
        with open(admin_pwd_path, "w") as f:
            f.write(pwd + "\n")
        print(f"[+] Generated new admin password in {admin_pwd_path}")
    else:
        print(f"[~] Admin password already exists in {admin_pwd_path}")
        
    # 3. Generate ssh-private-key if not present
    ssh_key_path = os.path.join(secrets_dir, "ssh-private-key")
    if not os.path.exists(ssh_key_path):
        print("[+] Generating SSH key pair...")
        try:
            subprocess.run([
                "ssh-keygen", "-t", "ed25519", 
                "-f", ssh_key_path, 
                "-N", "", 
                "-C", "alegacy-web-panel"
            ], check=True)
            print(f"[+] Generated SSH keypair in {ssh_key_path}")
        except subprocess.CalledProcessError as e:
            print(f"[-] Error generating SSH key: {e}")
    else:
        print(f"[~] SSH key already exists in {ssh_key_path}")
        
    # 4. Correct permissions of secret files
    for file_name in ["admin-password", "ssh-private-key"]:
        file_path = os.path.join(secrets_dir, file_name)
        if os.path.exists(file_path):
            os.chmod(file_path, 0o640)
            
            # Try to change group to 1654 (standard app group in asp.net core containers)
            try:
                os.chown(file_path, -1, CONTAINER_GID)
                print(f"[+] Set group ownership of {file_path} to {CONTAINER_GID}")
            except PermissionError:
                print(f"[~] Permission denied setting group ownership of {file_path} to {CONTAINER_GID}.")
                print("    Attempting to adjust GID using 'sudo chgrp'...")
                try:
                    subprocess.run(["sudo", "chgrp", str(CONTAINER_GID), file_path], check=True)
                    print(f"[+] Successfully set group ownership of {file_path} via sudo.")
                except Exception as e:
                    print(f"[-] Warning: Failed to set group ownership: {e}")
                    print(f"    Please execute manually if the container fails to load secrets:")
                    print(f"    sudo chgrp {CONTAINER_GID} {file_path}")

def detect_docker_gid():
    # Attempt to read GID from /var/run/docker.sock
    if os.path.exists("/var/run/docker.sock"):
        try:
            return os.stat("/var/run/docker.sock").st_gid
        except Exception:
            pass
            
    # Try grp lookup
    try:
        import grp
        return grp.getgrnam("docker").gr_gid
    except Exception:
        pass
        
    # Fallback default GID
    return 956

def read_current_domain():
    if os.path.exists(".env"):
        with open(".env", "r") as f:
            for line in f:
                if line.startswith("DOMAIN="):
                    return line.strip().split("=", 1)[1]
    return "panel.example.com"

def write_env_files(domain):
    print("\nWriting environment files...")
    
    # Get values
    workspace_path = os.path.abspath(os.path.dirname(__file__))
    docker_gid = detect_docker_gid()
    
    try:
        uid = os.getuid()
        gid = os.getgid()
    except AttributeError:
        uid = 1000
        gid = 1000
        
    # 1. Root .env
    with open(".env", "w") as f:
        f.write("# Auto-generated by manage.py\n")
        f.write(f"DOCKER_GID={docker_gid}\n")
        f.write(f"HOST_WORKSPACE_PATH={workspace_path}\n")
        f.write(f"DOMAIN={domain}\n")
    print("[+] Wrote root .env")
    
    # 2. Server environments
    for env_dir in [os.path.join("Server", "Development"), os.path.join("Server", "Production")]:
        os.makedirs(os.path.join(env_dir, "Server"), exist_ok=True)
        os.makedirs(os.path.join(env_dir, "Data"), exist_ok=True)
        env_file_path = os.path.join(env_dir, ".env")
        archive_url = read_env_value(env_file_path, "VINTAGESTORY_ARCHIVE_URL", DEFAULT_SERVER_ARCHIVE_URL)
        with open(env_file_path, "w") as f:
            f.write("# Auto-generated by manage.py\n")
            f.write(f"SERVER_UID={uid}\n")
            f.write(f"SERVER_GID={gid}\n")
            f.write(f"VINTAGESTORY_ARCHIVE_URL={archive_url}\n")
        print(f"[+] Wrote environment to {env_file_path}")

def setup_server_permissions():
    print("\nSetting up server folder permissions for local operations...")
    
    # Paths that need to be writable by group 1654 (web panel container user)
    writable_dirs = [
        os.path.join("Server", "Development", "Data"),
        os.path.join("Server", "Production", "Data")
    ]
    
    for path in writable_dirs:
        if os.path.exists(path):
            print(f"[~] Configuring permissions for writable directory: {path}")
            
            # 1. Make group-writable (recursively if files exist)
            try:
                for root, dirs, files in os.walk(path):
                    for d in dirs:
                        os.chmod(os.path.join(root, d), 0o775)
                    for f in files:
                        os.chmod(os.path.join(root, f), 0o664)
                os.chmod(path, 0o775)
            except Exception as e:
                print(f"[-] Warning: Failed to chmod {path}: {e}")
                
            # 2. Set group to 1654
            try:
                os.chown(path, -1, CONTAINER_GID)
                for root, dirs, files in os.walk(path):
                    for d in dirs:
                        os.chown(os.path.join(root, d), -1, CONTAINER_GID)
                    for f in files:
                        os.chown(os.path.join(root, f), -1, CONTAINER_GID)
                print(f"[+] Set group ownership to {CONTAINER_GID} for {path}")
            except PermissionError:
                print(f"[~] Permission denied setting group ownership of {path} to {CONTAINER_GID}.")
                print("    Attempting to adjust GID using 'sudo chgrp'...")
                try:
                    subprocess.run(["sudo", "chgrp", "-R", str(CONTAINER_GID), path], check=True)
                    subprocess.run(["sudo", "chmod", "-R", "g+w", path], check=True)
                    print(f"[+] Successfully set group ownership and write permissions for {path} via sudo.")
                except Exception as e:
                    print(f"[-] Warning: Failed to set group ownership/permissions for {path}: {e}")
                    print(f"    Please execute manually if the panel file manager has permission issues:")
                    print(f"    sudo chgrp -R {CONTAINER_GID} {path}")
                    print(f"    sudo chmod -R g+w {path}")

def cmd_setup(args):
    check_prerequisites()
    
    domain = args.domain
    if not domain:
        current_domain = read_current_domain()
        if args.interactive:
            inp = input(f"Enter the public domain for production (default: {current_domain}): ").strip()
            domain = inp if inp else current_domain
        else:
            domain = current_domain
            
    setup_secrets()
    write_env_files(domain)
    setup_server_permissions()
    print("\n[+] Setup completed successfully!")
    print("    - To run development:  python3 manage.py dev")
    print("    - To run production:   python3 manage.py prod")


def cmd_dev(args):
    print("Starting development environment...")
    subprocess.run(["docker", "compose", "up", "-d", "--build"], check=True)

def cmd_prod(args):
    print("Starting production environment...")
    subprocess.run(["docker", "compose", "-f", "compose.production.yaml", "up", "-d", "--build"], check=True)


def cmd_install_restart_timer(args):
    if not sys.platform.startswith("linux"):
        raise RuntimeError("The restart timer can only be installed on a systemd-based Linux host.")

    systemctl = shutil.which("systemctl")
    if systemctl is None:
        raise RuntimeError("systemctl was not found; this command requires a systemd-based Linux host.")

    workspace_path = Path(__file__).resolve().parent
    production_path = workspace_path / "Server" / "Production"
    ops_path = production_path / "ops"
    service_template_path = ops_path / RESTART_SERVICE_NAME
    timer_source_path = ops_path / RESTART_TIMER_NAME
    restart_script_path = ops_path / "scheduled-restart.sh"

    for required_path in (service_template_path, timer_source_path, restart_script_path):
        if not required_path.is_file():
            raise RuntimeError(f"Required restart-timer file is missing: {required_path}")

    current_user = pwd.getpwuid(os.getuid()).pw_name
    current_group = grp.getgrgid(os.getgid()).gr_name
    service_contents = service_template_path.read_text(encoding="utf-8")
    service_contents = service_contents.replace("@SERVICE_USER@", current_user)
    service_contents = service_contents.replace("@SERVICE_GROUP@", current_group)
    service_contents = service_contents.replace("@PRODUCTION_DIRECTORY@", str(production_path))
    service_contents = service_contents.replace(
        "@RESTART_SCRIPT@", systemd_exec_argument(str(restart_script_path)))

    privilege_prefix = [] if os.geteuid() == 0 else ["sudo"]
    if privilege_prefix and shutil.which("sudo") is None:
        raise RuntimeError("sudo was not found; run this command as root to install systemd units.")

    print("Installing the production Vintage Story restart timer...")
    with tempfile.TemporaryDirectory(prefix="alegacy-restart-timer-") as temp_dir:
        generated_service_path = Path(temp_dir) / RESTART_SERVICE_NAME
        generated_service_path.write_text(service_contents, encoding="utf-8")

        for source_path, unit_name in (
            (generated_service_path, RESTART_SERVICE_NAME),
            (timer_source_path, RESTART_TIMER_NAME),
        ):
            subprocess.run(
                privilege_prefix
                + ["install", "-m", "0644", str(source_path), f"/etc/systemd/system/{unit_name}"],
                check=True,
            )

    subprocess.run(privilege_prefix + [systemctl, "daemon-reload"], check=True)
    subprocess.run(privilege_prefix + [systemctl, "enable", "--now", RESTART_TIMER_NAME], check=True)
    print("[+] Production midnight restart timer installed and enabled.")

def cmd_stop(args):
    print("Stopping all containers...")
    subprocess.run(["docker", "compose", "down"], check=False)
    subprocess.run(["docker", "compose", "-f", "compose.production.yaml", "down"], check=False)

def cmd_status(args):
    print("=== Development Stack ===")
    subprocess.run(["docker", "compose", "ps"], check=False)
    print("\n=== Production Stack ===")
    subprocess.run(["docker", "compose", "-f", "compose.production.yaml", "ps"], check=False)

def cmd_test(args):
    print("Running integration tests...")
    subprocess.run(["docker", "compose", "--profile", "test", "run", "--rm", "--build", "tests"], check=True)

def main():
    parser = argparse.ArgumentParser(description="AlegacyWebPanel Management CLI")
    subparsers = parser.add_subparsers(dest="command", required=True)
    
    # setup
    parser_setup = subparsers.add_parser("setup", help="Initialize configuration, secrets, and environments")
    parser_setup.add_argument("--domain", help="Public DNS name for production Caddy gateway")
    parser_setup.add_argument("--non-interactive", dest="interactive", action="store_false", help="Disable interactive prompts")
    parser_setup.set_defaults(func=cmd_setup)
    
    # dev
    parser_dev = subparsers.add_parser("dev", help="Start development stack")
    parser_dev.set_defaults(func=cmd_dev)
    
    # prod
    parser_prod = subparsers.add_parser("prod", help="Start production stack")
    parser_prod.set_defaults(func=cmd_prod)

    # install-restart-timer
    parser_restart_timer = subparsers.add_parser(
        "install-restart-timer",
        help="Install and enable the production Vintage Story midnight restart timer",
    )
    parser_restart_timer.set_defaults(func=cmd_install_restart_timer)
    
    # stop
    parser_stop = subparsers.add_parser("stop", help="Stop all stacks")
    parser_stop.set_defaults(func=cmd_stop)
    
    # status
    parser_status = subparsers.add_parser("status", help="Show stacks running status")
    parser_status.set_defaults(func=cmd_status)
    
    # test
    parser_test = subparsers.add_parser("test", help="Run full integration test suite")
    parser_test.set_defaults(func=cmd_test)
    
    args = parser.parse_args()
    args.func(args)

if __name__ == "__main__":
    main()
