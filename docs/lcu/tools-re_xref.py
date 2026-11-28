"""静态交叉引用分析：在 LeagueClientBase.dll 里找路由字符串的引用点。

做法：
  1. 在文件里定位目标字符串（UTF-8），换算成 VA
  2. 扫 .text 段里的 RIP-relative lea（48 8D xx disp32），算出目标地址
  3. 命中目标的指令位置 → 用 capstone 反汇编前后若干字节，看上下文

用法：python tools/re_xref.py "/lol-lobby-team-builder/champ-select/v1/session/quit"
      python tools/re_xref.py --list        # 先列出所有可搜索的路由字符串
"""
import re
import struct
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
import pefile

DLL = r"D:\WeGameApps\英雄联盟\LeagueClient\LeagueClientBase.dll"
MAX_XREFS = 40


def load():
    pe = pefile.PE(DLL, fast_load=True)
    image_base = pe.OPTIONAL_HEADER.ImageBase
    data = open(DLL, "rb").read()
    return pe, image_base, data


def va_of(pe, image_base, file_offset):
    rva = pe.get_rva_from_offset(file_offset)
    return image_base + rva if rva is not None else None


def find_strings(pe, image_base, data):
    out = {}
    for m in re.finditer(rb"/(?:lol|riotclient|process-control|performance|data-store)[A-Za-z0-9_\-/{}.$:]{6,}", data):
        s = m.group().decode("ascii", "ignore")
        va = va_of(pe, image_base, m.start())
        if va is not None:
            out.setdefault(s, va)
    return out


def scan_lea_xref(pe, image_base, data, target_va):
    """扫 RIP-relative lea: REX.W(48|4C) 8D modrm disp32，modrm 的 mod=00 且 rm=101。"""
    hits = []
    for m in re.finditer(rb"[\x48\x4c]\x8d[\x05\x0d\x15\x1d\x25\x2d\x35\x3d]", data):
        off = m.start()
        if off + 7 > len(data):
            continue
        disp = struct.unpack_from("<i", data, off + 3)[0]
        insn_va = va_of(pe, image_base, off)
        if insn_va is None:
            continue
        tgt = insn_va + 7 + disp
        if tgt == target_va:
            hits.append((insn_va, off))
            if len(hits) >= MAX_XREFS:
                break
    return hits


def disasm_around(pe, image_base, data, insn_va, before=24, after=48):
    try:
        from capstone import Cs, CS_ARCH_X86, CS_MODE_64
    except ImportError:
        return "(capstone 未安装)"
    start_va = insn_va - before
    start_off = pe.get_offset_from_rva(start_va - image_base)
    if start_off is None or start_off <= 0:
        return "(无法定位)"
    blob = data[start_off:start_off + before + 7 + after]
    md = Cs(CS_ARCH_X86, CS_MODE_64)
    md.detail = False
    lines = []
    for insn in md.disasm(blob, start_va):
        mark = "  <<<" if insn.address == insn_va else ""
        lines.append(f"    {insn.address:016X}  {insn.bytes.hex():<16} {insn.mnemonic} {insn.op_str}{mark}")
    return "\n".join(lines)


def main():
    pe, image_base, data = load()
    print(f"[info] {DLL}")
    print(f"[info] ImageBase={hex(image_base)}  size={len(data)/1024/1024:.1f} MB")

    strings = find_strings(pe, image_base, data)
    print(f"[info] 可搜索路由字符串: {len(strings)}")

    if "--list" in sys.argv or len(sys.argv) < 2:
        keys = [k for k in strings if any(w in k for w in
                ("quit", "dodge", "request-lobby", "early-exit", "toggle-player-muted",
                 "session", "champ"))]
        for k in sorted(keys)[:60]:
            print(f"    {hex(strings[k]):>18}  {k}")
        if len(strings) > 60:
            print(f"    ... 共 {len(strings)} 条")
        return

    target = sys.argv[1]
    if target not in strings:
        print(f"[miss] 未找到字符串: {target}")
        near = [k for k in strings if target in k or k in target]
        for k in near[:10]:
            print("    近似:", k)
        return

    tva = strings[target]
    print(f"\n[target] {target}\n         VA={hex(tva)}")

    hits = scan_lea_xref(pe, image_base, data, tva)
    print(f"[xref] lea 引用命中 {len(hits)} 处\n")
    for i, (va, off) in enumerate(hits[:10]):
        print(f"  #{i+1} 指令地址 {hex(va)}  (文件偏移 {hex(off)})")
        print(disasm_around(pe, image_base, data, va))
        print()


if __name__ == "__main__":
    main()
