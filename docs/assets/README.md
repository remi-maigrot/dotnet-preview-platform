# Images used by the README

| File | Content | Status |
|------|---------|--------|
| `grafana-sample-api.png` | *Sample API* dashboard on the local environment under load | captured |
| `grafana-canary-abort.png` | The same dashboard during the canary test: release markers, the faulty canary's error spike, back to zero after the abort | captured |
| `devctl-up.gif` | Terminal recording of `devctl up`, then `devctl preview create --pr 42` | to record |
| `preview-comment.png` | The comment the preview workflow posts on a pull request | to capture after the first pull request on GitHub |

Screenshots are taken at 1920×1080 with the Grafana kiosk mode (`&kiosk` in the URL). For the GIF,
[vhs](https://github.com/charmbracelet/vhs) or asciinema with agg give a sharp result at a small size.
