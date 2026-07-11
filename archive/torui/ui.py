"""
ui.py
=====

Live terminal dashboard for Tor state and measurement providers.
Scrollable, keyboard-navigable, presentation-only.
"""

from __future__ import annotations

import sys
import time
import threading
from dataclasses import dataclass

from rich.console import Console, Group
from rich.live import Live
from rich.panel import Panel
from rich.progress import Progress, BarColumn, TextColumn, TaskProgressColumn, SpinnerColumn
from rich.table import Table
from rich.text import Text
from rich.style import Style
from rich.segment import Segment
from rich import box

from measure import MeasurementState, TorMeasurements
from tor_state import Circuit, Relay, TorClient, TorState


REFRESH_RATE = 4
REFRESH_INTERVAL = 1.0 / REFRESH_RATE
MIN_VISIBLE_HEIGHT = 8

console = Console()


# ---------------------------------------------------------------------------
# Scroll state
# ---------------------------------------------------------------------------


@dataclass
class ScrollState:
    offset: int = 0
    at_bottom: bool = True
    quit: bool = False
    visible_height: int = 0
    total_lines: int = 0

    @property
    def max_offset(self) -> int:
        return max(0, self.total_lines - self.visible_height)

    def clamp(self) -> None:
        if self.at_bottom:
            self.offset = self.max_offset
        else:
            self.offset = max(0, min(self.offset, self.max_offset))


# ---------------------------------------------------------------------------
# Keyboard input (platform-specific)
# ---------------------------------------------------------------------------


def _start_input_listener(scroll: ScrollState) -> threading.Thread:
    if sys.platform == "win32":
        target = _listen_win32
    else:
        target = _listen_unix

    t = threading.Thread(target=target, args=(scroll,), daemon=True, name="kbd")
    t.start()
    return t


def _listen_win32(scroll: ScrollState) -> None:
    import msvcrt

    while not scroll.quit:
        try:
            if not msvcrt.kbhit():
                time.sleep(0.02)
                continue
            ch = msvcrt.getch()
        except (OSError, AttributeError):
            break

        if ch in (b"q", b"Q"):
            scroll.quit = True
            break
        if ch == b"\r":
            scroll.at_bottom = True
            continue
        if ch != b"\xe0":
            continue

        try:
            ch2 = msvcrt.getch()
        except (OSError, AttributeError):
            break

        if ch2 == b"H":  # Up
            if scroll.offset > 0:
                scroll.offset -= 1
                scroll.at_bottom = False
        elif ch2 == b"P":  # Down
            if scroll.offset < scroll.max_offset:
                scroll.offset += 1
                scroll.at_bottom = scroll.offset >= scroll.max_offset
        elif ch2 == b"I":  # Page Up
            scroll.offset = max(0, scroll.offset - scroll.visible_height)
            scroll.at_bottom = False
        elif ch2 == b"Q":  # Page Down
            scroll.offset = min(
                scroll.max_offset,
                scroll.offset + scroll.visible_height,
            )
            scroll.at_bottom = scroll.offset >= scroll.max_offset
        elif ch2 == b"G":  # Home
            scroll.offset = 0
            scroll.at_bottom = False
        elif ch2 == b"O":  # End
            scroll.offset = scroll.max_offset
            scroll.at_bottom = True


def _listen_unix(scroll: ScrollState) -> None:
    import termios
    import tty
    import select
    import os

    fd = sys.stdin.fileno()
    old = termios.tcgetattr(fd)
    try:
        tty.setraw(fd)
        while not scroll.quit:
            if not select.select([sys.stdin], [], [], 0.05)[0]:
                continue
            ch = sys.stdin.read(1)
            if ch == "q":
                scroll.quit = True
                break
            if ch == "\r":
                scroll.at_bottom = True
                continue
            if ch != "\x1b":
                continue
            if not select.select([sys.stdin], [], [], 0.05)[0]:
                continue
            ch2 = sys.stdin.read(1)
            if ch2 != "[":
                continue
            if not select.select([sys.stdin], [], [], 0.05)[0]:
                continue
            ch3 = sys.stdin.read(1)

            if ch3 == "A":  # Up
                if scroll.offset > 0:
                    scroll.offset -= 1
                    scroll.at_bottom = False
            elif ch3 == "B":  # Down
                if scroll.offset < scroll.max_offset:
                    scroll.offset += 1
                    scroll.at_bottom = scroll.offset >= scroll.max_offset
            elif ch3 == "5":  # Page Up
                if select.select([sys.stdin], [], [], 0.05)[0]:
                    sys.stdin.read(1)
                    scroll.offset = max(
                        0,
                        scroll.offset - scroll.visible_height,
                    )
                    scroll.at_bottom = False
            elif ch3 == "6":  # Page Down
                if select.select([sys.stdin], [], [], 0.05)[0]:
                    sys.stdin.read(1)
                    scroll.offset = min(
                        scroll.max_offset,
                        scroll.offset + scroll.visible_height,
                    )
                    scroll.at_bottom = (
                        scroll.offset >= scroll.max_offset
                    )
            elif ch3 == "H":  # Home
                scroll.offset = 0
                scroll.at_bottom = False
            elif ch3 == "F":  # End
                scroll.offset = scroll.max_offset
                scroll.at_bottom = True
    finally:
        termios.tcsetattr(fd, termios.TCSADRAIN, old)


# ---------------------------------------------------------------------------
# Formatting helpers
# ---------------------------------------------------------------------------


def format_bytes(value: int) -> str:
    size = float(value)
    for unit in ("B", "KB", "MB", "GB", "TB"):
        if size < 1024:
            return f"{size:.1f} {unit}"
        size /= 1024
    return f"{size:.1f} PB"


def format_duration(seconds: float) -> str:
    seconds = int(seconds)
    hours, remainder = divmod(seconds, 3600)
    minutes, seconds = divmod(remainder, 60)
    return f"{hours:02d}:{minutes:02d}:{seconds:02d}"


def value_or_dash(value) -> str:
    if value is None or value == "":
        return "\u2014"
    return str(value)


def status_text(text: str, good: bool) -> Text:
    return Text(text, style="bold green" if good else "bold red")


def latency_label(latency_ms: float | None) -> str:
    if latency_ms is None:
        return "\u2014"
    if latency_ms < 500:
        return "GOOD"
    if latency_ms < 1000:
        return "OK"
    if latency_ms < 2000:
        return "SLOW"
    return "VERY SLOW"


def relay_label(relay: Relay | None) -> str:
    if relay is None:
        return "\u2014"
    nickname = relay.nickname or "Unnamed"
    country = relay.country_code or "??"
    return f"{nickname} [{country}]"


# ---------------------------------------------------------------------------
# Section renderers
# ---------------------------------------------------------------------------


def render_header(state: TorState) -> Panel:
    connected = state.control_connected
    title = Text()
    title.append("Tor ")
    title.append(state.version or "unknown", style="bold")
    status = status_text(
        "\u25cf CONNECTED" if connected else "\u25cf DISCONNECTED",
        connected,
    )
    table = Table.grid(expand=True)
    table.add_column()
    table.add_column(justify="right")
    table.add_row(title, status)
    return Panel(
        table,
        border_style="green" if connected else "red",
        padding=(0, 1),
        box=box.SIMPLE,
    )


def render_bootstrap(state: TorState) -> Panel:
    table = Table.grid(padding=(0, 1), expand=True)
    table.add_column(width=12)
    table.add_column(ratio=1)

    progress = Progress(
        SpinnerColumn("dots", style="cyan"),
        TextColumn("{task.description}", style="bold"),
        BarColumn(
            bar_width=None,
            style=Style(color="grey35"),
            complete_style=Style(color="green"),
        ),
        TaskProgressColumn(),
        expand=True,
    )
    progress.add_task("", total=100, completed=state.bootstrap_percent)

    table.add_row("Bootstrap", progress)
    table.add_row("Status", value_or_dash(state.bootstrap_summary))
    table.add_row("Phase", value_or_dash(state.bootstrap_tag))
    table.add_row("Uptime", format_duration(state.uptime_seconds))

    return Panel(
        table,
        title="BOOTSTRAP",
        border_style="dim",
        padding=(0, 1),
        box=box.SIMPLE,
    )


def render_circuit(state: TorState) -> Panel:
    circuit = state.primary_circuit
    table = Table.grid(padding=(0, 1), expand=True)
    table.add_column(width=12)
    table.add_column()

    if circuit is None:
        table.add_row("Circuit", "No built circuit")
        return Panel(
            table,
            title="CIRCUIT",
            border_style="dim",
            padding=(0, 1),
            box=box.SIMPLE,
        )

    guard = circuit.guard
    middle = circuit.middle
    exit_relay = circuit.exit

    table.add_row("Circuit", f"#{circuit.id}  {circuit.status}")
    table.add_row("Purpose", value_or_dash(circuit.purpose))
    table.add_row("Guard", relay_label(guard))

    if guard:
        table.add_row("Guard IP", value_or_dash(guard.ip))
        table.add_row("Country", value_or_dash(guard.country_code))

    table.add_row(
        "Path",
        f"{relay_label(guard)}  \u2192  {relay_label(middle)}  \u2192  {relay_label(exit_relay)}",
    )

    if circuit.build_time_ms is not None:
        table.add_row("Build time", f"{circuit.build_time_ms:.1f} ms")

    return Panel(
        table,
        title="CIRCUIT",
        border_style="dim",
        padding=(0, 1),
        box=box.SIMPLE,
    )


def _listener_status(listeners: tuple[str, ...]) -> Text:
    return status_text(
        "\u25cf READY" if listeners else "\u25cf OFFLINE",
        bool(listeners),
    )


def _listener_value(listeners: tuple[str, ...]) -> str:
    return ", ".join(listeners) if listeners else "\u2014"


def render_connections(state: TorState) -> Panel:
    table = Table.grid(padding=(0, 1), expand=True)
    table.add_column(width=12)
    table.add_column(ratio=1)
    table.add_column(width=12, justify="right")

    table.add_row(
        "SOCKS",
        _listener_value(state.socks_listeners),
        _listener_status(state.socks_listeners),
    )
    table.add_row(
        "DNS",
        _listener_value(state.dns_listeners),
        _listener_status(state.dns_listeners),
    )
    table.add_row(
        "Control",
        _listener_value(state.control_listeners),
        _listener_status(state.control_listeners),
    )
    table.add_row("Circuits", str(len(state.built_circuits)), "")
    table.add_row("Streams", str(len(state.streams)), "")
    table.add_row("Downloaded", format_bytes(state.bytes_read), "")
    table.add_row("Uploaded", format_bytes(state.bytes_written), "")

    return Panel(
        table,
        title="CONNECTION",
        border_style="dim",
        padding=(0, 1),
        box=box.SIMPLE,
    )


def render_measurement(state: MeasurementState) -> Panel:
    table = Table.grid(padding=(0, 1), expand=True)
    table.add_column(width=12)
    table.add_column(ratio=1)
    table.add_column(width=12, justify="right")

    latency = (
        "\u2014"
        if state.latency_ms is None
        else f"{state.latency_ms:.1f} ms"
    )
    quality = latency_label(state.latency_ms)

    table.add_row("Latency", latency, quality)

    last_probe = (
        "Never"
        if state.seconds_since_probe is None
        else f"{state.seconds_since_probe:.0f}s ago"
    )
    table.add_row("Last probe", last_probe, "")
    table.add_row(
        "Probes",
        f"{state.successful_probes} ok / {state.failed_probes} fail",
        "",
    )

    if state.consecutive_failures:
        table.add_row(
            "Failures",
            str(state.consecutive_failures),
            status_text("\u25cf DEGRADED", False),
        )

    if state.error:
        table.add_row("Last error", state.error, "")

    return Panel(
        table,
        title="MEASUREMENT",
        border_style="dim",
        padding=(0, 1),
        box=box.SIMPLE,
    )


def render_messages(state: TorState) -> Panel:
    messages = state.messages[-50:]
    if not messages:
        body: object = Text("No Tor messages.", style="dim")
    else:
        lines = []
        for msg in messages:
            ts = time.strftime(
                "%H:%M:%S",
                time.localtime(msg.timestamp),
            )
            line = Text()
            line.append(f"{ts} ", style="dim")
            level_style = {
                "NOTICE": "cyan",
                "WARN": "yellow",
                "ERR": "bold red",
            }.get(msg.level, "white")
            line.append(f"[{msg.level}] ", style=level_style)
            line.append(msg.message)
            lines.append(line)
        body = Group(*lines)

    return Panel(
        body,
        title="TOR EVENTS",
        border_style="dim",
        padding=(0, 1),
        box=box.SIMPLE,
    )


# ---------------------------------------------------------------------------
# Full content builder
# ---------------------------------------------------------------------------


def _build_full(
    state: TorState,
    msmt: MeasurementState,
) -> Group:
    return Group(
        render_header(state),
        render_bootstrap(state),
        render_circuit(state),
        render_connections(state),
        render_measurement(msmt),
        render_messages(state),
    )


# ---------------------------------------------------------------------------
# Viewport / scroll rendering
# ---------------------------------------------------------------------------


class ScrollableViewport:
    """Clips a renderable to a scrollable viewport window."""

    def __init__(
        self,
        renderable: object,
        scroll: ScrollState,
        height: int,
    ) -> None:
        self._renderable = renderable
        self._scroll = scroll
        self._height = height

    def __rich_console__(
        self,
        console: Console,
        options: object,
    ) -> object:
        segments = console.render(self._renderable, options)
        lines = list(Segment.split_lines(segments))

        total = len(lines)
        self._scroll.total_lines = total
        self._scroll.clamp()

        start = self._scroll.offset
        end = start + self._height
        visible = lines[start:end]

        for line in visible:
            yield from line
            yield Segment.line()

        for _ in range(self._height - len(visible)):
            yield Segment.line()

        if total > self._height:
            yield from self._scrollbar_segments()


    def _scrollbar_segments(self) -> object:
        ratio = (
            self._scroll.offset / self._scroll.max_offset
            if self._scroll.max_offset > 0
            else 0.0
        )
        width = 20
        pos = round(ratio * (width - 1))
        chars = ["\u2500"] * width
        chars[pos] = "\u25cf"
        bar = "".join(chars)
        pct = round(ratio * 100)
        text = (
            f"  {bar}  {pct}%"
            f"  |  {chr(8593)}{chr(8595)} PgUp/PgDn Home End scroll"
            f"  r reset  q quit"
        )
        yield Segment(text)
        yield Segment.line()


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------


def main() -> None:
    tor = TorClient()
    measurements = TorMeasurements()

    tor.start()
    measurements.start()

    scroll = ScrollState()
    _start_input_listener(scroll)

    try:
        with Live(
            console=console,
            refresh_per_second=REFRESH_RATE,
            screen=True,
        ) as live:
            while not scroll.quit:
                scroll.visible_height = max(
                    MIN_VISIBLE_HEIGHT,
                    console.height - 1,
                )

                state = tor.snapshot()
                msmt = measurements.snapshot()

                live.update(
                    ScrollableViewport(
                        _build_full(state, msmt),
                        scroll,
                        scroll.visible_height,
                    )
                )
                time.sleep(REFRESH_INTERVAL)

    except KeyboardInterrupt:
        scroll.quit = True
    finally:
        measurements.stop()
        tor.stop()


if __name__ == "__main__":
    main()
