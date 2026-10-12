# Historical telemetry acceptance inventory

This inventory covers the implemented monitoring changes, not every remaining platform feature.

| Claim/control | Functional evidence | Visual state |
| --- | --- | --- |
| Live, 1h, 24h, 7d selector | API bounded slots/resolutions; full cycle back to live | Three readable relative axis labels; missing intervals disconnected |
| Network/disk, total/device | User clicks mode and device selectors | Toolbar and chart bounds at 320/1045/1440 px, light/dark |
| Throughput/IOPS/latency | Native unit tests and real Linux/PostgreSQL comparison | MB/s, IOPS, ms; idle is not zero latency |
| Refresh maintains rolling history | Six-second refresh retains populated paths | Settled populated chart before/after refresh, no full-page spinner |
| History query failure | 503 warning, current host state remains, failed history is cleared | Warning and empty chart without old curves |
| Late response / permission loss | Slow 1h then 24h; 403 clears administrator panel | Selected window remains correct; private metrics disappear |
| Canonical host sampling | Concurrent readers share batch identity; native timestamps checked | Page reads cannot perturb displayed throughput |

Automated browser cases use fixtures exclusively; remote acceptance uses actual Agent, database and OIDC dependencies. Visual screenshot review is separate from assertions. Exploratory pass: switch windows/measurements/devices rapidly, open/close selectors with keyboard, change viewport and theme while controls are populated. Check both document overflow and individual toolbar/chart bounds. Do not claim metric partition retention acceptance: daily partitions are not yet implemented.
