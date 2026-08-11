# Local-endpoint security

*Part of the [ACT spec](../overview.md). Italicised cross-references name sections of the sibling spec files listed there.*

- The hook/ingestion HTTP endpoint binds to **localhost only**, on an
  **OS-assigned port ACT then keeps**, and requires a **per-session token**
  (injected into the hook config at launch) so only ACT's own launched agents can
  post to it.
- **One endpoint for every session and both agents** — a loopback endpoint on
  ACT's own web host, not a second server and not a port per session. The port is
  not the boundary: any local process can enumerate listening ports, so a port
  ACT did not publish buys collision-avoidance, and the **token** does the
  authorizing. A port per session would multiply listeners for no isolation the
  token does not already give, and would break Codex's hook-trust hash (below).
- **Sticky, not fresh each start.** ACT binds port `0` on first run, stores the
  port it got, and reuses it after that (falling back to a new one if it is
  taken). Codex hashes each hook *definition* and re-prompts for trust whenever it
  changes, so any value that moves between restarts and appears in the hook
  command string costs the user an approval prompt every launch.
- **Routed per agent** — `/hooks/claude` and `/hooks/codex`. The two CLIs' hook
  payloads are different dialects, and the path is what picks the parser, so a
  payload arriving on the wrong route fails loudly instead of being mis-parsed.
- Which card an event belongs to comes from the **payload** (`session_id`, plus
  ACT's own task id on the process environment) — never from the port.
- **The MCP server shares all of it** — same listener, same kept port, same token
  header, on `/mcp`. One difference is load-bearing: a hook payload is an
  observation ACT can afford to drop, while an MCP call **mutates** ACT's store by
  creating a card, so the token is the *only* thing that decides which card becomes
  the parent — it is never an argument the agent supplies. See *Agent ↔ ACT contract*.
- **`/attachments/{cardId}/{fileName}` is the one route that serves a local file** —
  it exists so an attachment chip can show a thumbnail on hover, since
  attachments deliberately live outside `wwwroot` and an `<img>` cannot read a path.
  It is the narrowest thing that does that job, and every narrowing is deliberate:
  - **Images only, from a fixed list** (`TaskAttachment.ImageContentTypeFor`), with the
    content type taken from the extension rather than sniffed from the bytes. **SVG is
    excluded** — it is an image everywhere else and a script host here, and served from
    ACT's own origin it would run against ACT's own page.
  - **The folder is the boundary, and it is proved rather than assumed.**
    `IAttachmentStore.ResolveInside` resolves the name and then requires the result to sit
    inside that card's own directory, so `..`, an absolute path, and a sibling folder
    sharing the id's prefix all answer null — as does a name that simply is not there, so
    nothing can be probed for existence. The check is a store rule, not route code,
    precisely so a unit test can attack it.
  - **Never the HTML a clipboard supplied.** A pasted `<img src="file:///…">` names a real
    path and is ignored; only files the browser itself handed over are ever copied in, and
    only what a card's own folder holds is ever served back out.
  - Read-only `GET`, on the app port only — `HookPortGuard` already answers hook paths on
    the hook port and everything else off it, so the route needs no guard of its own.
