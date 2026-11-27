"""Opt-in mainland public HTTPS measurement; never submits authenticated APIs."""
import concurrent.futures
import csv
import json
import pathlib
import re
import statistics
import time
import urllib.error
import urllib.parse
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[1]
OUT = ROOT / "mainland-route-results"
API = "https://api.globalping.io/v1"
CARRIERS = {"telecom": (4134, 4809, 4812, 58453), "unicom": (4837, 9929, 17621),
            "mobile": (9808, 56040, 56046, 56048, 24400)}
ORIGIN = "https://raw.githubusercontent.com/xianyumht-cmd/facm/main/online/version.json"
REFERENCE = json.loads((ROOT / "online/version.json").read_text(encoding="utf-8-sig"))


def api(path, payload=None):
    data = None if payload is None else json.dumps(payload).encode()
    req = urllib.request.Request(API + path, data=data, headers={
        "Content-Type": "application/json", "User-Agent": "GGman-public-route-audit/1"})
    with urllib.request.urlopen(req, timeout=15) as response:
        return json.load(response)


def targets():
    result = [("github-direct", ORIGIN, True)]
    catalog = json.loads((ROOT / "online/mirrors.json").read_text(encoding="utf-8-sig"))
    for item in catalog["sources"]:
        if not item.get("enabled") or not item.get("prefix"):
            continue
        prefix = urllib.parse.urlsplit(item["prefix"])
        if prefix.scheme != "https" or not prefix.hostname or prefix.username or prefix.password:
            raise ValueError("Invalid public mirror prefix")
        result.append((item["name"], item["prefix"].rstrip("/") + "/" + ORIGIN, True))
    result += [
        ("hexdata", "https://hexdata.com.cn/heroes", False),
        ("opgg-build", "https://op.gg/zh-cn/lol/modes/aram-mayhem/ashe/build", False),
        ("aramgg", "https://aramgg.com/en", False),
        ("arammayhem", "https://arammayhem.com/", False),
        ("tencent-news", "https://lol.qq.com/news/index.shtml", False),
        ("akari-catalog", "https://akari-api.yuru-yuri.com/config/v1/sgp/league-servers", False),
    ]
    return result


def available_asns():
    try:
        probes = api("/probes")
        available = {p.get("location", p).get("asn") for p in probes
                     if p.get("location", p).get("country") == "CN"}
        return {name: [asn for asn in asns if asn in available][:2]
                for name, asns in CARRIERS.items()}
    except (OSError, ValueError, TypeError, AttributeError) as exc:
        print("Probe inventory unavailable:", type(exc).__name__, flush=True)
        return {name: [asns[0]] for name, asns in CARRIERS.items()}


def classify(result, is_manifest):
    if result.get("status") != "finished":
        return "probe-" + str(result.get("status", "unknown"))
    code = result.get("statusCode")
    if not isinstance(code, int) or not 200 <= code < 300:
        return "http-" + str(code)
    if result.get("tls", {}).get("authorized") is not True:
        return "tls-unverified"
    if not is_manifest:
        return "http-reachable-only"
    if result.get("truncated") or not isinstance(result.get("rawBody"), str):
        return "body-unavailable"
    try:
        body = json.loads(result["rawBody"])
        if not isinstance(body, dict):
            return "invalid-manifest"
        if not re.fullmatch(r"\d+(?:\.\d+){1,3}", str(body.get("version", ""))):
            return "invalid-manifest"
        if not re.fullmatch(r"[0-9a-fA-F]{64}", str(body.get("sha256", ""))):
            return "invalid-manifest"
        if not str(body.get("download_url", "")).startswith("https://"):
            return "invalid-manifest"
        if (body.get("version") == REFERENCE.get("version")
                and body["sha256"].lower() == REFERENCE["sha256"].lower()
                and body.get("download_url") == REFERENCE.get("download_url")):
            return "manifest-matches-checkout"
        return "manifest-other-version-or-content"
    except (ValueError, TypeError, KeyError):
        return "invalid-manifest"


def measure(target, carrier, asns):
    name, url, is_manifest = target
    row = {"source": name, "carrier": carrier, "url": url}
    if not asns:
        return [dict(row, evidence="no-mainland-probe")]
    parts = urllib.parse.urlsplit(url)
    payload = {
        "type": "http", "target": parts.hostname,
        "locations": [{"country": "CN", "asn": asn, "limit": 2 if len(asns) == 1 else 1}
                      for asn in asns],
        "measurementOptions": {
            "protocol": "HTTPS", "request": {"method": "GET", "path": parts.path or "/",
                "headers": {"User-Agent": "GGman-Windows/3.5", "Cache-Control": "no-cache"}}
        }
    }
    try:
        mid = api("/measurements", payload)["id"]
        if not re.fullmatch(r"[a-zA-Z0-9_-]+", mid):
            raise ValueError("unexpected measurement ID")
        row["measurement_id"] = mid
        deadline = time.monotonic() + 45
        while True:
            time.sleep(1.5)
            data = api("/measurements/" + mid)
            if data.get("status") != "in-progress" or time.monotonic() >= deadline:
                break
        rows = []
        for item in data.get("results", []):
            probe = item.get("probe", {})
            result = item.get("result", {})
            timings = result.get("timings") or {}
            verified = probe.get("country") == "CN" and probe.get("asn") in asns
            rows.append(dict(row, country=probe.get("country"), city=probe.get("city"),
                asn=probe.get("asn"), network=probe.get("network"),
                http_status=result.get("statusCode"), total_ms=timings.get("total"),
                dns_ms=timings.get("dns"), tcp_ms=timings.get("tcp"),
                tls_ms=timings.get("tls"), first_byte_ms=timings.get("firstByte"),
                download_ms=timings.get("download"),
                evidence=classify(result, is_manifest) if verified else "unexpected-probe"))
        # Keep the provider measurement IDs; no cookies, response headers or bodies in artifacts.
        return rows or [dict(row, evidence="no-probe-result")]
    except (OSError, ValueError, KeyError, TypeError, AttributeError) as exc:
        return [dict(row, evidence="api-error-" + str(getattr(exc, "code", type(exc).__name__)))]


def main():
    OUT.mkdir(exist_ok=True)
    asns = available_asns()
    print("Mainland probe ASNs:", json.dumps(asns), flush=True)
    jobs = [(target, carrier, nums) for target in targets() for carrier, nums in asns.items()]
    rows = []
    with concurrent.futures.ThreadPoolExecutor(max_workers=6) as pool:
        tasks = [pool.submit(measure, *job) for job in jobs]
        for future in concurrent.futures.as_completed(tasks):
            for row in future.result():
                rows.append(row)
                print(json.dumps(row, ensure_ascii=True), flush=True)
    fields = sorted({key for row in rows for key in row})
    with (OUT / "measurements.csv").open("w", newline="", encoding="utf-8") as file:
        writer = csv.DictWriter(file, fieldnames=fields)
        writer.writeheader()
        writer.writerows(rows)
    summary = []
    for name, _, manifest in targets():
        for carrier in CARRIERS:
            group = [r for r in rows if r["source"] == name and r["carrier"] == carrier]
            expected = "manifest-matches-checkout" if manifest else "http-reachable-only"
            usable = [r for r in group if r["evidence"] == expected and isinstance(r.get("total_ms"), (int, float))]
            attempted = sum(r.get("country") == "CN" and r.get("asn") in asns[carrier] for r in group)
            summary.append(dict(source=name, carrier=carrier, attempted=attempted, usable=len(usable),
                median_ms=statistics.median([r["total_ms"] for r in usable]) if usable else None))
    (OUT / "summary.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
    print("SUMMARY", json.dumps(summary), flush=True)
    if not any(r["attempted"] for r in summary):
        raise SystemExit("No mainland probe results. Do not interpret this as endpoint failure.")


if __name__ == "__main__":
    main()
