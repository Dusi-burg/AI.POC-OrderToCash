namespace Dusiburg.AI.O2C.Shared.Telemetry;

public static class O2CTelemetry
{
    /// <summary>Filtro usato da ServiceDefaults per raccogliere tutte le sorgenti del POC.</summary>
    public const string SourceWildcard = "Dusiburg.AI.O2C.*";

    public static class Sources
    {
        public const string Orchestrator = "Dusiburg.AI.O2C.Orchestrator";
        public const string McpErp = "Dusiburg.AI.O2C.Mcp.Erp";
        public const string McpCrm = "Dusiburg.AI.O2C.Mcp.Crm";
        public const string ErpApi = "Dusiburg.AI.O2C.Erp.Api";
        public const string Approvals = "Dusiburg.AI.O2C.Approvals";
    }

    public static class Attributes
    {
        public const string AgentName = "agent.name";
        public const string ToolName = "tool.name";
        public const string CorrelationId = "correlation.id";
        public const string ToolOutcome = "tool.outcome";
        public const string DealId = "deal.id";
        public const string DealOutcome = "o2c.outcome";
        public const string DealCloseOutcome = "deal.close.outcome";
        public const string HandoffFrom = "handoff.from";
        public const string HandoffTo = "handoff.to";
        public const string HandoffReason = "handoff.reason";
        public const string ApprovalId = "approval.id";
        public const string ApprovalReasons = "approval.reasons";
        public const string ApprovalDecision = "approval.decision";
        public const string ApprovalDecidedBy = "approval.decided_by";
    }
}
