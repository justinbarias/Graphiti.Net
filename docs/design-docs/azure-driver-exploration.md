# Azure driver exploration

Status: exploration, 2026-11-28. No driver code exists. The maintainer made each decision in this document on 2026-11-28.

## Purpose

The maintainer asked for a graph database driver that runs on Azure as a native service.
The first question was Azure Cosmos DB, with the Gremlin API or the NoSQL API.
During the interview the maintainer added Azure Database for PostgreSQL and Azure SQL Database to the comparison.

This document lists what the database must do, compares four Azure options, and records the decisions.
It ends with a driver sketch and a spike plan.
All facts about Azure come from pages that were read on 2026-11-28. Each fact names its source, and the source list gives the date of each page.

## Result

The target is Azure Database for PostgreSQL flexible server, with plain tables, pgvector, and the built-in full-text search.

- The Gremlin API of Cosmos DB does not fit. It has no vector search, no scored full-text search, and no transactions.
- The NoSQL API of Cosmos DB fits with limits. It has no graph operator, so the driver builds the graph itself on a document store.
- A driver that builds the graph itself gets more from a relational database: full transactions, joins, and recursive queries.
- PostgreSQL has the best local test story of the four. Azure SQL Database has the best full-text rank, but its container does not run on arm64.

## Decisions of the maintainer

| # | Question | Decision on 2026-11-28 |
| --- | --- | --- |
| 1 | Which Cosmos DB API, if Cosmos DB is the target? | The NoSQL API. The maintainer then asked why the target is not PostgreSQL or Azure SQL. |
| 2 | One Azure service or two? | One service. The maintainer prefers PostgreSQL flexible server or Azure SQL to a pair of services. |
| 3 | Which Azure database is the target? | Azure Database for PostgreSQL flexible server. |
| 4 | How does the driver store the graph in PostgreSQL? | Plain tables and SQL. No Apache AGE. |
| 5 | How does a driver with no Cypher handle the Cypher call sites that have no hook? | The core gets hook methods for those sites after M1, with the Cypher as the default path. |
| 6 | How do the tests run? | A local container for the end-to-end tests. A real Azure database only with the approval of the maintainer for each run. |
| 7 | Where is the work in the roadmap? | The spike runs after M1. The build keeps the M6 slot, and the maintainer decides it after the spike report. |
| 8 | How does the driver rank full-text results? | `ts_rank_cd`. The spike measures the difference from Neo4j, and the maintainer decides again with the numbers. |
| 9 | Which files does this change write? | This document, the M6 text of the product specification, and the spike plan. |
| 10 | What is the name of the package? | `Graphiti.Net.PostgreSql`. |

Two answers apply only if Cosmos DB NoSQL becomes the target later.
The partition key is `/group_id` with one container for all kinds. The client is `Microsoft.Azure.Cosmos` version 3.

The lead records decisions 3, 4, 5, and 10 in [port/DECISIONS.md](../../port/DECISIONS.md) when the spike starts. This change does not edit the `port/` folder.

## What the database must do

The list comes from the driver contract in `src/Graphiti.Net/Core/Driver/` and the Neo4j package in `src/Graphiti.Net.Neo4j/`, at commit `b35329e`.

| Capability | Where the port uses it | What Neo4j does |
| --- | --- | --- |
| Save one node or edge | `SaveAsync` of the 9 node and edge operation classes | `MERGE` by uuid, then `SET` |
| Save many in one transaction | `SaveBulkAsync`, `BulkUtils.AddNodesAndEdgesBulkTxAsync`, `GraphDriver.TransactionAsync` | `UNWIND` inside one transaction, with commit or rollback |
| A write with a time limit | `GraphDriverSession.ExecuteWriteAsync(func, timeout)` (decision D52) | A transaction timeout of the driver |
| Read by uuid | `GetByUuidAsync`, `GetByUuidsAsync` | `MATCH` by uuid. The call has no group id. |
| Read by group, with a cursor | `GetByGroupIdsAsync(groupIds, limit, uuidCursor)` | A filter on `group_id`, an order by uuid, and a limit |
| Full-text search with a score | `NodeFulltextSearchAsync`, `EdgeFulltextSearchAsync`, `EpisodeFulltextSearchAsync`, `CommunityFulltextSearchAsync` | 4 Lucene indexes, `db.index.fulltext.queryNodes`, a BM25 score, a Lucene query string that holds the group filter |
| Vector similarity search | `NodeSimilaritySearchAsync` (name embeddings), `EdgeSimilaritySearchAsync` (fact embeddings), `CommunitySimilaritySearchAsync` | `vector.similarity.cosine` over each row that passes the filter. The search is exact. The score is normalized to the range 0 to 1, and the default limit is 0.6. |
| Breadth-first search to a fixed depth | `NodeBfsSearchAsync`, `EdgeBfsSearchAsync` | A path pattern `-[:RELATES_TO\|MENTIONS*1..depth]->`. The default depth is 3 (`MaxSearchDepth`). |
| Filters | `SearchFilters`: node labels, edge types, date ranges on 4 date fields, with AND groups inside OR groups | Cypher text from `SearchFiltersModule` |
| Rerank helpers | `NodeDistanceRerankerAsync`, `EpisodeMentionsRerankerAsync` | A match of direct neighbors, and a count of `MENTIONS` edges |
| Index creation and removal | `BuildIndicesAndConstraintsAsync`, `DeleteAllIndexesAsync` | 28 range indexes and 4 full-text indexes from `GraphQueries` |
| Community queries | `GetCommunityClustersAsync`, `DetermineEntityCommunityAsync`, `GetCommunitiesByNodesAsync`, `RemoveCommunitiesAsync` | Counts of neighbors by node, and matches over `HAS_MEMBER` edges |
| Episode queries | `RetrieveEpisodesAsync`, `GetMentionedNodesAsync`, `GetByEntityNodeUuidAsync`, the saga queries | Filters on `valid_at`, order by date, matches over `MENTIONS`, `HAS_EPISODE`, and `NEXT_EPISODE` |
| Deletes | `DeleteAsync`, `DeleteByUuidsAsync`, `DeleteByGroupIdAsync`, `ClearDataAsync` | `DETACH DELETE`, which also removes the edges of a node |
| Embeddings on demand | `LoadEmbeddingsAsync`, `LoadEmbeddingsBulkAsync` | A read of one property. Other reads leave the embedding out. |

The default embedding has 1,024 dimensions (`EMBEDDING_DIM` in `Core/Embedder/Client.cs`). A host can set another size.

The search layer of the core combines the result lists by rank (RRF, MMR, node distance, episode mentions, cross-encoder).
So the order of each list is part of the behavior, and the raw score values are not.

## How upstream handles a missing capability

Upstream has four drivers at the pinned commit. All four speak a Cypher dialect.

| Driver | Query language | Full-text search | Vector search |
| --- | --- | --- | --- |
| Neo4j | Cypher | Lucene indexes in the database | Exact cosine in the query |
| FalkorDB | Cypher, with its own dialect for full-text queries and vector functions | RediSearch syntax from `falkordb/fulltext.py` | `vec.cosineDistance` in the query |
| Neptune | openCypher | A second service, Amazon OpenSearch. `save_to_aoss` writes each item to an index, and `run_aoss_query` returns uuids and scores. A Cypher query then reads the rows. | None in the database. The driver reads each embedding of the group and computes the cosine in Python. |
| Relate | Cypher that `translate_relate_query` rewrites for the store | In the store | In the store |

Three points follow.

- Upstream accepts a second service for search (Neptune) and client logic for vectors (Neptune).
- No upstream driver works without Cypher. A PostgreSQL driver with plain SQL is the first one.
- The Relate driver sets both legacy hook classes and all 11 operation classes. A new driver follows that shape.

## Where the driver contract assumes Cypher

The port keeps the structure of upstream, so these points apply to any driver that does not speak Cypher.

1. `QueryExecutor.ExecuteQueryAsync(string cypherQuery, ...)` is the base of the contract. `Transaction.RunAsync` and `GraphDriverSession.RunAsync` take the same Cypher string.
2. The core holds 97 calls of `ExecuteQueryAsync` with Cypher text, outside the driver folder. They are in `Nodes.cs`, `Edges.cs`, `Search/SearchUtils.cs`, `Graphiti.cs`, and three files of `Utils/Maintenance/`.
3. A scan of those files found 77 methods that first try a legacy hook (`GraphOperationsInterface` or `SearchInterface`) and then fall back to Cypher. A driver that sets both hook classes and overrides each method never reaches that Cypher.
4. The scan found 7 methods with Cypher and no hook. They hold 13 call sites.
   - On public paths: `GraphitiCore.FindSagaAsync` and `GraphitiCore.RemoveEpisodeAsync`.
   - A public method: `GraphitiCore.SummarizeSagaAsync`.
   - With no caller in the port today: `SearchUtils.GetRelevantNodesAsync`, `GetRelevantEdgesAsync`, `GetEdgeInvalidationCandidatesAsync`, and `EdgeOperations.FilterExistingDuplicateOfEdgesAsync`.
5. `GraphProvider` has four members. About 70 sites choose a query dialect from it, and an unknown provider gets the Neo4j dialect.
6. `SearchFiltersModule` builds filter text in Cypher. The hooks receive the `SearchFilters` object, so a driver can build its own filter text.
7. `BuildFulltextQuery` returns a Lucene string. A driver with another text engine builds its own query from the same input.
8. The transaction forms pass a `Transaction` object to a work function. The bulk hooks receive that object, so a driver can give it typed members.
9. The driver contract is `internal`. A new driver package needs an `InternalsVisibleTo` entry in the core project, which only the lead changes (rule R3).

The scan is a script result on one commit. The first spike step repeats it with a script that stays in the repository.

Decision 5 covers point 4: the core gets a hook for each of those methods after M1.
Points 5 and 9 need the same kind of change: one new member of `GraphProvider`, and one `InternalsVisibleTo` entry.
A driver with no Cypher throws `NotSupportedException` from `ExecuteQueryAsync`, so a missed site fails loudly and not silently.

## The four Azure options

| Capability | Cosmos DB Gremlin | Cosmos DB NoSQL | PostgreSQL flexible server | Azure SQL Database |
| --- | --- | --- | --- | --- |
| Data model | Vertices and edges as JSON documents [S1] | JSON items in containers | Tables | Tables, with node and edge tables [S30] |
| Transaction scope | None [S1] | One logical partition, 100 operations, 2 MB, 5 seconds [S13] | Any set of statements | Any set of statements |
| Vector search | None | `flat`, `quantizedFlat`, `diskANN` [S7] | pgvector 0.8.7, exact by default, HNSW and DiskANN indexes [S20] [S21] [S22] | `vector` type, exact `VECTOR_DISTANCE`, DiskANN index [S26] [S27] [S28] |
| Vector limits | Not applicable | 505 dimensions for `flat`, 4,096 for the other two [S7] | 2,000 dimensions for an HNSW index, 16,000 for DiskANN with quantization [S21] [S22] [S24] | 1,998 dimensions as float32, 3,996 as float16 [S26] |
| Full-text search with a score | None. Only `TextP.containing` [S1] | BM25 with `FullTextScore` [S8] [S10] | `tsvector` with `ts_rank`. Not BM25. Azure lists no BM25 extension [S20] | `FREETEXTTABLE` with an Okapi BM25 rank [S32] |
| Hybrid rank in the database | None | `RRF` in `ORDER BY RANK` [S9] [S11] | SQL that the driver writes | SQL that the driver writes |
| Traversal to a fixed depth | `repeat()`, at most 32 iterations [S2] | None. The driver runs one query for each level. | Recursive query | `MATCH` with `SHORTEST_PATH` and `{1,n}`, or a recursive query [S31] |
| .NET client | Gremlin.Net 3.4.13, Apache-2.0 [S1] [S39] | Microsoft.Azure.Cosmos 3.64.0, MIT [S37] | Npgsql 11.0.0, PostgreSQL license [S38] | Microsoft.Data.SqlClient 8.0.0, MIT [S40] |
| Cancellation token | Not in 3.4.13. A later release added it [S42] | Yes | Yes | Yes |
| Native AOT | Not known | No. Two issues are open [S41] | Yes, since Npgsql 8.0 [S43] | Not known |
| Microsoft Entra ID | Not known. The quickstart uses the account key [S5] | Yes, with data plane roles [S17] | Yes, an access token is the password [S23] | Yes, with managed identities [S34] |
| Local container | Windows emulator only [S16] | Linux emulator. DiskANN only, no full-text index [S15] | Any PostgreSQL image with pgvector, 170 MB for arm64 [S45] | x86-64 only. Emulation is not supported. Full-text search is a separate package [S35] [S33] |

### Cosmos DB for Apache Gremlin

- Microsoft puts a note at the top of each Gremlin page. It points new work with high scale to the NoSQL API, and analytic graph work to Microsoft Fabric [S1] [S2] [S3].
- Vertices and edges are JSON documents. An edge lives in the partition of its source vertex, and an `in()` step always fans out [S4].
- The service does not support transactions, Gremlin bytecode, lambdas, the `match()` step, objects as properties, or `null` values [S1].
- A query is a text string. A script is at most 64 KB, and a traversal ends after 30 seconds [S2].
- The supported client is Gremlin.Net 3.4.13 from January 2022. Microsoft reports compatibility issues with 3.5 and 3.6 [S1] [S42].
- The change log lists cancellation of the async methods of Gremlin.Net (TINKERPOP-2794) under release 3.7.0. So the supported client cannot stop a request [S42].
- The service has no vector index and no text index with a score. Both need a second service, for example Azure AI Search.
- The account sets the consistency level for Gremlin. The client cannot set it for one request [S14].
- The Windows emulator has a Gremlin endpoint. The Linux emulator supports only the NoSQL API [S16] [S15].

The Gremlin API gives one thing that the port needs, which is traversal. The port needs it only to depth 3.

### Cosmos DB for NoSQL

- Vector search is on by default. A container has a vector policy with a path, a data type, a dimension count, and a distance function [S7].
- `quantizedFlat` and `diskANN` need 1,000 vectors. Below that number the service scans all vectors [S7].
- `VectorDistance` takes a flag for an exact search, so a small group can get exact results [S10].
- The cosine value is from -1 to +1. Neo4j gives a value from 0 to 1, so the driver maps the limit [S7].
- `diskANN` results can change between two runs of the same query [S7].
- Full-text search has a policy and an index for each text path. `FullTextScore` gives a BM25 score for one path [S8] [S10].
- English is the only language outside preview. The service removes stopwords and applies stemming [S8] [S9].
- The page of the `RRF` function says that the feature needs enrollment. The vector page says that vector search needs none. These pages do not agree [S11] [S7].
- Full-text results can differ between Linux or macOS clients and Windows clients [S9].
- A transactional batch is atomic inside one logical partition. It holds at most 100 operations and 2 MB, and it runs for at most 5 seconds [S13].
- One logical partition holds at most 20 GB. One item holds at most 2 MB [S12].
- A hierarchical partition key removes the 20 GB limit. Microsoft asks for contact before vector search on such a container [S18] [S7].
- A query with no partition key filter runs once for each physical partition [S19].
- Session consistency gives read-your-writes inside one client. The session token belongs to one partition [S14].
- The Linux emulator supports the batch API and DiskANN. It does not support full-text indexes, `flat`, `quantizedFlat`, or request unit numbers [S15].
- The Linux emulator does not enforce authentication [S15].
- The SDK needs `Newtonsoft.Json` as a direct reference of the host [S36].

The NoSQL API has no traversal operator, no joins between items, and no transaction across groups.
The driver writes each of those parts itself.

### Azure Database for PostgreSQL flexible server

- pgvector is extension `vector` version 0.8.7 on PostgreSQL 14 to 19 [S20].
- With no index, pgvector does an exact search with perfect recall. That matches the exact search of Neo4j [S24] [S44].
- An HNSW or IVFFlat index accepts at most 2,000 dimensions. The default of 1,024 fits [S24].
- `pg_diskann` 0.6.6 accepts up to 16,000 dimensions with product quantization. The quantization feature is in preview [S20] [S22].
- The operator `<=>` gives the cosine distance [S21].
- Full-text search is part of PostgreSQL. A `tsvector` holds at most 1 MB of lexemes and positions [S46].
- The extension list of Azure has no BM25 extension. The built-in rank functions are not BM25 [S20].
- Microsoft Entra authentication sends an access token as the password. A managed identity can be a database user [S23].
- An operator adds `vector` to the `azure.extensions` parameter before `CREATE EXTENSION` works [S25].
- Apache AGE is available as an extension (1.6.0 to 1.8.1). Azure limits in-place major upgrades with AGE [S20] [S25] [S6].
- Npgsql 8.0 and later support native AOT and trimming [S43].

### Azure SQL Database

- The `vector` type is generally available. A float32 vector has at most 1,998 dimensions [S26].
- `VECTOR_DISTANCE` is always exact. The cosine distance is from 0 to 2 [S27].
- The vector index (DiskANN) and `VECTOR_SEARCH` are generally available in Azure SQL Database, and in preview in SQL Server 2025 [S28].
- Node and edge tables support `MATCH`. `SHORTEST_PATH` accepts the quantifier `{1,n}` and returns one shortest path for each pair [S30] [S31].
- `FREETEXTTABLE` ranks with the Okapi BM25 formula. `CONTAINSTABLE` uses another formula. The rank is a number from 0 to 1,000 [S32].
- A full-text index fills through a population process after the data changes [S29]. This exploration did not measure the delay.
- Microsoft supports the SQL Server container only on x86-64 hosts. Emulation such as Rosetta 2 is not tested and not supported [S35].
- Full-text search on Linux is the separate package `mssql-server-fts` [S33].

The development machine of this repository is arm64. So a local test of full-text search on SQL Server is not possible on it.

### Other services

- Apache AGE on PostgreSQL runs openCypher. AGE keeps all properties in one column, so vector and full-text search still need hand-written SQL. The maintainer chose plain tables (decision 4).
- Azure AI Search can hold the text index and the vector index next to any store. It adds a second write path and a delay after each write. The maintainer chose one service (decision 2).
- Neo4j can run on Azure virtual machines or as a hosted service of Neo4j in an Azure region. The current driver works there with no change. It is not a service of Microsoft, and this exploration did not study its identity model.
- Graph in Microsoft Fabric is the option that Microsoft names for analytic graph work [S1]. Graphiti is a transactional workload.

## Recommendation

Build the driver on Azure Database for PostgreSQL flexible server.

1. Each write of an episode is one transaction, with no limit on size and no limit to one group.
2. The exact vector search of pgvector matches what Neo4j does today, so vector results can be equal and not only similar.
3. A write updates the text index in the same transaction, so a search after a write sees the new rows.
4. A recursive query gives the breadth-first search in one statement.
5. The same driver runs on any PostgreSQL 16 or later with pgvector. Azure is one host.
6. The tests run on this machine in a small container, next to the Neo4j container.
7. Npgsql supports cancellation tokens and native AOT, which the Cosmos DB SDK and Gremlin.Net 3.4.13 do not.

The cost of this choice is the text rank. `ts_rank_cd` is not BM25, so the order of full-text results can differ from Neo4j.
The spike measures that difference (decision 8).

## Driver sketch for PostgreSQL

This section is a sketch. The stored data shape is an ask-first item, and the spike brings the final schema to the maintainer.

### Package

- Name: `Graphiti.Net.PostgreSql`. Root namespace: `Graphiti.PostgreSql`.
- References: the core project, `Npgsql`, and `Pgvector` (MIT, the type mapping for the `vector` type).
- No reference to `Azure.Identity`. The host builds an `NpgsqlDataSource` with its own token provider.
- Public surface, in the shape of the Neo4j package: `AddPostgreSql(Action<PostgreSqlOptions>)` and `AddPostgreSql(Func<IServiceProvider, NpgsqlDataSource>)`.
- Options: the connection string, the schema name (default `graphiti`), the embedding dimension, and the text search configuration.

### Tables

One table for each node kind and each edge kind, all in one schema.

| Table | Columns |
| --- | --- |
| `entity_node` | `uuid text primary key`, `group_id text`, `name text`, `labels text[]`, `summary text`, `attributes jsonb`, `name_embedding vector(n)`, `created_at timestamptz`, `search tsvector` |
| `episodic_node` | `uuid`, `group_id`, `name`, `content text`, `source`, `source_description`, `valid_at timestamptz`, `created_at`, `entity_edges text[]`, `search tsvector` |
| `community_node` | `uuid`, `group_id`, `name`, `summary`, `name_embedding vector(n)`, `created_at`, `search tsvector` |
| `saga_node` | `uuid`, `group_id`, `name`, `summary`, `first_episode_uuid`, `last_episode_uuid`, `last_summarized_at`, `created_at`, and the other saga fields of the model |
| `entity_edge` (`RELATES_TO`) | `uuid`, `group_id`, `source_node_uuid`, `target_node_uuid`, `name`, `fact text`, `fact_embedding vector(n)`, `episodes text[]`, `created_at`, `expired_at`, `valid_at`, `invalid_at`, `attributes jsonb`, `search tsvector` |
| `episodic_edge` (`MENTIONS`), `community_edge` (`HAS_MEMBER`), `has_episode_edge`, `next_episode_edge` | `uuid`, `group_id`, `source_node_uuid`, `target_node_uuid`, `created_at` |

- Each `search` column is a generated column over the same fields as the Neo4j full-text index of that kind, with a GIN index.
- Each table has an index on `(group_id, uuid)`. Each edge table has indexes on `source_node_uuid` and on `target_node_uuid`.
- The date columns of `entity_edge` and `episodic_node` have indexes, as in the Neo4j index list.
- The first version has no vector index, so the search is exact. An HNSW index is an option for large groups.

### How each operation maps

| Operation | SQL form |
| --- | --- |
| Save | `INSERT ... ON CONFLICT (uuid) DO UPDATE` |
| Bulk save | One statement for each kind with array parameters, inside the transaction of the caller |
| Edge save | `INSERT ... SELECT ... WHERE` both nodes exist. Neo4j writes no edge if a node is absent, and this form does the same. |
| Transaction | `NpgsqlTransaction`. `TransactionAsync` commits on a clean exit and rolls back on an exception. |
| Write with a time limit | `SET LOCAL statement_timeout` inside the transaction |
| Read by uuid | A lookup on the primary key |
| Read by group | `WHERE group_id = ANY(@ids) AND uuid < @cursor ORDER BY uuid DESC LIMIT @n` |
| Full-text search | `WHERE search @@ @q AND group_id = ANY(@ids) ORDER BY ts_rank_cd(search, @q) DESC, uuid LIMIT @n` |
| Vector search | `(2 - (name_embedding <=> @v)) / 2 AS score`, a filter `score > @min`, `ORDER BY name_embedding <=> @v LIMIT @n` |
| Breadth-first search | A recursive query over `entity_edge` and `episodic_edge` from source to target, with a depth counter up to `@depth` |
| Filters | A driver class that turns `SearchFilters` into SQL text with parameters: `labels && @labels`, `name = ANY(@types)`, and the date groups |
| Node distance reranker | One query for edges between the center node and the candidate nodes |
| Episode mentions reranker | `SELECT target_node_uuid, count(*) FROM episodic_edge ... GROUP BY target_node_uuid` |
| Index creation | `CREATE EXTENSION IF NOT EXISTS vector`, `CREATE TABLE IF NOT EXISTS`, `CREATE INDEX IF NOT EXISTS` |
| Index removal | `DROP INDEX` for each secondary index. The tables stay. |
| Community clusters | Neighbor counts with `GROUP BY` over `entity_edge` in both directions. The label propagation stays in the core. |
| Delete a node | One transaction that deletes the node and each edge that names it |
| Delete a group | `DELETE ... WHERE group_id = @id` on each table |
| `ExecuteQueryAsync` with Cypher | `NotSupportedException` with the first line of the query |

The vector score formula is the one that the core already uses for FalkorDB, `(2 - distance) / 2`. It gives the normalized value of Neo4j.

The driver builds the text query itself. Lucene joins the words of a query with OR, so the driver joins the lexemes with `|`.
The group filter is a normal `WHERE` clause and not part of the text query.

### Temporal fields and the group filter

- Each date is a `timestamptz` column. PostgreSQL stores the instant and not the offset.
- The public records use `DateTimeOffset`, so a host cannot create a date with no offset.
- The four dates of an entity edge (`created_at`, `expired_at`, `valid_at`, `invalid_at`) are plain columns. A date filter is a comparison on a column.
- `group_id` is a column of each table and the first column of each main index. A call with no group ids scans all groups, as on Neo4j.

### Tests

- The end-to-end tests get a `postgres` service in `docker-compose.yml`, with an image that has pgvector.
- Each driver test runs on Neo4j and on PostgreSQL with the same fixtures. A result with no defined order is compared as a set.
- The judge has no case for this driver. Its golden files come from Python on Neo4j.
- A real Azure server is needed for two things: the Microsoft Entra sign-in, and the `azure.extensions` allow list. Each run needs the approval of the maintainer.

### What differs from Neo4j

| Area | Neo4j | PostgreSQL driver |
| --- | --- | --- |
| Full-text rank | BM25 of Lucene | `ts_rank_cd`. The order can differ. |
| Text analysis | The standard analyzer of Lucene | A text search configuration. `simple` has no stemming and no stopwords. `english` has both. |
| Full-text query syntax | A Lucene string. Upstream defect U4 drops all groups but the last. | Lexemes joined with OR, and a correct group filter |
| Rows with no `ORDER BY` (upstream defect U6) | An order that can change | Another order that can change |
| Batched delete (upstream defect U1) | Raises `DatabaseError` inside a transaction | Deletes the rows |
| Stored offset of a date | Kept | Not kept. The instant is the same. |
| Embedding dimension | Any length for each row | One fixed length for a column |
| Large text | No limit of the index | A `tsvector` over 1 MB is an error [S46] |
| Edge with an absent node | No edge, no error | The same, by the form of the insert |
| Concurrent queries | One session for each query | One pooled connection for each query. The pool size must cover the concurrency limit of the core. |

## Sketch for Cosmos DB NoSQL, if it becomes the target later

The maintainer chose PostgreSQL. This short sketch records what the exploration found, with the two answers of the maintainer.

- One container holds all kinds, with a `kind` field. The partition key is `/group_id`.
- An item is `{ id: uuid, kind, group_id, ...fields }`. An edge item adds `source_node_uuid` and `target_node_uuid`.
- Each kind uses its own text field name, so the BM25 numbers of one kind do not mix with another kind.
- A save is an upsert. A bulk save is a list of transactional batches of at most 100 operations and 2 MB.
- An item with a 1,024-dimension embedding is about 20 KB of JSON by estimate. So a batch holds fewer than 100 such items.
- A read by uuid with no group id is a query over all physical partitions.
- The breadth-first search is one query for each level, with the uuids of the last level as a parameter.
- The client is `Microsoft.Azure.Cosmos` version 3, with a `TokenCredential` from the host.
- The tests of full-text search, of cost, and of sign-in need a real account, because the emulator lacks them [S15].

## Risks

| Risk | Effect | How the spike answers it |
| --- | --- | --- |
| The text rank differs from BM25 | Search results and the candidate lists in prompts differ from Neo4j | Step 5 reports the overlap of the top 10 results on the same fixtures |
| No oracle | A wrong result has no golden file to fail against | The conformance suite runs each test on both drivers |
| More Cypher sites with no hook than the scan found | A call fails at run time with `NotSupportedException` | Step 1 repeats the scan with a script. Step 6 lists each new hook. |
| The hook change departs from the structure of upstream | Each new upstream pin needs a merge of those methods | The change waits for M1, and it gets a decision record |
| A large episode exceeds the 1 MB limit of a `tsvector` | The save fails | Step 3 measures the limit in characters, and the report proposes a rule |
| Azure limits an extension or a parameter | The driver works in the container and fails on Azure | Step 7 runs the suite on a real server, with approval |
| The connection pool is smaller than the concurrency of the core | Calls wait or time out | Step 4 runs a search with the default concurrency limit |

## Cost notes

- This exploration fetched no price. Read the Azure price pages before any spend.
- PostgreSQL flexible server bills for compute time and storage, not for each request. A stopped development server bills for storage only.
- An `add_episode` call on PostgreSQL is one transaction with a small number of statements: one for each kind that the episode writes.
- A search call sends up to three queries for each kind (full-text, vector, breadth-first), and then the reranker queries.
- A vector of 1,024 dimensions takes about 4 KB as float32. Each entity node and each entity edge holds one.
- An exact vector search reads each embedding of the groups in the filter. Its cost grows with the size of the group.
- Cosmos DB bills in request units. The emulator reports none, so no number exists without a real account [S15].
- The Cosmos DB pages give no request unit number for a vector write or a hybrid query. A spike on a real account is the only source.

## What this exploration does not know

- If the Gremlin data plane of Cosmos DB accepts a Microsoft Entra token. The security page names managed identities, and the .NET quickstart uses the account key [S5].
- If `Microsoft.Data.SqlClient` 8.0.0 is ready for native AOT. Its AOT issues are closed, and no page that was read states support [S40].
- If a `WHERE` clause on `VectorDistance` uses the vector index in Cosmos DB.
- If `RRF` in Cosmos DB accepts two `FullTextScore` terms with no vector term.
- The delay between a write and the full-text index in Azure SQL Database and in Cosmos DB.
- If the data plane SDK of Cosmos DB can create a container with a Microsoft Entra identity.
- The precision of an embedding after a save and a read on each database. The Neo4j queries of the port save through `db.create.setNodeVectorProperty`, and pgvector holds single-precision values [S44].
- The size of the Cosmos DB emulator image. Ask the maintainer before a pull, because the Docker disk of the development machine was full on 2026-11-28.
- All request unit numbers and all prices.

## Spike plan

The plan is [0006 PostgreSQL driver spike](../exec-plans/active/0006-postgresql-driver-spike.md). It starts after M1.
It has eight small steps, each with a model and an effort, and it ends with a report and a build decision of the maintainer.

## Sources

All pages were read on 2026-11-28. The date is the `ms.date` value of a Microsoft Learn page, or the release date of a package or file.

| Id | Page | Date |
| --- | --- | --- |
| S1 | [Cosmos DB for Gremlin support and compatibility](https://learn.microsoft.com/en-us/azure/cosmos-db/gremlin/support) | 2021-07-06, updated 2026-04-27 |
| S2 | [Limits of Cosmos DB for Gremlin](https://learn.microsoft.com/en-us/azure/cosmos-db/gremlin/limits) | 2019-10-04, updated 2026-04-27 |
| S3 | [Cosmos DB for Apache Gremlin overview](https://learn.microsoft.com/en-us/azure/cosmos-db/gremlin/overview) | 2025-07-22 |
| S4 | [Graph partitioning in Cosmos DB for Gremlin](https://learn.microsoft.com/en-us/azure/cosmos-db/gremlin/partitioning) | 2019-06-24, updated 2026-04-27 |
| S5 | [Gremlin .NET quickstart](https://learn.microsoft.com/en-us/azure/cosmos-db/gremlin/quickstart-dotnet) and [Gremlin security](https://learn.microsoft.com/en-us/azure/cosmos-db/gremlin/security) | 2025-07-22 and 2026-04-29 |
| S6 | [Apache AGE extension in Azure Database for PostgreSQL](https://learn.microsoft.com/en-us/azure/postgresql/azure-ai/generative-ai-age-overview) | 2026-06-08 |
| S7 | [Vector search in Cosmos DB for NoSQL](https://learn.microsoft.com/en-us/azure/cosmos-db/vector-search) | 2025-09-25, updated 2026-11-17 |
| S8 | [Full-text search in Cosmos DB for NoSQL](https://learn.microsoft.com/en-us/azure/cosmos-db/gen-ai/full-text-search) | 2026-11-17 |
| S9 | [Full-text search FAQ](https://learn.microsoft.com/en-us/azure/cosmos-db/gen-ai/full-text-search-faq) and [hybrid search](https://learn.microsoft.com/en-us/azure/cosmos-db/gen-ai/hybrid-search) | 2025-10-20 and 2025-09-09, both updated 2026-11-17 |
| S10 | [VECTORDISTANCE](https://learn.microsoft.com/en-us/cosmos-db/query/vectordistance) and [FULLTEXTSCORE](https://learn.microsoft.com/en-us/cosmos-db/query/fulltextscore) | 2025-11-10 and 2026-11-17 |
| S11 | [RRF](https://learn.microsoft.com/en-us/cosmos-db/query/rrf) | 2025-11-10 |
| S12 | [Cosmos DB service quotas](https://learn.microsoft.com/en-us/azure/cosmos-db/concepts-limits) | 2025-09-03, updated 2026-08-25 |
| S13 | [Transactional batch](https://learn.microsoft.com/en-us/azure/cosmos-db/transactional-batch) | 2025-07-16 |
| S14 | [Consistency levels](https://learn.microsoft.com/en-us/azure/cosmos-db/consistency-levels) | 2025-09-03 |
| S15 | [Linux-based emulator, vNext](https://learn.microsoft.com/en-us/azure/cosmos-db/emulator-linux) | 2026-10-07 |
| S16 | [Emulator](https://learn.microsoft.com/en-us/azure/cosmos-db/emulator) and [Windows emulator arguments](https://learn.microsoft.com/en-us/azure/cosmos-db/emulator-windows-arguments) | 2026-09-01 and 2025-03-10 |
| S17 | [Role-based access control for Cosmos DB for NoSQL](https://learn.microsoft.com/en-us/azure/cosmos-db/how-to-connect-role-based-access-control) | 2025-09-10, updated 2026-10-15 |
| S18 | [Hierarchical partition keys](https://learn.microsoft.com/en-us/azure/cosmos-db/hierarchical-partition-keys) | 2026-02-02 |
| S19 | [Query a container](https://learn.microsoft.com/en-us/azure/cosmos-db/how-to-query-container) | 2025-07-09, updated 2026-10-05 |
| S20 | [Extensions by name, Azure Database for PostgreSQL](https://learn.microsoft.com/en-us/azure/postgresql/extensions/concepts-extensions-versions) | 2026-07-10 |
| S21 | [Vector search with pgvector](https://learn.microsoft.com/en-us/azure/postgresql/extensions/how-to-use-pgvector) | 2026-07-10 |
| S22 | [DiskANN in Azure Database for PostgreSQL](https://learn.microsoft.com/en-us/azure/postgresql/extensions/how-to-use-pgdiskann) | 2026-07-10 |
| S23 | [Microsoft Entra authentication in Azure Database for PostgreSQL](https://learn.microsoft.com/en-us/azure/postgresql/security/security-entra-concepts) | 2026-07-14 |
| S24 | [Optimize performance with pgvector](https://learn.microsoft.com/en-us/azure/postgresql/extensions/how-to-optimize-performance-pgvector) | 2026-07-10 |
| S25 | [Considerations for extensions](https://learn.microsoft.com/en-us/azure/postgresql/extensions/concepts-extensions-considerations) | 2026-08-12 |
| S26 | [Vector data type](https://learn.microsoft.com/en-us/sql/t-sql/data-types/vector-data-type) | 2026-10-13 |
| S27 | [VECTOR_DISTANCE](https://learn.microsoft.com/en-us/sql/t-sql/functions/vector-distance-transact-sql) | 2025-11-18 |
| S28 | [CREATE VECTOR INDEX](https://learn.microsoft.com/en-us/sql/t-sql/statements/create-vector-index-transact-sql) | 2026-09-15 |
| S29 | [Full-text search, SQL Server](https://learn.microsoft.com/en-us/sql/relational-databases/search/full-text-search) | 2026-07-20 |
| S30 | [Graph processing, SQL Server and Azure SQL Database](https://learn.microsoft.com/en-us/sql/relational-databases/graphs/sql-graph-overview) | 2025-10-14 |
| S31 | [SHORTEST_PATH](https://learn.microsoft.com/en-us/sql/relational-databases/graphs/sql-graph-shortest-path) | 2023-06-28 |
| S32 | [Limit search results with RANK](https://learn.microsoft.com/en-us/sql/relational-databases/search/limit-search-results-with-rank) | 2026-07-20 |
| S33 | [Install full-text search on Linux](https://learn.microsoft.com/en-us/sql/linux/sql-server-linux-setup-full-text-search) | 2026-01-08 |
| S34 | [Microsoft Entra authentication, Azure SQL](https://learn.microsoft.com/en-us/azure/azure-sql/database/authentication-aad-overview) | 2026-10-07 |
| S35 | [Run SQL Server container images with Docker](https://learn.microsoft.com/en-us/sql/linux/quickstart-install-connect-docker) | 2026-05-07 |
| S36 | [Cosmos DB .NET SDK v3](https://learn.microsoft.com/en-us/azure/cosmos-db/sdk-dotnet-v3) | 2026-10-07 |
| S37 | [Microsoft.Azure.Cosmos on NuGet](https://www.nuget.org/packages/Microsoft.Azure.Cosmos), version 3.64.0 | Read 2026-11-28 |
| S38 | [Npgsql on NuGet](https://www.nuget.org/packages/Npgsql), version 11.0.0 | Read 2026-11-28 |
| S39 | [Gremlin.Net on NuGet](https://www.nuget.org/packages/Gremlin.Net), versions 3.4.13 and 3.8.2 | Read 2026-11-28 |
| S40 | [Microsoft.Data.SqlClient on NuGet](https://www.nuget.org/packages/Microsoft.Data.SqlClient), version 8.0.0, and [its issues](https://github.com/dotnet/SqlClient/issues/4193) | Read 2026-11-28 |
| S41 | Cosmos DB SDK issues [4458](https://github.com/Azure/azure-cosmos-dotnet-v3/issues/4458) and [5142](https://github.com/Azure/azure-cosmos-dotnet-v3/issues/5142) | Open, updated 2026-09-11 |
| S42 | [TinkerPop change log](https://github.com/apache/tinkerpop/blob/master/CHANGELOG.asciidoc) | 3.4.13 on 2022-01-10, 3.7.0 on 2023-07-31 |
| S43 | [Npgsql 8.0 release notes](https://www.npgsql.org/doc/release-notes/8.0.html) | Read 2026-11-28 |
| S44 | [pgvector README](https://github.com/pgvector/pgvector) | Read 2026-11-28 |
| S45 | [pgvector image on Docker Hub](https://hub.docker.com/r/pgvector/pgvector/tags), tag `0.8.7-pg19` | 2026-11-20 |
| S46 | [PostgreSQL text search limitations](https://www.postgresql.org/docs/current/textsearch-limitations.html) | Read 2026-11-28 |

[S1]: https://learn.microsoft.com/en-us/azure/cosmos-db/gremlin/support
[S2]: https://learn.microsoft.com/en-us/azure/cosmos-db/gremlin/limits
[S3]: https://learn.microsoft.com/en-us/azure/cosmos-db/gremlin/overview
[S4]: https://learn.microsoft.com/en-us/azure/cosmos-db/gremlin/partitioning
[S5]: https://learn.microsoft.com/en-us/azure/cosmos-db/gremlin/quickstart-dotnet
[S6]: https://learn.microsoft.com/en-us/azure/postgresql/azure-ai/generative-ai-age-overview
[S7]: https://learn.microsoft.com/en-us/azure/cosmos-db/vector-search
[S8]: https://learn.microsoft.com/en-us/azure/cosmos-db/gen-ai/full-text-search
[S9]: https://learn.microsoft.com/en-us/azure/cosmos-db/gen-ai/full-text-search-faq
[S10]: https://learn.microsoft.com/en-us/cosmos-db/query/vectordistance
[S11]: https://learn.microsoft.com/en-us/cosmos-db/query/rrf
[S12]: https://learn.microsoft.com/en-us/azure/cosmos-db/concepts-limits
[S13]: https://learn.microsoft.com/en-us/azure/cosmos-db/transactional-batch
[S14]: https://learn.microsoft.com/en-us/azure/cosmos-db/consistency-levels
[S15]: https://learn.microsoft.com/en-us/azure/cosmos-db/emulator-linux
[S16]: https://learn.microsoft.com/en-us/azure/cosmos-db/emulator
[S17]: https://learn.microsoft.com/en-us/azure/cosmos-db/how-to-connect-role-based-access-control
[S18]: https://learn.microsoft.com/en-us/azure/cosmos-db/hierarchical-partition-keys
[S19]: https://learn.microsoft.com/en-us/azure/cosmos-db/how-to-query-container
[S20]: https://learn.microsoft.com/en-us/azure/postgresql/extensions/concepts-extensions-versions
[S21]: https://learn.microsoft.com/en-us/azure/postgresql/extensions/how-to-use-pgvector
[S22]: https://learn.microsoft.com/en-us/azure/postgresql/extensions/how-to-use-pgdiskann
[S23]: https://learn.microsoft.com/en-us/azure/postgresql/security/security-entra-concepts
[S24]: https://learn.microsoft.com/en-us/azure/postgresql/extensions/how-to-optimize-performance-pgvector
[S25]: https://learn.microsoft.com/en-us/azure/postgresql/extensions/concepts-extensions-considerations
[S26]: https://learn.microsoft.com/en-us/sql/t-sql/data-types/vector-data-type
[S27]: https://learn.microsoft.com/en-us/sql/t-sql/functions/vector-distance-transact-sql
[S28]: https://learn.microsoft.com/en-us/sql/t-sql/statements/create-vector-index-transact-sql
[S29]: https://learn.microsoft.com/en-us/sql/relational-databases/search/full-text-search
[S30]: https://learn.microsoft.com/en-us/sql/relational-databases/graphs/sql-graph-overview
[S31]: https://learn.microsoft.com/en-us/sql/relational-databases/graphs/sql-graph-shortest-path
[S32]: https://learn.microsoft.com/en-us/sql/relational-databases/search/limit-search-results-with-rank
[S33]: https://learn.microsoft.com/en-us/sql/linux/sql-server-linux-setup-full-text-search
[S34]: https://learn.microsoft.com/en-us/azure/azure-sql/database/authentication-aad-overview
[S35]: https://learn.microsoft.com/en-us/sql/linux/quickstart-install-connect-docker
[S36]: https://learn.microsoft.com/en-us/azure/cosmos-db/sdk-dotnet-v3
[S37]: https://www.nuget.org/packages/Microsoft.Azure.Cosmos
[S38]: https://www.nuget.org/packages/Npgsql
[S39]: https://www.nuget.org/packages/Gremlin.Net
[S40]: https://www.nuget.org/packages/Microsoft.Data.SqlClient
[S41]: https://github.com/Azure/azure-cosmos-dotnet-v3/issues/5142
[S42]: https://github.com/apache/tinkerpop/blob/master/CHANGELOG.asciidoc
[S43]: https://www.npgsql.org/doc/release-notes/8.0.html
[S44]: https://github.com/pgvector/pgvector
[S45]: https://hub.docker.com/r/pgvector/pgvector/tags
[S46]: https://www.postgresql.org/docs/current/textsearch-limitations.html
