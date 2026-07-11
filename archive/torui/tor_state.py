"""
tor.py
======

Read-only live state provider for a local Tor daemon.

Responsibilities:
- Connect to Tor ControlPort.
- Authenticate with Tor's control cookie.
- Subscribe to Tor control events.
- Maintain a coherent in-memory snapshot.
- Resolve relay metadata from Tor network-status documents.
- Resolve relay country codes using Tor's local GeoIP files.

Does NOT:
- Perform network probes.
- Write telemetry.
- Analyse historical data.
- Render UI.
"""

from __future__ import annotations

import ipaddress
import threading
import time
from dataclasses import dataclass, replace
from pathlib import Path
from typing import Optional

import stem
import stem.control
import stem.response.events


CONTROL_HOST = "127.0.0.1"
CONTROL_PORT = 9051

TOR_ROOT = Path(
    r"D:\Tools\tor-expert-bundle"
)

COOKIE_PATH = TOR_ROOT / "data" / "control_auth_cookie"
GEOIP_PATH = TOR_ROOT / "data" / "geoip"
GEOIP6_PATH = TOR_ROOT / "data" / "geoip6"

RECONNECT_DELAY = 3.0


# ---------------------------------------------------------------------------
# Public data models
# ---------------------------------------------------------------------------


@dataclass(frozen=True)
class Relay:
    fingerprint: str
    nickname: Optional[str]
    ip: Optional[str]
    country_code: Optional[str]
    or_port: Optional[int]
    dir_port: Optional[int]
    flags: tuple[str, ...]


@dataclass(frozen=True)
class Circuit:
    id: str
    status: str
    purpose: Optional[str]
    build_flags: tuple[str, ...]
    path: tuple[Relay, ...]
    build_time_ms: Optional[float]

    @property
    def guard(self) -> Optional[Relay]:
        return self.path[0] if self.path else None

    @property
    def middle(self) -> Optional[Relay]:
        return self.path[1] if len(self.path) >= 2 else None

    @property
    def exit(self) -> Optional[Relay]:
        return self.path[-1] if len(self.path) >= 3 else None


@dataclass(frozen=True)
class Stream:
    id: str
    status: str
    target: str
    circuit_id: Optional[str]
    purpose: Optional[str]
    reason: Optional[str]


@dataclass(frozen=True)
class ORConnection:
    endpoint: str
    fingerprint: Optional[str]
    nickname: Optional[str]
    status: str
    reason: Optional[str]


@dataclass(frozen=True)
class TorMessage:
    level: str
    message: str
    timestamp: float


@dataclass(frozen=True)
class TorState:
    control_connected: bool
    version: Optional[str]

    bootstrap_percent: int
    bootstrap_tag: Optional[str]
    bootstrap_summary: Optional[str]

    network_liveness: Optional[str]

    socks_listeners: tuple[str, ...]
    dns_listeners: tuple[str, ...]
    control_listeners: tuple[str, ...]

    bytes_read: int
    bytes_written: int

    circuits: tuple[Circuit, ...]
    streams: tuple[Stream, ...]
    or_connections: tuple[ORConnection, ...]

    messages: tuple[TorMessage, ...]

    started_at: Optional[float]
    last_update_at: Optional[float]
    error: Optional[str]

    @property
    def uptime_seconds(self) -> float:
        if self.started_at is None:
            return 0.0
        return max(0.0, time.monotonic() - self.started_at)

    @property
    def built_circuits(self) -> tuple[Circuit, ...]:
        return tuple(
            circuit
            for circuit in self.circuits
            if circuit.status == "BUILT"
        )

    @property
    def primary_circuit(self) -> Optional[Circuit]:
        general = [
            circuit
            for circuit in self.built_circuits
            if circuit.purpose == "GENERAL"
        ]

        if general:
            return general[0]

        circuits = self.built_circuits
        return circuits[0] if circuits else None

    @property
    def primary_guard(self) -> Optional[Relay]:
        circuit = self.primary_circuit
        return circuit.guard if circuit else None


# ---------------------------------------------------------------------------
# GeoIP
# ---------------------------------------------------------------------------


class GeoIPDatabase:
    def __init__(
        self,
        geoip_path: Path,
        geoip6_path: Path,
    ) -> None:
        self._ipv4: list[tuple[int, int, str]] = []
        self._ipv6: list[tuple[int, int, str]] = []

        self._load(geoip_path, self._ipv4)
        self._load(geoip6_path, self._ipv6)

    @staticmethod
    def _load(
        path: Path,
        target: list[tuple[int, int, str]],
    ) -> None:
        if not path.exists():
            return

        with path.open(
            "r",
            encoding="utf-8",
            errors="replace",
        ) as file:
            for raw_line in file:
                line = raw_line.strip()

                if not line or line.startswith("#"):
                    continue

                try:
                    range_part, country = line.rsplit(",", 1)
                    start_text, end_text = range_part.split("-", 1)

                    start = int(ipaddress.ip_address(start_text))
                    end = int(ipaddress.ip_address(end_text))

                    target.append(
                        (
                            start,
                            end,
                            country.strip().upper(),
                        )
                    )
                except Exception:
                    continue

        target.sort(key=lambda item: item[0])

    def country_code(self, address: Optional[str]) -> Optional[str]:
        if not address:
            return None

        try:
            ip = ipaddress.ip_address(address)
        except ValueError:
            return None

        database = self._ipv4 if ip.version == 4 else self._ipv6
        value = int(ip)

        left = 0
        right = len(database) - 1

        while left <= right:
            middle = (left + right) // 2
            start, end, country = database[middle]

            if value < start:
                right = middle - 1
            elif value > end:
                left = middle + 1
            else:
                return country

        return None


# ---------------------------------------------------------------------------
# Tor client
# ---------------------------------------------------------------------------


class TorClient:
    _EVENT_TYPES = (
        stem.control.EventType.CIRC,
        stem.control.EventType.STREAM,
        stem.control.EventType.ORCONN,
        stem.control.EventType.BW,
        stem.control.EventType.STATUS_CLIENT,
        stem.control.EventType.STATUS_GENERAL,
        stem.control.EventType.NOTICE,
        stem.control.EventType.WARN,
        stem.control.EventType.ERR,
    )

    def __init__(
        self,
        control_host: str = CONTROL_HOST,
        control_port: int = CONTROL_PORT,
        cookie_path: Path = COOKIE_PATH,
        geoip_path: Path = GEOIP_PATH,
        geoip6_path: Path = GEOIP6_PATH,
    ) -> None:
        self._control_host = control_host
        self._control_port = control_port
        self._cookie_path = Path(cookie_path)

        self._geoip = GeoIPDatabase(
            Path(geoip_path),
            Path(geoip6_path),
        )

        self._lock = threading.RLock()
        self._stop_event = threading.Event()
        self._thread: Optional[threading.Thread] = None
        self._controller: Optional[
            stem.control.Controller
        ] = None

        self._circuits: dict[str, Circuit] = {}
        self._streams: dict[str, Stream] = {}
        self._or_connections: dict[str, ORConnection] = {}
        self._messages: list[TorMessage] = []

        self._circuit_launch_times: dict[str, float] = {}

        self._state = TorState(
            control_connected=False,
            version=None,
            bootstrap_percent=0,
            bootstrap_tag=None,
            bootstrap_summary=None,
            network_liveness=None,
            socks_listeners=(),
            dns_listeners=(),
            control_listeners=(),
            bytes_read=0,
            bytes_written=0,
            circuits=(),
            streams=(),
            or_connections=(),
            messages=(),
            started_at=None,
            last_update_at=None,
            error=None,
        )

    # ------------------------------------------------------------------
    # Public API
    # ------------------------------------------------------------------

    def start(self) -> None:
        if self._thread and self._thread.is_alive():
            return

        self._stop_event.clear()

        self._thread = threading.Thread(
            target=self._run,
            name="TorClient",
            daemon=True,
        )

        self._thread.start()

    def stop(self) -> None:
        self._stop_event.set()

        controller = self._controller

        if controller is not None:
            try:
                controller.close()
            except Exception:
                pass

        if self._thread:
            self._thread.join(timeout=5)

    def snapshot(self) -> TorState:
        with self._lock:
            return replace(
                self._state,
                circuits=tuple(self._circuits.values()),
                streams=tuple(self._streams.values()),
                or_connections=tuple(
                    self._or_connections.values()
                ),
                messages=tuple(self._messages),
            )

    def request_new_identity(self) -> bool:
        controller = self._controller

        if controller is None:
            return False

        try:
            if not controller.is_newnym_available():
                return False

            controller.signal(stem.Signal.NEWNYM)
            return True
        except Exception:
            return False

    # ------------------------------------------------------------------
    # Connection lifecycle
    # ------------------------------------------------------------------

    def _run(self) -> None:
        while not self._stop_event.is_set():
            try:
                self._connect()
                self._wait_for_disconnect()
            except Exception as exc:
                self._set_disconnected(str(exc))

            if not self._stop_event.wait(RECONNECT_DELAY):
                continue

    def _connect(self) -> None:
        controller = stem.control.Controller.from_port(
            address=self._control_host,
            port=self._control_port,
        )

        with self._cookie_path.open("rb") as file:
            cookie = file.read()

        controller.authenticate(cookie)

        self._controller = controller

        self._load_initial_state(controller)

        controller.add_event_listener(
            self._on_event,
            *self._EVENT_TYPES,
        )

        with self._lock:
            self._state = replace(
                self._state,
                control_connected=True,
                error=None,
                last_update_at=time.time(),
            )

    def _wait_for_disconnect(self) -> None:
        controller = self._controller

        if controller is None:
            return

        while not self._stop_event.wait(1.0):
            try:
                if not controller.is_alive():
                    raise ConnectionError(
                        "Tor control connection closed"
                    )

                self._refresh_counters(controller)
            except Exception:
                raise

    def _set_disconnected(self, error: str) -> None:
        controller = self._controller
        self._controller = None

        if controller is not None:
            try:
                controller.close()
            except Exception:
                pass

        with self._lock:
            self._state = replace(
                self._state,
                control_connected=False,
                error=error,
                last_update_at=time.time(),
            )

    # ------------------------------------------------------------------
    # Initial state
    # ------------------------------------------------------------------

    def _load_initial_state(
        self,
        controller: stem.control.Controller,
    ) -> None:
        version = str(controller.get_version())

        bootstrap = controller.get_info(
            "status/bootstrap-phase",
            default="",
        )

        progress, tag, summary = self._parse_bootstrap(bootstrap)

        socks = self._get_listener_conf(
            controller,
            "SocksPort",
        )

        dns = self._get_listener_conf(
            controller,
            "DNSPort",
        )

        control = self._get_listener_conf(
            controller,
            "ControlPort",
        )

        circuits: dict[str, Circuit] = {}

        for circuit in controller.get_circuits(default=[]):
            parsed = self._parse_circuit(circuit)
            circuits[parsed.id] = parsed

        streams: dict[str, Stream] = {}

        for stream in controller.get_streams(default=[]):
            parsed = self._parse_stream(stream)
            streams[parsed.id] = parsed

        with self._lock:
            self._circuits = circuits
            self._streams = streams

            self._state = replace(
                self._state,
                version=version,
                bootstrap_percent=progress,
                bootstrap_tag=tag,
                bootstrap_summary=summary,
                socks_listeners=socks,
                dns_listeners=dns,
                control_listeners=control,
                started_at=time.monotonic(),
                last_update_at=time.time(),
                error=None,
            )

        self._refresh_counters(controller)

    @staticmethod
    def _get_listener_conf(
        controller: stem.control.Controller,
        key: str,
    ) -> tuple[str, ...]:
        try:
            values = controller.get_conf(
                key,
                multiple=True,
            )

            if not values:
                return ()

            return tuple(str(value) for value in values)
        except Exception:
            return ()

    # ------------------------------------------------------------------
    # Event dispatch
    # ------------------------------------------------------------------

    def _on_event(self, event) -> None:
        try:
            if isinstance(
                event,
                stem.response.events.CircuitEvent,
            ):
                self._handle_circuit(event)

            elif isinstance(
                event,
                stem.response.events.StreamEvent,
            ):
                self._handle_stream(event)

            elif isinstance(
                event,
                stem.response.events.ORConnEvent,
            ):
                self._handle_or_connection(event)

            elif isinstance(
                event,
                stem.response.events.StatusEvent,
            ):
                self._handle_status(event)

            elif isinstance(
                event,
                stem.response.events.LogEvent,
            ):
                self._handle_log(event)

            elif isinstance(
                event,
                stem.response.events.BandwidthEvent,
            ):
                self._handle_bandwidth(event)

        except Exception as exc:
            with self._lock:
                self._state = replace(
                    self._state,
                    error=f"event error: {exc}",
                    last_update_at=time.time(),
                )

    # ------------------------------------------------------------------
    # Circuit events
    # ------------------------------------------------------------------

    def _handle_circuit(self, event) -> None:
        circuit_id = str(event.id)
        status = self._enum_name(event.status)
        now = time.monotonic()

        build_time_ms = None

        with self._lock:
            if status == "LAUNCHED":
                self._circuit_launch_times[circuit_id] = now

            elif status == "BUILT":
                launched = self._circuit_launch_times.pop(
                    circuit_id,
                    None,
                )

                if launched is not None:
                    build_time_ms = (
                        now - launched
                    ) * 1000.0

            elif status in ("FAILED", "CLOSED"):
                self._circuit_launch_times.pop(
                    circuit_id,
                    None,
                )

        circuit = self._parse_circuit(
            event,
            build_time_ms,
        )

        with self._lock:
            if status == "CLOSED":
                self._circuits.pop(circuit_id, None)
            else:
                self._circuits[circuit_id] = circuit

            self._touch()

    def _parse_circuit(
        self,
        circuit,
        build_time_ms: Optional[float] = None,
    ) -> Circuit:
        path = tuple(
            self._relay_from_hop(hop)
            for hop in (circuit.path or [])
        )

        build_flags = tuple(
            self._enum_name(flag)
            for flag in (circuit.build_flags or [])
        )

        return Circuit(
            id=str(circuit.id),
            status=self._enum_name(circuit.status),
            purpose=self._optional_enum(circuit.purpose),
            build_flags=build_flags,
            path=path,
            build_time_ms=build_time_ms,
        )

    # ------------------------------------------------------------------
    # Stream events
    # ------------------------------------------------------------------

    def _handle_stream(self, event) -> None:
        stream = self._parse_stream(event)

        with self._lock:
            if stream.status in ("CLOSED", "FAILED"):
                self._streams.pop(stream.id, None)
            else:
                self._streams[stream.id] = stream

            self._touch()

    def _parse_stream(self, event) -> Stream:
        return Stream(
            id=str(event.id),
            status=self._enum_name(event.status),
            target=str(event.target or ""),
            circuit_id=(
                str(event.circ_id)
                if event.circ_id
                else None
            ),
            purpose=self._optional_enum(event.purpose),
            reason=self._optional_enum(event.reason),
        )

    # ------------------------------------------------------------------
    # OR connection events
    # ------------------------------------------------------------------

    def _handle_or_connection(self, event) -> None:
        endpoint = str(event.endpoint or "")

        connection = ORConnection(
            endpoint=endpoint,
            fingerprint=(
                str(event.endpoint_fingerprint)
                if event.endpoint_fingerprint
                else None
            ),
            nickname=(
                str(event.endpoint_nickname)
                if event.endpoint_nickname
                else None
            ),
            status=self._enum_name(event.status),
            reason=self._optional_enum(event.reason),
        )

        key = (
            connection.fingerprint
            or connection.endpoint
        )

        with self._lock:
            if connection.status in (
                "CLOSED",
                "FAILED",
            ):
                self._or_connections.pop(key, None)
            else:
                self._or_connections[key] = connection

            self._touch()

    # ------------------------------------------------------------------
    # Status events
    # ------------------------------------------------------------------

    def _handle_status(self, event) -> None:
        action = self._enum_name(event.action)

        if action == "BOOTSTRAP":
            raw = self._raw_event(event)
            progress, tag, summary = self._parse_bootstrap(raw)

            with self._lock:
                self._state = replace(
                    self._state,
                    bootstrap_percent=progress,
                    bootstrap_tag=tag,
                    bootstrap_summary=summary,
                )

                self._touch()

        elif action == "NETWORK_LIVENESS":
            arguments = str(
                getattr(event, "arguments", "") or ""
            )

            with self._lock:
                self._state = replace(
                    self._state,
                    network_liveness=arguments,
                )

                self._touch()

    # ------------------------------------------------------------------
    # Log events
    # ------------------------------------------------------------------

    def _handle_log(self, event) -> None:
        message = TorMessage(
            level=self._enum_name(event.runlevel),
            message=str(event.message or ""),
            timestamp=time.time(),
        )

        with self._lock:
            self._messages.append(message)

            if len(self._messages) > 100:
                del self._messages[:-100]

            self._touch()

    # ------------------------------------------------------------------
    # Bandwidth
    # ------------------------------------------------------------------

    def _handle_bandwidth(self, event) -> None:
        self._refresh_counters(self._controller)

    def _refresh_counters(
        self,
        controller: Optional[stem.control.Controller],
    ) -> None:
        if controller is None:
            return

        try:
            read = int(
                controller.get_info(
                    "traffic/read",
                    default="0",
                )
            )

            written = int(
                controller.get_info(
                    "traffic/written",
                    default="0",
                )
            )

            with self._lock:
                self._state = replace(
                    self._state,
                    bytes_read=read,
                    bytes_written=written,
                )

                self._touch()

        except Exception:
            pass

    # ------------------------------------------------------------------
    # Relay metadata
    # ------------------------------------------------------------------

    def _relay_from_hop(self, hop) -> Relay:
        if isinstance(hop, (tuple, list)):
            fingerprint = str(hop[0]).lstrip("$")
            nickname = (
                str(hop[1])
                if len(hop) > 1 and hop[1]
                else None
            )
        else:
            fingerprint = str(hop).lstrip("$")
            nickname = None

        controller = self._controller

        if controller is None:
            return Relay(
                fingerprint=fingerprint,
                nickname=nickname,
                ip=None,
                country_code=None,
                or_port=None,
                dir_port=None,
                flags=(),
            )

        try:
            network_status = controller.get_network_status(
                fingerprint,
                default=None,
            )
        except Exception:
            network_status = None

        if network_status is None:
            return Relay(
                fingerprint=fingerprint,
                nickname=nickname,
                ip=None,
                country_code=None,
                or_port=None,
                dir_port=None,
                flags=(),
            )

        address = str(network_status.address)

        flags = tuple(
            self._enum_name(flag)
            for flag in (network_status.flags or [])
        )

        return Relay(
            fingerprint=fingerprint,
            nickname=(
                str(network_status.nickname)
                if network_status.nickname
                else nickname
            ),
            ip=address,
            country_code=self._geoip.country_code(address),
            or_port=getattr(
                network_status,
                "or_port",
                None,
            ),
            dir_port=getattr(
                network_status,
                "dir_port",
                None,
            ),
            flags=flags,
        )

    # ------------------------------------------------------------------
    # Helpers
    # ------------------------------------------------------------------

    def _touch(self) -> None:
        self._state = replace(
            self._state,
            last_update_at=time.time(),
        )

    @staticmethod
    def _enum_name(value) -> str:
        if value is None:
            return ""

        text = str(value)

        if "." in text:
            text = text.rsplit(".", 1)[-1]

        return text.upper()

    @classmethod
    def _optional_enum(cls, value) -> Optional[str]:
        text = cls._enum_name(value)
        return text or None

    @staticmethod
    def _raw_event(event) -> str:
        try:
            raw = event.raw_content
            return raw() if callable(raw) else str(raw)
        except Exception:
            return str(event)

    @staticmethod
    def _parse_bootstrap(
        text: str,
    ) -> tuple[int, Optional[str], Optional[str]]:
        import re

        progress_match = re.search(
            r"PROGRESS=(\d+)",
            text,
        )

        tag_match = re.search(
            r"TAG=([^\s]+)",
            text,
        )

        summary_match = re.search(
            r'SUMMARY="([^"]*)"',
            text,
        )

        progress = (
            int(progress_match.group(1))
            if progress_match
            else 0
        )

        tag = (
            tag_match.group(1)
            if tag_match
            else None
        )

        summary = (
            summary_match.group(1)
            if summary_match
            else None
        )

        return progress, tag, summary