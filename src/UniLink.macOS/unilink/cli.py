"""UniLink macOS CLI - testing, configuration, and headless mode."""

from __future__ import annotations

import argparse
import getpass
import json
import sys
import time

from . import __version__
from .client import DEFAULT_PORTAL, PortalClient, check_internet
from .config import ConfigStore
from .watcher import Watcher


def _get_client(args: argparse.Namespace, store: ConfigStore) -> PortalClient:
    acc = None
    if getattr(args, "account", ""):
        for a in store.accounts:
            if a.label == args.account or a.username == args.account or a.id == args.account:
                acc = a
                break
    else:
        acc = store.active_account()

    portal = getattr(args, "portal", "") or store.get("portal_url", DEFAULT_PORTAL)
    username = getattr(args, "username", "") or (acc.username if acc else "")
    password = getattr(args, "password", "") or (acc.password if acc else "")
    return PortalClient(portal, username, password, logger=lambda m: print(f"  · {m}", file=sys.stderr))


def cmd_probe(args: argparse.Namespace, store: ConfigStore) -> int:
    client = _get_client(args, store)
    resp, is_cyberoam = client.probe()
    print(f"Portal:    {client.portal_url}")
    print(f"HTTP:      {resp.status} {resp.error or 'OK'}")
    print(f"Type:      {'Cyberoam / Sophos' if is_cyberoam else 'Other / Unknown'}")
    online, why = check_internet()
    print(f"Internet:  {'Online (' + why + ')' if online else 'Offline'}")
    return 0


def cmd_status(args: argparse.Namespace, store: ConfigStore) -> int:
    from . import netinfo
    portal = store.get("portal_url", DEFAULT_PORTAL)
    ctx = netinfo.inspect(store.get("campus_matchers", []), portal)
    print("UniLink Network Status:")
    print(f"  Wi-Fi SSID:   {ctx.ssid or '—'}")
    print(f"  Local IP:     {ctx.ip or '—'}")
    print(f"  Gateway:      {ctx.gateway or '—'}")
    print(f"  Campus Net:   {'Yes' if ctx.campus else 'No'} ({', '.join(ctx.reasons) or 'no match'})")
    print(f"  Portal:       {'Reachable' if ctx.portal_reachable else 'Unreachable'}")
    print(f"  Internet:     {'Online' if ctx.online else 'Offline'}")

    act = store.active_account()
    print(f"  Active User:  {act.label + ' (' + act.username + ')' if act else 'None'}")
    return 0


def cmd_login(args: argparse.Namespace, store: ConfigStore) -> int:
    client = _get_client(args, store)
    if not client.username or not client.password:
        print("Error: No username or password specified.", file=sys.stderr)
        return 1
    reply = client.login(verify_internet=True)
    symbol = "✔" if reply.kind.value == "Online" else "✘"
    print(f"{symbol} [{reply.kind.value}] {reply.message}")
    return 0 if reply.kind.value == "Online" else 1


def cmd_logout(args: argparse.Namespace, store: ConfigStore) -> int:
    client = _get_client(args, store)
    reply = client.logout()
    print(f"✔ {reply.message or 'Logout complete'}")
    return 0


def cmd_accounts(args: argparse.Namespace, store: ConfigStore) -> int:
    action = args.subaction or "list"
    if action == "list":
        accs = store.accounts
        act = store.active_account()
        if not accs:
            print("No accounts configured. Use 'unilink accounts add' to add one.")
            return 0
        print(f"{'':<3} {'Label':<18} {'Username':<18} {'Status':<10}")
        print("-" * 52)
        for a in accs:
            mark = "✓" if act and a.id == act.id else " "
            status = "Enabled" if a.enabled else "Disabled"
            if a.exhausted_month:
                status += f" (Quota: {a.exhausted_month})"
            print(f" {mark} {a.label:<18} {a.username:<18} {status:<10}")
        return 0

    if action == "add":
        label = args.label or input("Account Label: ").strip()
        username = args.username or input("Student Username: ").strip()
        password = args.password or getpass.getpass("Password: ")
        acc = store.add_account(label, username, password)
        print(f"✔ Added account '{acc.label}' ({acc.username})")
        return 0

    if action == "use":
        target = args.target
        if not target:
            print("Error: Account label or username required.", file=sys.stderr)
            return 1
        for a in store.accounts:
            if a.label == target or a.username == target or a.id == target:
                store.set_active(a.id)
                print(f"✔ Active account set to '{a.label}'")
                return 0
        print(f"Error: Account '{target}' not found.", file=sys.stderr)
        return 1

    if action == "remove":
        target = args.target
        if not target:
            print("Error: Account label or username required.", file=sys.stderr)
            return 1
        found_id = None
        for a in store.accounts:
            if a.label == target or a.username == target or a.id == target:
                found_id = a.id
                break
        if found_id and store.remove_account(found_id):
            print(f"✔ Removed account '{target}'")
            return 0
        print(f"Error: Account '{target}' not found.", file=sys.stderr)
        return 1

    return 0


def cmd_watch(args: argparse.Namespace, store: ConfigStore) -> int:
    watcher = Watcher(
        store,
        on_event=lambda k, v: print(f"[{k}] {v['status']}: {v['message']}" if k == "state" else f"[{k}] {v}", flush=True)
    )
    watcher.start()
    print("UniLink watcher running... Press Ctrl+C to stop.")
    try:
        while True:
            time.sleep(1)
    except KeyboardInterrupt:
        watcher.stop()
    return 0


def cmd_gui(args: argparse.Namespace, store: ConfigStore) -> int:
    from .window import show_dashboard
    show_dashboard(store)
    return 0


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(prog="unilink", description=f"UniLink {__version__} (macOS Edition)")
    parser.add_argument("--version", action="version", version=f"%(prog)s {__version__}")
    subparsers = parser.add_subparsers(dest="command")

    # probe
    p_probe = subparsers.add_parser("probe", help="Probe captive portal status")
    p_probe.add_argument("--portal", help="Portal URL override")

    # status
    subparsers.add_parser("status", help="Show network & Wi-Fi context")

    # login
    p_login = subparsers.add_parser("login", help="Log into captive portal")
    p_login.add_argument("--account", help="Account name or username")
    p_login.add_argument("--portal", help="Portal URL override")

    # logout
    p_logout = subparsers.add_parser("logout", help="Log out from captive portal")
    p_logout.add_argument("--account", help="Account name or username")

    # accounts
    p_acc = subparsers.add_parser("accounts", help="Manage student accounts")
    p_acc.add_argument("subaction", nargs="?", default="list", choices=["list", "add", "use", "remove"])
    p_acc.add_argument("--label", help="Account label")
    p_acc.add_argument("--username", help="Student ID / username")
    p_acc.add_argument("--password", help="Password")
    p_acc.add_argument("target", nargs="?", help="Account to use/remove")

    # watch
    subparsers.add_parser("watch", help="Run background monitor daemon in console")

    # gui
    subparsers.add_parser("gui", help="Open Dashboard GUI")

    args = parser.parse_args(argv)
    if not args.command:
        parser.print_help()
        return 0

    store = ConfigStore()
    commands = {
        "probe": cmd_probe,
        "status": cmd_status,
        "login": cmd_login,
        "logout": cmd_logout,
        "accounts": cmd_accounts,
        "watch": cmd_watch,
        "gui": cmd_gui,
    }

    fn = commands.get(args.command)
    if fn:
        return fn(args, store)
    return 0


if __name__ == "__main__":
    sys.exit(main())
