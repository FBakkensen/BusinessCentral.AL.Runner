#!/usr/bin/env python3
"""
Run all bucket-1 test codeunits against BC in parallel.
Usage: op run --env-file=.op.env -- python3 run-bc-tests.py [--workers N] [--output FILE] [--ids X,Y,Z]
"""
import os, sys, json, time, re, argparse
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor, as_completed
import requests
from requests.auth import HTTPBasicAuth

def build_config():
    host = os.environ.get("BC_SERVER", "localhost")
    # Credentials: env vars > .bcconfig.json > local-dev defaults (BCRUNNER is the
    # documented default for the bc-linux dev container, not a production secret)
    username = os.environ.get("BC_USERNAME")
    password = os.environ.get("BC_PASSWORD")
    if not username or not password:
        cfg_path = Path(__file__).parent / ".bcconfig.json"
        if cfg_path.exists():
            import json as _json
            cfg_file = _json.loads(cfg_path.read_text())
            username = username or cfg_file.get("username", "BCRUNNER")
            password = password or cfg_file.get("password", "Admin123!")
        else:
            username = username or "BCRUNNER"
            password = password or "Admin123!"
    return {
        "base_url": f"http://{host}:7052/BC",
        "auth": HTTPBasicAuth(username, password),
        "tenant": "default",
    }

def api_url(cfg, path, extra_params=""):
    t = cfg["tenant"]
    sep = "&" if extra_params else ""
    return f"{cfg['base_url']}/{path}?tenant={t}{sep}{extra_params}"

def get_company_id(cfg):
    r = requests.get(api_url(cfg, "api/v2.0/companies"), auth=cfg["auth"], timeout=15)
    r.raise_for_status()
    companies = r.json().get("value", [])
    if not companies:
        raise RuntimeError("No companies found in BC")
    return companies[0]["id"]

def fetch_log_failures(cfg, company_id, cu_id):
    """Fetch failing test method log entries for a codeunit (most recent run)."""
    # Filter by codeunitId and only failures; take last 50 to cover all methods
    url = api_url(cfg,
                  f"api/custom/automation/v1.0/companies({company_id})/logEntries",
                  f"$filter=codeunitId eq {cu_id} and success eq false&$orderby=entryNo desc&$top=50")
    try:
        r = requests.get(url, auth=cfg["auth"], timeout=15)
        if r.status_code == 200:
            return r.json().get("value", [])
    except Exception:
        pass
    return []

def run_codeunit(cfg, company_id, cu_id, timeout=180, retries=2):
    base = api_url(cfg, f"api/custom/automation/v1.0/companies({company_id})/codeunitRunRequests")

    for attempt in range(retries + 1):
        # 1. Create request
        r = requests.post(base, auth=cfg["auth"],
                          json={"CodeunitId": cu_id},
                          headers={"Content-Type": "application/json"},
                          timeout=15)
        if r.status_code not in (200, 201):
            if attempt < retries:
                time.sleep(2 + attempt * 2)
                continue
            return {"id": cu_id, "status": "ERROR",
                    "message": f"Create failed HTTP {r.status_code}: {r.text[:300]}",
                    "failures": []}

        req_id = r.json()["Id"]

        # 2. Trigger
        action = api_url(cfg, f"api/custom/automation/v1.0/companies({company_id})/codeunitRunRequests({req_id})/Microsoft.NAV.runCodeunit")
        r = requests.post(action, auth=cfg["auth"], timeout=30)
        if r.status_code == 409 and attempt < retries:
            time.sleep(3 + attempt * 3)
            continue
        if r.status_code not in (200, 201, 204):
            if attempt < retries:
                time.sleep(2)
                continue
            return {"id": cu_id, "status": "ERROR",
                    "message": f"Trigger failed HTTP {r.status_code}: {r.text[:200]}",
                    "failures": []}

        # 3. Poll
        poll_url = api_url(cfg, f"api/custom/automation/v1.0/companies({company_id})/codeunitRunRequests({req_id})")
        deadline = time.time() + timeout
        while time.time() < deadline:
            time.sleep(1)
            try:
                pr = requests.get(poll_url, auth=cfg["auth"], timeout=15)
                if pr.status_code != 200:
                    continue
                data = pr.json()
                status = data.get("Status", "")
                if status in ("Finished", "Error"):
                    result_msg = data.get("LastResult", "")
                    # Fetch log entries for failure details
                    failures = []
                    if status == "Error" or "failed" in result_msg.lower():
                        logs = fetch_log_failures(cfg, company_id, cu_id)
                        for entry in logs:
                            failures.append({
                                "method": entry.get("functionName", ""),
                                "error": entry.get("errorMessage", ""),
                            })
                    return {
                        "id": cu_id,
                        "status": status,
                        "message": result_msg,
                        "failures": failures,
                    }
            except Exception:
                continue

        return {"id": cu_id, "status": "TIMEOUT",
                "message": f"Timed out after {timeout}s", "failures": []}

    return {"id": cu_id, "status": "ERROR", "message": "Exhausted retries", "failures": []}

def discover_codeunit_ids(bucket_dir):
    ids = set()
    for f in Path(bucket_dir).rglob("test/*.al"):
        for line in f.read_text(errors="replace").splitlines():
            m = re.match(r'^codeunit\s+(\d+)', line.strip(), re.IGNORECASE)
            if m:
                ids.add(int(m.group(1)))
    return sorted(ids)

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--workers", type=int, default=4)
    parser.add_argument("--output", default=".dev/bc-test-results.json")
    parser.add_argument("--timeout", type=int, default=180)
    parser.add_argument("--ids", help="Comma-separated codeunit IDs to run")
    args = parser.parse_args()

    # Credentials fall back to local dev defaults — no env var required

    bucket_dir = Path(__file__).parent
    cfg = build_config()

    print("Connecting to BC...", flush=True)
    company_id = get_company_id(cfg)
    print(f"Company ID: {company_id}", flush=True)

    if args.ids:
        cu_ids = [int(x.strip()) for x in args.ids.split(",")]
    else:
        cu_ids = discover_codeunit_ids(bucket_dir)

    print(f"Running {len(cu_ids)} test codeunits with {args.workers} workers...", flush=True)

    results = []
    done = 0
    failed_count = 0

    with ThreadPoolExecutor(max_workers=args.workers) as pool:
        futures = {pool.submit(run_codeunit, cfg, company_id, cu_id, args.timeout): cu_id
                   for cu_id in cu_ids}
        for fut in as_completed(futures):
            cu_id = futures[fut]
            try:
                res = fut.result()
            except Exception as e:
                res = {"id": cu_id, "status": "ERROR", "message": str(e), "failures": []}
            results.append(res)
            done += 1
            is_fail = res["status"] != "Finished"
            if is_fail:
                failed_count += 1
                failures_summary = ""
                if res.get("failures"):
                    failures_summary = " | " + "; ".join(
                        f"{f['method']}: {f['error'][:80]}" for f in res["failures"][:3]
                    )
                print(f"  [{done}/{len(cu_ids)}] FAIL {cu_id}: {res['message'][:100]}{failures_summary}", flush=True)
            else:
                n_fail = len(res.get("failures", []))
                if n_fail:
                    failed_count += 1
                    print(f"  [{done}/{len(cu_ids)}] FAIL {cu_id} ({n_fail} methods):", flush=True)
                    for f in res.get("failures", [])[:5]:
                        print(f"      {f['method']}: {f['error'][:120]}", flush=True)
                else:
                    print(f"  [{done}/{len(cu_ids)}] PASS {cu_id}", flush=True)

    results.sort(key=lambda r: r["id"])
    out_path = bucket_dir / args.output
    out_path.parent.mkdir(parents=True, exist_ok=True)
    with open(out_path, "w") as f:
        json.dump(results, f, indent=2)

    passed = sum(1 for r in results if r["status"] == "Finished" and not r.get("failures"))
    print(f"\nResults: {passed} passed, {failed_count} failed (of {len(cu_ids)})")
    print(f"Saved to: {out_path}")
    sys.exit(0 if failed_count == 0 else 1)

if __name__ == "__main__":
    main()
