# SmolSearch Plan

SmolSearch is a lightweight search engine for the small Internet, initially targeting **Gemini** and **Gopher**.

The motivation is straightforward: existing Gemini search engines are unreliable enough that basic searches frequently time out or fail. SmolSearch should prioritize **fast search, simple architecture, polite crawling, and reliable availability** over clever infrastructure.

The first goal is not to build the Google of Geminispace. The first goal is to prove that a small, boring implementation can crawl useful content and return good search results quickly.

---

# Goals

## Primary goals

- Crawl Gemini capsules.
- Crawl Gopher servers.
- Normalize content from both protocols into one searchable document model.
- Store crawl metadata and content in SQLite.
- Use SQLite FTS5 for full-text indexing and BM25 ranking.
- Expose search over Gemini.
- Return search results quickly and reliably.
- Keep crawling completely independent from serving search requests.
- Be polite to remote servers.
- Remain simple enough to run comfortably on a small VPS.

## Secondary goals

Once the core search engine is useful:

- Search filtering by protocol, host, title, etc.
- Recent pages.
- Newly discovered pages.
- Random page/capsule discovery.
- Host directory.
- Backlinks.
- Related pages/capsules.
- Link-based ranking signals.
- Webring/directory functionality.
- Potential optional privacy-respecting sponsorship/advertising.

---

# Non-Goals for the MVP

Do **not** build these until the core system proves it needs them:

- Distributed crawling.
- Elasticsearch/OpenSearch.
- Redis.
- RabbitMQ.
- NATS.
- Kubernetes.
- PostgreSQL.
- Microservices.
- PageRank.
- Complex graph algorithms.
- Machine-learning ranking.
- Semantic/vector search.
- JavaScript/web frontend.
- Admin dashboard.
- User accounts.
- Analytics platform.
- Spartan, Nex, Finger, or other protocols.
- Gopher+.
- Image search.

If SQLite + FTS5 becomes an actual limitation, replace it because measurements demonstrate that limitation, not because search engines are traditionally complicated.

---

# High-Level Architecture

Initial solution:

```text
SmolSearch.sln

src/
    SmolSearch.Core
    SmolSearch.Storage
    SmolSearch.Crawler
    SmolSearch.Server

tests/
    SmolSearch.Core.Tests
    SmolSearch.Storage.Tests
    SmolSearch.Crawler.Tests
```

Keep the project count small initially.

Gemini and Gopher should be treated as protocol adapters inside the crawler rather than separate projects until separation is clearly useful.

The overall data flow:

```text
                 Gemini
                    │
                    ▼
             Gemini Fetcher
                    │
                    │
                    ▼
               Parser
                    │
                    │
Gopher              │
  │                 │
  ▼                 │
Gopher Fetcher ─────┤
                    │
                    ▼
            Normalized Document
                    │
                    ▼
              SQLite + FTS5
                    │
                    ▼
             Search Service
                    │
                    ▼
              Gemini Server
```

The key architectural rule:

> Crawling may be slow, broken, or temporarily unavailable. Search must not care.

---

# Core Domain Model

Both Gemini and Gopher content should normalize into the same model.

Example:

```csharp
public sealed record SearchDocument
{
    public required Uri Url { get; init; }

    public required string Protocol { get; init; }

    public required string Host { get; init; }

    public string? Title { get; init; }

    public string? Content { get; init; }

    public string? ContentType { get; init; }

    public DateTimeOffset FetchedAt { get; init; }

    public string? ContentHash { get; init; }
}
```

Crawler output should distinguish the fetched document from discovered links.

Example:

```csharp
public sealed record CrawlResult
{
    public required SearchDocument Document { get; init; }

    public IReadOnlyCollection<Uri> Links { get; init; }
        = Array.Empty<Uri>();
}
```

This allows Gemini and Gopher parsers to behave differently while the indexer remains protocol-agnostic.

---

# Storage

Use SQLite.

Enable WAL mode:

```sql
PRAGMA journal_mode = WAL;
```

The expected workload is ideal for it:

- one crawler writer;
- one or more search readers;
- relatively small textual documents;
- a corpus measured in hundreds of thousands or low millions of pages rather than billions.

## Documents

Initial conceptual schema:

```text
documents
---------
id
url
protocol
host
title
content
content_type
status
fetched_at
last_success
content_hash
```

`url` should be unique.

## Links

```text
links
-----
source_document_id
target_url
target_protocol
```

Store links immediately even if the target has not been crawled yet.

This gives us the link graph for later:

- discovery;
- backlinks;
- host relationships;
- ranking signals;
- related pages.

## Hosts

```text
hosts
-----
id
protocol
host
last_success
last_failure
failure_count
next_allowed_fetch
last_crawled
```

Host-level crawl state becomes important for politeness and failure handling.

---

# FTS5

Create an FTS5 index over the useful textual fields.

Conceptually:

```sql
CREATE VIRTUAL TABLE document_fts USING fts5(
    title,
    content,
    url
);
```

Exact schema may use external-content tables once implementation begins.

Initial search:

```sql
SELECT
    url,
    title,
    bm25(document_fts) AS rank
FROM document_fts
WHERE document_fts MATCH $query
ORDER BY rank
LIMIT 20;
```

Remember that FTS5's BM25 score sorts with better matches first when ordered ascending.

Initial ranking should remain deliberately simple.

Potential weights:

```text
Title match       strong
Body match        normal
URL match         weak
```

Search quality should be evaluated on real queries before adding more ranking logic.

---

# Gemini Crawler

Gemini is protocol #1 because the immediate problem is unreliable Gemini search.

The first crawler does not need to crawl all of Geminispace.

It only needs to establish the full vertical slice.

## Gemini fetch process

```text
URL
 ↓
TLS connection
 ↓
Gemini request
 ↓
response header
 ↓
status handling
 ↓
content
 ↓
Gemtext parser
 ↓
document + links
```

Relevant response handling:

```text
1x  input
2x  success
3x  redirect
4x  temporary failure
5x  permanent failure
6x  client certificate
```

For crawling, primarily care about:

- `20` success;
- `30/31` redirect;
- `40–44` temporary failures;
- `50–59` permanent failures.

A `53 Proxy Request Refused` should not be repeatedly retried as though it were an ordinary transient failure.

## Content types

Initially index:

```text
text/gemini
text/plain
```

Everything else can be recorded as metadata without indexing its body.

## Gemtext parsing

Extract:

- first useful heading as title;
- textual body;
- `=>` links.

Do not attempt Markdown-like interpretation beyond what search requires.

---

# Gopher Crawler

Gopher is protocol #2 and should be added after the Gemini vertical slice works.

Use the same normalized document model and storage.

Initially handle:

```text
0  text file
1  menu
7  search service
```

Type `1` menus primarily provide:

- searchable menu text;
- discovered links.

Type `0` documents provide searchable text.

Do not crawl arbitrary binary item types during the MVP.

Potential later support:

```text
9  binary
g  GIF
I  image
h  HTML
```

These may be useful for metadata but are not necessary for full-text search.

Gopher's oddities should remain isolated inside its protocol implementation:

- selectors;
- host/port menu fields;
- malformed menus;
- `URL:` selectors;
- historical server quirks.

The storage/search layer should not care.

---

# Crawl Frontier

The early proof-of-concept can use a simple in-memory queue.

Example conceptual state:

```text
Frontier
--------
queued URL
protocol
discovered from
depth
```

For the production crawler, scheduling should become host-aware.

Example:

```text
HostState
---------
Host
Queue<Uri>
NextAllowedFetch
ConsecutiveFailures
LastSuccessfulFetch
```

Important rules:

- only one active request per host;
- configurable host delay;
- bounded global concurrency;
- never allow one broken host to stall the whole crawler.

A reasonable initial production target:

```text
Global workers:      8
Per-host concurrency: 1
Host delay:          500–1000 ms
```

These numbers should remain configurable.

---

# Crawler Safety

Before pointing SmolSearch at a large corpus, implement:

- URL deduplication.
- Host-level throttling.
- Request timeout.
- TLS timeout.
- Maximum response size.
- Redirect limit.
- Crawl depth/page limits for test runs.
- Duplicate content detection.
- Failure tracking.
- Retry scheduling.
- Protection against infinitely generated URL spaces.
- Content-type filtering.
- Graceful cancellation.
- Persistent frontier or recrawl state.

A hostile or broken remote server must not be able to consume unlimited:

- sockets;
- memory;
- disk;
- crawler workers;
- database rows.

---

# URL Canonicalization

Canonicalization matters early because duplicate URLs poison both the corpus and ranking.

At minimum:

- lowercase schemes;
- lowercase hostnames;
- normalize default ports;
- normalize equivalent root paths;
- remove fragments where appropriate;
- resolve relative Gemini links;
- normalize Gopher URLs carefully without destroying selector semantics.

Do not aggressively rewrite URLs unless equivalence is certain.

---

# Search Service

The search request path must remain extremely small:

```text
request
 ↓
parse query
 ↓
FTS5 query
 ↓
hydrate top results
 ↓
render Gemtext
 ↓
response
```

There must be:

- no crawling;
- no remote networking;
- no graph traversal;
- no expensive asynchronous work;
- no dependency on crawler health.

Initial performance target:

```text
Warm query:
< 50 ms total server processing
```

The likely result should be much lower.

Instrument every query.

Example:

```text
query="freebsd"
fts=4ms
hydrate=1ms
render=1ms
total=6ms
results=20
```

If search becomes slow, we should know exactly which step caused it.

---

# Search Interface

SmolSearch itself should be usable through Gemini.

Initial endpoint:

```text
gemini://search.example/search
```

Request:

```text
10 Search SmolSearch:
```

Query:

```text
freebsd gopher
```

Response:

```text
# Search: freebsd gopher

20 results

=> gemini://example.org/freebsd.gmi FreeBSD Notes
example.org
Some text from the result...

=> gopher://example.net/0/freebsd.txt Running Gopher on FreeBSD
example.net
Some text from the result...
```

Results should identify protocol without overwhelming the display.

---

# Query Syntax

POC:

```text
freebsd gopher
```

MVP may add:

```text
"freebsd jail"

protocol:gemini

protocol:gopher

host:example.org

title:freebsd
```

Do not build a full Boolean query language unless users demonstrate a need for it.

FTS5 already provides useful syntax internally, but exposing all of it directly may create a poor interface.

---

# Snippets

Search results should eventually include a short relevant excerpt.

Prefer FTS5's snippet/highlight capabilities rather than scanning entire document bodies manually.

Example:

```text
=> gemini://example.org/post.gmi FreeBSD and Gemini
example.org
...running the Gemini server inside a FreeBSD jail...
```

Avoid giant result pages.

---

# Ranking

Start with BM25.

Do not implement PageRank during the POC.

Potential later ranking:

```text
Final score

BM25
+ title boost
+ exact phrase boost
+ unique inbound host boost
+ small inbound link boost
+ availability/reliability factor
+ very small freshness factor
```

Unique inbound hosts are potentially more useful than raw inbound links.

A capsule containing 50,000 internally generated links should not become authoritative simply because it links to itself repeatedly.

Ranking should favor useful content without recreating SEO hell.

---

# Availability Signal

SmolSearch should know whether indexed content is still reachable.

Possible fields:

```text
last_success
last_failure
failure_count
```

Search ranking may eventually slightly penalize pages that have been unreachable for extended periods.

Do not immediately delete temporarily unavailable content.

Small-network servers disappear and return frequently.

---

# Recrawling

The first crawler is discovery-oriented.

Later, URLs should have scheduled recrawls.

Possible strategy:

```text
Recently changed page       crawl sooner
Stable page                 crawl less often
Repeated temporary failure  exponential backoff
Permanent failure           crawl rarely
Gone                        retain metadata, stop frequent crawl
```

A full scheduler is not necessary for the initial POC.

---

# Seeding

Initially seed manually with known healthy Gemini capsules.

Once basic crawling is proven, bootstrap from existing known-host lists where available.

After enough links have been discovered, SmolSearch should maintain its own graph and not depend on another search engine for continued discovery.

Seed sources are hints, not dependencies.

---

# Process Isolation

Crawler and search server should eventually run as separate processes.

Example:

```text
SmolSearch.Crawler
        │
        │ writes
        ▼
  smolsearch.db
        ▲
        │ reads
SmolSearch.Server
```

SQLite WAL allows readers to continue while the crawler writes.

If the crawler crashes:

```text
Search keeps working.
```

If a remote Gemini host hangs:

```text
Search keeps working.
```

If crawling is disabled for maintenance:

```text
Search keeps working.
```

This is intentional.

---

# Observability

Keep telemetry boring but useful.

Crawler metrics:

```text
queued URLs
crawled URLs
successful fetches
failed fetches
active hosts
documents indexed
bytes downloaded
crawl latency
status codes
```

Search metrics:

```text
queries
results returned
FTS latency
total query latency
zero-result queries
```

Logging should make pathological hosts easy to identify.

Example:

```text
host=example.org
url=gemini://example.org/foo
status=20
duration=183ms
bytes=4212
links=8
```

---

# POC Milestones

## Milestone 1 — Repository and Models

Create:

```text
SmolSearch.sln

SmolSearch.Core
SmolSearch.Storage
SmolSearch.Crawler
SmolSearch.Server
```

Define:

- `SearchDocument`
- `CrawlResult`
- crawler/storage abstractions

Do not over-design interfaces.

---

## Milestone 2 — SQLite + FTS5

Create the database.

Prove manually that:

```text
insert document
 ↓
FTS5 index
 ↓
MATCH query
 ↓
BM25 result
```

works.

This milestone proves the search technology before crawler complexity exists.

---

## Milestone 3 — Fetch One Gemini Page

Implement enough Gemini client behavior to:

```text
fetch URL
 ↓
read response
 ↓
parse status
 ↓
return body
```

Use existing Gemini/shared TCP infrastructure where appropriate rather than duplicating solved protocol code.

---

## Milestone 4 — Parse Gemtext

Extract:

- title;
- searchable text;
- links.

Store the page.

Verify it can be searched.

---

## Milestone 5 — Small Frontier

Start with one seed.

Recursively crawl a deliberately small number of pages.

Example limit:

```text
100 pages
```

Verify:

- duplicates are avoided;
- discovered links enter the queue;
- fetched pages enter FTS5.

---

## Milestone 6 — Gemini Search Endpoint

Expose:

```text
/search
```

using Gemini input status `10`.

Search FTS5 and render links.

At this point the entire vertical slice exists:

```text
Internet
 ↓
crawler
 ↓
SQLite
 ↓
FTS5
 ↓
Gemini search
```

This is the POC.

---

# Definition of POC Complete

The POC is complete when:

1. SmolSearch can crawl at least one real Gemini capsule.
2. Links are discovered automatically.
3. At least dozens of documents are indexed.
4. A user can connect with Lagrange.
5. `/search` prompts for a query.
6. The query returns relevant links.
7. Search requests return quickly.
8. Crawler activity does not block searches.

Nothing else is required.

---

# MVP Milestones

After the POC works:

## 1. Crawler hardening

Implement:

- host throttling;
- bounded concurrency;
- timeout handling;
- redirects;
- response limits;
- failure/backoff behavior;
- persistent crawl state.

## 2. Larger Gemini corpus

Grow from dozens to:

```text
1,000
10,000
100,000+
```

documents incrementally.

Measure:

- DB size;
- indexing speed;
- search latency;
- search quality.

## 3. Gopher support

Add:

```text
Gopher fetcher
Gopher menu parser
type 0 indexing
type 1 discovery
```

Feed results into the same document/index pipeline.

## 4. Search quality

Add only if useful:

- field weighting;
- snippets;
- protocol filters;
- host filters;
- phrase handling;
- result diversity.

## 5. Public deployment

Deploy the crawler and search server independently.

Monitor search latency and crawler behavior.

---

# Definition of MVP Complete

SmolSearch MVP is complete when:

- Gemini crawling runs continuously.
- Gopher crawling runs continuously.
- Search indexes both protocols.
- Search remains available while crawling.
- Search latency is consistently low.
- Host throttling is polite.
- Broken hosts cannot stall crawling.
- The corpus survives restarts.
- Recrawling exists at least in basic form.
- Search results are useful enough to prefer SmolSearch over existing alternatives.

---

# Future Discovery Features

Once search works well, SmolSearch naturally has enough data for additional discovery tools.

Possible endpoints:

```text
/search
/recent
/new
/random
/hosts
/backlinks
/related
```

## Random

Random document or capsule discovery fits the small-web culture particularly well.

Potential filters:

```text
/random
/random?protocol=gemini
/random?protocol=gopher
```

## Recent

Show recently changed indexed content.

## New

Show newly discovered capsules/servers.

## Hosts

Directory of indexed hosts.

## Backlinks

Show pages linking to a particular URL.

---

# Webring

A voluntary SmolSearch webring could eventually use the existing host/link infrastructure.

Possible functions:

```text
Previous
Random
Next
```

Membership should be explicit rather than assuming every indexed site wants to participate.

This is future functionality and should not delay search.

---

# Sponsorship / Advertising

Potential far-future experiment:

A small-web advertising/sponsorship system consisting only of text links.

Example:

```text
## Sponsored

=> gemini://example.org/ Something interesting
```

Design principles:

- no JavaScript;
- no cookies;
- no fingerprinting;
- no tracking pixels;
- no user profiles;
- no behavioral targeting;
- no third-party client-side resources.

Possible accounting:

```text
campaign
publisher
coarse delivery count
click count
```

Any advertising system should remain optional and separate from organic ranking.

Sponsored results, if ever added, must be clearly labeled.

Do not build this until SmolSearch has users. The villain arc can wait.

---

# Engineering Principles

## 1. Boring is good

The corpus is small.

Take advantage of that.

## 2. Measure before scaling

Do not replace SQLite because a hypothetical future corpus might be large.

Replace it only if actual measurements justify doing so.

## 3. Search must stay fast

Crawler complexity must never leak into the query path.

## 4. Crawl politely

SmolSearch should not become the asshole scanner everyone blocks.

## 5. Protocol quirks stay at protocol boundaries

Gemini and Gopher differences belong in their fetchers/parsers.

The index should receive normalized documents.

## 6. Ranking should remain understandable

Prefer simple signals whose effects we can inspect.

## 7. Do not optimize for SEO

The purpose is helping people find useful small-web content, not creating a new algorithm people spend their lives gaming.

## 8. Add protocols intentionally

Gemini and Gopher are enough for the initial product.

Spartan, Nex, Finger, and other protocols may eventually make sense, but not until the first two work well.

---

# First Work Session

Today's target:

```text
Gemini URL
    ↓
fetch
    ↓
parse
    ↓
SQLite
    ↓
FTS5
    ↓
search
```

Recommended order:

1. Create repository.
2. Create solution/projects.
3. Add SQLite dependency.
4. Verify FTS5 support.
5. Create schema.
6. Insert a fake document.
7. Search fake document.
8. Fetch one Gemini document.
9. Parse title/body/links.
10. Insert real document.
11. Search real document.
12. Add a tiny crawl frontier.
13. Crawl a small controlled set.
14. Expose `/search` through Gemini.
15. Test from Lagrange.

Stop there if necessary.

If that works, SmolSearch already exists.

Everything after that is improvement.