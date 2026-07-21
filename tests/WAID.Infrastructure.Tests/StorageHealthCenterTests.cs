using WAID.Application.Abstractions;using WAID.Infrastructure.Diagnostics;namespace WAID.Infrastructure.Tests;
public sealed class StorageHealthCenterTests
{
 private static readonly DateTimeOffset Now=DateTimeOffset.UtcNow;
 [Fact]public void Thresholds_cite_evidence_and_uncertainty(){var s=new StorageSnapshot(Now,[Disk(65,85,150,"Warning")],[new("v","","NTFS",1000,40,"Healthy","volume")],[new(Now,"v",55,"NTFS error","event")],[]);var w=StorageHealthCenter.Evaluate(s);Assert.Contains(w,x=>x.Concern==StorageConcern.Capacity);Assert.Contains(w,x=>x.Concern==StorageConcern.Smart&&x.Confidence<1);Assert.Contains(w,x=>x.Concern==StorageConcern.Temperature);Assert.Contains(w,x=>x.Concern==StorageConcern.Wear);Assert.Contains(w,x=>x.Concern==StorageConcern.Performance);Assert.All(w,x=>Assert.NotEmpty(x.Evidence));}
 [Fact]public async Task Folder_analysis_reports_bytes_and_never_deletes(){var root=Path.Combine(Path.GetTempPath(),$"waid-folder-{Guid.NewGuid():N}");Directory.CreateDirectory(root);var file=Path.Combine(root,"data.bin");await File.WriteAllBytesAsync(file,new byte[1024]);try{var r=await new LargeFolderAnalyzer().AnalyzeAsync(root,CancellationToken.None);Assert.Equal(1024,r.Bytes);Assert.Equal(1,r.FileCount);Assert.True(File.Exists(file));}finally{Directory.Delete(root,true);}}
 [Fact]public async Task Folder_analysis_honors_cancellation(){using var c=new CancellationTokenSource();c.Cancel();var r=await new LargeFolderAnalyzer().AnalyzeAsync(Path.GetTempPath(),c.Token);Assert.True(r.Cancelled);}
 [Fact]public async Task Cleanup_is_dry_run_and_requires_explicit_selection(){var r=await new SafeCleanupEstimator().EstimateAsync(CancellationToken.None);Assert.All(r,x=>Assert.True(x.RequiresExplicitSelection));}
 private static PhysicalDiskHealth Disk(double? temp=null,double? wear=null,double? read=null,string health="Healthy")=>new("d","Disk","SSD","NVMe",1000,health,"OK",temp,wear,read,0,"source");
}
