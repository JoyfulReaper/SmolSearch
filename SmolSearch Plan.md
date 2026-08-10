# SmolSearch Plan

SmolSearch is a lightweight search engine for the small Internet, initially targeting **Gemini** and later **Gopher**.

The motivation is straightforward: Gemini search should be fast and dependable even when crawling is slow, broken, or offline. SmolSearch therefore keeps the query path boring: local SQLite FTS5, simple ranking, and no remote network work while serving a search.

The first Gemini proof of concept is complete. The next milestone is a **public snapshot POC**: crawl a useful Gemini corpus on the workstation, freeze the SQLite database, upload that snapshot to the VPS, and allow public searching while clearly stating that the index is not yet continuously updated.

---

# Engineering Rules

1. **Crawling may be slow or broken. Search must not care.**
2. **SQLite + FTS5 stays until measurements prove it is inadequate.**
3. **Protocol quirks stay at protocol boundaries.**
4. **Search ranking stays understandable.**
5. **Crawl politely.**
6. **Do not build infrastructure because search engines are traditionally complicated.**
7. **Gemini first, Gopher second. Other protocols wait.**

Do not add Elasticsearch/OpenSearch, Redis, RabbitMQ, NATS, PostgreSQL, Kubernetes, distributed crawling, vector search, machine-learning ranking, or an analytics platform unless a measured problem actually requires them.

---

# Current Architecture

The current solution is:

```text
SmolSearch.Core
SmolSearch.Storage
SmolSearch.Crawler
SmolSearch.Server
SmolSearch.HappyGemini

HappyGemini remains a separate project and knows nothing SmolSearch-specific. It provides generic plugin loading, Gemini request/response handling, virtual-host routing, TLS, and generic host services such as IHttpClientFactory.

SmolSearch.HappyGemini is a SmolSearch-owned HappyGemini plugin. It owns its own configuration and presentation logic.

Current query path:

Lagrange / Gemini client
        │
        │ Gemini
        ▼
HappyGemini
        │
        │ plugin invocation
        ▼
SmolSearch.HappyGemini
        │
        │ internal HTTP
        ▼
SmolSearch.Server
        │
        ▼
SQLite FTS5

Current crawl path:

Gemini capsules
      │
      ▼
SmolSearch.Crawler
      │
      ▼
Gemini fetch + Gemtext parse
      │
      ▼
Normalized SearchDocument
      │
      ▼
SQLite + FTS5

The crawler and search server are separate processes. Search does not depend on crawler health.

Current Storage and Search

SQLite runs in WAL mode.

The current document store contains:

documents
---------
id
url
title
content
content_type
fetched_at

The external-content FTS5 table indexes:

title
content
url

Search currently provides:

FTS5 MATCH queries;
BM25 ordering;
up to 20 results by default;
title fallback to URL;
query-aware FTS5 snippets;
Gemini-safe single-line display text.

Search results are normalized through SearchResult and returned by SmolSearch.Server as JSON. The Gemini plugin turns those results into Gemtext.

Gemini Crawler Status

Implemented:

Gemini TLS connections;
persistent TOFU certificate pins;
Gemini request/response parsing;
redirect following with a redirect limit;
request timeout;
response-size limit;
tolerant UTF-8 decoding;
text/gemini indexing;
Gemtext title extraction;
Gemtext link extraction;
relative-link resolution;
URL deduplication through the persistent frontier;
persistent crawl frontier;
per-page failure isolation;
graceful cancellation;
crawl page caps for controlled runs;
polite delay between requests.

The crawler has already indexed real Geminispace and resumed from persistent frontier state across runs.

Still intentionally deferred for the production crawler:

host-aware scheduling/fairness;
bounded multi-host concurrency;
persistent host failure state;
retry/backoff scheduling;
recrawl scheduling;
duplicate-content hashes;
stronger protection against pathological infinitely generated URL spaces;
broader crawl metrics.
POC Status — COMPLETE

The original Gemini POC is complete.

Proven end-to-end:

Internet
   ↓
Gemini crawler
   ↓
SQLite
   ↓
FTS5
   ↓
SmolSearch.Server
   ↓
SmolSearch.HappyGemini
   ↓
HappyGemini
   ↓
Lagrange

The POC has demonstrated:

Real Gemini pages can be fetched.
Gemtext links are discovered automatically.
A persistent frontier can continue across crawler runs.
Real documents are indexed into FTS5.
/search uses Gemini status 10 for input.
Search returns real ranked links in Lagrange.
Results include query-aware snippets.
Search remains independent from crawler activity.

No further work is required to prove the architecture.

Next Milestone — Public Snapshot POC

The first public version does not need continuous crawling.

For this milestone:

Workstation crawl session
        │
        ▼
Frozen smolsearch.db snapshot
        │
        │ upload
        ▼
VPS SmolSearch.Server
        │
        ▼
SmolSearch.HappyGemini
        │
        ▼
gemini://gemini.kgivler.com/search

The public capsule must clearly state that this is a proof of concept using a static crawl snapshot and that the index is not currently updated continuously.

This is an intentional deployment mode, not a disguised production crawler.

Required before public snapshot
1. Harden public query handling

Raw public input must not be able to turn an ordinary malformed FTS5 query into an HTTP 500 or broken Gemini request.

Before launch:

define how ordinary user text is translated to FTS5 syntax;
handle malformed quotes/operators safely;
return a useful Gemini response rather than exposing an exception;
verify empty/no-result searches behave cleanly.

The public interface should be forgiving even if FTS5 internally supports more syntax than SmolSearch wants to expose.

2. Handle backend failure as Gemini failure

If SmolSearch.Server is unavailable or returns an error, the HappyGemini plugin should return an appropriate temporary Gemini failure instead of allowing an unhandled HTTP exception to escape.

A broken internal API should not look like a broken Gemini server.

3. Add explicit POC/snapshot notice

The public search page should say, concisely, that:

SmolSearch is a proof of concept;
the current corpus is a static crawl snapshot;
the index is not continuously updated yet;
results may therefore become stale.

Optionally include the snapshot generation date.

4. Make the database path deployment-safe

SmolSearch.Server should not depend accidentally on whichever working directory the process starts in.

Use an explicit configurable database path for deployment so replacing a snapshot is predictable.

5. Keep the HTTP API private

SmolSearch.Server is an internal search service for the Gemini plugin during this milestone.

Do not expose its HTTP port publicly unless there is a deliberate reason to offer a public HTTP API later.

6. Build a useful but deliberately limited corpus

Do not attempt to crawl all of Geminispace for the public POC.

Target a corpus large enough to demonstrate useful search and diverse enough that one prolific capsule does not dominate every query.

A reasonable first snapshot target is roughly:

1,000–5,000 indexed documents

Quality and host diversity matter more than raw count.

Before doing a much larger crawl, implement host-aware scheduling/fairness.

7. Freeze SQLite correctly

Because the database uses WAL mode, do not copy a live database file blindly.

For the snapshot handoff:

Stop the crawler and any local process writing the database.
Checkpoint/truncate the WAL.
Run an SQLite integrity check.
Keep a local backup of the exact snapshot being deployed.
Upload the completed database snapshot to the VPS.
Start SmolSearch.Server against that explicit snapshot path.

The deployed database should be treated as immutable crawl data for this POC. SQLite may still create normal WAL/SHM runtime files when the server opens it.

8. Smoke-test the public deployment

At minimum test:

normal query          linux
normal query          gemini
phrase/punctuation    "gemini server"
weird punctuation     quotes/operators that previously could upset FTS5
zero-result query     intentionally nonsense text
empty /search         receives status 10 prompt
backend unavailable   returns a controlled Gemini temporary failure

Also verify:

the live TLS certificate is correct;
/search is available only on the intended Gemini virtual host;
Mystery does not inherit the SmolSearch page;
result links and snippets render correctly;
search remains fast on the VPS.
9. Measure a small baseline

Record at least:

snapshot document count
snapshot database size
representative warm query latency
zero-result query latency

This becomes the baseline for later 10K/100K corpus experiments.

Definition of Public Snapshot POC Complete

The public snapshot milestone is complete when:

A workstation crawl produces a useful multi-host Gemini corpus.
The frozen database passes an integrity check.
The exact snapshot is uploaded to the VPS.
SmolSearch.Server serves that snapshot independently of the crawler.
gemini://gemini.kgivler.com/search is publicly usable.
The page clearly identifies the index as a non-continuously-updated POC snapshot.
Malformed public queries do not produce unhandled server errors.
Internal backend failures produce a controlled Gemini response.
Live TLS and virtual-host isolation are correct.
Representative searches return quickly and useful results are visible in Lagrange.

At that point the public can use SmolSearch even though crawling remains an offline workstation process.

After the Public POC — MVP Work
1. Host-aware crawler scheduling

This is the next important crawler improvement.

Goals:

one active request per host
configurable host delay
bounded global concurrency
fairness between hosts
one pathological host cannot dominate the frontier

The current FIFO-style frontier is good enough to prove persistence but not good enough for a long-running public crawler.

2. Failure tracking and backoff

Track enough host/URL state to distinguish:

recent success;
temporary failure;
repeated temporary failure;
permanent failure;
unreachable host.

Retry temporary failures with backoff rather than treating all failures equally.

3. Continuous Gemini crawling and recrawling

Move from discovery sessions to a scheduler that can run indefinitely.

Possible recrawl behavior:

recently changed page       sooner
stable page                 less often
temporary failure           exponential backoff
permanent failure           rarely
gone/unreachable            retain metadata, stop frequent crawling

Do not immediately delete temporarily unavailable small-web content.

4. Larger Gemini corpus

Grow deliberately:

1,000
10,000
100,000+

Measure at each stage:

database size;
crawl/index speed;
warm search latency;
zero-result rate;
result quality;
host diversity.

Do not assume a larger corpus is automatically a better corpus.

5. Gopher support

Gopher remains protocol #2.

Initial support:

type 0  text documents
type 1  menus/discovery
type 7  search-service metadata/discovery where useful

Gopher implementation should remain isolated at the fetch/parser boundary. The normalized document and search layers should not care whether content came from Gemini or Gopher.

6. Search quality

Only add features when real searches show they help.

Candidates:

title/body/URL weighting;
phrase handling;
protocol filter;
host filter;
result diversity by host;
availability/reliability signal;
very small freshness signal;
later link-based signals such as unique inbound hosts.

Do not implement PageRank during the MVP.

7. Link graph and host state

When Gopher, backlinks, recrawling, and ranking signals require them, add explicit link/host storage.

Potential conceptual tables:

links
-----
source_document_id
target_url
target_protocol

hosts
-----
protocol
host
last_success
last_failure
failure_count
next_allowed_fetch
last_crawled

Do not add these merely because the original design imagined them; add them when the crawler/search features actually consume them.

8. Observability

Keep it boring.

Useful crawler metrics:

queued URLs
attempted URLs
successful fetches
failed fetches
active hosts
documents indexed
bytes downloaded
crawl latency
status codes

Useful search metrics:

queries
results returned
FTS latency
total query latency
zero-result queries

Do not build an analytics platform.

Definition of MVP Complete

SmolSearch MVP is complete when:

Gemini crawling runs continuously;
Gopher crawling runs continuously;
both protocols feed the same searchable corpus;
search stays available while crawling;
search latency remains consistently low;
host throttling and fairness are polite;
broken hosts cannot stall the crawler;
the corpus survives restarts;
basic recrawling/backoff exists;
search results are useful enough to prefer SmolSearch over existing alternatives.
Future Discovery Features

Once search and continuous crawling are useful, the existing corpus can support:

/recent
/new
/random
/hosts
/backlinks
/related

Potential future work also includes a voluntary webring/directory and, much later, optional privacy-respecting text sponsorships.

None of these should delay reliable search.