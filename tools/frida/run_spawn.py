"""Spawn com.c4cat.dynamix2 under frida with the license bypass script, then stay alive."""
import sys
import time
import frida

SCRIPT = "community/tools/frida/bypass_license.js"
PKG = "com.c4cat.dynamix2"


def main():
    device = frida.get_usb_device(timeout=15)
    try:
        device.get_process(PKG)
        print(f"[*] {PKG} already running, killing first", flush=True)
        device.kill(PKG)
        time.sleep(2)
    except frida.ProcessNotFoundError:
        pass

    pid = device.spawn([PKG])
    session = device.attach(pid)
    with open(SCRIPT, encoding="utf-8") as f:
        script = session.create_script(f.read())
    script.on("message", lambda msg, data: print(f"[msg] {msg}", flush=True))
    script.load()
    device.resume(pid)
    print(f"[*] spawned pid={pid}, resumed, hooks live", flush=True)

    while True:
        time.sleep(5)


if __name__ == "__main__":
    main()
