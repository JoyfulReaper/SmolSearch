# SmolSearch

SmolSearch is a small search engine for the small web, currently focused on
[Gemini](https://geminiprotocol.net/). It crawls Geminispace into a local SQLite
database and serves ranked searches from SQLite FTS5.

The architectural rule is simple:

> Crawling may be slow or broken. Search must not care.

Crawling and query serving are separate processes. A search request only reads
the local index; it never makes a live Gemini request.

## Status

This is a proof of concept, not a production-ready search service. The current
public-POC direction is deliberately based on a frozen crawl snapshot: run the
crawler on another machine, stop writes, copy the resulting SQLite database,
and serve that snapshot read-only. The crawler does not need to run beside the
search service. Results can become stale, and for this stage that is expected.

Gemini is the first protocol target. The shared document and result models are
not tied to Gemini presentation, so another small-web fetcher can feed the same
index later. The project plan identifies Gopher as the next protocol, but it is
not implemented.

## Architecture

```text
Crawl path                              Query path

Gemini capsules                         Gemini client
       |                                      |
       v                                      v
SmolSearch.Crawler                       HappyGemini
       |                                      |
       | fetch + parse                        | plugin invocation
       v                                      v
SearchDocument -> SQLite + FTS5    SmolSearch.HappyGemini
                                              |
                                              | HTTP
                                              v
                                      SmolSearch.Server
                                              |
                                              v
                                      SQLite FTS5 snapshot
```

HappyGemini is a separate, generic Gemini server. It does not contain
SmolSearch-specific behavior. `SmolSearch.HappyGemini` is the plugin that adds
the Gemini search page and calls the SmolSearch HTTP service.

### Project layout

- `SmolSearch.Core` — shared `SearchDocument`, `SearchResult`, and certificate-pin models.
- `SmolSearch.Storage` — SQLite/Dapper schema, document persistence, FTS5 queries, certificate pins, and crawl frontier.
- `SmolSearch.Crawler` — the Gemini client, Gemtext parser, and bounded crawl runner.
- `SmolSearch.Server` — a small HTTP search API over an existing read-only SQLite snapshot.
- `SmolSearch.HappyGemini` — a HappyGemini plugin that presents search results to Gemini clients.

All projects currently target .NET 10. Building requires a .NET 10 SDK:

```sh
dotnet build SmolSearch.slnx
```

## Crawling

The crawler starts from the fixed seed `gemini://geminiprotocol.net/`. For each
pending URL it:

1. opens a TLS connection and sends a Gemini request;
2. follows Gemini redirects, subject to the limits below;
3. indexes successful `text/gemini` responses;
4. uses the first Gemtext heading as the title;
5. resolves and queues Gemini links found outside preformatted blocks; and
6. marks the URL attempted even when fetching or parsing fails.

Documents are upserted by URL. The raw Gemtext body is stored as searchable
content along with its content type and fetch time. Non-success responses,
non-Gemtext responses, and responses without a text body are not indexed.
Failures are isolated per page and printed to the console.

### Persistent frontier

Discovered URLs and attempt timestamps live in the same SQLite database as the
index. `INSERT OR IGNORE` provides URL deduplication, so restarting the crawler
continues with URLs that have not yet been attempted. Pending selection takes
one candidate per host and randomly chooses among those candidates.

The current frontier is intentionally simple. It holds at most 50,000 entries,
does not retry attempted failures, and has no recrawl schedule, backoff state,
or concurrent workers.

### TOFU certificates

Gemini TLS uses trust on first use (TOFU). Pins are keyed by host and port and
stored in the `gemini_certificates` table. On first contact, a certificate that
is currently within its validity period is accepted and its SHA-256 fingerprint
and expiry are stored. Until that pin expires, a different fingerprint is
rejected. An expired pin can be replaced by the next currently valid
certificate. This is a TOFU check, not public-CA chain validation.

### Current safety and resource limits

The limits below come from the crawler source:

- one request at a time, followed by a 500 ms delay;
- 15-second timeout covering DNS, connection, TLS, and response reading;
- TLS 1.2 or TLS 1.3;
- remote-address filtering rejects loopback, private, link-local, CGNAT,
  benchmark, multicast, and selected other non-public ranges;
- Gemini request URL and response meta limited to 1,024 UTF-8 bytes each;
- at most five redirects, and only to `gemini:` URLs;
- successful text bodies limited to 1,048,576 decoded UTF-16 code units;
- at most 512 extracted links per Gemtext page;
- Gemini links longer than 1,024 UTF-8 bytes are ignored;
- at most 50,000 persistent frontier entries; and
- a caller-supplied cap on attempted pages per run.

These are useful guardrails, not a complete hostile-content or crawler-abuse
defense.

### Run the crawler

```sh
dotnet run --project SmolSearch.Crawler -- <max-pages> [database-path]
```

`max-pages` is the maximum number of URLs attempted in this run and must be
greater than zero. If the first argument is missing or is not an integer, it
defaults to 50. `database-path` is optional, defaults to `smolsearch.db` in the
current working directory, and is resolved to an absolute path. Because it is
the second positional argument, provide a page limit when providing a database
path.

For example:

```sh
# Attempt up to 50 pages and use ./smolsearch.db
dotnet run --project SmolSearch.Crawler -- 50

# Attempt up to 500 pages using an explicit snapshot path
dotnet run --project SmolSearch.Crawler -- 500 ./data/smolsearch.db
```

The checked-in crawler launch profile supplies `100` as its command-line
argument. Explicit arguments after `--`, as in the examples above, make the run
size unambiguous.

The crawler creates and initializes the database when needed. SQLite is put in
WAL mode, and the schema includes the documents, external-content FTS5,
certificate-pin, and frontier tables.

## Search and ranking

The FTS5 virtual table indexes title, content, and URL, backed by the `documents`
table and synchronized with triggers. Search input is split on whitespace and
each term is escaped as an FTS phrase. Matching rows are ordered by FTS5's
`bm25()` rank (ascending).

Each result contains its URL, an optional title, a query-aware snippet, and the
raw BM25 rank. Titles are truncated to 256 characters and snippets to 512
characters; FTS5 is asked to build snippets around up to 24 tokens.

## Run the HTTP server

The server requires an already initialized database. It fails at startup if the
file does not exist and opens it with SQLite's read-only mode; it does not run
schema initialization or crawling.

By default it looks for `smolsearch.db` in the current working directory. Set
the ASP.NET Core configuration key `SmolSearch:DatabasePath` to use a different
snapshot. In environment variables, nested configuration keys use a double
underscore:

```powershell
$env:SmolSearch__DatabasePath = "C:\data\smolsearch.db"
dotnet run --project SmolSearch.Server
```

```sh
SmolSearch__DatabasePath=/srv/smolsearch/smolsearch.db \
  dotnet run --project SmolSearch.Server
```

The checked-in development launch profile listens on
`https://localhost:55864` and `http://localhost:55865`. The snapshot POC treats
this API as an internal service for the Gemini plugin; the application itself
does not add authentication or enforce private network binding.

### HTTP search API

```http
GET /api/search?q=<query>&limit=<limit>
```

- `q` is required, must be 256 characters or fewer, and cannot contain control
  characters.
- `limit` is optional and defaults to 20. Values are clamped to the range
  1–100.
- Invalid queries return HTTP 400 with a JSON `error` property.
- Successful queries return HTTP 200 with a JSON array approximately like:

```json
[
  {
    "url": "gemini://example.org/page",
    "title": "Example page",
    "snippet": "...matching text...",
    "rank": -1.234
  }
]
```

`title` and `snippet` may be `null`; the numeric rank shown above is only an
example of the response shape, not a promised score or range.

## HappyGemini integration

The plugin exposes `/search` only for the configured host
`gemini.kgivler.com`. An empty query returns Gemini status 10 with an input
prompt. A submitted query is URL-decoded, sent to
`{baseUrl}/api/search`, and rendered as Gemtext links with optional snippets.
The page identifies the results as a possibly stale POC snapshot. Connection,
HTTP, timeout, or JSON failures from the backend produce a Gemini “server
unavailable” response.

The request path is:

```text
Gemini client
  -> HappyGemini
  -> SmolSearch.HappyGemini
  -> SmolSearch.Server
  -> SQLite FTS5
```

`SmolSearch.HappyGemini/smolsearch.json` currently sets `baseUrl` to
`http://localhost:55865`, matching the server's development HTTP URL. The
repository includes the plugin manifest and project, but does not document a
complete HappyGemini installation or deployment procedure.

## Current limitations

- The snapshot deployment is intentionally stale until a new database is
  crawled and copied; automatic snapshot publication is not implemented.
- Crawling is sequential and bounded. There is no continuous scheduler,
  recrawling, retry/backoff, or persistent host health state.
- The seed URL is compiled into the crawler and cannot be selected on the
  command line.
- Only successful `text/gemini` responses are indexed. Gopher and other
  protocols are not implemented.
- Frontier capacity and per-page link/body limits can exclude content from a
  large or pathological crawl.
- Ranking is the direct, understandable FTS5 BM25 ordering; there are no
  freshness, host-diversity, link-graph, or learned-ranking signals.
- The HTTP API is designed as an internal POC component and has no
  authentication, rate limiting, or public-API stability guarantee.

## Near-term direction

The checked-in plan separates future work from the current code. For the public
snapshot POC it calls for producing a useful, host-diverse crawl on a
workstation, safely checkpointing and integrity-checking the stopped SQLite
database, deploying the frozen snapshot, smoke-testing the complete Gemini
path, and recording a small baseline.

After that POC, planned work includes proper host-aware scheduling with bounded
concurrency, retry/backoff and recrawl state, continuous crawling, deliberate
corpus growth, and Gopher support at the fetch/parser boundary. These are
directions, not features available today; see [SmolSearch Plan.md](SmolSearch%20Plan.md)
for the longer design notes.

## License

SmolSearch is licensed under the [GNU Affero General Public License v3](LICENSE).
