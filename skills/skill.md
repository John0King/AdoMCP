---
name: adomcp
description: Use AdoMcp tools to understand database structures and data. This skill must be used when users mention table schemas, field meanings, synonyms, views, stored procedures, indexes, or read-only SQL queries.
---

# adomcp

AdoMcp is used for database exploration and read-only analysis. Follow the fixed workflow below to avoid mistakes.

## Tool Mapping

- `list_connections`
- `add_connection`
- `list_objects`
- `get_table_schema`
- `get_table_indexes`
- `query_sql`
- `execute_sql` (only when the user explicitly requests write operations)

## Standard Workflow

1. First call `list_connections` to check available connections.
2. If no connection is available, call `add_connection`.
3. Before querying tables/views, call `list_objects` to confirm schema, object type, and whether it is a synonym.
4. After confirming the target table, call `get_table_schema` to get fields, types, nullability, primary key, defaults, and comments.
5. When performance or key design matters, call `get_table_indexes`.
7. For read-only data verification, use `query_sql`.

## Oracle Notes

- In Oracle, objects without an owner prefix may be synonyms. Always use `list_objects` first to confirm the real schema.
- If the same object name exists across multiple schemas, ask the user to confirm the target schema before checking schema details or querying data.

## Output Requirements

- First provide object location results (`schema + objectType + objectName`).
- Then provide structure conclusions (key fields, primary key, nullability, defaults, comments).
- If SQL was executed, include purpose and returned row count/sample.
- If uncertain, clearly mark it as a "pending confirmation" item. Do not guess business meaning of fields.


## do not do

- Do not use `query_sql` for any write operation such as `insert` / `update` / `delete` / `alter` / `drop`.
- Do not call `execute_sql` without explicit user authorization.
- Do not skip connection checks and object-location steps, to avoid querying the wrong database, schema, or synonym.
