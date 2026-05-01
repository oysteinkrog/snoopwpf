namespace SnoopWPF.Agent.Tools;

using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// MCP tool: wpf_pump_until_idle — blocks until the WPF Dispatcher AND composition pipeline
/// are simultaneously idle, or until a timeout fires (M2-11).
/// </summary>
[McpServerToolType]
public sealed class PumpUntilIdleTool(ISnoopInspector inspector)
{
    [McpServerTool(Name = "wpf_pump_until_idle")]
    [Description(
        "Wait until the WPF Dispatcher queue AND composition rendering pipeline are simultaneously " +
        "idle (AND-gate, M2-11). Returns immediately when idle; throws DISPATCHER_BUSY if the " +
        "ceiling is reached. " +
        "\n\nDefault ceiling: 30 000 ms. Callers may raise maxTimeoutMs up to 60 000 ms for " +
        "long perf-profiling scenarios (e.g. cold-start session loads). " +
        "\n\nUse before wpf_poll_changes or wpf_wait_for_property when you need deterministic results " +
        "after a UI mutation. " +
        "\n\nNested-pump guard: calling this tool from within an active pump on the same thread " +
        "is rejected with DISPATCHER_BUSY immediately. " +
        "\n\nresources filter: pass an array of resource names to monitor only a subset " +
        "(e.g. [\"Dispatcher\"] to skip CompositionRendering). " +
        "Omit or pass null to monitor all built-in resources (Dispatcher + CompositionRendering). " +
        "\n\nwaitForLayoutQuiescence: when true, also waits for WPF layout passes to quiesce " +
        "(no LayoutUpdated event for one full ContextIdle tick). Useful for perf scenarios where " +
        "layout thrash must settle before measuring. Default false.")]
    public Task<string> PumpUntilIdleAsync(
        [Description("Maximum wait time in milliseconds. Capped at maxTimeoutMs (default ceiling: 30 000 ms). Default: 30000.")] int timeoutMs = 30000,
        [Description("Optional array of resource names to monitor (e.g. [\"Dispatcher\", \"CompositionRendering\"]). Omit or null to monitor all.")] IReadOnlyList<string>? resources = null,
        [Description("Hard ceiling in ms. Default null = 30 000 ms. May be raised up to 60 000 ms for perf-profiling cold-start scenarios.")] int? maxTimeoutMs = null,
        [Description("When true, AND-gates a LayoutUpdated quiescence check: waits until no WPF layout pass has fired for one ContextIdle tick. Default false.")] bool waitForLayoutQuiescence = false,
        CancellationToken ct = default)
    {
        return ToolExceptionMapper.Wrap(async () =>
        {
            var result = await inspector.PumpUntilIdleAsync(timeoutMs, resources, ct, maxTimeoutMs, waitForLayoutQuiescence).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, ToolSerializerOptions.Default);
        });
    }
}
