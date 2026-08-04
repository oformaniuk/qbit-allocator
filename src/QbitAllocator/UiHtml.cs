namespace QbitAllocator;

public static class UiHtml
{
    public const string Page = """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>qBittorrent Allocator</title>
  <style>
    :root { color-scheme: light dark; font-family: system-ui, -apple-system, Segoe UI, sans-serif; }
    body { margin: 0; background: #f7f8fa; color: #20242a; }
    header { padding: 24px 32px 12px; border-bottom: 1px solid #d9dee7; background: #ffffff; }
    h1 { margin: 0; font-size: 24px; letter-spacing: 0; }
    main { padding: 24px 32px; display: grid; gap: 24px; }
    section { display: grid; gap: 12px; }
    h2 { margin: 0; font-size: 16px; }
    table { width: 100%; border-collapse: collapse; background: #fff; border: 1px solid #d9dee7; }
    th, td { padding: 10px 12px; text-align: left; border-bottom: 1px solid #e8ebf0; font-size: 14px; }
    th { color: #596170; font-weight: 600; background: #fbfcfe; }
    .grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 12px; }
    .metric { background: #fff; border: 1px solid #d9dee7; padding: 14px; border-radius: 8px; }
    .label { color: #596170; font-size: 12px; text-transform: uppercase; letter-spacing: .04em; }
    .value { margin-top: 6px; font-size: 18px; font-weight: 650; overflow-wrap: anywhere; }
    @media (prefers-color-scheme: dark) {
      body { background: #111418; color: #e6e8eb; }
      header, table, .metric { background: #181c22; border-color: #333944; }
      th { background: #1d222a; color: #aab2c0; }
      td, th { border-color: #333944; }
      .label { color: #aab2c0; }
    }
  </style>
</head>
<body>
  <header><h1>qBittorrent Allocator</h1></header>
  <main>
    <section class="grid" id="metrics"></section>
    <section><h2>Disks</h2><div id="disks"></div></section>
    <section><h2>Recent Decisions</h2><div id="decisions"></div></section>
    <section><h2>Skipped and Failed</h2><div id="skipped"></div></section>
  </main>
  <script>
    const fmtBytes = value => {
      if (value == null) return "";
      const units = ["B","KiB","MiB","GiB","TiB"];
      let n = Number(value), i = 0;
      while (n >= 1024 && i < units.length - 1) { n /= 1024; i++; }
      return `${n.toFixed(i ? 1 : 0)} ${units[i]}`;
    };
    const cell = value => `<td>${value ?? ""}</td>`;
    const table = (headers, rows) => `<table><thead><tr>${headers.map(h => `<th>${h}</th>`).join("")}</tr></thead><tbody>${rows.join("") || `<tr><td colspan="${headers.length}">No data</td></tr>`}</tbody></table>`;
    async function refresh() {
      const s = await fetch("/status").then(r => r.json());
      document.getElementById("metrics").innerHTML = [
        ["Upstream", s.upstream.reachable ? (s.upstream.authenticated ? "connected" : "auth required") : "offline"],
        ["Policy", s.policy],
        ["Reconciler", s.reconciler.enabled ? `${s.reconciler.intervalSeconds}s interval` : "disabled"],
        ["Unassigned", s.pausedNeverStartedUnassigned ?? ""]
      ].map(([label, value]) => `<div class="metric"><div class="label">${label}</div><div class="value">${value}</div></div>`).join("");
      document.getElementById("disks").innerHTML = table(["Label","Path","Free","Used","Reserve","Enabled"], s.disks.map(d => `<tr>${cell(d.label)}${cell(d.path)}${cell(fmtBytes(d.freeBytes))}${cell(`${d.usedPercent}%`)}${cell(fmtBytes(d.reserveBytes))}${cell(d.enabled ? "yes" : "no")}</tr>`));
      document.getElementById("decisions").innerHTML = table(["Time","Torrent","Message"], s.recentDecisions.map(e => `<tr>${cell(e.atUtc)}${cell(e.name || e.hash)}${cell(e.message)}</tr>`));
      document.getElementById("skipped").innerHTML = table(["Time","Torrent","Reason"], s.recentSkipped.map(e => `<tr>${cell(e.atUtc)}${cell(e.name || e.hash)}${cell(e.message)}</tr>`));
    }
    refresh();
    setInterval(refresh, 10000);
  </script>
</body>
</html>
""";
}
