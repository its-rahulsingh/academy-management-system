# Ojas marketing

Everything used to advertise Ojas.

| Folder | What is in it |
|---|---|
| `web/` | The animated videos as web pages. The server publishes this folder at `/media`, so they are live at `/media/ojas-ad.html` and `/media/ojas-demo.html`, and the landing page plays them. |
| `tv-ad/` | The production kit for the TV-style ad with AI characters: cast, shot-by-shot Gemini prompts and edit plan. Open the HTML file in a browser. |
| `exports/` | Finished MP4 files. Kept out of git because videos are large; back them up elsewhere. |

## The videos in `web/`

- **`ojas-ad.html`**: 30-second vertical reel (1080×1920) for Instagram and Stories. Its buttons also export the TV-ad clips: *Insert A · Fees*, *Insert B · QR* and *End card*.
- **`ojas-demo.html`**: 2-minute landscape product tour (1920×1080) covering every feature.

To make an MP4: open the page in Chrome or Edge, click a record button and keep the tab visible until it downloads. The offer and contact lines are in `CONFIG` at the top of each file.

Adding `?embed=1` to either address plays it on a loop with no controls; the landing page uses that.
