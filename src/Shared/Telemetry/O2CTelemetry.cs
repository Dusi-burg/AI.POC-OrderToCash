namespace O2C.Shared.Telemetry;

public static class O2CTelemetry
{
    /// <summary>Filtro usato da ServiceDefaults per raccogliere tutte le sorgenti del POC.</summary>
    public const string SourceWildcard = "O2C.*";

    public static class Sources
    {
        public const string Orchestrator = "O2C.Orchestrator";
        public const string McpErp = "O2C.Mcp.Erp";
        public const string McpCrm = "O2C.Mcp.Crm";
        public const string ErpApi = "O2C.Erp.Api";
        public const string Approvals = "O2C.Approvals";
    }

    public static class Attributes
    {
        public const string AgentName = "agent.name";
        public const string ToolName = "tool.name";
        public const string CorrelationId = "correlation.id";
        public const string ToolOutcome = "tool.outcome";
    }
}
