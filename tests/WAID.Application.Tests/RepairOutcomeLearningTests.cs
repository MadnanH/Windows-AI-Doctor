using WAID.Application.Services;
using WAID.Domain.Repairs;

namespace WAID.Application.Tests;

public sealed class RepairOutcomeLearningTests
{
 [Fact]public async Task Validation_backed_success_is_the_only_success_class(){var repository=new MemoryRepository();var service=new RepairOutcomeRecorder(repository,TimeProvider.System);await service.RecordAsync(Record(RepairOrchestrationStage.Committed,new(true,"v",DateTimeOffset.UtcNow,"validated",["before","after"])),CancellationToken.None);await service.RecordAsync(Record(RepairOrchestrationStage.Committed,null)with{Id=Guid.NewGuid()},CancellationToken.None);var entries=await repository.QueryAsync(new(),CancellationToken.None);Assert.Contains(entries,x=>x.Outcome==RepairOutcomeClass.ValidatedSuccess);Assert.Contains(entries,x=>x.Outcome==RepairOutcomeClass.Unknown);}
 [Fact]public async Task Audit_identity_is_deterministic_and_append_is_immutable(){var repository=new MemoryRepository();var service=new RepairOutcomeRecorder(repository,TimeProvider.System);var record=Record(RepairOrchestrationStage.Committed,new(true,"v",DateTimeOffset.UtcNow,"validated",[]));await service.RecordAsync(record,CancellationToken.None);await service.RecordAsync(record,CancellationToken.None);Assert.Single(repository.Entries);}
 [Fact]public async Task Aggregate_rebuild_is_bounded_explainable_and_does_not_create_logic(){var repository=new MemoryRepository();var service=new RepairOutcomeRecorder(repository,TimeProvider.System);await service.RecordAsync(Record(RepairOrchestrationStage.Committed,new(true,"v",DateTimeOffset.UtcNow,"validated",[])),CancellationToken.None);await service.RecordAsync(Record(RepairOrchestrationStage.Failed,null)with{Id=Guid.NewGuid()},CancellationToken.None);var aggregate=Assert.Single(await service.RebuildAggregatesAsync(CancellationToken.None));Assert.Equal(2,aggregate.Total);Assert.Equal(50,aggregate.ValidatedSuccessRate);Assert.Contains("does not authorize",aggregate.Interpretation,StringComparison.OrdinalIgnoreCase);}
 [Fact]public async Task Feedback_and_evidence_are_redacted(){var repository=new MemoryRepository();var service=new RepairOutcomeRecorder(repository,TimeProvider.System);await service.RecordFeedbackAsync(Guid.NewGuid(),"waid.test","user","token=secret-value",CancellationToken.None);var entry=Assert.Single(repository.Entries);Assert.DoesNotContain("secret-value",entry.Summary,StringComparison.Ordinal);}
 private static RepairOrchestrationRecord Record(RepairOrchestrationStage stage,RepairValidationOutcome? validation)=>new(Guid.NewGuid(),"waid.test",DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,stage,"1.0",null,new(true,true,DateTimeOffset.UtcNow,"user","policy"),null,null,null,null,false,validation,[],"result");
 private sealed class MemoryRepository:IRepairOutcomeRepository
 {
  public List<RepairAuditEntry> Entries{get;}=[];public IReadOnlyList<RepairOutcomeAggregate> Aggregates=[];
  public Task AppendAsync(RepairAuditEntry entry,CancellationToken token){if(Entries.All(x=>x.Id!=entry.Id))Entries.Add(entry);return Task.CompletedTask;}
  public Task<IReadOnlyList<RepairAuditEntry>> QueryAsync(RepairAuditQuery query,CancellationToken token)=>Task.FromResult<IReadOnlyList<RepairAuditEntry>>(Entries.Take(query.Limit).ToArray());
  public Task ReplaceAggregatesAsync(IReadOnlyList<RepairOutcomeAggregate> aggregates,CancellationToken token){Aggregates=aggregates;return Task.CompletedTask;}
  public Task<IReadOnlyList<RepairOutcomeAggregate>> GetAggregatesAsync(CancellationToken token)=>Task.FromResult(Aggregates);
 }
}
