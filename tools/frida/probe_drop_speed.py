# -*- coding: utf-8 -*-
"""Capture Dynamix UV's baked per-note movement data for a narrow bar range.

This clean-room probe records numeric ECS/Burst inputs only. It does not dump
assets, chart files, textures, audio, or executable memory.
"""

import argparse
import json
import time

import frida


HOOKS = (
    (0x499700, "x13", "x15", "linear_mask"),
    (0x4997E4, "x11", "x8", "linear"),
    (0x4998DC, "x6", "x5", "enabled_mask"),
    (0x4999CC, "x11", "x8", "enabled_tail"),
)


def make_script(min_bar: float, max_bar: float) -> str:
    hook_specs = json.dumps(HOOKS)
    return f"""
(function() {{
    const moduleName = 'lib_burst_generated.so';
    const hooks = {hook_specs};
    const seenNotes = Object.create(null);
    let lastJobMs = 0;
    let installed = false;

    function f32(p, offset) {{ return p.add(offset).readFloat(); }}

    function installWhenLoaded() {{
        if (installed) return;
        const mod = Process.findModuleByName(moduleName);
        if (mod === null) return;
        installed = true;

        hooks.forEach(function(spec) {{
            const offset = spec[0];
            const noteReg = spec[1];
            const auxReg = spec[2];
            const path = spec[3];
            Interceptor.attach(mod.base.add(offset), {{
                onEnter: function() {{
                    try {{
                        const job = this.context.x0;
                        const note = this.context[noteReg];
                        const aux = this.context[auxReg];
                        const bar = f32(note, -8);
                        if (!Number.isFinite(bar) || bar < {min_bar} || bar > {max_bar}) return;

                        const width = f32(note, -4);
                        const position = f32(note, 0);
                        const correction = f32(aux, -4);
                        const withExit = aux.readU8();
                        const current = f32(job, 0);
                        const threshold = f32(job, 4);
                        const a = f32(job, 0x98);
                        const exitAccel = f32(job, 0x9c);
                        const baseSpeed = f32(job, 0xa4);
                        const divisor = f32(job, 0xa8);
                        const b = f32(job, 0xb0);
                        const d = bar - current + correction;
                        const z = withExit !== 0 && d <= threshold
                            ? a * b * threshold + (d - threshold) * b * exitAccel
                            : a * b * d;

                        const key = [bar.toFixed(5), position.toFixed(5), width.toFixed(5),
                                     correction.toFixed(6), withExit].join('|');
                        if (seenNotes[key] === undefined) {{
                            seenNotes[key] = true;
                            send({{
                                kind: 'note', path: path, bar: bar, position: position,
                                width: width, correction: correction, withExit: withExit,
                                current: current, d: d, z: z
                            }});
                        }}

                        const now = Date.now();
                        if (now - lastJobMs >= 400) {{
                            lastJobMs = now;
                            send({{
                                kind: 'job', current: current, threshold: threshold,
                                a: a, exitAccel: exitAccel, baseSpeed: baseSpeed,
                                divisor: divisor, b: b, sampleBar: bar,
                                sampleCorrection: correction, sampleWithExit: withExit
                            }});
                        }}
                    }} catch (e) {{
                        send({{ kind: 'error', path: path, message: String(e) }});
                    }}
                }}
            }});
        }});

        send({{ kind: 'ready', moduleBase: mod.base.toString(), hooks: hooks.length }});
    }}

    installWhenLoaded();
    setInterval(installWhenLoaded, 250);
    send({{ kind: 'waiting', module: moduleName }});
}})();
"""


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--min-bar", type=float, default=95.0)
    parser.add_argument("--max-bar", type=float, default=112.0)
    parser.add_argument("--duration", type=float, default=600.0)
    args = parser.parse_args()

    device = frida.get_usb_device(timeout=10)
    game = next(
        (
            process
            for process in device.enumerate_processes()
            if "dynamix" in process.name.lower() or "c4cat" in process.name.lower()
        ),
        None,
    )
    if game is None:
        raise SystemExit("Dynamix process not found")

    session = device.attach(game.pid)
    script = session.create_script(make_script(args.min_bar, args.max_bar))

    def on_message(message, _data) -> None:
        if message["type"] == "send":
            print(json.dumps(message["payload"], ensure_ascii=False), flush=True)
        else:
            print(json.dumps(message, ensure_ascii=False), flush=True)

    script.on("message", on_message)
    script.load()
    print(json.dumps({"kind": "attached", "pid": game.pid, "name": game.name}), flush=True)

    deadline = time.monotonic() + args.duration
    try:
        while time.monotonic() < deadline:
            time.sleep(0.25)
    except KeyboardInterrupt:
        pass
    finally:
        session.detach()


if __name__ == "__main__":
    main()
