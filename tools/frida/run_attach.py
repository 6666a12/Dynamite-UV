"""Attach to a running com.c4cat.dynamix2 with the license bypass, stay alive, report detach."""
import sys
import time
import frida

SCRIPT = "community/tools/frida/bypass_license_attach.js"
PKG = "com.c4cat.dynamix2"


def main():
    device = frida.get_usb_device(timeout=15)
    pid = None
    for _ in range(30):
        for p in device.enumerate_processes():
            if "dynamix" in p.name.lower():
                pid = p.pid
                break
        if pid is not None:
            break
        time.sleep(1)
    if pid is None:
        print("[!] game not running", flush=True)
        sys.exit(1)

    session = device.attach(pid)
    session.on("detached", lambda reason, crash: print(f"[!] DETACHED: {reason} {crash}", flush=True))
    with open(SCRIPT, encoding="utf-8") as f:
        script = session.create_script(f.read())
    script.on("message", lambda msg, data: print(f"[msg] {msg}", flush=True))
    script.load()
    print(f"[*] attached pid={pid}, hooks live", flush=True)

    while True:
        time.sleep(5)


if __name__ == "__main__":
    main()
