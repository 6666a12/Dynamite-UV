# -*- coding: utf-8 -*-
"""Attach to Dynamix UV on AVD, scan memory for JudgeSettings float signature."""
import frida, sys, struct

PATTERN = bytes.fromhex(
    "0000F042"  # MinBPM 120.0
    "00004843"  # MaxBPM 200.0
    "00001643"  # StandardBPM 150.0
    "0000803D"  # PrefectBarTime 0.0625
    "0000C03D"  # GreatBarTime 0.09375
    "0000003E"  # GoodBarTime 0.125
    "0000203E"  # MissBarTime 0.15625
    "0000003E"  # HoldHoldingJudgeBarTime 0.125
    "0000003E"  # MixerHoldingJudgeBarTime 0.125
    "0000803E"  # MixerFlyNSWidthCenter 0.25
    "0000003F"  # MixerFlyNSWidthSides 0.5
)

JS = """
var pattern = '%s';
var results = [];
var ranges = Process.enumerateRanges({protection: 'r--', coalesce: true})
    .concat(Process.enumerateRanges({protection: 'rw-', coalesce: true}));
ranges.forEach(function(r) {
    try {
        var matches = Memory.scanSync(r.base, r.size, pattern);
        matches.forEach(function(m) { results.push({addr: m.address.toString(), size: r.size}); });
    } catch (e) {}
});
send(results);
""" % PATTERN.hex().upper().replace("..", "")  # hex pattern string

# Memory.scanSync wants a pattern like "00 00 F0 42"
hexstr = " ".join("%02X" % b for b in PATTERN)
JS = """
var pattern = '%s';
var results = [];
['r--','rw-'].forEach(function(prot){
  Process.enumerateRanges({protection: prot, coalesce: true}).forEach(function(r) {
    try {
        Memory.scanSync(r.base, r.size, pattern).forEach(function(m){
            results.push(m.address.toString());
        });
    } catch (e) {}
  });
});
send(results);
""" % hexstr

def main():
    dev = frida.get_usb_device(timeout=10)
    target = None
    for p in dev.enumerate_processes():
        if "dynamix" in p.name.lower():
            target = p
            break
    if not target:
        print("process not found"); sys.exit(1)
    print("attaching:", target.name, target.pid)
    session = dev.attach(target.pid)
    found = []
    def on_msg(msg, data):
        if msg.get("type") == "send":
            found.extend(msg["payload"])
        elif msg.get("type") == "error":
            print("JS error:", msg.get("description"))
    script = session.create_script(JS)
    script.on("message", on_msg)
    script.load()
    import time; time.sleep(30)
    print("matches:", found)
    for addr in found:
        base = int(addr, 16)
        # dump 96 bytes starting 16 bytes before the match
        try:
            raw = session.read_memory if False else None
        except Exception:
            pass
    if found:
        ctx_js = """
        var addrs = %s;
        addrs.forEach(function(a){
            try { send({addr: a}, ptr(a).sub(32).readByteArray(160)); } catch(e){}
        });
        """ % (str(found).replace("'", '"'))
        got = []
        def on_msg2(m, d):
            if m.get("type") == "send":
                got.append({"addr": m["payload"]["addr"], "hex": d})
            elif m.get("type") == "error":
                print("JS error:", m.get("description"))
        s2 = session.create_script(ctx_js)
        s2.on("message", on_msg2)
        s2.load()
        time.sleep(5)
        for item in got:
            print("== match at", item["addr"])
            b = item["hex"] or b""
            for off in range(0, len(b), 4):
                chunk = bytes(b[off:off+4])
                if len(chunk) == 4:
                    f = struct.unpack("<f", chunk)[0]
                    i = struct.unpack("<i", chunk)[0]
                    mark = " <-- pattern start" if off == 32 else ""
                    print("  %+d: float=%-14g int=%d%s" % (off-32, f, i, mark))
    session.detach()

if __name__ == "__main__":
    main()
