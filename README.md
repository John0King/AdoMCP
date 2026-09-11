# AdoMcp

[![NuGet](https://img.shields.io/nuget/v/AdoMcp.svg)](https://www.nuget.org/packages/AdoMcp)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

一个开箱即用的数据库 [Model Context Protocol (MCP)](https://modelcontextprotocol.io/) Server，让 AI Agent 能够发现数据库对象、理解表结构和注释、检查索引，并安全地执行 SQL。

A ready-to-run database MCP server for schema discovery, comments, indexes, and controlled SQL execution.

支持 SQL Server、MySQL/MariaDB、PostgreSQL、SQLite 和 Oracle；插件核心遵循 **Agent Plugins（Open Plugin Spec）1.0**，可用于 Codex、Claude Code、GitHub Copilot App 和 Copilot CLI。

<!-- mcp-name: io.github.John0King/adomcp -->

Server name 和 version 由 MCP SDK 根据程序集元数据自动生成；Server 会在 MCP `initialize`/`server/discover` 响应中返回根级 `instructions`，指导客户端先定位连接与对象、只用 `query_sql` 读数据，并仅在明确授权后调用 `execute_sql`。

## 功能

| Tool | 说明 |
|---|---|
| `list_connections` | 列出已配置的数据库连接 |
| `add_connection` | 在当前进程中新增或替换连接 |
| `remove_connection` | 删除运行时新增的连接 |
| `list_objects` | 查找表、视图、存储过程、函数、触发器、序列和同义词等对象 |
| `get_table_schema` | 获取字段、类型、可空性、主键、默认值和注释 |
| `get_table_indexes` | 获取表索引 |
| `query_sql` | 执行只读 SQL，返回 CSV |
| `execute_sql` | 执行写 SQL；默认禁用，需显式启用 |

| 数据库 | 驱动 | 注释支持 |
|---|---|---|
| SQL Server | `Microsoft.Data.SqlClient` | `MS_Description` 扩展属性 |
| MySQL / MariaDB | `MySqlConnector` | `TABLE_COMMENT` / `COLUMN_COMMENT` |
| PostgreSQL | `Npgsql` | `obj_description` / `col_description` |
| SQLite | `Microsoft.Data.Sqlite` | SQLite 无原生注释 |
| Oracle | `Oracle.ManagedDataAccess.Core` | `ALL_TAB_COMMENTS` / `ALL_COL_COMMENTS`，包含 PUBLIC synonym |

## 前置要求

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- 使用插件时，客户端需要允许启动本地 `dnx` 进程

无需提前安装 AdoMcp。插件和示例配置使用 `dnx -y AdoMcp`，首次运行时会从 NuGet 获取包。

## 插件安装

仓库根目录的 `plugin.json`、`mcp.json` 和 `skills/adomcp/SKILL.md` 是通用的 Agent Plugins 1.0 插件核心。`.codex-plugin/`、`.claude-plugin/`、`.mcp.json` 和 marketplace 文件是客户端兼容与分发入口。插件默认以 stdio 启动 AdoMcp，且不会开启写 SQL。

### Codex

仓库包含 `.codex-plugin/plugin.json`，可被 Codex 插件市场或本地插件源使用。若只需要 MCP Server，可直接添加：

```bash
codex mcp add adomcp -- dnx -y AdoMcp
```

### Claude Code（cc）

```text
/plugin marketplace add John0King/AdoMCP
/plugin install adomcp@adomcp-plugins
```

安装后执行 `/reload-plugins`；使用 `/mcp` 检查 `adomcp` 是否已连接。

### GitHub Copilot CLI

可以从 GitHub 仓库直接安装：

```bash
copilot plugin install John0King/AdoMCP
copilot mcp get adomcp
```

也可以通过 marketplace 安装：

```bash
copilot plugin marketplace add John0King/AdoMCP
copilot plugin install adomcp@adomcp-plugins
```

### GitHub Copilot App

在 Copilot App 中打开 **Customize → Plugins**，添加 `John0King/AdoMCP` marketplace，然后安装 **AdoMcp**。本仓库的 `.github/plugin/marketplace.json`、`plugin.json`、`.mcp.json` 和 skill 会作为一个插件加载。

> Copilot App/云端 Agent 必须能运行 .NET 10 和访问 NuGet，才能启动这个本地 stdio MCP Server。若运行环境不允许本地进程，请先自行以 HTTP 模式部署 AdoMcp，再按下文配置远程地址。

## 直接配置 MCP

适用于任何支持本地 MCP 的客户端：

```json
{
  "mcpServers": {
    "adomcp": {
      "command": "dnx",
      "args": ["-y", "AdoMcp"]
    }
  }
}
```

HTTP 模式需先启动服务：

```bash
dnx -y AdoMcp -- --http
```

然后连接端点：

```json
{
  "mcpServers": {
    "adomcp": {
      "url": "http://localhost:5100/mcp"
    }
  }
}
```

## 数据库连接配置

AdoMcp 按以下顺序加载配置，后面的来源覆盖前面的来源：

1. 应用目录中的 `appsettings.json`
2. `appsettings.{Environment}.json`
3. 用户配置 `~/.adomcp.json`（Windows 为 `%USERPROFILE%\.adomcp.json`）
4. `ADOMCP_` 前缀的环境变量
5. .NET User Secrets
6. 命令行 `--allow-any-sql`（仅覆盖写 SQL 开关）

最常用的方式是在用户目录创建 `.adomcp.json`：

```json
{
  "AllowAnySql": false,
  "Databases": [
    {
      "Name": "mydb",
      "DbType": "PostgreSql",
      "ConnectionString": "Host=localhost;Database=app;Username=postgres;Password=***;",
      "Description": "Local development database"
    }
  ]
}
```

`DbType` 可选值：`SqlServer`、`MySql`、`PostgreSql`、`Sqlite`、`Oracle`。

也可以不创建配置文件，让 Agent 在会话中调用 `add_connection`。动态连接仅在当前 AdoMcp 进程中有效，重启后不会保留。

> 不要把真实连接字符串提交到 Git。生产环境建议使用环境变量、User Secrets 或客户端的密钥管理能力。

## 运行模式

默认使用 stdio；stdout 仅传输 MCP JSON-RPC，日志写入 stderr。

```bash
# 默认 stdio
dnx -y AdoMcp

# 显式 stdio（兼容旧配置）
dnx -y AdoMcp -- --stdio

# HTTP，默认监听 http://localhost:5100/mcp
dnx -y AdoMcp -- --http

# 安装为全局 .NET tool
dotnet tool install -g AdoMcp
adomcp
```

### 写 SQL 安全开关

`execute_sql` 默认不可用。只有在用户明确授权写操作后，才应通过以下任一方式开启：

```bash
dnx -y AdoMcp -- --allow-any-sql
dnx -y AdoMcp -- --http --allow-any-sql
```

或在配置中设置 `"AllowAnySql": true`，或设置环境变量 `ADOMCP_ALLOWANYSQL=true`。命令行参数优先级最高。

## Agent 推荐工作流

1. 用 `list_connections` 确认连接。
2. 缺少连接时用 `add_connection` 添加。
3. 用 `list_objects` 确认 schema、对象类型和对象名。
4. 用 `get_table_schema` 查看结构和注释。
5. 涉及键或性能时用 `get_table_indexes`。
6. 只读验证使用 `query_sql`。
7. 仅在用户明确授权且 Server 开启写入能力时使用 `execute_sql`。

Oracle 中未带 owner 的对象可能是 synonym，应先通过 `list_objects` 解析真实 schema。

## 环境变量

| 变量 | 说明 |
|---|---|
| `ADOMCP_MODE` | `stdio` 或 `http` |
| `ADOMCP_URLS` | HTTP 监听地址，例如 `http://0.0.0.0:5100` |
| `ADOMCP_ALLOWANYSQL` | 是否启用 `execute_sql`，默认 `false` |
| `ADOMCP_DATABASES` | JSON 编码的 `Databases` 数组 |

## MCP Registry

AdoMcp 已注册到官方 [MCP Registry](https://registry.modelcontextprotocol.io/)，Server name 为 `io.github.John0King/adomcp`。

## License

[MIT](LICENSE)
