using System.Text.Json;
using JobPilot.Domain.Entities;
using JobPilot.Domain.Enums;

namespace JobPilot.Application.Features.Automation;

public sealed record AutomationCommandDto(
    Guid CommandId,
    Guid SessionId,
    AutomationCommandType CommandType,
    AutomationCommandStatus Status,
    string? Outcome,
    string? Error,
    DateTimeOffset EnqueuedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt);

public sealed record FillFieldRequest(
    Guid FieldId,
    string Value,
    string? OptionKey = null);

public sealed record AutofillPayload(
    IReadOnlyList<FillFieldRequest> Fields);

public sealed record NavigatePayload(
    string Direction); // "Next" or "Back"

public sealed record StartSessionPayload(
    string ApplicationUrl,
    string TenantKey,
    bool Headless = false);

public interface IAutomationCommandService
{
    Task<AutomationCommandDto> EnqueueCommandAsync(
        Guid sessionId,
        AutomationCommandType commandType,
        object? payload = null,
        CancellationToken ct = default);

    Task<AutomationCommand?> FetchNextPendingCommandAsync(
        string workerInstanceId,
        CancellationToken ct = default);

    Task CompleteCommandAsync(
        Guid commandId,
        string outcome,
        string? resultJson = null,
        CancellationToken ct = default);

    Task FailCommandAsync(
        Guid commandId,
        string error,
        CancellationToken ct = default);

    Task<IReadOnlyList<AutomationCommandDto>> GetSessionCommandsAsync(
        Guid sessionId,
        CancellationToken ct = default);
}

public interface IWorkerHeartbeatService
{
    Task RecordHeartbeatAsync(
        string workerInstanceId,
        string status,
        Guid? activeSessionId = null,
        CancellationToken ct = default);

    Task<bool> IsWorkerOnlineAsync(CancellationToken ct = default);
}
