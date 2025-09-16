# OpenData.Mcp.Server Integration Tests

These tests exercise the live UK Parliament APIs via the MCP tool classes. They are **skipped by default** to avoid network traffic during normal development and CI runs.

## Running locally

`powershell
 = 'true'
dotnet test OpenDataMcpServer.sln
`

Any value other than 	rue (case-insensitive) keeps the tests skipped.

These tests also run weekly via the Live Integration Tests GitHub Actions workflow (cron at 03:00 UTC on Mondays).
