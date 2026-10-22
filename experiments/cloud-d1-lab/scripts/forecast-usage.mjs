// Estimate only; this script never calls Cloudflare or reads user records.
const args = process.argv.slice(2);
const [users = "50", hours = "24", intervalSeconds = "90", readsPerPoll = "2"] = args;
const numbers = [users, hours, intervalSeconds, readsPerPoll].map(Number);
if (numbers.some(x => !Number.isFinite(x) || x <= 0) || numbers[1] > 24 ||
    numbers[0] > 1000000 || numbers[2] < 1 || numbers[3] > 10) {
  console.error("Usage: node scripts/forecast-usage.mjs <concurrent-users> <active-hours> <poll-seconds> <requests-per-poll>");
  process.exitCode = 1;
} else {
  const [u, h, seconds, reads] = numbers;
  const syncCalls = Math.ceil(h * 3600 / seconds) * reads * u;
  const cap = 100000;
  const pct = (syncCalls / cap * 100).toFixed(1);
  console.log(JSON.stringify({
    category: "MODELED_SCENARIO_NOT_REAL_USAGE",
    concurrent_users: u,
    active_hours_per_day: h,
    interval_seconds: seconds,
    independent_worker_requests_per_poll: reads,
    estimated_worker_requests_per_day: syncCalls,
    worker_free_daily_cap: cap,
    percent_of_cap: Number(pct),
    headroom_for_auth_updates_game_and_other_traffic: cap - syncCalls,
    warning: "Real request counts, D1 rows read/written and costs must come from Cloudflare account analytics."
  }, null, 2));
}
