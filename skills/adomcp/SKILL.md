---
name: adomcp
description: Use AdoMcp to discover database objects, inspect schemas, comments and indexes, and run SQL. Use when a user asks about SQL Server, MySQL, PostgreSQL, SQLite, or Oracle structure or data.
---

# AdoMcp

Use AdoMcp for database discovery and analysis. Identify the connection and object before querying so that work is performed against the intended database and schema.

## Safe workflow

1. Call `list_connections` to discover configured connections.
2. If the required connection is absent, ask for its database type and connection string, then call `add_connection`.
3. Call `list_objects` before inspecting or querying an object. Confirm its schema, type, and name.
4. Call `get_table_schema` for columns, types, nullability, primary keys, defaults, and comments.
5. Call `get_table_indexes` when keys or query performance matter.
6. Use `query_sql` for read-only verification and report the query's purpose plus the returned row count or a concise sample.
7. Use `execute_sql` only when the user explicitly authorizes the write and the server has been started with `--allow-any-sql`.

## Oracle

Unqualified Oracle objects may be synonyms. Always locate the object with `list_objects` and use the resolved owner. If the name exists in more than one schema, ask the user to choose a schema.

## Guardrails

- Never use `query_sql` for `INSERT`, `UPDATE`, `DELETE`, DDL, or other writes.
- Never call `execute_sql` without explicit user authorization.
- Do not infer business meanings that are absent from schema comments or user-provided context; label uncertain interpretations.
- Do not expose connection strings or credentials in the response.
