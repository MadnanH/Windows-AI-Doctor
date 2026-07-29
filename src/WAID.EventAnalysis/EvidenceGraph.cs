using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;

namespace WAID.EventAnalysis;

public enum EvidenceDomain { Event, Driver, Update, Storage, Network, Crash, Performance, Repair, Other }
public enum EvidenceFreshness { Current, Recent, Historical, Expired }
public enum EvidenceRelationshipKind { TemporalAssociation, SharedSignal, ConflictingObservation, RepairFollowUp, CausalCandidate }

public sealed record EvidenceProvenance(string SourceId, string SourceType, string SourceReference, string? ScannerVersion);
public sealed record EvidenceObservation(string SourceId, EvidenceDomain Domain, string Code, string Summary, DateTimeOffset ObservedAtUtc, IReadOnlyDictionary<string,string> Attributes, EvidenceProvenance Provenance);
public sealed record EvidenceNode(string Id, EvidenceDomain Domain, string Code, string Summary, DateTimeOffset FirstObservedAtUtc, DateTimeOffset LastObservedAtUtc, EvidenceFreshness Freshness, IReadOnlyDictionary<string,string> Attributes, IReadOnlyList<EvidenceProvenance> Provenance, int DuplicateCount);
public sealed record EvidenceRelationship(string Id, string FromNodeId, string ToNodeId, EvidenceRelationshipKind Kind, double Confidence, string StrategyId, string StrategyVersion, string Rationale, bool IsCausalClaim = false);
public sealed record EvidenceGraph(Guid Id, DateTimeOffset GeneratedAtUtc, string SchemaVersion, string StrategyVersion, IReadOnlyList<EvidenceNode> Nodes, IReadOnlyList<EvidenceRelationship> Relationships, DateTimeOffset RetainUntilUtc);
public sealed record EvidenceCorrelationOptions(TimeSpan TemporalWindow, TimeSpan CurrentAge, TimeSpan RecentAge, TimeSpan Retention, int MaximumNodes)
{
    public static EvidenceCorrelationOptions Default { get; } = new(TimeSpan.FromHours(6), TimeSpan.FromHours(24), TimeSpan.FromDays(7), TimeSpan.FromDays(90), 10_000);
    public EvidenceCorrelationOptions Validate() { if(TemporalWindow<=TimeSpan.Zero||CurrentAge<=TimeSpan.Zero||RecentAge<CurrentAge||Retention<RecentAge||MaximumNodes is <1 or >100_000) throw new ArgumentOutOfRangeException(nameof(EvidenceCorrelationOptions)); return this; }
}
public interface IEvidenceGraphRepository { Task SaveAsync(EvidenceGraph graph,CancellationToken token); Task<EvidenceGraph?> GetLatestAsync(CancellationToken token); }

public sealed class EvidenceAggregationEngine(TimeProvider timeProvider)
{
    public const string SchemaVersion="1.0"; public const string StrategyVersion="deterministic-v1";
    public EvidenceGraph Build(IEnumerable<EvidenceObservation> source, EvidenceCorrelationOptions? options=null)
    {
        ArgumentNullException.ThrowIfNull(source); options=(options??EvidenceCorrelationOptions.Default).Validate(); var now=timeProvider.GetUtcNow();
        var raw=source.Select(Validate).Where(x=>now-x.ObservedAtUtc<=options.Retention).OrderBy(x=>x.ObservedAtUtc).ThenBy(x=>x.SourceId,StringComparer.Ordinal).Take(options.MaximumNodes*4).ToArray();
        var nodes=raw.GroupBy(Fingerprint,StringComparer.Ordinal).Select(g=>Node(g,now,options)).OrderBy(x=>x.FirstObservedAtUtc).ThenBy(x=>x.Id,StringComparer.Ordinal).Take(options.MaximumNodes).ToArray();
        var edges=new List<EvidenceRelationship>(); var reachable=new Dictionary<string,HashSet<string>>(StringComparer.Ordinal);
        for(var j=0;j<nodes.Length;j++) for(var i=j-1;i>=0;i--) { var a=nodes[i];var b=nodes[j];var delta=b.FirstObservedAtUtc-a.LastObservedAtUtc;if(delta>options.TemporalWindow)break;if(delta.Duration()>options.TemporalWindow)continue;
            if(a.Code.Equals(b.Code,StringComparison.OrdinalIgnoreCase)) Add(edges,reachable,a,b,EvidenceRelationshipKind.SharedSignal,.82,"matching normalized signal");
            if(Conflict(a,b)) Add(edges,reachable,a,b,EvidenceRelationshipKind.ConflictingObservation,.9,"sources report conflicting normalized state");
            if(a.Domain==EvidenceDomain.Repair||b.Domain==EvidenceDomain.Repair) Add(edges,reachable,a,b,EvidenceRelationshipKind.RepairFollowUp,.7,"observation occurred within the repair follow-up window");
            else if(a.Domain!=b.Domain) Add(edges,reachable,a,b,EvidenceRelationshipKind.CausalCandidate,.6,"cross-domain signals occurred within the configured temporal window");
            else Add(edges,reachable,a,b,EvidenceRelationshipKind.TemporalAssociation,.55,"signals occurred within the configured temporal window"); }
        return new(Guid.NewGuid(),now,SchemaVersion,StrategyVersion,nodes,edges,now+options.Retention);
    }
    private static EvidenceObservation Validate(EvidenceObservation x){if(string.IsNullOrWhiteSpace(x.SourceId)||string.IsNullOrWhiteSpace(x.Code)||string.IsNullOrWhiteSpace(x.Summary)||x.ObservedAtUtc==default)throw new ArgumentException("Evidence requires source, code, summary, and timestamp.");return x with{SourceId=x.SourceId.Trim(),Code=x.Code.Trim().ToUpperInvariant(),Summary=x.Summary.Trim(),Attributes=new ReadOnlyDictionary<string,string>(new Dictionary<string,string>(x.Attributes,StringComparer.OrdinalIgnoreCase))};}
    private static string Fingerprint(EvidenceObservation x)=>Hash($"{x.Domain}|{x.Code}|{x.Summary.ToUpperInvariant()}|{x.ObservedAtUtc.UtcTicks/TimeSpan.TicksPerMinute}");
    private static EvidenceNode Node(IEnumerable<EvidenceObservation> values,DateTimeOffset now,EvidenceCorrelationOptions o){var a=values.ToArray();var first=a.Min(x=>x.ObservedAtUtc);var last=a.Max(x=>x.ObservedAtUtc);var age=now-last;var freshness=age<=o.CurrentAge?EvidenceFreshness.Current:age<=o.RecentAge?EvidenceFreshness.Recent:age<=o.Retention?EvidenceFreshness.Historical:EvidenceFreshness.Expired;var attrs=a.SelectMany(x=>x.Attributes).GroupBy(x=>x.Key,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>string.Join(" | ",g.Select(x=>x.Value).Distinct(StringComparer.OrdinalIgnoreCase)),StringComparer.OrdinalIgnoreCase);return new(Fingerprint(a[0]),a[0].Domain,a[0].Code,a[0].Summary,first,last,freshness,new ReadOnlyDictionary<string,string>(attrs),a.Select(x=>x.Provenance).Distinct().ToArray(),a.Length-1);}
    private static bool Conflict(EvidenceNode a,EvidenceNode b){if(!a.Code.Equals(b.Code,StringComparison.OrdinalIgnoreCase))return false;foreach(var key in a.Attributes.Keys.Intersect(b.Attributes.Keys,StringComparer.OrdinalIgnoreCase))if(!a.Attributes[key].Equals(b.Attributes[key],StringComparison.OrdinalIgnoreCase))return true;return false;}
    private static void Add(List<EvidenceRelationship> edges,Dictionary<string,HashSet<string>> reachable,EvidenceNode a,EvidenceNode b,EvidenceRelationshipKind kind,double confidence,string rationale){var from=string.CompareOrdinal(a.Id,b.Id)<0?a:b;var to=ReferenceEquals(from,a)?b:a;if(edges.Any(x=>x.FromNodeId==from.Id&&x.ToNodeId==to.Id&&x.Kind==kind))return;if(Reachable(reachable,to.Id,from.Id))return;edges.Add(new(Hash($"{from.Id}|{to.Id}|{kind}"),from.Id,to.Id,kind,confidence,$"evidence-{kind.ToString().ToLowerInvariant()}",StrategyVersion,rationale,false));if(!reachable.TryGetValue(from.Id,out var set))reachable[from.Id]=set=[];set.Add(to.Id);}
    private static bool Reachable(Dictionary<string,HashSet<string>> map,string start,string target){var seen=new HashSet<string>();var q=new Queue<string>();q.Enqueue(start);while(q.Count>0){var n=q.Dequeue();if(!seen.Add(n))continue;if(n==target)return true;if(map.TryGetValue(n,out var next))foreach(var x in next)q.Enqueue(x);}return false;}
    private static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..24];
}