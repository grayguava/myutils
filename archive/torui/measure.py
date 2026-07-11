"""
measure.py
==========

Active network measurements through a Tor SOCKS5 listener.

Responsibilities:
- Connect through Tor's SOCKS5 proxy.
- Perform remote hostname resolution through SOCKS5.
- Measure HTTPS application-level latency.
- Maintain a live in-memory measurement snapshot.

Does NOT:
- Connect to Tor ControlPort.
- Inspect circuits.
- Read Tor configuration.
- Render UI.
- Persist telemetry.
"""

from __future__ import annotations

import socket
import ssl
import struct
import threading
import time
from dataclasses import dataclass, replace
from typing import Optional


SOCKS_HOST = "127.0.0.1"
SOCKS_PORT = 9050

PROBE_HOST = "check.torproject.org"
PROBE_PORT = 443
PROBE_PATH = "/"

PROBE_INTERVAL = 10.0
PROBE_TIMEOUT = 15.0


@dataclass(frozen=True)
class MeasurementState:
    running: bool

    latency_ms: Optional[float]
    last_probe_ok: Optional[bool]
    last_probe_at: Optional[float]

    consecutive_failures: int
    total_probes: int
    successful_probes: int
    failed_probes: int

    error: Optional[str]

    @property
    def seconds_since_probe(self) -> Optional[float]:
        if self.last_probe_at is None:
            return None

        return max(
            0.0,
            time.time() - self.last_probe_at,
        )


class TorMeasurements:
    def __init__(
        self,
        socks_host: str = SOCKS_HOST,
        socks_port: int = SOCKS_PORT,
        probe_host: str = PROBE_HOST,
        probe_port: int = PROBE_PORT,
        probe_path: str = PROBE_PATH,
        interval: float = PROBE_INTERVAL,
        timeout: float = PROBE_TIMEOUT,
    ) -> None:
        self._socks_host = socks_host
        self._socks_port = socks_port

        self._probe_host = probe_host
        self._probe_port = probe_port
        self._probe_path = probe_path

        self._interval = interval
        self._timeout = timeout

        self._lock = threading.RLock()
        self._stop_event = threading.Event()
        self._wake_event = threading.Event()

        self._thread: Optional[threading.Thread] = None

        self._state = MeasurementState(
            running=False,
            latency_ms=None,
            last_probe_ok=None,
            last_probe_at=None,
            consecutive_failures=0,
            total_probes=0,
            successful_probes=0,
            failed_probes=0,
            error=None,
        )

    # ------------------------------------------------------------------
    # Public API
    # ------------------------------------------------------------------

    def start(self) -> None:
        if self._thread and self._thread.is_alive():
            return

        self._stop_event.clear()
        self._wake_event.clear()

        with self._lock:
            self._state = replace(
                self._state,
                running=True,
            )

        self._thread = threading.Thread(
            target=self._run,
            name="TorMeasurements",
            daemon=True,
        )

        self._thread.start()

    def stop(self) -> None:
        self._stop_event.set()
        self._wake_event.set()

        if self._thread:
            self._thread.join(timeout=5)

        with self._lock:
            self._state = replace(
                self._state,
                running=False,
            )

    def probe_now(self) -> None:
        self._wake_event.set()

    def snapshot(self) -> MeasurementState:
        with self._lock:
            return replace(self._state)

    # ------------------------------------------------------------------
    # Worker
    # ------------------------------------------------------------------

    def _run(self) -> None:
        self._probe()

        while not self._stop_event.is_set():
            self._wake_event.wait(self._interval)
            self._wake_event.clear()

            if self._stop_event.is_set():
                break

            self._probe()

    def _probe(self) -> None:
        started = time.perf_counter()

        try:
            sock = self._open_socks_connection(
                self._probe_host,
                self._probe_port,
            )

            context = ssl.create_default_context()

            with context.wrap_socket(
                sock,
                server_hostname=self._probe_host,
            ) as tls:
                request = (
                    f"HEAD {self._probe_path} HTTP/1.1\r\n"
                    f"Host: {self._probe_host}\r\n"
                    "User-Agent: tor-live-ui/1\r\n"
                    "Connection: close\r\n"
                    "\r\n"
                )

                tls.sendall(request.encode("ascii"))

                first_byte = tls.recv(1)

                if not first_byte:
                    raise ConnectionError(
                        "probe returned no response"
                    )

            latency_ms = (
                time.perf_counter() - started
            ) * 1000.0

            with self._lock:
                self._state = replace(
                    self._state,
                    latency_ms=round(latency_ms, 1),
                    last_probe_ok=True,
                    last_probe_at=time.time(),
                    consecutive_failures=0,
                    total_probes=self._state.total_probes + 1,
                    successful_probes=(
                        self._state.successful_probes + 1
                    ),
                    error=None,
                )

        except Exception as exc:
            with self._lock:
                self._state = replace(
                    self._state,
                    latency_ms=None,
                    last_probe_ok=False,
                    last_probe_at=time.time(),
                    consecutive_failures=(
                        self._state.consecutive_failures + 1
                    ),
                    total_probes=self._state.total_probes + 1,
                    failed_probes=(
                        self._state.failed_probes + 1
                    ),
                    error=str(exc),
                )

    # ------------------------------------------------------------------
    # SOCKS5
    # ------------------------------------------------------------------

    def _open_socks_connection(
        self,
        hostname: str,
        port: int,
    ) -> socket.socket:
        sock = socket.create_connection(
            (self._socks_host, self._socks_port),
            timeout=self._timeout,
        )

        sock.settimeout(self._timeout)

        try:
            self._socks_negotiate(sock)
            self._socks_connect(sock, hostname, port)
            return sock

        except Exception:
            sock.close()
            raise

    @staticmethod
    def _socks_negotiate(sock: socket.socket) -> None:
        # SOCKS5
        # 1 authentication method
        # 0x00 = no authentication

        sock.sendall(b"\x05\x01\x00")

        response = TorMeasurements._recv_exact(sock, 2)

        if response[0] != 0x05:
            raise ConnectionError(
                "invalid SOCKS version"
            )

        if response[1] == 0xFF:
            raise ConnectionError(
                "SOCKS proxy rejected authentication methods"
            )

        if response[1] != 0x00:
            raise ConnectionError(
                f"unsupported SOCKS authentication method "
                f"0x{response[1]:02x}"
            )

    @staticmethod
    def _socks_connect(
        sock: socket.socket,
        hostname: str,
        port: int,
    ) -> None:
        encoded_host = hostname.encode("idna")

        if len(encoded_host) > 255:
            raise ValueError(
                "SOCKS hostname exceeds 255 bytes"
            )

        request = (
            b"\x05"              # SOCKS5
            b"\x01"              # CONNECT
            b"\x00"              # reserved
            b"\x03"              # domain name
            + bytes([len(encoded_host)])
            + encoded_host
            + struct.pack("!H", port)
        )

        sock.sendall(request)

        header = TorMeasurements._recv_exact(sock, 4)

        version, result, _, address_type = header

        if version != 0x05:
            raise ConnectionError(
                "invalid SOCKS response version"
            )

        if result != 0x00:
            raise ConnectionError(
                TorMeasurements._socks_error(result)
            )

        if address_type == 0x01:
            TorMeasurements._recv_exact(sock, 4)

        elif address_type == 0x03:
            length = TorMeasurements._recv_exact(
                sock,
                1,
            )[0]

            TorMeasurements._recv_exact(
                sock,
                length,
            )

        elif address_type == 0x04:
            TorMeasurements._recv_exact(sock, 16)

        else:
            raise ConnectionError(
                f"unknown SOCKS address type "
                f"0x{address_type:02x}"
            )

        TorMeasurements._recv_exact(sock, 2)

    @staticmethod
    def _recv_exact(
        sock: socket.socket,
        length: int,
    ) -> bytes:
        data = bytearray()

        while len(data) < length:
            chunk = sock.recv(length - len(data))

            if not chunk:
                raise ConnectionError(
                    "SOCKS connection closed unexpectedly"
                )

            data.extend(chunk)

        return bytes(data)

    @staticmethod
    def _socks_error(code: int) -> str:
        messages = {
            0x01: "general SOCKS failure",
            0x02: "SOCKS connection not allowed",
            0x03: "SOCKS network unreachable",
            0x04: "SOCKS host unreachable",
            0x05: "SOCKS connection refused",
            0x06: "SOCKS TTL expired",
            0x07: "SOCKS command unsupported",
            0x08: "SOCKS address type unsupported",
        }

        return messages.get(
            code,
            f"SOCKS failure 0x{code:02x}",
        )