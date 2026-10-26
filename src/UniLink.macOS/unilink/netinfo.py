"""Network context for macOS: Wi-Fi SSID, interface IP, DNS, gateway, and portal reachability."""

from __future__ import annotations

import ipaddress
import re
import socket
import subprocess
from dataclasses import dataclass, field
from typing import Any, Dict, List, Optional, Tuple

from .client import DEFAULT_PORTAL, check_internet, split_host_port

FAKE_IP_NETS = (
    ipaddress.ip_network("198.18.0.0/15"),
    ipaddress.ip_network("240.0.0.0/4"),
    ipaddress.ip_network("100.64.0.0/10"),
)


def _run(cmd: List[str], timeout: float = 4.0) -> str:
    try:
        res = subprocess.run(cmd, capture_output=True, text=True, timeout=timeout)
        return res.stdout
    except Exception:
        return ""


def wifi_ssid(interface: str = "en0") -> str:
    for args in (["ipconfig", "getsummary", interface], ["networksetup", "-getairportnetwork", interface]):
        out = _run(args)
        m = re.search(r"^\s*SSID\s*:\s*(.+)$", out, re.M) or re.search(r"Current Wi-Fi Network:\s*(.+)", out)
        if m and "<redacted>" not in m.group(1):
            return m.group(1).strip()
    return ""


def local_ipv4(interface: str = "en0") -> str:
    ip = _run(["ipconfig", "getifaddr", interface]).strip()
    if ip:
        return ip
    try:
        s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        s.connect(("1.1.1.1", 80))
        ip = s.getsockname()[0]
        s.close()
        return ip
    except OSError:
        return ""


def default_gateway() -> str:
    out = _run(["route", "-n", "get", "default"])
    m = re.search(r"gateway:\s*(\S+)", out)
    return m.group(1) if m else ""


def dns_servers() -> List[str]:
    out = _run(["scutil", "--dns"])
    return re.findall(r"nameserver\[\d+\]\s*:\s*(\S+)", out)


def is_fake_ip(ip: str) -> bool:
    try:
        addr = ipaddress.ip_address(ip)
    except ValueError:
        return False
    return any(addr in net for net in FAKE_IP_NETS)


def resolve(host: str) -> List[str]:
    try:
        infos = socket.getaddrinfo(host, None, socket.AF_INET)
        return sorted({str(i[4][0]) for i in infos})
    except OSError:
        return []


def portal_reachable(portal_url: str, timeout: float = 2.0) -> Tuple[bool, str]:
    """Test TCP reachability of portal, ignoring VPN fake-IP addresses."""
    host, port = split_host_port(portal_url)
    ips = resolve(host)
    real_ips = [ip for ip in ips if not is_fake_ip(ip)]
    if ips and not real_ips:
        return False, f"fake-ip ({ips[0]}) - VPN is intercepting DNS"

    for ip in (real_ips or [host]):
        try:
            with socket.create_connection((ip, port), timeout=timeout):
                return True, ip
        except OSError:
            continue
    return False, "unreachable"


@dataclass
class NetContext:
    ssid: str = ""
    ip: str = ""
    gateway: str = ""
    dns: List[str] = field(default_factory=list)
    online: bool = False
    portal_reachable: bool = False
    campus: bool = False
    reasons: List[str] = field(default_factory=list)
    portal: str = ""

    def summary(self) -> str:
        bits = [f"wifi={self.ssid or '—'}"]
        if self.ip:
            bits.append(f"ip={self.ip}")
        if self.gateway:
            bits.append(f"gw={self.gateway}")
        bits.append("online" if self.online else "offline")
        bits.append("campus" if self.campus else "not-campus")
        return " ".join(bits)


def _match(matcher: Dict[str, Any], ctx: NetContext) -> bool:
    m_type = matcher.get("type", "")
    val = matcher.get("value", "")
    if m_type == "ssid" and ctx.ssid:
        return ctx.ssid.lower() == str(val).lower()
    if m_type == "ssid_re" and ctx.ssid:
        try:
            return bool(re.search(str(val), ctx.ssid))
        except re.error:
            return False
    if m_type == "dns":
        return any(d.startswith(str(val)) for d in ctx.dns)
    if m_type == "probe":
        host, port = split_host_port(str(val))
        try:
            with socket.create_connection((host, port), timeout=1.5):
                return True
        except OSError:
            return False
    return False


def inspect(matchers: List[Dict[str, Any]], portal_url: str = DEFAULT_PORTAL, quick: bool = False) -> NetContext:
    """Inspect local networking state and check whether current environment is campus."""
    ctx = NetContext(portal=portal_url)
    ctx.ssid = wifi_ssid()
    ctx.ip = local_ipv4()
    ctx.gateway = default_gateway()
    ctx.dns = dns_servers()

    matched = []
    for m in matchers:
        if _match(m, ctx):
            matched.append(f"{m.get('type')}={m.get('value')}")

    ctx.campus = bool(matched)
    ctx.reasons = matched

    if not quick:
        reachable, _ = portal_reachable(portal_url)
        ctx.portal_reachable = reachable
        online, _ = check_internet(timeout=2.0)
        ctx.online = online
        if ctx.portal_reachable and "portal_reachable" not in ctx.reasons:
            ctx.campus = True
            ctx.reasons.append("portal_reachable")

    return ctx
